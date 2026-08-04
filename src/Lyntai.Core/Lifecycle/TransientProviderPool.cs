using System.Runtime.CompilerServices;

namespace Lyntai.Lifecycle;

/// <summary>The NEVER-REUSE strategy: every call builds a fresh instance.
///
/// <para>For the host that wants configuration re-read and applied on every single call. Registering this
/// instead of <see cref="BoundedProviderPool{TProvider}"/> changes that behaviour without touching a
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
    /// <remarks>Always 0 — see <see cref="Retire"/>. The blank-slot guard still runs: swapping which strategy
    /// is registered must change REUSE and nothing else, so a call that is a bug under
    /// <see cref="BoundedProviderPool{TProvider}"/> cannot quietly succeed here.</remarks>
    public int RetireSlot(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return 0;
    }

    /// <inheritdoc/>
    public ProviderPoolStatistics Statistics =>
        new(Live: 0, Created: Interlocked.Read(ref _created), Reused: 0, Retired: 0);
}

/// <summary>The validations a pool — and the router factories built on one — owe their caller. Both are
/// the same shape: a router matches candidates by id, so anything that makes an instance unreachable by id
/// must fail at the call that caused it rather than present as a silently missing backend later.</summary>
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

    /// <summary>Two registrations sharing a slot in ONE router-factory call are a silent half-working
    /// router, which is the worst available outcome.
    ///
    /// <para>Every instance built for a slot reports that slot as its <see cref="IProviderIdentity.Id"/>
    /// (<see cref="EnsureIdMatchesSlot"/> enforces exactly that), and a router resolves a candidate by id —
    /// first match wins, with no error and no log. So the second configuration would be constructed, pooled,
    /// and then unreachable forever. Worse, a caller passing the SAME instance under two keys rebinds it to
    /// the later key in the pool's lookup table, quietly attributing the earlier configuration's cooldown to
    /// the wrong one.</para>
    ///
    /// <para>Compared case-insensitively, because that is how ids are matched everywhere else.</para></summary>
    internal static void EnsureDistinctSlots<TProvider>(
        IReadOnlyList<ProviderRegistration<TProvider>> registrations, string paramName)
        where TProvider : class, IProviderIdentity
    {
        if (registrations.Count < 2) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            if (seen.Add(registration.Key.Slot)) continue;
            throw new ArgumentException(
                $"two registrations in this call share the slot '{registration.Key.Slot}'. A router resolves " +
                "candidates by id and cannot tell two providers reporting the same id apart, so only the " +
                "first would ever be routed to — the second would be built, pooled, and unreachable. One " +
                "For(...) call composes ONE caller's provider set, in which each backend id appears at most " +
                "once; give a second configuration of that backend its own For(...) call, and its own router.",
                paramName);
        }
    }
}
