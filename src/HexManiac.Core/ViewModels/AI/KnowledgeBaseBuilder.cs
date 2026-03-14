using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Code;
using HavenSoft.HexManiac.Core.Models.Runs;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class KnowledgeBaseBuilder {
      private readonly Singletons singletons;

      public KnowledgeBaseBuilder(Singletons singletons) {
         this.singletons = singletons;
      }

      public IEmbeddingStore BuildStaticKnowledgeBase() {
         var store = new BM25Store();
         store.AddRange(ChunkScriptLines("XSE", singletons.ScriptLines));
         store.AddRange(ChunkScriptLines("BSE", singletons.BattleScriptLines));
         store.AddRange(ChunkScriptLines("ASE", singletons.AnimationScriptLines));
         store.AddRange(ChunkScriptLines("TSE", singletons.BattleAIScriptLines));
         store.AddRange(ChunkThumbInstructions());
         store.AddRange(ChunkGameReferenceTables());
         return store;
      }

      private IEnumerable<DocumentChunk> ChunkScriptLines(string scriptType, IReadOnlyList<IScriptLine> lines) {
         foreach (var line in lines) {
            if (string.IsNullOrEmpty(line.LineCommand)) continue;
            var sb = new StringBuilder();
            sb.AppendLine($"[{scriptType} Command] {line.LineCommand}");
            if (!string.IsNullOrEmpty(line.Usage)) {
               sb.AppendLine($"Usage: {line.Usage}");
            }
            if (line.Args != null && line.Args.Count > 0) {
               sb.Append("Arguments: ");
               sb.AppendLine(string.Join(", ", line.Args.Select(a => a.Name)));
            }
            if (line.Documentation != null) {
               foreach (var doc in line.Documentation) {
                  sb.AppendLine(doc);
               }
            }
            if (line.IsEndingCommand) sb.AppendLine("(This is an ending/terminator command)");

            yield return new DocumentChunk {
               Id = $"{scriptType}.{line.LineCommand}",
               Content = sb.ToString(),
               Category = $"Script:{scriptType}",
            };
         }
      }

      private IEnumerable<DocumentChunk> ChunkThumbInstructions() {
         if (singletons.ThumbInstructionTemplates == null) yield break;
         foreach (var instr in singletons.ThumbInstructionTemplates) {
            yield return new DocumentChunk {
               Id = $"ARM.{instr}",
               Content = $"[ARM/Thumb Instruction] {instr}",
               Category = "ARM",
            };
         }
      }

      private IEnumerable<DocumentChunk> ChunkGameReferenceTables() {
         if (singletons.GameReferenceTables == null) yield break;
         foreach (var (gameCode, tables) in singletons.GameReferenceTables) {
            if (tables == null) continue;
            foreach (var table in tables) {
               yield return new DocumentChunk {
                  Id = $"Table.{gameCode}.{table.Name}",
                  Content = $"[Table Reference] {table.Name} (Game: {gameCode})\nFormat: {table.Format}\nAddress: 0x{table.Address:X6}",
                  Category = "Table",
               };
            }
         }
      }

      public IEmbeddingStore BuildFullKnowledgeBase(IDataModel model) {
         var store = BuildStaticKnowledgeBase();
         IndexRomData(model, store);
         return store;
      }

      public void IndexRomData(IDataModel model, IEmbeddingStore store) {
         // Index all named anchors
         var anchors = model.GetAutoCompleteAnchorNameOptions(string.Empty, 500);
         foreach (var anchorName in anchors) {
            var address = model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, anchorName);
            if (address < 0) continue;

            var run = model.GetNextRun(address);
            var sb = new StringBuilder();
            sb.AppendLine($"[ROM Anchor] {anchorName} @ 0x{address:X6}");
            sb.AppendLine($"Run type: {run?.GetType().Name ?? "unknown"}");

            if (run is ITableRun table) {
               sb.AppendLine($"Table with {table.ElementCount} entries, {table.ElementLength} bytes each");
               if (table.ElementContent != null) {
                  sb.Append("Fields: ");
                  sb.AppendLine(string.Join(", ", table.ElementContent.Select(s => $"{s.Name}:{s.Type}")));
               }
            }

            store.Add(new DocumentChunk {
               Id = $"ROM.{anchorName}",
               Content = sb.ToString(),
               Category = "ROM",
            });
         }
      }
   }
}
