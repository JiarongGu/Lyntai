using Microsoft.Extensions.Logging;

namespace Lyntai.Inference;

/// <summary>Builds a <see cref="ProviderRouter{TRequest,TResponse}"/> over a provider set the CALLER
/// chooses — the generic counterpart of <see cref="ITextRouterFactory"/> and
/// <see cref="IMediaRouterFactory"/>, for the kinds that have no named router of their own: vector, score,
/// and whatever an application closes <see cref="IProviderCall{TRequest,TResponse}"/> over.
///
/// <para><b>Building a router per call is cheap; what must NOT be rebuilt is the BOOKKEEPING.</b> The
/// dead-host tracker and the admission table are injected once and shared by every router this hands out,
/// because a consumer that rebuilds its tracker along with its router can never bench a failing backend —
/// the knowledge that it is failing is thrown away between calls. That is the whole reason this exists:
/// <b>D153</b> gave every kind the routing MECHANISM and left these two to whatever the call site happened
/// to have, which was nothing.</para>
///
/// <para><b>The cooldown key is the CONFIGURATION, not the backend id</b>, wherever the pool knows the
/// instance — so one tenant's exhausted quota never benches another's. An instance the pool never built
/// falls back to the id, which is the single-configuration behaviour.</para></summary>
public interface IProviderRouterFactory
{
    /// <summary>A router over <paramref name="providers"/>, carrying the shared cooldown and admission.</summary>
    /// <param name="providers">The caller's own backends. Null is an empty set, so a consumer with nothing
    /// registered gets a router that reports it rather than a null reference.</param>
    /// <param name="synthesize">Builds the reply for "nothing answered" — the one thing a generic router
    /// cannot construct, because only the kind knows what an empty response looks like.</param>
    /// <param name="serves">Whether a backend's DECLARED capabilities cover this call, asked in addition to
    /// the type test. Null asks the type test only.</param>
    /// <param name="policy">Per-verdict fallback behaviour; null takes the CONFIGURED policy
    /// (<see cref="LyntaiOptions.Routing"/>, i.e. <c>ConfigureRouting</c>) where the factory was built with
    /// one, else <see cref="RoutingPolicy"/>'s defaults — so an operator's retries and per-verdict overrides
    /// reach every kind, not chat alone.</param>
    /// <param name="logger">Null = no logging. Pass none for a FAIL-OPEN seam that runs on every call,
    /// where a transport blip would otherwise become per-call noise at Warning.</param>
    ProviderRouter<TRequest, TResponse> For<TRequest, TResponse>(
        IEnumerable<IModelProvider>? providers,
        Func<ProviderVerdict, string, TResponse> synthesize,
        Func<ProviderCapabilities, bool>? serves = null,
        RoutingPolicy? policy = null,
        ILogger? logger = null)
        where TResponse : class, IProviderOutcome;
}

/// <summary>The shipped <see cref="IProviderRouterFactory"/>: one tracker, one admission table, one pool.
///
/// <para>Every dependency but the tracker is optional, so this composes in a container that registered
/// none of them and in a test that wants bare routing.</para></summary>
/// <param name="deadHosts">The ONE tracker. Shared with the text and media routers on purpose — every
/// router this builds scopes its keys per closed shape (<c>vector::</c>, <c>score::</c>, …), so a chat
/// outage never benches a vector backend that happens to share an id, nor a reranker an embedder.</param>
/// <param name="pool">Used only to attribute cooldown and admission to a CONFIGURATION. Null keys on the
/// backend id, which is correct where each backend is configured once.</param>
/// <param name="admission">Bounds concurrent calls per key. Null = unbounded, which is what these kinds
/// had before.</param>
/// <param name="options">Where the CONFIGURED routing policy lives (<see cref="LyntaiOptions.Routing"/>).
/// Null routes on <see cref="RoutingPolicy"/>'s defaults, which is the bare-composition behaviour.</param>
public sealed class ProviderRouterFactory(
    DeadHostTracker deadHosts,
    IProviderPool<IModelProvider>? pool = null,
    IProviderAdmission? admission = null,
    LyntaiOptions? options = null) : IProviderRouterFactory
{
    // TryGetKey answers from a table independent of the pool's entries, so an instance whose configuration
    // was retired mid-call still attributes its cooldown correctly.
    private readonly Func<IModelProvider, ProviderKey?>? _configuration =
        pool is null ? null : p => pool.TryGetKey(p, out var key) ? key : null;

    /// <inheritdoc/>
    public ProviderRouter<TRequest, TResponse> For<TRequest, TResponse>(
        IEnumerable<IModelProvider>? providers,
        Func<ProviderVerdict, string, TResponse> synthesize,
        Func<ProviderCapabilities, bool>? serves = null,
        RoutingPolicy? policy = null,
        ILogger? logger = null)
        where TResponse : class, IProviderOutcome
    {
        ArgumentNullException.ThrowIfNull(synthesize);
        return new ProviderRouter<TRequest, TResponse>(
            providers ?? [], synthesize, serves, policy ?? options?.Routing, deadHosts, admission,
            _configuration, Scope<TRequest>(), logger);
    }

    /// <summary>The cooldown namespace for one closed shape — <c>VectorRequest</c> → <c>vector::</c> —
    /// derived from the request type so an application's own kind gets its own namespace without telling
    /// this factory anything. Stable per closed type, which is what a cooldown key must be.</summary>
    private static string Scope<TRequest>()
    {
        var name = typeof(TRequest).Name;
        if (name.EndsWith("Request", StringComparison.Ordinal)) name = name[..^"Request".Length];
        return name.ToLowerInvariant() + "::";
    }
}
