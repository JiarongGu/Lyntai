using Microsoft.Extensions.Logging;

namespace Lyntai.Inference;

/// <summary>Embedding for consumers that hold a provider COLLECTION rather than an injected router — the
/// memory seams, the tool selector. It builds a <see cref="ProviderRouter{TRequest,TResponse}"/> and asks that
/// (<c>docs/DECISIONS.md</c> <b>D153</b>), with the shared bookkeeping when handed an
/// <see cref="IProviderRouterFactory"/> and bare otherwise, for a caller composing by hand. Availability is read
/// per call rather than cached: a cached "unavailable" would outlive the outage that caused it.</summary>
internal static class EmbeddingRouting
{
    /// <summary>A backend serves this when it IMPLEMENTS the vector call (the router's type test) and DECLARES
    /// the capability (<see cref="ProviderShapes.Embeds"/>). Both, because one class can do the first and be
    /// configured against the second — an HTTP backend implements the shape whatever its <c>Produces</c> says,
    /// so the type test alone would hand a chat-only endpoint an embed call.</summary>
    private static ProviderRouter<VectorRequest, VectorResponse> Router(
        IEnumerable<IModelProvider>? providers, ILogger? logger, IProviderRouterFactory? routing) =>
        (routing ?? ProviderRouterFactory.Bare)
            .For<VectorRequest, VectorResponse>(providers, VectorResponse.Failure, ProviderShapes.Embeds, logger: logger);

    /// <summary>Whether anything can embed at all — what a consumer asks instead of null-checking a seam. The
    /// answer is derived from what the registered backends IMPLEMENT, so a deployment cannot claim an embedding
    /// capability it has no backend for.
    ///
    /// <para><b>It SHORT-CIRCUITS and allocates nothing</b>, because callers sit on hot paths —
    /// <c>GraphMemoryEngine.Enriches</c> is read on every write and every recall — so it asks the two questions
    /// directly rather than building a router for a yes/no. <see cref="IModelProvider.IsAvailable"/> is asked
    /// LAST and is not always free — a CLI backend's resolves a command on PATH.</para></summary>
    public static bool CanEmbed(IEnumerable<IModelProvider>? providers) =>
        providers is not null
        && providers.Any(p => p is IVectorProvider && ProviderShapes.Embeds(p.Capabilities) && p.IsAvailable);

    /// <summary>Embed a batch, falling over to the next capable backend when one fails.</summary>
    /// <exception cref="InvalidOperationException">Nothing can embed, or every backend failed. <b>Thrown
    /// rather than returned</b> because these callers have no verdict to put it in — the routing beneath
    /// now has one, and a caller that wants it asks the router directly.</exception>
    /// <param name="providers">The registered backends.</param>
    /// <param name="texts">What to embed, in the order the vectors come back.</param>
    /// <param name="role">Which side of a retrieval the texts are.</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="routing">The factory carrying the shared bookkeeping and governance.</param>
    /// <param name="consumer">Who is asking (<see cref="ProviderConsumers"/>) — stamped onto the request so
    /// the library's own embedding traffic is attributable, budgetable and separable from the
    /// application's (D163). Null bills to the default bucket.</param>
    /// <param name="ct">Caller cancellation.</param>
    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        IEnumerable<IModelProvider>? providers, IReadOnlyList<string> texts,
        EmbeddingRole role = EmbeddingRole.Document, ILogger? logger = null,
        IProviderRouterFactory? routing = null, string? consumer = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var router = Router(providers, logger, routing);
        if (!router.CanServe()) throw new InvalidOperationException(NothingEmbeds);

        var response = await router.CallAsync(new VectorRequest(texts, role, consumer), ct).ConfigureAwait(false);
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
        IProviderRouterFactory? routing = null, string? consumer = null, CancellationToken ct = default) =>
        (await EmbedAsync(providers, [text], role, logger, routing, consumer, ct).ConfigureAwait(false))[0];

    /// <summary>Names every shipped way to get an embedding backend, because "nothing can embed" is
    /// otherwise a dead end for a consumer who does not know the capability model.</summary>
    public const string NothingEmbeds =
        "No registered backend produces ProviderKinds.Vector from text. Register one — "
        + "AddModel2VecProvider(dir) or AddOnnxProvider(dir) in process, or AddHttpProvider / "
        + "AddOllamaProvider with Produces = ProviderKinds.Vector against any HTTP "
        + "/embeddings endpoint. A backend of your own is an IModelProvider that also implements "
        + "IVectorProvider (docs/DECISIONS.md D151, D153).";
}
