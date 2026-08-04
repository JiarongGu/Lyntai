using Lyntai.Lifecycle;
using Lyntai.Llm.Budgeting;
using Lyntai.Llm.RateLimiting;
using Lyntai.Llm.Routing;
using Microsoft.Extensions.Logging;

namespace Lyntai.Generation.Routing;

/// <summary>Builds a fully-governed <see cref="IGenerationRouter"/> over a provider set the CALLER chooses.
///
/// <para>Needed because a router snapshots its provider set at construction, and once several
/// configurations are live only the caller knows which ones are its own. Building a router per call is
/// cheap; what must NOT be rebuilt is the bookkeeping, so the tracker, the limiter and the usage ledger are
/// injected once and shared by every router this factory hands out. That is precisely what per-call
/// hand-construction gets wrong: a consumer that rebuilds its tracker with its router can never bench a
/// failing backend, because the knowledge that it is failing is thrown away between calls.</para></summary>
public interface IGenerationRouterFactory
{
    /// <summary>Route over POOLED backends: each registration is resolved through the pool, and each
    /// provider's dead-host cooldown is keyed on its <see cref="ProviderKey"/> — so one tenant's rate
    /// limit never benches another's.
    ///
    /// <para>One call composes ONE caller's provider set, in which each backend id appears at most once. A
    /// router resolves candidates by id, so two configurations of one id in the same call cannot both be
    /// reachable; that is a second CALLER's set, and belongs in its own call and its own router.</para></summary>
    /// <param name="providers">The caller's backends, at most one per backend id.</param>
    /// <exception cref="ArgumentException">Two registrations share a <see cref="ProviderKey.Slot"/>
    /// (compared case-insensitively).</exception>
    IGenerationRouter For(IReadOnlyList<ProviderRegistration<IGenerationProvider>> providers);

    /// <summary>Route over already-constructed backends — the container-composed path. No pool is
    /// involved and cooldown stays keyed on the provider id, exactly as it was before pooling existed.</summary>
    IGenerationRouter For(IReadOnlyList<IGenerationProvider> providers);
}

/// <inheritdoc cref="IGenerationRouterFactory"/>
/// <param name="pool">Owns backend lifetime for the pooled overload.</param>
/// <param name="deadHosts">Cooldown bookkeeping, SHARED by every router this hands out — see the type
/// summary for why sharing it is the whole point.</param>
/// <param name="policy">Per-verdict fallback behaviour; null = the <see cref="GenerationRoutingPolicy"/>
/// defaults.</param>
/// <param name="rateLimiter">The generation limiter (its own instance and rate — never the chat one). Null =
/// no throttling decorator.</param>
/// <param name="usage">Spend ledger. Null — or a null <paramref name="options"/> — = no budget decorator.</param>
/// <param name="options">Where the spend caps live. Null = no budget decorator.</param>
/// <param name="loggers">Null = no logging from the governance decorators.</param>
/// <param name="admission">Bounds concurrent attempts per configuration; shared for the same reason the
/// tracker is. Null = unbounded. Applies to the pooled overload only, because it needs a configuration to
/// key on.</param>
public sealed class GenerationRouterFactory(
    IProviderPool<IGenerationProvider> pool,
    DeadHostTracker deadHosts,
    GenerationRoutingPolicy? policy = null,
    IRateLimiter? rateLimiter = null,
    IUsageTracker? usage = null,
    LyntaiOptions? options = null,
    ILoggerFactory? loggers = null,
    ProviderAdmission? admission = null) : IGenerationRouterFactory
{
    /// <inheritdoc/>
    public IGenerationRouter For(IReadOnlyList<ProviderRegistration<IGenerationProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        // checked BEFORE anything is built, so a rejected call pools nothing
        ProviderPoolGuard.EnsureDistinctSlots(providers, nameof(providers));

        var instances = new List<IGenerationProvider>(providers.Count);
        // the caller's own delegate goes to the pool untouched: it runs under the pool's lock, so anything
        // added around it here would serialize every other key and caller behind it
        foreach (var registration in providers)
            instances.Add(pool.GetOrAdd(registration.Key, registration.Create));

        return Compose(new GenerationRouter(instances, policy, deadHosts,
            // TryGetKey answers from a table independent of the pool's entries, so an instance whose
            // configuration was retired mid-call still attributes its cooldown correctly
            p => pool.TryGetKey(p, out var key) ? key : null, admission));
    }

    /// <inheritdoc/>
    public IGenerationRouter For(IReadOnlyList<IGenerationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return Compose(new GenerationRouter(providers, policy, deadHosts));
    }

    /// <summary>Governance, in the order the LLM front door uses: the limiter sits INSIDE the budget, so a
    /// call refused for spend never spends a permit.</summary>
    private IGenerationRouter Compose(IGenerationRouter router)
    {
        if (rateLimiter is not null)
            router = new RateLimitedGenerationRouter(router, rateLimiter,
                loggers?.CreateLogger<RateLimitedGenerationRouter>());

        if (usage is not null && options is not null)
            router = new BudgetedGenerationRouter(router, usage, options,
                loggers?.CreateLogger<BudgetedGenerationRouter>());

        return router;
    }
}
