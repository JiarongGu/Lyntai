using Lyntai.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Lyntai.Llm.Routing;

/// <summary>Builds an <see cref="ILlmRouter"/> over a provider set the CALLER chooses — the chat counterpart
/// of <see cref="Lyntai.Generation.Routing.IGenerationRouterFactory"/>.
///
/// <para>Needed because a router snapshots its provider set at construction, and once several
/// configurations are live only the caller knows which ones are its own. Building a router per call is
/// cheap; what must NOT be rebuilt is the bookkeeping, so the tracker and the admission table are injected
/// once and shared by every router this factory hands out — a consumer that rebuilds its tracker along with
/// its router can never bench a failing backend, because the knowledge that it is failing is thrown away
/// between calls.</para>
///
/// <para>Unlike the generation side there is no governance to compose here: chat spend caps, response
/// caching and throttling live on the <see cref="ILlmClient"/> front door, ABOVE the router, so a router
/// built by this factory is the bare routing engine.</para></summary>
public interface ILlmRouterFactory
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
    ILlmRouter For(IReadOnlyList<ProviderRegistration<ILlmProvider>> providers);

    /// <summary>Route over already-constructed backends — the container-composed path. No pool is
    /// involved and cooldown stays keyed on the provider id, exactly as it was before pooling existed.</summary>
    ILlmRouter For(IReadOnlyList<ILlmProvider> providers);
}

/// <inheritdoc cref="ILlmRouterFactory"/>
/// <param name="pool">Owns backend lifetime for the pooled overload.</param>
/// <param name="deadHosts">Cooldown bookkeeping, SHARED by every router this hands out — see the type
/// summary for why sharing it is the whole point.</param>
/// <param name="options">Platform options; <see cref="LyntaiOptions.Routing"/> supplies the fallback policy,
/// retry budgets and cooldown granularity.</param>
/// <param name="loggers">Null = no logging from the routers.</param>
/// <param name="modelRouting">Live per-consumer model overrides; null = the configured defaults alone.</param>
/// <param name="admission">Bounds concurrent completions per configuration; shared for the same reason the
/// tracker is. Null = unbounded. Applies to the pooled overload only, because it needs a configuration to
/// key on — and to completions only, never to streaming (see <see cref="LlmRouter"/>).</param>
public sealed class LlmRouterFactory(
    IProviderPool<ILlmProvider> pool,
    DeadHostTracker deadHosts,
    LyntaiOptions options,
    ILoggerFactory? loggers = null,
    IModelRoutingStore? modelRouting = null,
    ProviderAdmission? admission = null) : ILlmRouterFactory
{
    /// <inheritdoc/>
    public ILlmRouter For(IReadOnlyList<ProviderRegistration<ILlmProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        // checked BEFORE anything is built, so a rejected call pools nothing
        ProviderPoolGuard.EnsureDistinctSlots(providers, nameof(providers));

        var instances = new List<ILlmProvider>(providers.Count);
        // the caller's own delegate goes to the pool untouched: it runs under the pool's lock, so anything
        // added around it here would serialize every other key and caller behind it
        foreach (var registration in providers)
            instances.Add(pool.GetOrAdd(registration.Key, registration.Create));

        return new LlmRouter(instances, deadHosts, options, loggers?.CreateLogger<LlmRouter>(), modelRouting,
            // TryGetKey answers from a table independent of the pool's entries, so an instance whose
            // configuration was retired mid-call still attributes its cooldown correctly
            p => pool.TryGetKey(p, out var key) ? key : null, admission);
    }

    /// <inheritdoc/>
    public ILlmRouter For(IReadOnlyList<ILlmProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return new LlmRouter(providers, deadHosts, options, loggers?.CreateLogger<LlmRouter>(), modelRouting);
    }
}
