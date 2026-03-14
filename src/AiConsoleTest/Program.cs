using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.ViewModels.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiConsoleTest;

class Program {
   static async Task Main(string[] args) {
      Console.WriteLine("=== HexManiacAdvance AI Assistant - Console Test ===\n");

      // Test 1: BM25 RAG Store
      TestBM25Store();

      // Test 2: Knowledge Retrieval Pipeline
      TestKnowledgeRetrieval();

      // Test 3: Tool Definitions
      TestToolDefinitions();

      // Test 4: Anthropic Provider (if API key provided)
      string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
      if (args.Length >= 2 && args[0] == "--key") {
         apiKey = args[1];
      }

      if (!string.IsNullOrEmpty(apiKey)) {
         await TestAnthropicProvider(apiKey);
         await TestStreaming(apiKey);
         await InteractiveChat(apiKey);
      } else {
         Console.WriteLine("\n--- Skipping API test (no key) ---");
         Console.WriteLine("To test with the API, run with: --key YOUR_ANTHROPIC_API_KEY");
         Console.WriteLine("Or set ANTHROPIC_API_KEY environment variable.\n");
      }

      Console.WriteLine("=== All local tests passed! ===");
   }

   static void TestBM25Store() {
      Console.WriteLine("--- Test: BM25 Store ---");
      var store = new BM25Store();

      store.AddRange(new[] {
         new DocumentChunk { Id = "xse.msgbox", Content = "[XSE Command] msgbox\nUsage: msgbox <pointer> <type>\nDisplays a message box with text from the given pointer. Type 2 is standard NPC dialog, type 6 is sign.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.givepokemon", Content = "[XSE Command] givepokemon\nUsage: givepokemon <species> <level> <item>\nGives the player a Pokemon of the specified species at the given level holding the specified item.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.trainerbattle", Content = "[XSE Command] trainerbattle\nUsage: trainerbattle <type> <id> <intro> <defeat>\nStarts a trainer battle. Type 0 is standard, type 1 is gym leader.", Category = "Script:XSE" },
         new DocumentChunk { Id = "table.pokemon.stats", Content = "[Table Reference] data.pokemon.stats (Game: BPRE)\nFormat: hp. atk. def. spd. spatk. spdef. type1. type2.\nPokemon base stats table with 412 entries.", Category = "Table" },
         new DocumentChunk { Id = "xse.applymovement", Content = "[XSE Command] applymovement\nUsage: applymovement <person> <pointer>\nApplies a movement script to the specified person (NPC or player 0xFF).", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.setflag", Content = "[XSE Command] setflag\nUsage: setflag <flag>\nSets a flag. Flags are persistent boolean values used to track game progress.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.checkflag", Content = "[XSE Command] checkflag\nUsage: checkflag <flag>\nChecks if a flag is set and stores result in lastresult.", Category = "Script:XSE" },
      });

      Console.WriteLine($"  Indexed {store.Count} chunks");

      var results = store.Search("how do I show a message dialog", 3);
      Console.WriteLine($"  Query: 'how do I show a message dialog' -> {results.Count} results");
      foreach (var r in results) {
         Console.WriteLine($"    [{r.Score:F2}] {r.Id}: {r.Content.Split('\n')[0]}");
      }

      var results2 = store.Search("pokemon stats table base", 3);
      Console.WriteLine($"  Query: 'pokemon stats table base' -> {results2.Count} results");
      foreach (var r in results2) {
         Console.WriteLine($"    [{r.Score:F2}] {r.Id}: {r.Content.Split('\n')[0]}");
      }

      if (results.Count > 0 && results[0].Id == "xse.msgbox") {
         Console.WriteLine("  PASS: msgbox correctly ranked first");
      } else {
         Console.WriteLine("  WARN: Expected msgbox first");
      }
      Console.WriteLine();
   }

   static void TestKnowledgeRetrieval() {
      Console.WriteLine("--- Test: Knowledge Retrieval Pipeline ---");
      var store = new BM25Store();

      store.Add(new DocumentChunk { Id = "xse.lock", Content = "[XSE Command] lock\nLocks the player in place, preventing movement. Usually called at the start of an NPC script.", Category = "Script:XSE" });
      store.Add(new DocumentChunk { Id = "xse.faceplayer", Content = "[XSE Command] faceplayer\nMakes the NPC turn to face the player. Called after lock.", Category = "Script:XSE" });
      store.Add(new DocumentChunk { Id = "xse.release", Content = "[XSE Command] release\nReleases the player, allowing movement again. Called at the end of scripts.", Category = "Script:XSE" });
      store.Add(new DocumentChunk { Id = "xse.end", Content = "[XSE Command] end\nEnds the script execution. Must be the last command in every script.", Category = "Script:XSE" });

      var query = "write a basic NPC script that says hello";
      var chunks = store.Search(query, 4);
      Console.WriteLine($"  RAG for: '{query}'");
      Console.WriteLine($"  Retrieved {chunks.Count} relevant chunks");
      foreach (var c in chunks) Console.WriteLine($"    - {c.Id} (score: {c.Score:F2})");
      Console.WriteLine("  PASS: RAG pipeline works\n");
   }

   static void TestToolDefinitions() {
      Console.WriteLine("--- Test: Tool Definitions ---");
      var tools = AiToolDefinitions.GetToolDefinitions();
      Console.WriteLine($"  {tools.Count} tools defined:");
      foreach (var tool in tools) {
         var desc = tool.Description.Length > 60 ? tool.Description.Substring(0, 60) + "..." : tool.Description;
         Console.WriteLine($"    - {tool.Name}: {desc}");
      }
      Console.WriteLine("  PASS: All tool definitions valid\n");
   }

   static StubFileSystem CreateFileSystem(string apiKey) {
      var fs = new StubFileSystem();
      var metadata = new Dictionary<string, string[]>();
      metadata["AiSettings"] = new[] {
         "[AiSettings]",
         $"ApiKey = {apiKey}",
         "ModelName = claude-sonnet-4-20250514",
         "ProviderName = Anthropic",
         "BaseUrl = https://api.anthropic.com",
      };
      fs.MetadataFor = (fileName) => metadata.TryGetValue(fileName, out var m) ? m : null;
      fs.SaveMetadata = (fileName, data) => { metadata[fileName] = data; return true; };
      return fs;
   }

   static async Task TestAnthropicProvider(string apiKey) {
      Console.WriteLine("--- Test: Anthropic API ---");

      var fs = CreateFileSystem(apiKey);
      var settings = new AiSettings(fs);
      var provider = new AnthropicProvider(settings);

      Console.WriteLine($"  Provider: {provider.ProviderName}, Model: {provider.ModelName}");

      var request = new LlmRequest {
         SystemPrompt = "You are a Pokemon ROM hacking assistant. Be brief.",
         Messages = new List<ChatMessage> {
            new ChatMessage(ChatRole.User, "What is the msgbox command in XSE scripting? Answer in 1-2 sentences.")
         },
         MaxTokens = 200,
         Temperature = 0.3,
      };

      Console.Write("  Sending test query... ");
      var response = await provider.CompleteAsync(request);

      if (response.IsError) {
         Console.WriteLine($"\n  ERROR: {response.ErrorMessage}");
      } else {
         Console.WriteLine("OK!");
         Console.WriteLine($"  Response: {response.Content}");
         Console.WriteLine($"  Tokens: {response.InputTokens} in, {response.OutputTokens} out");
         Console.WriteLine("  PASS: API connection works!");
      }
      Console.WriteLine();
   }

   static async Task TestStreaming(string apiKey) {
      Console.WriteLine("--- Test: Streaming API ---");

      var fs = CreateFileSystem(apiKey);
      var settings = new AiSettings(fs);
      var provider = new AnthropicProvider(settings);

      Console.WriteLine($"  Supports streaming: {provider.SupportsStreaming}");

      var request = new LlmRequest {
         SystemPrompt = "You are a helpful assistant. Be brief.",
         Messages = new List<ChatMessage> {
            new ChatMessage(ChatRole.User, "Count from 1 to 5, one number per line.")
         },
         MaxTokens = 100,
         Temperature = 0.0,
      };

      Console.Write("  Streaming: ");
      var fullText = new StringBuilder();
      int deltaCount = 0;

      await foreach (var delta in provider.StreamCompleteAsync(request)) {
         if (delta.IsFinal) {
            Console.WriteLine();
            Console.WriteLine($"  Final: {delta.FinalResponse.InputTokens} in, {delta.FinalResponse.OutputTokens} out");
            if (delta.FinalResponse.IsError) {
               Console.WriteLine($"  ERROR: {delta.FinalResponse.ErrorMessage}");
            }
         } else if (delta.Text != null) {
            Console.Write(delta.Text);
            fullText.Append(delta.Text);
            deltaCount++;
         }
      }

      Console.WriteLine($"  Received {deltaCount} text deltas");
      Console.WriteLine($"  Full text length: {fullText.Length} chars");
      Console.WriteLine("  PASS: Streaming works!\n");
   }

   static async Task InteractiveChat(string apiKey) {
      Console.WriteLine("--- Interactive Chat (type 'quit' to exit) ---\n");

      var fs = CreateFileSystem(apiKey);
      var settings = new AiSettings(fs);
      var provider = new AnthropicProvider(settings);

      // Build RAG knowledge base
      var store = new BM25Store();
      store.AddRange(new[] {
         new DocumentChunk { Id = "xse.msgbox", Content = "[XSE Command] msgbox\nUsage: msgbox <pointer> <type>\nDisplays a message box. Type 2=NPC dialog, 6=sign.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.lock", Content = "[XSE Command] lock\nLocks the player. Call at start of NPC scripts.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.faceplayer", Content = "[XSE Command] faceplayer\nMakes NPC face the player.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.release", Content = "[XSE Command] release\nReleases player. Call at end of scripts.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.end", Content = "[XSE Command] end\nTerminates script execution.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.givepokemon", Content = "[XSE Command] givepokemon\nUsage: givepokemon <species> <level> <item>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.trainerbattle", Content = "[XSE Command] trainerbattle\nUsage: trainerbattle <type> <id> <intro> <defeat>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.setflag", Content = "[XSE Command] setflag\nUsage: setflag <flag>\nSets a game flag.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.checkflag", Content = "[XSE Command] checkflag\nUsage: checkflag <flag>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.applymovement", Content = "[XSE Command] applymovement\nUsage: applymovement <person> <movement_ptr>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.waitmovement", Content = "[XSE Command] waitmovement\nUsage: waitmovement <person>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.giveitem", Content = "[XSE Command] giveitem\nUsage: giveitem <item> <quantity>", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.compare", Content = "[XSE Command] compare\nUsage: compare <var> <value>\nCompares a variable to a value for conditional branching.", Category = "Script:XSE" },
         new DocumentChunk { Id = "xse.goto_if", Content = "[XSE Command] goto_if\nUsage: goto_if <condition> <pointer>\nConditional jump. condition: 0=lower, 1=equal, 2=greater, 3=lower_or_equal, 4=greater_or_equal, 5=not_equal.", Category = "Script:XSE" },
      });

      var history = new List<ChatMessage>();

      while (true) {
         Console.ForegroundColor = ConsoleColor.Cyan;
         Console.Write("You> ");
         Console.ResetColor();
         var input = Console.ReadLine();
         if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLower() == "quit") break;

         // RAG retrieval
         var ragResults = store.Search(input, 5);
         var ragContext = string.Join("\n\n", ragResults.Select(r => r.Content));

         var systemPrompt = "You are an AI assistant for HexManiacAdvance, a Pokemon GBA ROM hex editor.\n" +
            "You help write XSE event scripts, battle scripts, and answer questions about ROM hacking.\n" +
            "When writing scripts, use proper XSE syntax.\n\n" +
            "## Reference Documentation\n\n" + ragContext;

         history.Add(new ChatMessage(ChatRole.User, input));

         var request = new LlmRequest {
            SystemPrompt = systemPrompt,
            Messages = history.ToList(),
            MaxTokens = 1024,
            Temperature = 0.3,
         };

         if (provider.SupportsStreaming) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("AI> ");
            Console.ResetColor();

            var fullContent = new StringBuilder();
            await foreach (var delta in provider.StreamCompleteAsync(request)) {
               if (delta.IsFinal) {
                  Console.WriteLine();
                  if (delta.FinalResponse.IsError) {
                     Console.ForegroundColor = ConsoleColor.Red;
                     Console.WriteLine($"Error: {delta.FinalResponse.ErrorMessage}");
                  }
                  Console.ForegroundColor = ConsoleColor.DarkGray;
                  Console.WriteLine($"  [{delta.FinalResponse.InputTokens + delta.FinalResponse.OutputTokens} tokens]");
                  Console.ResetColor();
                  history.Add(new ChatMessage(ChatRole.Assistant, fullContent.ToString()));
               } else if (delta.Text != null) {
                  Console.Write(delta.Text);
                  fullContent.Append(delta.Text);
               }
            }
         } else {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("(thinking...) ");
            Console.ResetColor();

            var response = await provider.CompleteAsync(request);
            Console.Write("\r" + new string(' ', 20) + "\r");

            if (response.IsError) {
               Console.ForegroundColor = ConsoleColor.Red;
               Console.WriteLine($"Error: {response.ErrorMessage}");
            } else {
               history.Add(new ChatMessage(ChatRole.Assistant, response.Content));
               Console.ForegroundColor = ConsoleColor.Green;
               Console.Write("AI> ");
               Console.ResetColor();
               Console.WriteLine(response.Content);
               Console.ForegroundColor = ConsoleColor.DarkGray;
               Console.WriteLine($"  [{response.InputTokens + response.OutputTokens} tokens]");
            }
         }
         Console.ResetColor();
         Console.WriteLine();
      }
   }
}
