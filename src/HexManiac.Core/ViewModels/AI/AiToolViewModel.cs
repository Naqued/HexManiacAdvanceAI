using HavenSoft.HexManiac.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class AiToolViewModel : ViewModelCore {
      private readonly EditorViewModel editor;
      private readonly IFileSystem fileSystem;
      private readonly IWorkDispatcher dispatcher;
      private readonly AiSettings settings;
      private readonly AiContextAssembler contextAssembler;
      private ILlmProvider provider;
      private CancellationTokenSource currentRequest;
      private readonly List<ChatMessage> conversationHistory = new();
      private IEmbeddingStore knowledgeStore;
      private string lastRomIdentity;

      public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

      private string inputText = string.Empty;
      public string InputText { get => inputText; set => Set(ref inputText, value ?? string.Empty); }

      private bool isProcessing;
      public bool IsProcessing { get => isProcessing; set => Set(ref isProcessing, value); }

      private string statusText = "Ready";
      public string StatusText { get => statusText; set => Set(ref statusText, value ?? string.Empty); }

      private bool showSettings;
      public bool ShowSettings { get => showSettings; set => Set(ref showSettings, value); }

      public AiSettings Settings => settings;

      private StubCommand sendCommand, cancelCommand, clearCommand, toggleSettingsCommand;
      public ICommand SendCommand => StubCommand(ref sendCommand, () => SendMessage(), () => !IsProcessing && !string.IsNullOrWhiteSpace(InputText));
      public ICommand CancelCommand => StubCommand(ref cancelCommand, () => CancelCurrentRequest(), () => IsProcessing);
      public ICommand ClearCommand => StubCommand(ref clearCommand, () => ClearConversation());
      public ICommand ToggleSettingsCommand => StubCommand(ref toggleSettingsCommand, () => ShowSettings = !ShowSettings);

      public AiToolViewModel(EditorViewModel editor, IFileSystem fileSystem, IWorkDispatcher dispatcher) {
         this.editor = editor;
         this.fileSystem = fileSystem;
         this.dispatcher = dispatcher;
         settings = new AiSettings(fileSystem);
         contextAssembler = new AiContextAssembler(editor);
         provider = CreateProvider();

         if (!settings.IsConfigured) showSettings = true;

         settings.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(AiSettings.ProviderName)) {
               ApplyProviderDefaults(settings.ProviderName);
               provider = CreateProvider();
            } else if (e.PropertyName == nameof(AiSettings.ApiKey)) {
               provider = CreateProvider();
            }
         };

         LoadConversation();
      }

      private ILlmProvider CreateProvider() {
         return settings.ProviderName switch {
            "Mistral" => new OpenAiCompatibleProvider(settings, "https://api.mistral.ai"),
            "OpenAI" => new OpenAiCompatibleProvider(settings, "https://api.openai.com"),
            "Ollama" => new OpenAiCompatibleProvider(settings, "http://localhost:11434"),
            _ => new AnthropicProvider(settings),
         };
      }

      private static void ApplyProviderDefaults(string providerName) {
         // Defaults are applied via the CreateProvider's base URL fallback.
         // Model names can be changed by the user in settings.
      }

      public void SendMessage() {
         if (IsProcessing || string.IsNullOrWhiteSpace(InputText)) return;
         var userText = InputText.Trim();
         InputText = string.Empty;

         EnsureKnowledgeStore();

         var userMessage = new ChatMessage(ChatRole.User, userText);
         conversationHistory.Add(userMessage);
         Messages.Add(new ChatMessageViewModel(userMessage));

         _ = SendToProviderAsync(userText);
      }

      private void EnsureKnowledgeStore() {
         var currentIdentity = GetRomIdentity();
         if (knowledgeStore != null && currentIdentity == lastRomIdentity) return;
         if (editor.Singletons != null) {
            var builder = new KnowledgeBaseBuilder(editor.Singletons);
            if (editor.SelectedTab is IViewPort vp && vp.Model != null) {
               knowledgeStore = builder.BuildFullKnowledgeBase(vp.Model);
            } else {
               knowledgeStore = builder.BuildStaticKnowledgeBase();
            }
            lastRomIdentity = currentIdentity;
         }
      }

      private string GetRomIdentity() {
         if (editor.SelectedTab is IViewPort vp && vp.Model != null) {
            return vp.Model.GetGameCode();
         }
         return null;
      }

      private void TrimConversationIfNeeded() {
         var budget = settings.MaxContextTokens;
         var total = EstimateTokens(conversationHistory);
         if (total <= budget) return;
         while (conversationHistory.Count > 4 && EstimateTokens(conversationHistory) > budget) {
            conversationHistory.RemoveAt(0);
         }
      }

      private static int EstimateTokens(List<ChatMessage> messages) {
         int total = 0;
         foreach (var m in messages) total += EstimateTokens(m.Content);
         return total;
      }

      public static int EstimateTokens(string text) {
         if (string.IsNullOrEmpty(text)) return 0;
         return text.Length / 4;
      }

      private async Task SendToProviderAsync(string userText) {
         IsProcessing = true;
         StatusText = "Thinking...";
         sendCommand?.RaiseCanExecuteChanged();
         cancelCommand?.RaiseCanExecuteChanged();

         currentRequest = new CancellationTokenSource();

         try {
            TrimConversationIfNeeded();

            var ragChunks = knowledgeStore?.Search(userText, 8);
            var ragTexts = ragChunks?.Select(c => c.Content);

            var request = new LlmRequest {
               SystemPrompt = contextAssembler.BuildSystemPrompt(ragTexts),
               Messages = conversationHistory.ToList(),
               MaxTokens = 4096,
               Temperature = 0.3,
            };

            if (editor.Singletons != null) {
               request.Tools = AiToolDefinitions.GetToolDefinitions();
            }

            if (provider.SupportsStreaming) {
               await HandleStreamingResponse(request);
            } else {
               await HandleNonStreamingResponse(request);
            }

            SaveConversation();
         } catch (OperationCanceledException) {
            Messages.Add(new ChatMessageViewModel(new ChatMessage(ChatRole.Assistant, "(Cancelled)")));
            StatusText = "Cancelled";
         } catch (Exception ex) {
            Messages.Add(new ChatMessageViewModel(new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")));
            StatusText = "Error";
         } finally {
            IsProcessing = false;
            sendCommand?.RaiseCanExecuteChanged();
            cancelCommand?.RaiseCanExecuteChanged();
            currentRequest?.Dispose();
            currentRequest = null;
         }
      }

      private async Task HandleStreamingResponse(LlmRequest request) {
         var assistantMsg = new ChatMessage { Role = ChatRole.Assistant, Content = string.Empty };
         var assistantVm = new ChatMessageViewModel(assistantMsg);
         Messages.Add(assistantVm);

         LlmResponse finalResponse = null;
         await foreach (var delta in provider.StreamCompleteAsync(request, currentRequest.Token)) {
            if (delta.IsFinal) {
               finalResponse = delta.FinalResponse;
            } else if (delta.Text != null) {
               assistantVm.AppendContent(delta.Text);
            }
         }

         if (finalResponse == null) return;

         if (finalResponse.IsError) {
            assistantVm.AppendContent($"\nError: {finalResponse.ErrorMessage}");
            StatusText = "Error";
            return;
         }

         assistantMsg.Content = finalResponse.Content;
         assistantMsg.ToolCalls = finalResponse.ToolCalls;
         conversationHistory.Add(assistantMsg);

         if (string.IsNullOrEmpty(finalResponse.Content) && finalResponse.ToolCalls?.Count > 0) {
            Messages.Remove(assistantVm);
         }

         if (finalResponse.ToolCalls != null && finalResponse.ToolCalls.Count > 0) {
            await HandleToolCallsAsync(finalResponse.ToolCalls);
         }

         var tokenCount = EstimateTokens(conversationHistory);
         StatusText = $"Ready ({tokenCount / 1000.0:F1}K tokens)";
      }

      private async Task HandleNonStreamingResponse(LlmRequest request) {
         var response = await provider.CompleteAsync(request, currentRequest.Token);

         if (response.IsError) {
            Messages.Add(new ChatMessageViewModel(new ChatMessage(ChatRole.Assistant, $"Error: {response.ErrorMessage}")));
            StatusText = "Error";
         } else {
            var assistantMessage = new ChatMessage {
               Role = ChatRole.Assistant,
               Content = response.Content,
               ToolCalls = response.ToolCalls,
            };
            conversationHistory.Add(assistantMessage);

            if (response.ToolCalls != null && response.ToolCalls.Count > 0) {
               if (!string.IsNullOrEmpty(response.Content)) {
                  Messages.Add(new ChatMessageViewModel(assistantMessage));
               }
               await HandleToolCallsAsync(response.ToolCalls);
            } else {
               Messages.Add(new ChatMessageViewModel(assistantMessage));
            }

            var tokenCount = EstimateTokens(conversationHistory);
            StatusText = $"Ready ({tokenCount / 1000.0:F1}K tokens)";
         }
      }

      private async Task HandleToolCallsAsync(List<ToolCall> toolCalls) {
         var executor = new AiToolExecutor(editor);
         foreach (var toolCall in toolCalls) {
            var result = executor.Execute(toolCall);

            Messages.Add(new ChatMessageViewModel(new ChatMessage(ChatRole.Tool, $"[{toolCall.Name}]: {result.Content}") {
               ToolCallId = toolCall.Id,
            }));

            var toolResultMessage = new ChatMessage {
               Role = ChatRole.Tool,
               Content = result.Content,
               ToolCallId = toolCall.Id,
            };
            conversationHistory.Add(toolResultMessage);

            if (result.ProposedAction != null) {
               Messages.Add(new ChatMessageViewModel(result.ProposedAction));
            }
         }

         if (toolCalls.Count > 0) {
            await SendFollowUpAsync();
         }
      }

      private async Task SendFollowUpAsync() {
         StatusText = "Processing tool results...";
         try {
            TrimConversationIfNeeded();

            var request = new LlmRequest {
               SystemPrompt = contextAssembler.BuildSystemPrompt(),
               Messages = conversationHistory.ToList(),
               MaxTokens = 4096,
               Temperature = 0.3,
            };

            if (editor.Singletons != null) {
               request.Tools = AiToolDefinitions.GetToolDefinitions();
            }

            var token = currentRequest?.Token ?? CancellationToken.None;

            if (provider.SupportsStreaming) {
               var assistantMsg = new ChatMessage { Role = ChatRole.Assistant, Content = string.Empty };
               var assistantVm = new ChatMessageViewModel(assistantMsg);
               Messages.Add(assistantVm);

               LlmResponse finalResponse = null;
               await foreach (var delta in provider.StreamCompleteAsync(request, token)) {
                  if (delta.IsFinal) {
                     finalResponse = delta.FinalResponse;
                  } else if (delta.Text != null) {
                     assistantVm.AppendContent(delta.Text);
                  }
               }

               if (finalResponse != null && !finalResponse.IsError) {
                  assistantMsg.Content = finalResponse.Content;
                  assistantMsg.ToolCalls = finalResponse.ToolCalls;
                  conversationHistory.Add(assistantMsg);

                  if (string.IsNullOrEmpty(finalResponse.Content) && finalResponse.ToolCalls?.Count > 0) {
                     Messages.Remove(assistantVm);
                  }

                  if (finalResponse.ToolCalls != null && finalResponse.ToolCalls.Count > 0) {
                     await HandleToolCallsAsync(finalResponse.ToolCalls);
                  }
               } else if (finalResponse?.IsError == true) {
                  assistantVm.AppendContent($"\nError: {finalResponse.ErrorMessage}");
               }
            } else {
               var response = await provider.CompleteAsync(request, token);

               if (response.IsError) {
                  Messages.Add(new ChatMessageViewModel(new ChatMessage(ChatRole.Assistant, $"Error: {response.ErrorMessage}")));
               } else {
                  var assistantMessage = new ChatMessage {
                     Role = ChatRole.Assistant,
                     Content = response.Content,
                     ToolCalls = response.ToolCalls,
                  };
                  conversationHistory.Add(assistantMessage);

                  if (response.ToolCalls != null && response.ToolCalls.Count > 0) {
                     if (!string.IsNullOrEmpty(response.Content)) {
                        Messages.Add(new ChatMessageViewModel(assistantMessage));
                     }
                     await HandleToolCallsAsync(response.ToolCalls);
                  } else {
                     Messages.Add(new ChatMessageViewModel(assistantMessage));
                  }
               }
            }

            SaveConversation();
         } catch (OperationCanceledException) {
            // already handled
         }
      }

      private void CancelCurrentRequest() {
         currentRequest?.Cancel();
      }

      public void ClearConversation() {
         Messages.Clear();
         conversationHistory.Clear();
         StatusText = "Ready";
         SaveConversation();
      }

      #region Conversation Persistence

      private void SaveConversation() {
         if (conversationHistory.Count == 0) {
            fileSystem.SaveMetadata("AiChat", Array.Empty<string>());
            return;
         }

         var lines = new List<string> { "[AiConversation]" };
         foreach (var msg in conversationHistory) {
            var obj = new Dictionary<string, string> {
               ["role"] = msg.Role.ToString().ToLowerInvariant(),
               ["content"] = msg.Content ?? string.Empty,
            };
            if (msg.ToolCallId != null) obj["tool_call_id"] = msg.ToolCallId;
            if (msg.ToolCalls != null && msg.ToolCalls.Count > 0) {
               obj["tool_calls"] = JsonSerializer.Serialize(msg.ToolCalls);
            }
            lines.Add(JsonSerializer.Serialize(obj));
         }

         fileSystem.SaveMetadata("AiChat", lines.ToArray());
      }

      private void LoadConversation() {
         var metadata = fileSystem.MetadataFor("AiChat");
         if (metadata == null) return;

         foreach (var line in metadata) {
            if (line.StartsWith("[") || string.IsNullOrWhiteSpace(line)) continue;
            try {
               var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(line);
               if (obj == null || !obj.ContainsKey("role")) continue;

               var role = obj["role"] switch {
                  "user" => ChatRole.User,
                  "assistant" => ChatRole.Assistant,
                  "tool" => ChatRole.Tool,
                  "system" => ChatRole.System,
                  _ => ChatRole.User,
               };

               var msg = new ChatMessage {
                  Role = role,
                  Content = obj.TryGetValue("content", out var c) ? c : string.Empty,
               };

               if (obj.TryGetValue("tool_call_id", out var tcId)) msg.ToolCallId = tcId;
               if (obj.TryGetValue("tool_calls", out var tcJson)) {
                  msg.ToolCalls = JsonSerializer.Deserialize<List<ToolCall>>(tcJson);
               }

               conversationHistory.Add(msg);
               Messages.Add(new ChatMessageViewModel(msg));
            } catch {
               // Skip malformed lines
            }
         }

         if (conversationHistory.Count > 0) {
            var tokenCount = EstimateTokens(conversationHistory);
            StatusText = $"Ready ({tokenCount / 1000.0:F1}K tokens)";
         }
      }

      #endregion

      public void Close() => editor.ShowAiPanel = false;
   }

   public class ChatMessageViewModel : ViewModelCore {
      public ChatRole Role { get; }

      private string content;
      public string Content { get => content; set => Set(ref content, value ?? string.Empty); }

      public bool IsUser => Role == ChatRole.User;
      public bool IsAssistant => Role == ChatRole.Assistant;
      public bool IsTool => Role == ChatRole.Tool;
      public ProposedAction ProposedAction { get; }
      public bool HasProposedAction => ProposedAction != null;

      public ChatMessageViewModel(ChatMessage message) {
         Role = message.Role;
         content = message.Content ?? string.Empty;
      }

      public ChatMessageViewModel(ProposedAction action) {
         Role = ChatRole.Assistant;
         content = action.Preview ?? action.Description;
         ProposedAction = action;
      }

      public void AppendContent(string delta) {
         if (string.IsNullOrEmpty(delta)) return;
         Content = (Content ?? string.Empty) + delta;
      }
   }
}
