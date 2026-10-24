namespace Lyntai.Storage.InMemory;

/// <summary>In-memory <see cref="ICuratedMemoryStore"/> — a small managed catalog under one lock, for
/// tests and ephemeral use. Its semantics are <see cref="CuratedCatalog"/>'s, shared with the file-system
/// store.</summary>
public sealed class InMemoryCuratedMemoryStore(Func<DateTimeOffset>? clock = null) : IRevisionedCuratedMemoryStore
{
    private readonly Lock _lock = new();
    private readonly CuratedCatalog _catalog = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var (entry, isNew) = _catalog.PlanAdd(kind, content, enabled, taskKey, scope, dedup, metadata, now);
            if (isNew) _catalog.Apply(entry);
            return Task.FromResult(entry.Id);
        }
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        Task.FromResult(Update(id, null, content, enabled, kind, taskKey, scope, metadata) == CuratedUpdateOutcome.Updated);

    public Task<CuratedUpdateOutcome> UpdateAsync(long id, long expectedRevision, string? content = null,
        bool? enabled = null, string? kind = null, string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        Task.FromResult(Update(id, expectedRevision, content, enabled, kind, taskKey, scope, metadata));

    private CuratedUpdateOutcome Update(long id, long? expectedRevision, string? content, bool? enabled, string? kind,
        string? taskKey, string? scope, IReadOnlyDictionary<string, string>? metadata)
    {
        var now = _clock();
        lock (_lock)
        {
            var (outcome, updated) = _catalog.PlanUpdate(id, expectedRevision, content, enabled, kind, taskKey, scope, metadata, now);
            if (updated is not null) _catalog.Apply(updated);
            return outcome;
        }
    }

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_catalog.Remove(id));
    }

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_catalog.Get(id));
    }

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_catalog.List(kind, enabledOnly, taskKey, scope, limit, metadataMatch));
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);
        lock (_lock)
            return Task.FromResult(_catalog.Search(query, kind, enabledOnly, taskKey, scope, limit, metadataMatch));
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeSet = scopes as IReadOnlyCollection<string> ?? [.. scopes];
        lock (_lock) return Task.FromResult(_catalog.ForComposition(taskKey, scopeSet, enabledOnly));
    }
}
