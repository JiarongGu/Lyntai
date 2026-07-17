using Lyntai.Storage;

namespace Lyntai.Storage.InMemory;

/// <summary>
/// In-memory <see cref="IMemoryStore"/> honoring the domain contract: dedup on remember, per-entry
/// TTL, per-(task, scope) cap, and fail-open recall. Recall matches by case-insensitive SUBSTRING
/// (there is no trigram/bm25 ranking here — results are recency-ordered), which is the in-memory
/// analogue of the SQLite backend's LIKE fallback; adequate for tests and ephemeral use.
/// </summary>
public sealed class InMemoryMemoryStore(LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private sealed record Entry(long Id, string TaskKey, string Scope, string Content, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private long _nextId = 1;

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        var expiresAt = ttl is null ? (DateTimeOffset?)null : now + ttl.Value;
        lock (_lock)
        {
            // dedup: refresh an identical fact rather than duplicating it
            var existing = _entries.FindIndex(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content);
            if (existing >= 0)
                _entries[existing] = _entries[existing] with { CreatedAt = now, ExpiresAt = expiresAt };
            else
                _entries.Add(new Entry(_nextId++, taskKey, scope, content, now, expiresAt));

            // cap: keep the newest @cap LIVE entries, trim the rest — expired sort last (evicted before
            // live ones); recency is by created_at so a refreshed fact ranks newest.
            var scoped = _entries.Where(e => e.TaskKey == taskKey && e.Scope == scope)
                .OrderBy(e => e.ExpiresAt is null || e.ExpiresAt > now ? 0 : 1)
                .ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).ToList();
            foreach (var stale in scoped.Skip(options.MemoryCapPerScope))
                _entries.Remove(stale);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock();
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

                IReadOnlyList<MemoryEntry> result =
                [
                    .. candidates.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(take)
                        .Select(e => new MemoryEntry(e.Id, e.TaskKey, e.Scope, e.Content, e.CreatedAt))
                ];
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
