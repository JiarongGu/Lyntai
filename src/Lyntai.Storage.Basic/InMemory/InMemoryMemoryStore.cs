namespace Lyntai.Storage.InMemory;

/// <summary>
/// In-memory <see cref="IMemoryStore"/> honoring the domain contract: dedup on remember, per-entry TTL, a
/// configurable <see cref="MemoryEvictionPolicy"/> (count cap + FIFO/LRU eviction, default TTL, size
/// budget), and fail-open recall. Its semantics are <see cref="MemoryEntryLog"/>'s, shared with the
/// file-system store — adequate for tests and ephemeral use.
/// </summary>
public sealed class InMemoryMemoryStore(LyntaiOptions options, Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private readonly Lock _lock = new();
    private readonly MemoryEntryLog _log = new(options);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var (write, evicted) = _log.PlanRemember(taskKey, scope, content, ttl, now);
            _log.Apply(write);
            foreach (var id in evicted) _log.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var now = _clock();
        try
        {
            lock (_lock)
            {
                var hits = _log.Recall(taskKey, scope, query, limit, now);
                if (_log.Touches(query))
                    foreach (var hit in hits) _log.Apply(hit with { LastAccessedAt = now }); // LRU: recently used
                IReadOnlyList<MemoryEntry> result = [.. hits.Select(e => e.ToEntry())];
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
            foreach (var id in _log.ForgetIds(taskKey, scope)) _log.Remove(id);
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var ids = _log.PruneIds(taskKey, olderThan, now);
            foreach (var id in ids) _log.Remove(id);
            return Task.FromResult(ids.Count);
        }
    }
}
