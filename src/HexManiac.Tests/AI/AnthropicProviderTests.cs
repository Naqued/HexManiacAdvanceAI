using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class AnthropicProviderTests {
      private static StubFileSystem CreateStubFileSystem(string apiKey = null) {
         var metadata = new Dictionary<string, string[]>();
         if (apiKey != null) {
            metadata["AiSettings"] = new[] {
               "[AiSettings]",
               $"ApiKey = {apiKey}",
               "ModelName = claude-sonnet-4-20250514",
               "ProviderName = Anthropic",
               "BaseUrl = https://api.anthropic.com",
            };
         }
         var fs = new StubFileSystem {
            MetadataFor = key => metadata.TryGetValue(key, out var m) ? m : null,
            SaveMetadata = (key, data) => { metadata[key] = data; return true; },
         };
         return fs;
      }

      [Fact]
      public void Properties_ReportCorrectValues() {
         var fs = CreateStubFileSystem("test-key");
         var settings = new AiSettings(fs);
         var provider = new AnthropicProvider(settings);

         Assert.Equal("Anthropic", provider.ProviderName);
         Assert.Equal("claude-sonnet-4-20250514", provider.ModelName);
         Assert.True(provider.SupportsTools);
         Assert.False(provider.SupportsEmbeddings);
         Assert.True(provider.SupportsStreaming);
         Assert.True(provider.IsConfigured);
      }

      [Fact]
      public void IsConfigured_FalseWithoutApiKey() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         var provider = new AnthropicProvider(settings);

         Assert.False(provider.IsConfigured);
      }

      [Fact]
      public async Task CompleteAsync_ReturnsError_WhenNotConfigured() {
         var fs = CreateStubFileSystem();
         var settings = new AiSettings(fs);
         var provider = new AnthropicProvider(settings);

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
         var provider = new AnthropicProvider(settings);

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
         Assert.Contains("API key", lastDelta.FinalResponse.ErrorMessage);
      }

      [Fact]
      public async Task EmbedAsync_ReturnsEmptyArray() {
         var fs = CreateStubFileSystem("test-key");
         var settings = new AiSettings(fs);
         var provider = new AnthropicProvider(settings);

         var result = await provider.EmbedAsync("test text");
         Assert.Empty(result);
      }

      [Fact]
      public void ParseResponse_TextOnly() {
         var json = @"{
            ""id"": ""msg_test"",
            ""type"": ""message"",
            ""role"": ""assistant"",
            ""content"": [
               { ""type"": ""text"", ""text"": ""Hello, world!"" }
            ],
            ""stop_reason"": ""end_turn"",
            ""usage"": { ""input_tokens"": 10, ""output_tokens"": 5 }
         }";

         var response = AnthropicProvider.ParseResponse(json);

         Assert.Equal("Hello, world!", response.Content);
         Assert.Null(response.ToolCalls);
         Assert.Equal("end_turn", response.StopReason);
         Assert.Equal(10, response.InputTokens);
         Assert.Equal(5, response.OutputTokens);
         Assert.False(response.IsError);
      }

      [Fact]
      public void ParseResponse_WithToolCalls() {
         var json = @"{
            ""id"": ""msg_test"",
            ""type"": ""message"",
            ""role"": ""assistant"",
            ""content"": [
               { ""type"": ""text"", ""text"": ""Let me search for that."" },
               {
                  ""type"": ""tool_use"",
                  ""id"": ""toolu_123"",
                  ""name"": ""search_anchors"",
                  ""input"": { ""partial_name"": ""pokemon"" }
               }
            ],
            ""stop_reason"": ""tool_use"",
            ""usage"": { ""input_tokens"": 20, ""output_tokens"": 15 }
         }";

         var response = AnthropicProvider.ParseResponse(json);

         Assert.Equal("Let me search for that.", response.Content);
         Assert.NotNull(response.ToolCalls);
         Assert.Single(response.ToolCalls);
         Assert.Equal("toolu_123", response.ToolCalls[0].Id);
         Assert.Equal("search_anchors", response.ToolCalls[0].Name);
         Assert.Contains("pokemon", response.ToolCalls[0].Arguments);
         Assert.Equal("tool_use", response.StopReason);
      }

      [Fact]
      public void ParseResponse_MultipleTextBlocks() {
         var json = @"{
            ""content"": [
               { ""type"": ""text"", ""text"": ""First part."" },
               { ""type"": ""text"", ""text"": ""Second part."" }
            ],
            ""stop_reason"": ""end_turn"",
            ""usage"": { ""input_tokens"": 5, ""output_tokens"": 10 }
         }";

         var response = AnthropicProvider.ParseResponse(json);
         Assert.Equal("First part.\nSecond part.", response.Content);
      }

      [Fact]
      public void ParseResponse_EmptyContent() {
         var json = @"{
            ""content"": [],
            ""stop_reason"": ""end_turn"",
            ""usage"": { ""input_tokens"": 5, ""output_tokens"": 0 }
         }";

         var response = AnthropicProvider.ParseResponse(json);
         Assert.Equal(string.Empty, response.Content);
         Assert.Null(response.ToolCalls);
      }
   }
}
