using Lyntai.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Embeddings;

/// <summary>The embedding FRONT DOOR: an <see cref="IEmbedder"/> that routes over every registered backend
/// producing <see cref="ProviderKinds.Vector"/>, falling over to the next when one fails.
///
/// <para><b>This is the embedding analogue of <c>ILlmClient</c>, and its absence was a real gap.</b> Chat
/// has always had a front door separate from its backends; embedding had ONE type doing both jobs, so a
/// consumer held a backend directly and a failing embedder took the whole recall path with it
/// (<c>docs/DECISIONS.md</c> D129). <c>HttpEmbeddingsTransport</c>'s own shipped doc admitted the other half: "there
/// is one embedder slot, so a later registration wins".</para>
///
/// <para><b>It loses to an explicitly registered <see cref="IEmbedder"/></b>, which is what keeps the
/// bring-your-own story: <c>AddEmbeddings(myEmbedder)</c> registers directly inside the configure callback,
/// and this is added afterwards with <c>TryAdd</c>.</para></summary>
internal sealed class RoutedEmbedder(
    IEnumerable<IModelProvider> providers, ILogger<RoutedEmbedder>? logger = null) : IEmbedder
{
    private readonly ILogger _logger = logger ?? NullLogger<RoutedEmbedder>.Instance;

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
        EmbedAsync(texts, EmbeddingRole.Document, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        // Availability is checked per call rather than cached: a backend can become usable between one
        // recall and the next, and a cached "unavailable" would outlive the outage that caused it.
        var capable = providers
            .Where(p => p.IsAvailable && p.Capabilities.Supports(
                ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text))
            .ToList();

        if (capable.Count == 0)
            throw new InvalidOperationException(
                "No registered backend produces ProviderKinds.Vector from text. Register one "
                + "(AddModel2VecProvider / AddOnnxProvider, or AddHttpProvider with Produces = "
                + "ProviderKinds.Vector), or supply your own with AddEmbeddings.");

        Exception? last = null;
        foreach (var provider in capable)
        {
            try
            {
                return await provider.EmbedAsync(texts, role, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // the caller's cancel belongs to the caller, never to fallback
            }
            catch (Exception ex)
            {
                // A provider's own deadline or transport failure is what fallback exists for. The reason is
                // logged per attempt because only the LAST one survives to the throw below, and the first
                // failure is usually the informative one.
                _logger.LogWarning(ex, "embedder {Id} failed; trying the next capable backend", provider.Id);
                last = ex;
            }
        }

        throw new InvalidOperationException(
            $"Every embedding backend failed ({capable.Count} tried). The last reason is the inner exception.",
            last);
    }
}
