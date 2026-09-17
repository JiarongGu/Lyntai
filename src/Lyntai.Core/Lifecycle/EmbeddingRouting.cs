using Microsoft.Extensions.Logging;

namespace Lyntai.Lifecycle;

/// <summary>Routing for the <see cref="ProviderKinds.Vector"/> capability: pick the backends that can
/// embed, try them in order, fall over when one fails.
///
/// <para><b>A helper rather than a seam, which is the whole of D151.</b> Embedding is what a provider
/// DECLARES, so it needs no consumer-facing type of its own — the same shape
/// <see cref="Lyntai.Memory.Verification.ScoringVerificationPolicy"/> already has for
/// <see cref="ProviderKinds.Score"/>: take the providers, filter on capability, use what is left. The
/// FAILOVER that D129 added is the part worth keeping and it lives here, in one place, so the four
/// consumers cannot each grow their own version of it.</para>
///
/// <para>Availability is checked per call rather than cached: a backend can become usable between one
/// recall and the next, and a cached "unavailable" would outlive the outage that caused it.</para></summary>
internal static class EmbeddingRouting
{
    private static bool Embeds(IModelProvider p) =>
        p.IsAvailable && p.Capabilities.Supports(
            ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text);

    /// <summary>The registered backends that turn text into vectors, in registration order.</summary>
    public static IReadOnlyList<IModelProvider> Capable(IEnumerable<IModelProvider>? providers) =>
        providers?.Where(Embeds).ToList() ?? [];

    /// <summary>Whether anything can embed at all — what a consumer asks instead of null-checking a seam.
    ///
    /// <para>This REPLACES "is a vector backend registered?": the answer is derived from what the registered
    /// backends declare, so a deployment cannot claim an embedding capability it has no backend for.</para>
    ///
    /// <para><b>It SHORT-CIRCUITS and allocates nothing</b>, because callers sit on hot paths —
    /// <c>GraphMemoryEngine.Enriches</c> is read on every write and every recall. Answering it through
    /// <see cref="Capable"/> would build a list per call to ask a yes/no question, and
    /// <see cref="IModelProvider.IsAvailable"/> is not always free: a CLI backend's resolves a command on
    /// PATH. Ask the cheap question first and stop at the first backend that answers.</para></summary>
    public static bool CanEmbed(IEnumerable<IModelProvider>? providers) =>
        providers is not null && providers.Any(Embeds);

    /// <summary>Embed a batch, falling over to the next capable backend when one fails.</summary>
    /// <exception cref="InvalidOperationException">Nothing can embed, or every backend failed.</exception>
    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        IEnumerable<IModelProvider>? providers, IReadOnlyList<string> texts,
        EmbeddingRole role = EmbeddingRole.Document, ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var capable = Capable(providers);
        if (capable.Count == 0) throw new InvalidOperationException(NothingEmbeds);

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
                logger?.LogWarning(ex, "embedding backend {Id} failed; trying the next capable one", provider.Id);
                last = ex;
            }
        }

        throw new InvalidOperationException(
            $"Every embedding backend failed ({capable.Count} tried). The last reason is the inner exception.",
            last);
    }

    /// <summary>Embed a single text — a thin wrapper over the batch primitive, which is what real endpoints
    /// reward.</summary>
    public static async Task<float[]> EmbedOneAsync(
        IEnumerable<IModelProvider>? providers, string text,
        EmbeddingRole role = EmbeddingRole.Document, ILogger? logger = null,
        CancellationToken ct = default) =>
        (await EmbedAsync(providers, [text], role, logger, ct).ConfigureAwait(false))[0];

    /// <summary>Names every shipped way to get an embedding backend, because "nothing can embed" is
    /// otherwise a dead end for a consumer who does not know the capability model.</summary>
    public const string NothingEmbeds =
        "No registered backend produces ProviderKinds.Vector from text. Register one — "
        + "AddModel2VecProvider(dir) or AddOnnxProvider(dir) in process, or AddHttpProvider / "
        + "AddOllamaProvider with Produces = ProviderKinds.Vector against any OpenAI-compatible "
        + "/embeddings endpoint. A backend of your own is an IModelProvider declaring that it produces "
        + "vectors (docs/DECISIONS.md D151).";
}
