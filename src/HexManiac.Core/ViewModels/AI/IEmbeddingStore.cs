using System.Collections.Generic;
using System.Threading.Tasks;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public class DocumentChunk {
      public string Id { get; set; }
      public string Content { get; set; }
      public string Category { get; set; }
      public float[] Embedding { get; set; }
      public double Score { get; set; }
   }

   public interface IEmbeddingStore {
      int Count { get; }
      void Add(DocumentChunk chunk);
      void AddRange(IEnumerable<DocumentChunk> chunks);
      IReadOnlyList<DocumentChunk> Search(string query, int topK = 5);
      IReadOnlyList<DocumentChunk> Search(float[] queryEmbedding, int topK = 5);
      void Clear();
   }
}
