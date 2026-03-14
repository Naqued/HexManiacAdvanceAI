using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HavenSoft.HexManiac.Core.ViewModels.AI {
   public interface ILlmProvider {
      string ProviderName { get; }
      string ModelName { get; }
      bool SupportsTools { get; }
      bool SupportsEmbeddings { get; }
      bool SupportsStreaming { get; }
      bool IsConfigured { get; }

      Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
      Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
      IAsyncEnumerable<StreamDelta> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
   }
}
