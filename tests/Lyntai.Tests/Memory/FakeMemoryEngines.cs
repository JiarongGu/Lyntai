using Lyntai.Memory;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>In-process <see cref="IMemoryStore"/> with the contiguous-substring recall semantics the
/// InMemory backend has, so engine tests need no database.</summary>
internal sealed class FakeMemoryStore : IMemoryStore
{
    private readonly List<MemoryEntry> _entries = [];
    private long _next = 1;

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        if (!_entries.Any(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content))
            _entries.Add(new MemoryEntry(_next++, taskKey, scope, content, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEnumerable<MemoryEntry> hits = _entries.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (!string.IsNullOrWhiteSpace(query))
            hits = hits.Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<MemoryEntry>>([.. hits]);
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope));
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
        CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>In-process <see cref="ISemanticMemory"/> whose "similarity" is substring containment, so a
/// test needs no embedder and spends no tokens.</summary>
internal sealed class FakeSemanticMemory : ISemanticMemory
{
    private readonly List<(string Task, string Scope, string Content)> _entries = [];

    public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        if (!_entries.Any(e => e.Task == taskKey && e.Scope == scope && e.Content == content))
            _entries.Add((taskKey, scope, content));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var hits = _entries
            .Where(e => e.Task == taskKey && e.Scope == scope &&
                        e.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(k)
            .Select(e => new SemanticHit(e.Content, 0.9))
            .ToList();
        return Task.FromResult<IReadOnlyList<SemanticHit>>(hits);
    }

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.Task == taskKey && e.Scope == scope);
        return Task.CompletedTask;
    }
}

/// <summary>In-process <see cref="ICuratedMemoryStore"/> covering the read paths the curated engine uses:
/// dedup-aware add, substring search, and the composition read.</summary>
internal sealed class FakeCuratedStore : ICuratedMemoryStore
{
    private readonly List<CuratedMemory> _entries = [];
    private long _next = 1;

    public Task<long> AddAsync(string kind, string content, bool enabled = true, string? taskKey = null,
        string? scope = null, bool dedup = false, IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (dedup)
        {
            var existing = _entries.FirstOrDefault(e =>
                e.Kind == kind && e.Content == content && e.TaskKey == taskKey && e.Scope == scope);
            if (existing is not null) return Task.FromResult(existing.Id);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new CuratedMemory(_next++, kind, content, enabled, now, now, taskKey, scope, metadata);
        _entries.Add(entry);
        return Task.FromResult(entry.Id);
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        IEnumerable<CuratedMemory> hits = _entries;
        if (kind is not null) hits = hits.Where(e => e.Kind == kind);
        if (taskKey is not null) hits = hits.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null,
        string? taskKey = null, string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);

        IEnumerable<CuratedMemory> hits =
            _entries.Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (kind is not null) hits = hits.Where(e => e.Kind == kind);
        if (taskKey is not null) hits = hits.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var wanted = scopes.ToList();
        IEnumerable<CuratedMemory> hits = _entries.Where(e => e.TaskKey is null || e.TaskKey == taskKey);
        if (wanted.Count > 0)
            hits = hits.Where(e => string.IsNullOrEmpty(e.Scope) || wanted.Contains(e.Scope));
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }
}
