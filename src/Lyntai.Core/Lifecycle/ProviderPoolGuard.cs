namespace Lyntai.Lifecycle;

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
