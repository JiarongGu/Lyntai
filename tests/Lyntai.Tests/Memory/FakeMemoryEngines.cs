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
