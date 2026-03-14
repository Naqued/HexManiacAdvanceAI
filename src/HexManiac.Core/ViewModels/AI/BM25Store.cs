using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   /// <summary>
   /// BM25-based keyword search store. Works without embeddings API access.
   /// Uses TF-IDF-like scoring for document retrieval.
   /// </summary>
   public class BM25Store : IEmbeddingStore {
      private readonly List<DocumentChunk> documents = new();
      private readonly Dictionary<string, Dictionary<int, int>> invertedIndex = new();
      private readonly Dictionary<int, int> documentLengths = new();
      private double avgDocLength;

      private const double K1 = 1.2;
      private const double B = 0.75;

      public int Count => documents.Count;

      public void Add(DocumentChunk chunk) {
         var docIndex = documents.Count;
         documents.Add(chunk);
         IndexDocument(docIndex, chunk.Content);
         UpdateAvgDocLength();
      }

      public void AddRange(IEnumerable<DocumentChunk> chunks) {
         foreach (var chunk in chunks) {
            var docIndex = documents.Count;
            documents.Add(chunk);
            IndexDocument(docIndex, chunk.Content);
         }
         UpdateAvgDocLength();
      }

      public IReadOnlyList<DocumentChunk> Search(string query, int topK = 5) {
         var queryTerms = Tokenize(query);
         var scores = new Dictionary<int, double>();

         foreach (var term in queryTerms) {
            if (!invertedIndex.TryGetValue(term, out var postings)) continue;

            double idf = Math.Log((documents.Count - postings.Count + 0.5) / (postings.Count + 0.5) + 1.0);

            foreach (var (docIndex, termFreq) in postings) {
               var docLen = documentLengths.GetValueOrDefault(docIndex, 1);
               double tf = (termFreq * (K1 + 1)) / (termFreq + K1 * (1 - B + B * docLen / Math.Max(avgDocLength, 1)));
               double score = idf * tf;

               if (!scores.ContainsKey(docIndex)) scores[docIndex] = 0;
               scores[docIndex] += score;
            }
         }

         return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => {
               var doc = documents[kv.Key];
               return new DocumentChunk {
                  Id = doc.Id,
                  Content = doc.Content,
                  Category = doc.Category,
                  Score = kv.Value,
               };
            })
            .ToList();
      }

      public IReadOnlyList<DocumentChunk> Search(float[] queryEmbedding, int topK = 5) {
         // Fallback: cannot do vector search, return empty
         return Array.Empty<DocumentChunk>();
      }

      public void Clear() {
         documents.Clear();
         invertedIndex.Clear();
         documentLengths.Clear();
         avgDocLength = 0;
      }

      private void IndexDocument(int docIndex, string content) {
         var terms = Tokenize(content);
         documentLengths[docIndex] = terms.Length;

         var termCounts = new Dictionary<string, int>();
         foreach (var term in terms) {
            if (!termCounts.ContainsKey(term)) termCounts[term] = 0;
            termCounts[term]++;
         }

         foreach (var (term, count) in termCounts) {
            if (!invertedIndex.ContainsKey(term)) invertedIndex[term] = new Dictionary<int, int>();
            invertedIndex[term][docIndex] = count;
         }
      }

      private void UpdateAvgDocLength() {
         if (documentLengths.Count == 0) { avgDocLength = 0; return; }
         avgDocLength = documentLengths.Values.Average();
      }

      private static string[] Tokenize(string text) {
         return text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '(', ')', '[', ']', '{', '}', ':', ';', '/', '\\', '-', '_', '=', '+', '<', '>', '"', '\'' },
               StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .ToArray();
      }
   }
}
