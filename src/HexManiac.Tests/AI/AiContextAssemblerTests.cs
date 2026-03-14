using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Collections.Generic;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class AiContextAssemblerTests {
      [Fact]
      public void BuildSystemPrompt_ContainsBaseInstructions() {
         // AiContextAssembler requires EditorViewModel, but we can test with null-safe behavior
         // by verifying the prompt structure when no tab is selected
         var assembler = new AiContextAssembler(null);
         var prompt = assembler.BuildSystemPrompt();

         Assert.Contains("HexManiacAdvance", prompt);
         Assert.Contains("ROM", prompt);
         Assert.Contains("hex editor", prompt);
      }

      [Fact]
      public void BuildSystemPrompt_IncludesRagChunks() {
         var assembler = new AiContextAssembler(null);
         var chunks = new List<string> {
            "[XSE Command] msgbox - displays a message dialog",
            "[XSE Command] lock - locks player movement",
         };

         var prompt = assembler.BuildSystemPrompt(chunks);

         Assert.Contains("Reference Documentation", prompt);
         Assert.Contains("msgbox", prompt);
         Assert.Contains("lock", prompt);
      }

      [Fact]
      public void BuildSystemPrompt_NoRagChunks_NoDocSection() {
         var assembler = new AiContextAssembler(null);
         var prompt = assembler.BuildSystemPrompt();

         Assert.DoesNotContain("Reference Documentation", prompt);
      }

      [Fact]
      public void BuildSystemPrompt_EmptyRagChunks_NoDocSection() {
         var assembler = new AiContextAssembler(null);
         var prompt = assembler.BuildSystemPrompt(new List<string>());

         Assert.DoesNotContain("Reference Documentation", prompt);
      }
   }
}
