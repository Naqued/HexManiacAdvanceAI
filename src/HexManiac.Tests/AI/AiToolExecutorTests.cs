using HavenSoft.HexManiac.Core.ViewModels.AI;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class AiToolExecutorTests {
      [Fact]
      public void Execute_UnknownTool_ReturnsError() {
         var executor = new AiToolExecutor(null);
         var toolCall = new ToolCall {
            Id = "test_1",
            Name = "nonexistent_tool",
            Arguments = "{}",
         };

         var result = executor.Execute(toolCall);
         Assert.Contains("Unknown tool", result.Content);
      }

      [Fact]
      public void Execute_InvalidJson_ReturnsError() {
         var executor = new AiToolExecutor(null);
         var toolCall = new ToolCall {
            Id = "test_1",
            Name = "navigate_to",
            Arguments = "not json",
         };

         var result = executor.Execute(toolCall);
         Assert.Contains("error", result.Content, System.StringComparison.OrdinalIgnoreCase);
      }

      [Fact]
      public void Execute_NavigateTo_NoTab_ReturnsError() {
         // EditorViewModel with no selected tab
         var executor = new AiToolExecutor(null);
         var toolCall = new ToolCall {
            Id = "test_1",
            Name = "navigate_to",
            Arguments = @"{""target"": ""0x100000""}",
         };

         var result = executor.Execute(toolCall);
         // Should handle gracefully (null editor)
         Assert.NotNull(result.Content);
      }

      [Fact]
      public void Execute_CompileScript_ReturnsProposedAction() {
         var executor = new AiToolExecutor(null);
         var toolCall = new ToolCall {
            Id = "test_1",
            Name = "compile_script",
            Arguments = @"{""script_text"": ""lock\nfaceplayer\nmsgbox @text 2\nrelease\nend""}",
         };

         var result = executor.Execute(toolCall);
         Assert.NotNull(result.ProposedAction);
         Assert.Equal("compile_script", result.ProposedAction.ToolName);
         Assert.Contains("lock", result.ProposedAction.Preview);
      }

      [Fact]
      public void Execute_WriteTableField_ReturnsProposedAction() {
         var executor = new AiToolExecutor(null);
         var toolCall = new ToolCall {
            Id = "test_1",
            Name = "write_table_field",
            Arguments = @"{""anchor"": ""data.pokemon.stats"", ""index"": 1, ""field"": ""hp"", ""value"": ""80""}",
         };

         var result = executor.Execute(toolCall);
         Assert.NotNull(result.ProposedAction);
         Assert.Equal("write_table_field", result.ProposedAction.ToolName);
         Assert.Contains("data.pokemon.stats", result.ProposedAction.Preview);
      }

      [Fact]
      public void ToolDefinitions_AllHaveRequiredFields() {
         var tools = AiToolDefinitions.GetToolDefinitions();
         Assert.True(tools.Count > 0);

         foreach (var tool in tools) {
            Assert.False(string.IsNullOrEmpty(tool.Name), "Tool name should not be empty");
            Assert.False(string.IsNullOrEmpty(tool.Description), $"Tool {tool.Name} should have a description");
            Assert.False(string.IsNullOrEmpty(tool.InputSchemaJson), $"Tool {tool.Name} should have an input schema");

            // Verify schema is valid JSON
            var doc = System.Text.Json.JsonDocument.Parse(tool.InputSchemaJson);
            Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
         }
      }

      [Fact]
      public void ToolDefinitions_HaveExpectedTools() {
         var tools = AiToolDefinitions.GetToolDefinitions();
         var names = tools.ConvertAll(t => t.Name);

         Assert.Contains("navigate_to", names);
         Assert.Contains("decompile_script", names);
         Assert.Contains("compile_script", names);
         Assert.Contains("read_table", names);
         Assert.Contains("write_table_field", names);
         Assert.Contains("find_free_space", names);
         Assert.Contains("search_anchors", names);
         Assert.Contains("read_bytes", names);
      }

      [Fact]
      public void EstimateTokens_ApproximatesCorrectly() {
         // EstimateTokens is internal static on AiToolViewModel
         Assert.Equal(0, AiToolViewModel.EstimateTokens(""));
         Assert.Equal(0, AiToolViewModel.EstimateTokens(null));
         // "hello world" = 11 chars / 4 = 2 tokens
         Assert.Equal(2, AiToolViewModel.EstimateTokens("hello world"));
         // 400 chars / 4 = 100 tokens
         Assert.Equal(100, AiToolViewModel.EstimateTokens(new string('a', 400)));
      }
   }
}
