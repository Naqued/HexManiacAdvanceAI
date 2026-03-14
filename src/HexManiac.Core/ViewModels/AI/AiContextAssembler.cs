using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels.Tools;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class AiContextAssembler {
      private readonly EditorViewModel editor;

      public AiContextAssembler(EditorViewModel editor) {
         this.editor = editor;
      }

      public string BuildSystemPrompt(IEnumerable<string> ragChunks = null) {
         var sb = new StringBuilder();

         sb.AppendLine("You are an AI assistant for HexManiacAdvance, a Pokemon GBA ROM hex editor.");
         sb.AppendLine("You help with ROM hacking: writing event scripts (XSE), battle scripts (BSE), animation scripts (ASE), trainer AI scripts (TSE), editing tables, managing memory, and understanding ROM data layout.");
         sb.AppendLine("When proposing scripts or data changes, use the provided tools. For write operations, clearly explain what will change before executing.");
         sb.AppendLine();

         AppendRomContext(sb);
         AppendEditorState(sb);
         AppendActiveToolContext(sb);

         if (ragChunks != null) {
            var chunks = ragChunks.ToList();
            if (chunks.Count > 0) {
               sb.AppendLine("## Reference Documentation");
               sb.AppendLine();
               foreach (var chunk in chunks) {
                  sb.AppendLine(chunk);
                  sb.AppendLine();
               }
            }
         }

         return sb.ToString();
      }

      private void AppendRomContext(StringBuilder sb) {
         if (editor.SelectedTab is not IViewPort vp) return;
         var model = vp.Model;
         if (model == null) return;

         sb.AppendLine("## ROM Context");
         var gameCode = model.GetGameCode();
         if (!string.IsNullOrEmpty(gameCode)) {
            sb.AppendLine($"- Game: {GameCodeToName(gameCode)} ({gameCode})");
         }
         sb.AppendLine($"- ROM size: {model.Count:N0} bytes ({model.Count / 1024}KB)");
         sb.AppendLine();
      }

      private void AppendEditorState(StringBuilder sb) {
         if (editor.SelectedTab is not ViewPort vp) return;
         var model = vp.Model;

         sb.AppendLine("## Current Editor State");

         var address = vp.ConvertViewPointToAddress(vp.SelectionStart);
         sb.AppendLine($"- Selected address: 0x{address:X6}");

         var anchorName = model.GetAnchorFromAddress(-1, address);
         if (!string.IsNullOrEmpty(anchorName)) {
            sb.AppendLine($"- Anchor at cursor: {anchorName}");
         }

         var run = model.GetNextRun(address);
         if (run != null && run.Start <= address) {
            sb.AppendLine($"- Data type at cursor: {run.GetType().Name}");
            if (run is ITableRun table) {
               var tableName = model.GetAnchorFromAddress(-1, table.Start);
               sb.AppendLine($"- Table: {tableName} ({table.ElementCount} entries, {table.ElementLength} bytes each)");
            }
         }

         sb.AppendLine();
      }

      private void AppendActiveToolContext(StringBuilder sb) {
         if (editor.SelectedTab is not IViewPort vp) return;
         var tools = vp.Tools;
         if (tools == null) return;

         var selectedTool = tools.SelectedTool;
         if (selectedTool == null) return;

         sb.AppendLine($"## Active Tool: {selectedTool.Name}");

         if (selectedTool is CodeTool codeTool && codeTool.Contents.Count > 0) {
            var scriptText = codeTool.Contents[0].Content;
            if (!string.IsNullOrEmpty(scriptText)) {
               sb.AppendLine("Current script:");
               sb.AppendLine("```");
               // Limit script text to avoid overwhelming context
               var lines = scriptText.Split('\n');
               var truncated = lines.Length > 100;
               foreach (var line in lines.Take(100)) sb.AppendLine(line);
               if (truncated) sb.AppendLine("... (truncated)");
               sb.AppendLine("```");
            }
         } else if (selectedTool is TableTool) {
            sb.AppendLine("- Table tool is active");
         }

         sb.AppendLine();
      }

      private static string GameCodeToName(string code) {
         if (code.StartsWith("BPRE")) return "Pokemon FireRed";
         if (code.StartsWith("BPGE")) return "Pokemon LeafGreen";
         if (code.StartsWith("BPEE")) return "Pokemon Emerald";
         if (code.StartsWith("AXVE")) return "Pokemon Ruby";
         if (code.StartsWith("AXPE")) return "Pokemon Sapphire";
         return "Unknown Pokemon GBA ROM";
      }
   }
}
