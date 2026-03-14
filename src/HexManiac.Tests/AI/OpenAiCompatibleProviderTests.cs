using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class OpenAiCompatibleProviderTests {
      private static StubFileSystem CreateStubFileSystem(string apiKey = null) {
         var metadata = new Dictionary<string, string[]>();
         if (apiKey != null) {
            metadata["AiSettings"] = new[] {
               "[AiSettings]",
               $"ApiKey = {apiKey}",
               "ModelName = mistral-large-latest",
               "ProviderName = Mistral",
               "BaseUrl = https://api.mistral.ai",
            };
         }
         return new StubFileSystem {
            MetadataFor = key => metadata.TryGetValue(key, out var m) ? m : null,
            SaveMetadata = (key, data) => { metadata[key] = data; return true; },
         };
      }

      [Fact]
      public void Properties_ReportCorrectValues() {
         var fs = CreateStubFileSystem("test-key");
         var settings = new AiSettings(fs);
         var provider = new OpenAiCompatibleProvider(settings);

         Assert.Equal("Mistral", provider.ProviderName);
         Assert.Equal("mistral-large-latest", provider.ModelName);
         Assert.True(provider.SupportsTools);
         Assert.False(provider.SupportsEmbeddings);
         Assert.True(provider.SupportsStreaming);
         Assert.True(provider.IsConfigured);
      }

      [Fact]
      public async Task CompleteAsync_ReturnsError_WhenNotConfigured() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         var provider = new OpenAiCompatibleProvider(settings);

         var request = new LlmRequest {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "test") },
         };
         var response = await provider.CompleteAsync(request);

         Assert.True(response.IsError);
         Assert.Contains("API key", response.ErrorMessage);
      }

      [Fact]
      public async Task StreamCompleteAsync_ReturnsError_WhenNotConfigured() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         var provider = new OpenAiCompatibleProvider(settings);

         var request = new LlmRequest {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "test") },
         };

         StreamDelta lastDelta = null;
         await foreach (var delta in provider.StreamCompleteAsync(request)) {
            lastDelta = delta;
         }

         Assert.NotNull(lastDelta);
         Assert.True(lastDelta.IsFinal);
         Assert.True(lastDelta.FinalResponse.IsError);
      }

      [Fact]
      public void ParseResponse_TextOnly() {
         var json = @"{
            ""id"": ""chatcmpl-test"",
            ""object"": ""chat.completion"",
            ""choices"": [
               {
                  ""index"": 0,
                  ""message"": {
                     ""role"": ""assistant"",
                     ""content"": ""Hello from Mistral!""
                  },
                  ""finish_reason"": ""stop""
               }
            ],
            ""usage"": { ""prompt_tokens"": 12, ""completion_tokens"": 8, ""total_tokens"": 20 }
         }";

         var response = OpenAiCompatibleProvider.ParseResponse(json);

         Assert.Equal("Hello from Mistral!", response.Content);
         Assert.Null(response.ToolCalls);
         Assert.Equal("stop", response.StopReason);
         Assert.Equal(12, response.InputTokens);
         Assert.Equal(8, response.OutputTokens);
         Assert.False(response.IsError);
      }

      [Fact]
      public void ParseResponse_WithToolCalls() {
         var json = @"{
            ""choices"": [
               {
                  ""message"": {
                     ""role"": ""assistant"",
                     ""content"": null,
                     ""tool_calls"": [
                        {
                           ""id"": ""call_abc123"",
                           ""type"": ""function"",
                           ""function"": {
                              ""name"": ""search_anchors"",
                              ""arguments"": ""{\""partial_name\"": \""pokemon\""}""
                           }
                        }
                     ]
                  },
                  ""finish_reason"": ""tool_calls""
               }
            ],
            ""usage"": { ""prompt_tokens"": 20, ""completion_tokens"": 15 }
         }";

         var response = OpenAiCompatibleProvider.ParseResponse(json);

         Assert.NotNull(response.ToolCalls);
         Assert.Single(response.ToolCalls);
         Assert.Equal("call_abc123", response.ToolCalls[0].Id);
         Assert.Equal("search_anchors", response.ToolCalls[0].Name);
         Assert.Contains("pokemon", response.ToolCalls[0].Arguments);
         Assert.Equal("tool_calls", response.StopReason);
      }

      [Fact]
      public void ParseResponse_NullContent_ReturnsEmpty() {
         var json = @"{
            ""choices"": [
               {
                  ""message"": {
                     ""role"": ""assistant"",
                     ""content"": null
                  },
                  ""finish_reason"": ""stop""
               }
            ],
            ""usage"": { ""prompt_tokens"": 5, ""completion_tokens"": 0 }
         }";

         var response = OpenAiCompatibleProvider.ParseResponse(json);
         Assert.Equal(string.Empty, response.Content);
      }

      [Fact]
      public void ParseResponse_TextWithToolCalls() {
         var json = @"{
            ""choices"": [
               {
                  ""message"": {
                     ""role"": ""assistant"",
                     ""content"": ""Let me look that up."",
                     ""tool_calls"": [
                        {
                           ""id"": ""call_1"",
                           ""type"": ""function"",
                           ""function"": {
                              ""name"": ""read_table"",
                              ""arguments"": ""{\""anchor\"": \""data.pokemon.stats\""}""
                           }
                        }
                     ]
                  },
                  ""finish_reason"": ""tool_calls""
               }
            ],
            ""usage"": { ""prompt_tokens"": 10, ""completion_tokens"": 20 }
         }";

         var response = OpenAiCompatibleProvider.ParseResponse(json);

         Assert.Equal("Let me look that up.", response.Content);
         Assert.NotNull(response.ToolCalls);
         Assert.Single(response.ToolCalls);
         Assert.Equal("read_table", response.ToolCalls[0].Name);
      }

      [Fact]
      public async Task EmbedAsync_ReturnsEmptyArray() {
         var fs = CreateStubFileSystem("test-key");
         var settings = new AiSettings(fs);
         var provider = new OpenAiCompatibleProvider(settings);

         var result = await provider.EmbedAsync("test");
         Assert.Empty(result);
      }
   }
}
