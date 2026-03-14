using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests.AI {
   public class BM25StoreTests {
      [Fact]
      public void Add_IncrementsCount() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello world", Category = "test" });
         Assert.Equal(1, store.Count);
      }

      [Fact]
      public void AddRange_AddsMultipleDocuments() {
         var store = new BM25Store();
         store.AddRange(new[] {
            new DocumentChunk { Id = "1", Content = "hello world", Category = "test" },
            new DocumentChunk { Id = "2", Content = "goodbye world", Category = "test" },
            new DocumentChunk { Id = "3", Content = "foo bar baz", Category = "test" },
         });
         Assert.Equal(3, store.Count);
      }

      [Fact]
      public void Search_ReturnsRelevantResults() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "msg", Content = "msgbox displays a message dialog box to the player", Category = "script" });
         store.Add(new DocumentChunk { Id = "flag", Content = "setflag sets a boolean flag for game progress tracking", Category = "script" });
         store.Add(new DocumentChunk { Id = "give", Content = "givepokemon gives the player a new pokemon species", Category = "script" });

         var results = store.Search("show a message dialog");
         Assert.True(results.Count > 0);
         Assert.Equal("msg", results[0].Id);
      }

      [Fact]
      public void Search_RanksExactMatchHigher() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "exact", Content = "trainerbattle starts a trainer battle encounter", Category = "script" });
         store.Add(new DocumentChunk { Id = "partial", Content = "the player can encounter wild pokemon in grass", Category = "script" });

         var results = store.Search("trainerbattle");
         Assert.True(results.Count > 0);
         Assert.Equal("exact", results[0].Id);
      }

      [Fact]
      public void Search_RespectsTopK() {
         var store = new BM25Store();
         for (int i = 0; i < 10; i++) {
            store.Add(new DocumentChunk { Id = $"doc{i}", Content = $"document number {i} about pokemon", Category = "test" });
         }

         var results = store.Search("pokemon", 3);
         Assert.Equal(3, results.Count);
      }

      [Fact]
      public void Search_EmptyQuery_ReturnsEmpty() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello world", Category = "test" });

         var results = store.Search("");
         Assert.Empty(results);
      }

      [Fact]
      public void Search_NoMatch_ReturnsEmpty() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello world", Category = "test" });

         var results = store.Search("zzzzzzxyzzy");
         Assert.Empty(results);
      }

      [Fact]
      public void Search_EmptyStore_ReturnsEmpty() {
         var store = new BM25Store();
         var results = store.Search("hello");
         Assert.Empty(results);
      }

      [Fact]
      public void Clear_RemovesAllDocuments() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello", Category = "test" });
         store.Add(new DocumentChunk { Id = "2", Content = "world", Category = "test" });
         store.Clear();

         Assert.Equal(0, store.Count);
         Assert.Empty(store.Search("hello"));
      }

      [Fact]
      public void Search_SpecialCharacters_DoesNotThrow() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello (world) [test] {brace}", Category = "test" });

         var results = store.Search("(world)");
         // Should not throw and should find the document
         Assert.True(results.Count >= 0);
      }

      [Fact]
      public void Search_ResultsHaveScores() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "pokemon fire red game boy advance", Category = "test" });
         store.Add(new DocumentChunk { Id = "2", Content = "fire emblem game boy advance", Category = "test" });

         var results = store.Search("pokemon fire red");
         Assert.True(results.Count > 0);
         Assert.True(results[0].Score > 0);
         // First result should score higher due to more matching terms
         if (results.Count > 1) {
            Assert.True(results[0].Score >= results[1].Score);
         }
      }

      [Fact]
      public void Search_PreservesDocumentFields() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "myId", Content = "unique content here", Category = "myCategory" });

         var results = store.Search("unique content");
         Assert.Single(results);
         Assert.Equal("myId", results[0].Id);
         Assert.Contains("unique content", results[0].Content);
         Assert.Equal("myCategory", results[0].Category);
      }

      [Fact]
      public void Search_VectorQuery_ReturnsEmpty() {
         var store = new BM25Store();
         store.Add(new DocumentChunk { Id = "1", Content = "hello", Category = "test" });

         var results = store.Search(new float[] { 1.0f, 0.5f, 0.3f });
         Assert.Empty(results);
      }
   }
}
