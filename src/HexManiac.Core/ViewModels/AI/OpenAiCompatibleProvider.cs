using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   /// <summary>
   /// LLM provider for OpenAI-compatible APIs (Mistral, OpenAI, Ollama, Together AI, etc.).
   /// Uses the /v1/chat/completions endpoint format.
   /// </summary>
   public class OpenAiCompatibleProvider : ILlmProvider {
      private static readonly HttpClient httpClient = new();
      private readonly AiSettings settings;
      private readonly string defaultBaseUrl;

      public string ProviderName => settings.ProviderName;
      public string ModelName => settings.ModelName;
      public bool SupportsTools => true;
      public bool SupportsEmbeddings => false;
      public bool SupportsStreaming => true;
      public bool IsConfigured => settings.IsConfigured;

      private string ApiBase {
         get {
            var url = settings.BaseUrl;
            if (string.IsNullOrEmpty(url) || url == "https://api.anthropic.com") return defaultBaseUrl;
            return url;
         }
      }

      public OpenAiCompatibleProvider(AiSettings settings, string defaultBaseUrl = "https://api.mistral.ai") {
         this.settings = settings;
         this.defaultBaseUrl = defaultBaseUrl;
      }

      public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default) {
         if (!IsConfigured) return new LlmResponse { ErrorMessage = "API key not configured." };

         try {
            var body = BuildRequestBody(request);
            var json = JsonSerializer.Serialize(body, SerializerOptions);

            var httpRequest = CreateHttpRequest(json);
            var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
               return new LlmResponse { ErrorMessage = $"API error ({response.StatusCode}): {ExtractErrorMessage(responseJson)}" };
            }

            return ParseResponse(responseJson);
         } catch (TaskCanceledException) {
            return new LlmResponse { ErrorMessage = "Request was cancelled." };
         } catch (Exception ex) {
            return new LlmResponse { ErrorMessage = $"Request failed: {ex.Message}" };
         }
      }

      public async IAsyncEnumerable<StreamDelta> StreamCompleteAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
         if (!IsConfigured) {
            yield return new StreamDelta { IsFinal = true, FinalResponse = new LlmResponse { ErrorMessage = "API key not configured." } };
            yield break;
         }

         var sendResult = await SendStreamingRequestAsync(request, cancellationToken);
         if (sendResult.Error != null) {
            yield return new StreamDelta { IsFinal = true, FinalResponse = sendResult.Error };
            yield break;
         }

         var stream = await sendResult.Response.Content.ReadAsStreamAsync();
         using var reader = new StreamReader(stream);

         var textContent = new StringBuilder();
         int inputTokens = 0, outputTokens = 0;
         string stopReason = null;
         // Track tool calls by index: (id, name, argumentsBuilder)
         var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

         while (!reader.EndOfStream) {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6).Trim();
            if (data == "[DONE]") break;

            string textDelta = ParseStreamChunk(data, ref inputTokens, ref outputTokens,
               ref stopReason, toolCallBuilders, textContent);

            if (textDelta != null) {
               yield return new StreamDelta { Text = textDelta };
            }
         }

         List<ToolCall> toolCalls = null;
         if (toolCallBuilders.Count > 0) {
            toolCalls = toolCallBuilders.Values
               .Select(t => new ToolCall { Id = t.Id, Name = t.Name, Arguments = t.Args.ToString() })
               .ToList();
         }

         yield return new StreamDelta {
            IsFinal = true,
            FinalResponse = new LlmResponse {
               Content = textContent.ToString(),
               ToolCalls = toolCalls,
               StopReason = stopReason,
               InputTokens = inputTokens,
               OutputTokens = outputTokens,
            }
         };
      }

      public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) {
         return Task.FromResult(Array.Empty<float>());
      }

      #region Request Building

      private HttpRequestMessage CreateHttpRequest(string jsonBody) {
         var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/chat/completions");
         httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
         httpRequest.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
         return httpRequest;
      }

      private Dictionary<string, object> BuildRequestBody(LlmRequest request) {
         var messages = new List<object>();

         if (!string.IsNullOrEmpty(request.SystemPrompt)) {
            messages.Add(new { role = "system", content = request.SystemPrompt });
         }

         foreach (var msg in request.Messages) {
            if (msg.Role == ChatRole.System) continue;

            if (msg.Role == ChatRole.Tool && msg.ToolCallId != null) {
               messages.Add(new { role = "tool", content = msg.Content, tool_call_id = msg.ToolCallId });
            } else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0) {
               var toolCalls = msg.ToolCalls.Select(tc => new {
                  id = tc.Id,
                  type = "function",
                  function = new { name = tc.Name, arguments = tc.Arguments },
               }).ToList();
               messages.Add(new { role = "assistant", content = msg.Content, tool_calls = toolCalls });
            } else {
               var role = msg.Role == ChatRole.User ? "user" : "assistant";
               messages.Add(new { role, content = msg.Content });
            }
         }

         var body = new Dictionary<string, object> {
            ["model"] = settings.ModelName,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages,
         };

         if (request.Temperature > 0) {
            body["temperature"] = request.Temperature;
         }

         if (request.Tools != null && request.Tools.Count > 0) {
            body["tools"] = request.Tools.Select(t => new {
               type = "function",
               function = new {
                  name = t.Name,
                  description = t.Description,
                  parameters = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson),
               },
            }).ToList();
         }

         return body;
      }

      #endregion

      #region Response Parsing

      public static LlmResponse ParseResponse(string json) {
         using var doc = JsonDocument.Parse(json);
         var root = doc.RootElement;

         var response = new LlmResponse { Content = string.Empty };

         if (root.TryGetProperty("usage", out var usage)) {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) response.InputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) response.OutputTokens = ct.GetInt32();
         }

         if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0) {
            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null) {
               response.StopReason = fr.GetString();
            }

            if (choice.TryGetProperty("message", out var message)) {
               if (message.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null) {
                  response.Content = content.GetString();
               }

               if (message.TryGetProperty("tool_calls", out var toolCalls)) {
                  var calls = new List<ToolCall>();
                  foreach (var tc in toolCalls.EnumerateArray()) {
                     var func = tc.GetProperty("function");
                     calls.Add(new ToolCall {
                        Id = tc.GetProperty("id").GetString(),
                        Name = func.GetProperty("name").GetString(),
                        Arguments = func.GetProperty("arguments").GetString(),
                     });
                  }
                  if (calls.Count > 0) response.ToolCalls = calls;
               }
            }
         }

         return response;
      }

      #endregion

      #region Streaming

      private async Task<(HttpResponseMessage Response, LlmResponse Error)> SendStreamingRequestAsync(
            LlmRequest request, CancellationToken cancellationToken) {
         try {
            var body = BuildRequestBody(request);
            body["stream"] = true;
            var json = JsonSerializer.Serialize(body, SerializerOptions);

            var httpRequest = CreateHttpRequest(json);
            var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode) {
               var errorBody = await httpResponse.Content.ReadAsStringAsync();
               return (null, new LlmResponse { ErrorMessage = $"API error ({httpResponse.StatusCode}): {ExtractErrorMessage(errorBody)}" });
            }

            return (httpResponse, null);
         } catch (TaskCanceledException) {
            return (null, new LlmResponse { ErrorMessage = "Request was cancelled." });
         } catch (Exception ex) {
            return (null, new LlmResponse { ErrorMessage = $"Request failed: {ex.Message}" });
         }
      }

      private static string ParseStreamChunk(string data,
            ref int inputTokens, ref int outputTokens, ref string stopReason,
            Dictionary<int, (string Id, string Name, StringBuilder Args)> toolCallBuilders,
            StringBuilder textContent) {
         string textDelta = null;
         JsonDocument doc = null;
         try {
            doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage)) {
               if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
               if (usage.TryGetProperty("completion_tokens", out var ct)) outputTokens = ct.GetInt32();
            }

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0) {
               var choice = choices[0];

               if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null) {
                  stopReason = fr.GetString();
               }

               if (choice.TryGetProperty("delta", out var delta)) {
                  // Text content
                  if (delta.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null) {
                     textDelta = content.GetString();
                     if (textDelta != null) textContent.Append(textDelta);
                  }

                  // Tool calls (incremental)
                  if (delta.TryGetProperty("tool_calls", out var toolCalls)) {
                     foreach (var tc in toolCalls.EnumerateArray()) {
                        var index = tc.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;

                        if (tc.TryGetProperty("id", out var id)) {
                           // First chunk for this tool call: has id, name
                           var name = "";
                           if (tc.TryGetProperty("function", out var func) && func.TryGetProperty("name", out var n)) {
                              name = n.GetString();
                           }
                           toolCallBuilders[index] = (id.GetString(), name, new StringBuilder());
                        } else if (tc.TryGetProperty("function", out var func)) {
                           // Subsequent chunks: append arguments
                           if (func.TryGetProperty("arguments", out var args)) {
                              if (toolCallBuilders.TryGetValue(index, out var builder)) {
                                 builder.Args.Append(args.GetString());
                                 toolCallBuilders[index] = builder;
                              }
                           }
                        }
                     }
                  }
               }
            }
         } catch {
            // Skip malformed chunks
         } finally {
            doc?.Dispose();
         }
         return textDelta;
      }

      #endregion

      private static string ExtractErrorMessage(string json) {
         try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // OpenAI format: {"error": {"message": "...", "type": "...", "code": "..."}}
            if (root.TryGetProperty("error", out var error)) {
               if (error.TryGetProperty("message", out var msg)) return msg.GetString();
            }
            // Mistral format: {"message": "..."}
            if (root.TryGetProperty("message", out var directMsg)) return directMsg.GetString();
         } catch { }
         return json.Length > 200 ? json.Substring(0, 200) : json;
      }

      private static readonly JsonSerializerOptions SerializerOptions = new() {
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      };
   }
}
