using Microsoft.Extensions.Logging;

namespace Lyntai.Lifecycle;

/// <summary>Embedding for consumers that hold a provider COLLECTION rather than an injected router — the
/// memory seams, the tool selector.
///
/// <para><b>It no longer routes; it builds a <see cref="ProviderRouter{TRequest,TResponse}"/> and asks
/// that</b> (<c>docs/DECISIONS.md</c> <b>D153</b>). What used to be here was a second, weaker routing
/// implementation: a try/catch loop with no cooldown, no admission and no verdict — so an embedding backend
/// returning 429 was retried on the next recall exactly as if it had not, where the chat path would have
/// benched it.</para>
///
/// <para><b>What it still does NOT get, stated so the gap is not mistaken for finished work:</b> the
/// routers built here carry no <c>DeadHostTracker</c> and no admission, because these call sites have
/// neither to hand. Wiring one through DI is the remaining step; the mechanism is now in one place to
/// receive it.</para>
///
/// <para>Availability is read per call rather than cached: a backend can become usable between one recall
/// and the next, and a cached "unavailable" would outlive the outage that caused it.</para></summary>
internal static class EmbeddingRouting
{
    /// <summary>A backend serves this when it IMPLEMENTS the vector call and DECLARES the capability. Both,
    /// because one class can do the first and be configured against the second — an HTTP backend implements
    /// the shape whatever its <c>Produces</c> says, so the type test alone would hand a chat-only endpoint
    /// an embed call.</summary>
    private static bool Embeds(ProviderCapabilities capabilities) => capabilities.Supports(
        ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text);

    private static ProviderRouter<VectorRequest, VectorResponse> Router(
        IEnumerable<IModelProvider>? providers, ILogger? logger) =>
        new(providers ?? [], VectorResponse.Failure, Embeds, logger: logger);

    /// <summary>The registered backends that turn text into vectors, in registration order.</summary>
    public static IReadOnlyList<IModelProvider> Capable(IEnumerable<IModelProvider>? providers) =>
        [.. Router(providers, null).Capable().Cast<IModelProvider>()];

    /// <summary>Whether anything can embed at all — what a consumer asks instead of null-checking a seam.
    ///
    /// <para>This REPLACES "is a vector backend registered?": the answer is derived from what the registered
    /// backends IMPLEMENT, so a deployment cannot claim an embedding capability it has no backend for.</para>
    ///
    /// <para><b>It SHORT-CIRCUITS and allocates nothing</b>, because callers sit on hot paths —
    /// <c>GraphMemoryEngine.Enriches</c> is read on every write and every recall. Building the capable LIST
    /// to ask a yes/no question would allocate per call, and <see cref="IModelProvider.IsAvailable"/> is not
    /// always free: a CLI backend's resolves a command on PATH.</para></summary>
    public static bool CanEmbed(IEnumerable<IModelProvider>? providers) =>
        providers is not null && Router(providers, null).CanServe();

    /// <summary>Embed a batch, falling over to the next capable backend when one fails.</summary>
    /// <exception cref="InvalidOperationException">Nothing can embed, or every backend failed. <b>Thrown
    /// rather than returned</b> because these callers have no verdict to put it in — the routing beneath
    /// now has one, and a caller that wants it asks the router directly.</exception>
    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        IEnumerable<IModelProvider>? providers, IReadOnlyList<string> texts,
        EmbeddingRole role = EmbeddingRole.Document, ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var router = Router(providers, logger);
        if (!router.CanServe()) throw new InvalidOperationException(NothingEmbeds);

        var response = await router.CallAsync(new VectorRequest(texts, role), ct).ConfigureAwait(false);
        return response.IsOk
            ? response.Vectors
            : throw new InvalidOperationException(
                $"Every embedding backend failed ({response.Verdict}). {response.Detail}");
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
        + "/embeddings endpoint. A backend of your own is an IModelProvider that also implements "
        + "IVectorProvider (docs/DECISIONS.md D151, D153).";
}
