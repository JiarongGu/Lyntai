using Lyntai.Storage;

namespace Lyntai.Storage.InMemory;

/// <summary>
/// In-memory <see cref="IMemoryStore"/> honoring the domain contract: dedup on remember, per-entry TTL, a
/// configurable <see cref="MemoryRetentionPolicy"/> (count cap + FIFO/LRU eviction, default TTL, size
/// budget), and fail-open recall. Recall matches by case-insensitive SUBSTRING (recency-ordered; the
/// in-memory analogue of SQLite's LIKE fallback) — adequate for tests and ephemeral use.
/// </summary>
public sealed class InMemoryMemoryStore(LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private sealed record Entry(long Id, string TaskKey, string Scope, string Content,
        DateTimeOffset CreatedAt, DateTimeOffset LastAccessedAt, DateTimeOffset? ExpiresAt);

    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private long _nextId = 1;

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        var policy = options.MemoryRetention;
        var effectiveTtl = ttl ?? policy.DefaultTtl; // a per-call ttl wins over the policy default
        var expiresAt = effectiveTtl is null ? (DateTimeOffset?)null : now + effectiveTtl.Value;
        lock (_lock)
        {
            // dedup: refresh an identical fact (recency + access + TTL) rather than duplicating it
            var existing = _entries.FindIndex(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content);
            if (existing >= 0)
                _entries[existing] = _entries[existing] with { CreatedAt = now, LastAccessedAt = now, ExpiresAt = expiresAt };
            else
                _entries.Add(new Entry(_nextId++, taskKey, scope, content, now, now, expiresAt));

            // policy-driven eviction — the shared MemoryEviction helper picks the survivors (count cap +
            // FIFO/LRU + size budget), identical to the SQLite/Postgres backends. Manual = no size bound = keep all.
            if (policy.HasSizeBound)
            {
                var scoped = _entries.Where(e => e.TaskKey == taskKey && e.Scope == scope)
                    .Select(e => new MemoryEviction.Row(e.Id, e.CreatedAt, e.LastAccessedAt, e.ExpiresAt, e.Content.Length));
                var keep = MemoryEviction.Survivors(policy, scoped, now);
                _entries.RemoveAll(e => e.TaskKey == taskKey && e.Scope == scope && !keep.Contains(e.Id));
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock();
        var touch = options.MemoryRetention.TracksAccess; // LRU refreshes recency on recall
        try
        {
            lock (_lock)
            {
                var candidates = _entries.Where(e =>
                    e.TaskKey == taskKey
                    && (scope is null || e.Scope == scope)
                    && (e.ExpiresAt is null || e.ExpiresAt > now));

                if (!string.IsNullOrWhiteSpace(query))
                    candidates = candidates.Where(e =>
                        e.Content.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

                var ordered = candidates.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(take).ToList();

                if (touch && ordered.Count > 0) // LRU: mark the recalled entries as recently used
                {
                    var ids = ordered.Select(e => e.Id).ToHashSet();
                    for (var i = 0; i < _entries.Count; i++)
                        if (ids.Contains(_entries[i].Id)) _entries[i] = _entries[i] with { LastAccessedAt = now };
                }

                IReadOnlyList<MemoryEntry> result =
                    [.. ordered.Select(e => new MemoryEntry(e.Id, e.TaskKey, e.Scope, e.Content, e.CreatedAt))];
                return Task.FromResult(result);
            }
        }
        catch (Exception)
        {
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]); // fail-open
        }
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        lock (_lock)
            _entries.RemoveAll(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope));
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        var cutoff = olderThan is null ? (DateTimeOffset?)null : now - olderThan.Value;
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e =>
                (taskKey is null || e.TaskKey == taskKey)
                && ((e.ExpiresAt is not null && e.ExpiresAt <= now)
                    || (cutoff is not null && e.CreatedAt < cutoff)));
            return Task.FromResult(removed);
        }
    }
}
