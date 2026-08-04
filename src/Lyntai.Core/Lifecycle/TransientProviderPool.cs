using System.Runtime.CompilerServices;

namespace Lyntai.Lifecycle;

/// <summary>The NEVER-REUSE strategy: every call builds a fresh instance.
///
/// <para>For the host that wants configuration re-read and applied on every single call. Registering this
/// instead of <c>BoundedProviderPool{TProvider}</c> changes that behaviour without touching a
/// single call site — which is the point of pooling being a strategy rather than something the library
/// imposes.</para>
///
/// <para>It still records each instance's key, so dead-host cooldown and admission control remain keyed on
/// the CONFIGURATION. That is what makes the trade honest: a consumer can rebuild a provider every call and
/// still accumulate bench state correctly, which is exactly what rebuilding by hand fails to do.</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled.</typeparam>
public sealed class TransientProviderPool<TProvider> : IProviderPool<TProvider>
    where TProvider : class, IProviderIdentity
{
    // weak keys: the pool must not be the reason a transient instance stays alive
    private readonly ConditionalWeakTable<TProvider, StrongBox<ProviderKey>> _keys = [];
    private long _created;

    /// <inheritdoc/>
    public TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var instance = factory() ?? throw new InvalidOperationException(
            $"the factory for '{key}' returned null");
        ProviderPoolGuard.EnsureIdMatchesSlot(instance, key);

        _keys.AddOrUpdate(instance, new StrongBox<ProviderKey>(key));
        Interlocked.Increment(ref _created);
        return instance;
    }

    /// <inheritdoc/>
    public bool TryGetKey(TProvider instance, out ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_keys.TryGetValue(instance, out var box)) { key = box.Value; return true; }
        key = default;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>Always false — nothing is retained, so there is never an entry to retire.</remarks>
    public bool Retire(ProviderKey key) => false;

    /// <inheritdoc/>
    /// <remarks>Always 0 — see <see cref="Retire"/>.</remarks>
    public int RetireSlot(string slot) => 0;

    /// <inheritdoc/>
    public ProviderPoolStatistics Statistics =>
        new(Live: 0, Created: Interlocked.Read(ref _created), Reused: 0, Retired: 0);
}

/// <summary>The one validation every pool owes its caller.</summary>
internal static class ProviderPoolGuard
{
    /// <summary>A provider whose id disagrees with its slot is unroutable — routers match candidates on the
    /// id, so the mismatch would present as "this backend is not registered" long after the registration
    /// that caused it. Fail where the mistake is.</summary>
    internal static void EnsureIdMatchesSlot<TProvider>(TProvider instance, ProviderKey key)
        where TProvider : class, IProviderIdentity
    {
        if (!string.Equals(instance.Id, key.Slot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"the provider built for slot '{key.Slot}' reports Id '{instance.Id}'. A router matches " +
                $"candidates on Id, so this instance could never be routed to.", nameof(key));
    }
}
