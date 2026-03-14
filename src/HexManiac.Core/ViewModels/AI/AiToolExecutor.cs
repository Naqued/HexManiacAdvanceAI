using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class ToolExecutionResult {
      public string Content { get; set; }
      public ProposedAction ProposedAction { get; set; }
   }

   public class AiToolExecutor {
      private readonly EditorViewModel editor;

      public AiToolExecutor(EditorViewModel editor) {
         this.editor = editor;
      }

      public ToolExecutionResult Execute(ToolCall toolCall) {
         try {
            using var doc = JsonDocument.Parse(toolCall.Arguments);
            var args = doc.RootElement;

            return toolCall.Name switch {
               "navigate_to" => ExecuteNavigateTo(args),
               "decompile_script" => ExecuteDecompileScript(args),
               "compile_script" => ProposeCompileScript(args),
               "read_table" => ExecuteReadTable(args),
               "write_table_field" => ProposeWriteTableField(args),
               "find_free_space" => ExecuteFindFreeSpace(args),
               "search_anchors" => ExecuteSearchAnchors(args),
               "read_bytes" => ExecuteReadBytes(args),
               _ => new ToolExecutionResult { Content = $"Unknown tool: {toolCall.Name}" },
            };
         } catch (Exception ex) {
            return new ToolExecutionResult { Content = $"Tool execution error: {ex.Message}" };
         }
      }

      private ToolExecutionResult ExecuteNavigateTo(JsonElement args) {
         var target = args.GetProperty("target").GetString();
         if (editor.SelectedTab is ViewPort vp) {
            vp.Goto.Execute(target);
            return new ToolExecutionResult { Content = $"Navigated to {target}" };
         }
         return new ToolExecutionResult { Content = "No active tab to navigate." };
      }

      private ToolExecutionResult ExecuteDecompileScript(JsonElement args) {
         var addressStr = args.GetProperty("address").GetString();
         if (!TryParseAddress(addressStr, out int address)) {
            return new ToolExecutionResult { Content = $"Invalid address: {addressStr}" };
         }

         if (editor.SelectedTab is not ViewPort vp) {
            return new ToolExecutionResult { Content = "No active tab." };
         }

         var codeTool = vp.Tools?.CodeTool;
         if (codeTool == null) {
            return new ToolExecutionResult { Content = "Code tool not available." };
         }

         var scriptType = "xse";
         if (args.TryGetProperty("script_type", out var st)) scriptType = st.GetString();

         var parser = scriptType switch {
            "bse" => codeTool.BattleScriptParser,
            "ase" => codeTool.AnimationScriptParser,
            "tse" => codeTool.BattleAIScriptParser,
            _ => codeTool.ScriptParser,
         };

         var scripts = parser.CollectScripts(vp.Model, address);
         if (scripts == null || scripts.Count == 0) {
            return new ToolExecutionResult { Content = $"No script found at 0x{address:X6}" };
         }

         var sectionCount = 0;
         var length = parser.GetScriptSegmentLength(vp.Model, address);
         var result = parser.Parse(vp.Model, address, length, ref sectionCount);
         return new ToolExecutionResult { Content = result ?? $"No script at 0x{address:X6}" };
      }

      private ToolExecutionResult ProposeCompileScript(JsonElement args) {
         var scriptText = args.GetProperty("script_text").GetString();
         return new ToolExecutionResult {
            Content = $"Script compilation proposed. Script text:\n{scriptText}",
            ProposedAction = new ProposedAction {
               ToolName = "compile_script",
               Description = "Compile and insert script into ROM",
               Arguments = args.GetRawText(),
               Preview = scriptText,
            },
         };
      }

      private ToolExecutionResult ExecuteReadTable(JsonElement args) {
         var anchor = args.GetProperty("anchor").GetString();
         int? index = args.TryGetProperty("index", out var idx) ? idx.GetInt32() : null;

         if (editor.SelectedTab is not IEditableViewPort vp) {
            return new ToolExecutionResult { Content = "No active tab." };
         }

         var model = vp.Model;
         var address = model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, anchor);
         if (address == Pointer.NULL) {
            return new ToolExecutionResult { Content = $"Anchor '{anchor}' not found." };
         }

         var run = model.GetNextRun(address);
         if (run is not ITableRun table) {
            return new ToolExecutionResult { Content = $"'{anchor}' is not a table." };
         }

         var sb = new StringBuilder();
         sb.AppendLine($"Table: {anchor} ({table.ElementCount} entries, {table.ElementLength} bytes each)");

         if (index.HasValue) {
            if (index.Value < 0 || index.Value >= table.ElementCount) {
               return new ToolExecutionResult { Content = $"Index {index.Value} out of range (0-{table.ElementCount - 1})" };
            }
            AppendTableElement(sb, model, table, index.Value);
         } else {
            var count = Math.Min(table.ElementCount, 10);
            for (int i = 0; i < count; i++) {
               AppendTableElement(sb, model, table, i);
            }
            if (table.ElementCount > 10) {
               sb.AppendLine($"... and {table.ElementCount - 10} more entries");
            }
         }

         return new ToolExecutionResult { Content = sb.ToString() };
      }

      private static void AppendTableElement(StringBuilder sb, IDataModel model, ITableRun table, int index) {
         sb.Append($"  [{index}]: ");
         var elementStart = table.Start + index * table.ElementLength;
         int segmentOffset = 0;
         foreach (var segment in table.ElementContent) {
            sb.Append($"{segment.Name}=");
            if (segment.Type == ElementContentType.Integer) {
               sb.Append(model.ReadMultiByteValue(elementStart + segmentOffset, segment.Length));
            } else if (segment.Type == ElementContentType.PCS) {
               sb.Append(model.TextConverter?.Convert(model, elementStart + segmentOffset, segment.Length)?.Trim() ?? "?");
            } else if (segment.Type == ElementContentType.Pointer) {
               var ptr = model.ReadPointer(elementStart + segmentOffset);
               sb.Append($"<{ptr:X6}>");
            } else {
               sb.Append("...");
            }
            sb.Append(" ");
            segmentOffset += segment.Length;
         }
         sb.AppendLine();
      }

      private ToolExecutionResult ProposeWriteTableField(JsonElement args) {
         var anchor = args.GetProperty("anchor").GetString();
         var index = args.GetProperty("index").GetInt32();
         var field = args.GetProperty("field").GetString();
         var value = args.GetProperty("value").GetString();

         return new ToolExecutionResult {
            Content = $"Proposed: write {anchor}[{index}].{field} = {value}",
            ProposedAction = new ProposedAction {
               ToolName = "write_table_field",
               Description = $"Set {anchor}[{index}].{field} = {value}",
               Arguments = args.GetRawText(),
               Preview = $"{anchor}[{index}].{field} = {value}",
            },
         };
      }

      private ToolExecutionResult ExecuteFindFreeSpace(JsonElement args) {
         var length = args.GetProperty("length").GetInt32();
         if (editor.SelectedTab is not IEditableViewPort vp) {
            return new ToolExecutionResult { Content = "No active tab." };
         }

         var address = vp.Model.FindFreeSpace(0, length);
         if (address < 0) {
            return new ToolExecutionResult { Content = $"Could not find {length} bytes of free space." };
         }
         return new ToolExecutionResult { Content = $"Found {length} bytes of free space at 0x{address:X6}" };
      }

      private ToolExecutionResult ExecuteSearchAnchors(JsonElement args) {
         var partialName = args.GetProperty("partial_name").GetString();
         if (editor.SelectedTab is not IEditableViewPort vp) {
            return new ToolExecutionResult { Content = "No active tab." };
         }

         var results = vp.Model.GetAutoCompleteAnchorNameOptions(partialName, 20).ToList();
         if (results.Count == 0) {
            return new ToolExecutionResult { Content = $"No anchors matching '{partialName}'." };
         }

         var sb = new StringBuilder();
         sb.AppendLine($"Found {results.Count} matching anchors:");
         foreach (var name in results) {
            var addr = vp.Model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, name);
            sb.AppendLine($"  {name} @ 0x{addr:X6}");
         }
         return new ToolExecutionResult { Content = sb.ToString() };
      }

      private ToolExecutionResult ExecuteReadBytes(JsonElement args) {
         var addressStr = args.GetProperty("address").GetString();
         var length = Math.Min(args.GetProperty("length").GetInt32(), 256);

         if (!TryParseAddress(addressStr, out int address)) {
            return new ToolExecutionResult { Content = $"Invalid address: {addressStr}" };
         }

         if (editor.SelectedTab is not IEditableViewPort vp) {
            return new ToolExecutionResult { Content = "No active tab." };
         }

         if (address < 0 || address + length > vp.Model.Count) {
            return new ToolExecutionResult { Content = $"Address range 0x{address:X6}+{length} out of bounds." };
         }

         var sb = new StringBuilder();
         for (int i = 0; i < length; i++) {
            if (i > 0 && i % 16 == 0) sb.AppendLine();
            sb.Append(vp.Model[address + i].ToString("X2"));
            if (i < length - 1) sb.Append(' ');
         }
         return new ToolExecutionResult { Content = sb.ToString() };
      }

      private static bool TryParseAddress(string addressStr, out int address) {
         address = 0;
         if (string.IsNullOrEmpty(addressStr)) return false;
         addressStr = addressStr.Trim();
         if (addressStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            addressStr = addressStr.Substring(2);
         }
         return int.TryParse(addressStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
      }
   }
}
