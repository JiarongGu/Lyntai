namespace Lyntai.Storage;

/// <summary>Pure, backend-agnostic computation of which memory entries SURVIVE a
/// <see cref="MemoryRetentionPolicy"/> on write — shared by every <see cref="IMemoryStore"/> backend so
/// eviction is identical across InMemory / SQLite / Postgres (each fetches the scoped group's lightweight
/// metadata, calls this, and deletes the non-survivors). Keeping the logic in ONE tested place is why the
/// three backends can't diverge.</summary>
public static class MemoryEviction
{
    /// <summary>Lightweight metadata for one entry in a <c>(taskKey, scope)</c> group.</summary>
    public readonly record struct Row(long Id, DateTimeOffset CreatedAt, DateTimeOffset LastAccessedAt,
        DateTimeOffset? ExpiresAt, int Length);

    /// <summary>The ids to KEEP for a <c>(taskKey, scope)</c> group under <paramref name="policy"/> at
    /// <paramref name="now"/>. Order of preference: LIVE entries before expired (so a cap evicts dead facts
    /// first); then newest-recency first (created-at for FIFO, last-access for LRU); then the count cap;
    /// then the size (character) budget — at least one entry is always kept. When the policy has no size
    /// bound (<see cref="MemoryRetentionPolicy.HasSizeBound"/> false), every id survives.</summary>
    public static HashSet<long> Survivors(MemoryRetentionPolicy policy, IEnumerable<Row> rows, DateTimeOffset now)
    {
        var all = rows as IReadOnlyCollection<Row> ?? [.. rows];
        if (!policy.HasSizeBound) return [.. all.Select(r => r.Id)]; // nothing bounds size → keep all

        var lru = policy.Eviction == MemoryEvictionMode.Lru;
        IEnumerable<Row> kept = all
            .OrderBy(r => IsLive(r, now) ? 0 : 1)                        // live first (expired evicted first)
            .ThenByDescending(r => lru ? r.LastAccessedAt : r.CreatedAt) // newest-recency first
            .ThenByDescending(r => r.Id);

        if (policy.MaxEntriesPerScope is int n and > 0)
            kept = kept.Take(n);

        if (policy.MaxCharsPerScope is int budget and > 0)
        {
            var acc = 0;
            var trimmed = new List<Row>();
            foreach (var r in kept)
            {
                if (trimmed.Count > 0 && acc + r.Length > budget) break; // always keep at least one
                trimmed.Add(r);
                acc += r.Length;
            }
            kept = trimmed;
        }

        return [.. kept.Select(r => r.Id)];
    }

    private static bool IsLive(Row r, DateTimeOffset now) => r.ExpiresAt is null || r.ExpiresAt > now;
}
