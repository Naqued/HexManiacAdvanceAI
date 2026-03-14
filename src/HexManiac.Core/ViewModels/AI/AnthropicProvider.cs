using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class AnthropicProvider : ILlmProvider {
      private static readonly HttpClient httpClient = new();
      private readonly AiSettings settings;

      public string ProviderName => "Anthropic";
      public string ModelName => settings.ModelName;
      public bool SupportsTools => true;
      public bool SupportsEmbeddings => false;
      public bool SupportsStreaming => true;
      public bool IsConfigured => settings.IsConfigured;

      public AnthropicProvider(AiSettings settings) {
         this.settings = settings;
      }

      public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default) {
         if (!IsConfigured) return new LlmResponse { ErrorMessage = "API key not configured. Set your Anthropic API key in AI settings." };

         try {
            var body = BuildRequestBody(request);
            var json = JsonSerializer.Serialize(body, SerializerOptions);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl}/v1/messages");
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            httpRequest.Headers.Add("x-api-key", settings.ApiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

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
            yield return new StreamDelta {
               IsFinal = true,
               FinalResponse = new LlmResponse { ErrorMessage = "API key not configured. Set your Anthropic API key in AI settings." }
            };
            yield break;
         }

         // Send the HTTP request outside an iterator-friendly structure
         // (C# does not allow yield inside try-catch)
         var sendResult = await SendStreamingRequestAsync(request, cancellationToken);
         if (sendResult.Error != null) {
            yield return new StreamDelta { IsFinal = true, FinalResponse = sendResult.Error };
            yield break;
         }

         var stream = await sendResult.Response.Content.ReadAsStreamAsync();
         using var reader = new StreamReader(stream);

         var textContent = new StringBuilder();
         var toolCalls = new List<ToolCall>();
         int inputTokens = 0, outputTokens = 0;
         string stopReason = null;
         string currentToolId = null, currentToolName = null;
         var currentToolJson = new StringBuilder();

         while (!reader.EndOfStream) {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            // Parse SSE event without try-catch (to allow yield below)
            string textDelta = ParseSseEvent(data, ref inputTokens, ref outputTokens, ref stopReason,
               ref currentToolId, ref currentToolName, currentToolJson, toolCalls, textContent);

            if (textDelta != null) {
               yield return new StreamDelta { Text = textDelta };
            }
         }

         yield return new StreamDelta {
            IsFinal = true,
            FinalResponse = new LlmResponse {
               Content = textContent.ToString(),
               ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
               StopReason = stopReason,
               InputTokens = inputTokens,
               OutputTokens = outputTokens,
            }
         };
      }

      private async Task<(HttpResponseMessage Response, LlmResponse Error)> SendStreamingRequestAsync(
            LlmRequest request, CancellationToken cancellationToken) {
         try {
            var body = BuildRequestBody(request);
            body["stream"] = true;
            var json = JsonSerializer.Serialize(body, SerializerOptions);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl}/v1/messages");
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            httpRequest.Headers.Add("x-api-key", settings.ApiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

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

      private static string ParseSseEvent(string data,
            ref int inputTokens, ref int outputTokens, ref string stopReason,
            ref string currentToolId, ref string currentToolName,
            StringBuilder currentToolJson, List<ToolCall> toolCalls, StringBuilder textContent) {
         string textDelta = null;
         JsonDocument doc = null;
         try {
            doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type) {
               case "message_start":
                  if (root.TryGetProperty("message", out var msg) &&
                      msg.TryGetProperty("usage", out var startUsage) &&
                      startUsage.TryGetProperty("input_tokens", out var it)) {
                     inputTokens = it.GetInt32();
                  }
                  break;

               case "content_block_start":
                  if (root.TryGetProperty("content_block", out var cb)) {
                     var cbType = cb.GetProperty("type").GetString();
                     if (cbType == "tool_use") {
                        currentToolId = cb.GetProperty("id").GetString();
                        currentToolName = cb.GetProperty("name").GetString();
                        currentToolJson.Clear();
                     }
                  }
                  break;

               case "content_block_delta":
                  if (root.TryGetProperty("delta", out var delta)) {
                     var deltaType = delta.GetProperty("type").GetString();
                     if (deltaType == "text_delta") {
                        textDelta = delta.GetProperty("text").GetString();
                        textContent.Append(textDelta);
                     } else if (deltaType == "input_json_delta") {
                        var partialJson = delta.GetProperty("partial_json").GetString();
                        currentToolJson.Append(partialJson);
                     }
                  }
                  break;

               case "content_block_stop":
                  if (currentToolId != null) {
                     toolCalls.Add(new ToolCall {
                        Id = currentToolId,
                        Name = currentToolName,
                        Arguments = currentToolJson.Length > 0 ? currentToolJson.ToString() : "{}",
                     });
                     currentToolId = null;
                     currentToolName = null;
                     currentToolJson.Clear();
                  }
                  break;

               case "message_delta":
                  if (root.TryGetProperty("delta", out var msgDelta)) {
                     if (msgDelta.TryGetProperty("stop_reason", out var sr)) stopReason = sr.GetString();
                  }
                  if (root.TryGetProperty("usage", out var deltaUsage)) {
                     if (deltaUsage.TryGetProperty("output_tokens", out var ot)) outputTokens = ot.GetInt32();
                  }
                  break;
            }
         } catch {
            // Skip malformed SSE events
         } finally {
            doc?.Dispose();
         }
         return textDelta;
      }

      public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) {
         return Task.FromResult(Array.Empty<float>());
      }

      private Dictionary<string, object> BuildRequestBody(LlmRequest request) {
         var messages = new List<object>();
         foreach (var msg in request.Messages) {
            if (msg.Role == ChatRole.System) continue;
            var role = msg.Role switch {
               ChatRole.User => "user",
               ChatRole.Assistant => "assistant",
               ChatRole.Tool => "user",
               _ => "user",
            };

            if (msg.Role == ChatRole.Tool && msg.ToolCallId != null) {
               messages.Add(new {
                  role = "user",
                  content = new object[] {
                     new { type = "tool_result", tool_use_id = msg.ToolCallId, content = msg.Content }
                  }
               });
            } else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0) {
               var content = new List<object>();
               if (!string.IsNullOrEmpty(msg.Content)) {
                  content.Add(new { type = "text", text = msg.Content });
               }
               foreach (var tc in msg.ToolCalls) {
                  content.Add(new {
                     type = "tool_use",
                     id = tc.Id,
                     name = tc.Name,
                     input = JsonSerializer.Deserialize<JsonElement>(tc.Arguments)
                  });
               }
               messages.Add(new { role, content });
            } else {
               messages.Add(new { role, content = msg.Content });
            }
         }

         var body = new Dictionary<string, object> {
            ["model"] = settings.ModelName,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages,
         };

         if (!string.IsNullOrEmpty(request.SystemPrompt)) {
            body["system"] = request.SystemPrompt;
         }

         if (request.Temperature > 0) {
            body["temperature"] = request.Temperature;
         }

         if (request.Tools != null && request.Tools.Count > 0) {
            body["tools"] = request.Tools.Select(t => new {
               name = t.Name,
               description = t.Description,
               input_schema = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson),
            }).ToList();
         }

         return body;
      }

      public static LlmResponse ParseResponse(string json) {
         using var doc = JsonDocument.Parse(json);
         var root = doc.RootElement;

         var response = new LlmResponse {
            StopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null,
         };

         if (root.TryGetProperty("usage", out var usage)) {
            if (usage.TryGetProperty("input_tokens", out var it)) response.InputTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) response.OutputTokens = ot.GetInt32();
         }

         if (root.TryGetProperty("content", out var content)) {
            var textParts = new List<string>();
            var toolCalls = new List<ToolCall>();

            foreach (var block in content.EnumerateArray()) {
               var type = block.GetProperty("type").GetString();
               if (type == "text") {
                  textParts.Add(block.GetProperty("text").GetString());
               } else if (type == "tool_use") {
                  toolCalls.Add(new ToolCall {
                     Id = block.GetProperty("id").GetString(),
                     Name = block.GetProperty("name").GetString(),
                     Arguments = block.GetProperty("input").GetRawText(),
                  });
               }
            }

            response.Content = string.Join("\n", textParts);
            if (toolCalls.Count > 0) response.ToolCalls = toolCalls;
         }

         return response;
      }

      private static string ExtractErrorMessage(string json) {
         try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error)) {
               if (error.TryGetProperty("message", out var msg)) return msg.GetString();
            }
         } catch { }
         return json.Length > 200 ? json.Substring(0, 200) : json;
      }

      private static readonly JsonSerializerOptions SerializerOptions = new() {
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      };
   }
}
