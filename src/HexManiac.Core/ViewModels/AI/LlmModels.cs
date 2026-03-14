using System.Collections.Generic;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public enum ChatRole { System, User, Assistant, Tool }

   public class ChatMessage {
      public ChatRole Role { get; set; }
      public string Content { get; set; }
      public string ToolCallId { get; set; }
      public List<ToolCall> ToolCalls { get; set; }

      public ChatMessage() { }

      public ChatMessage(ChatRole role, string content) {
         Role = role;
         Content = content;
      }
   }

   public class ToolCall {
      public string Id { get; set; }
      public string Name { get; set; }
      public string Arguments { get; set; }
   }

   public class ToolDefinition {
      public string Name { get; set; }
      public string Description { get; set; }
      public string InputSchemaJson { get; set; }
   }

   public class LlmRequest {
      public string SystemPrompt { get; set; }
      public List<ChatMessage> Messages { get; set; } = new();
      public List<ToolDefinition> Tools { get; set; }
      public double Temperature { get; set; } = 0.3;
      public int MaxTokens { get; set; } = 4096;
   }

   public class LlmResponse {
      public string Content { get; set; }
      public List<ToolCall> ToolCalls { get; set; }
      public string StopReason { get; set; }
      public int InputTokens { get; set; }
      public int OutputTokens { get; set; }
      public string ErrorMessage { get; set; }
      public bool IsError => ErrorMessage != null;
   }

   public class StreamDelta {
      public string Text { get; set; }
      public bool IsFinal { get; set; }
      public LlmResponse FinalResponse { get; set; }
   }

   public class ProposedAction {
      public string Description { get; set; }
      public string ToolName { get; set; }
      public string Arguments { get; set; }
      public string Preview { get; set; }
      public bool IsApplied { get; set; }
      public bool IsRejected { get; set; }
   }
}
