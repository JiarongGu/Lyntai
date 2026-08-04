namespace Lyntai.Lifecycle;

/// <summary>Owns the LIFETIME of backend instances built from externally-owned configuration.
///
/// <para>Where configuration belongs to the deployment, register backends with <c>Add*</c> and ignore this
/// entirely. Where it belongs to an END USER or a store the process polls, three things follow: the
/// settings change at any moment, the CHOICE of backend is one of those settings, and several
/// configurations of the same backend are live at once. Resolving configuration per call then quietly
/// becomes constructing a backend per call — which churns instances and, far worse, makes every consumer
/// rewrite the same cache and get the key subtly wrong.</para>
///
/// <para><b>A pool is not a router.</b> Routing FALLS BACK; a pool must not. "The user chose this
/// configuration" has to FAIL rather than silently succeed against a different one and bill that
/// credential. A pool selects what was chosen; a router selects what is healthy.</para>
///
/// <para>Two strategies ship: <see cref="BoundedProviderPool{TProvider}"/> reuses while the key is
/// unchanged, and <see cref="TransientProviderPool{TProvider}"/> never reuses. Both keep
/// <see cref="TryGetKey"/> answering, which is what lets cooldown and admission stay keyed on the
/// configuration under either. Implement this interface for anything else — a pool shared across
/// processes, one reporting to the host's telemetry, or a lease-based one when a provider ever owns
/// something needing prompt release (see the remarks on <see cref="Retire"/>).</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled — <see cref="Lyntai.Llm.ILlmProvider"/>,
/// <see cref="Lyntai.Generation.IGenerationProvider"/>, or a concrete backend.</typeparam>
public interface IProviderPool<TProvider> where TProvider : class, IProviderIdentity
{
    /// <summary>The instance for this configuration, building one through <paramref name="factory"/> when
    /// the strategy has none to reuse. Thread-safe: a UI request and a background job arrive together.</summary>
    /// <param name="key">The configuration. See <see cref="ProviderKeyBuilder"/> for what belongs in it.</param>
    /// <param name="factory">Builds the instance. Invoked only when needed; exceptions propagate to the
    /// caller and nothing is stored.</param>
    /// <exception cref="ArgumentException">The built instance's <see cref="IProviderIdentity.Id"/> does not
    /// match <see cref="ProviderKey.Slot"/> — which would leave the instance unroutable, because routers
    /// match candidates on the id.</exception>
    TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory);

    /// <summary>Which configuration produced an instance. This is how a router attributes dead-host
    /// cooldown and admission to the CONFIGURATION rather than to the backend id — without it, one
    /// tenant's exhausted quota benches every other tenant sharing that backend.</summary>
    /// <returns>False for an instance this pool never built.</returns>
    bool TryGetKey(TProvider instance, out ProviderKey key);

    /// <summary>Retire one configuration: remove it from the lookup so subsequent calls build afresh.
    ///
    /// <para><b>Retiring never disposes.</b> In-flight callers hold their own reference and finish
    /// normally; the runtime reclaims the instance once the last of them is done. This is not squeamishness
    /// — without leases a pool cannot know when the last caller finished, so disposing on retirement means
    /// disposing while calls may still be running, and a render legitimately outlives the configuration
    /// that started it. A provider owning something that needs prompt release wants a lease-based pool,
    /// which is what implementing this interface is for.</para></summary>
    /// <returns>True if an entry was retired.</returns>
    bool Retire(ProviderKey key);

    /// <summary>Retire EVERY configuration of one backend — the backend the user removed outright.</summary>
    /// <returns>How many entries were retired.</returns>
    int RetireSlot(string slot);

    /// <summary>Counters for diagnostics and tests.</summary>
    ProviderPoolStatistics Statistics { get; }
}

/// <summary>What a pool has been doing. Counters are cumulative; <see cref="Live"/> is a point-in-time
/// reading.</summary>
/// <param name="Live">Entries currently held. Always 0 for a pool that does not reuse.</param>
/// <param name="Created">Instances built.</param>
/// <param name="Reused">Calls answered from an existing entry.</param>
/// <param name="Retired">Entries removed, whether explicitly or by eviction.</param>
public readonly record struct ProviderPoolStatistics(int Live, long Created, long Reused, long Retired);

/// <summary>Bounds for <see cref="BoundedProviderPool{TProvider}"/>.
///
/// <para>Defaults ship rather than forcing a choice: an unconfigured pool that grows without limit is a
/// worse default than one with generous bounds, and a host wanting unbounded can say so by setting both to
/// null.</para></summary>
public sealed class ProviderPoolOptions
{
    /// <summary>Live entries before the least-recently-used one is retired. Null = unbounded.</summary>
    public int? MaxEntries { get; set; } = 64;

    /// <summary>Retire an entry unused for this long, evaluated on access. Null = never on idle.
    ///
    /// <para>A long-idle entry is a credential sitting in memory for no reason, which is the same argument
    /// a connection pool makes for its idle timeout.</para></summary>
    public TimeSpan? IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
