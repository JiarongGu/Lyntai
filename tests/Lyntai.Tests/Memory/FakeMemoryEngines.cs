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

/// <summary>An engine that returns a fixed set of items, so composition and routing can be tested without
/// any store at all.</summary>
internal sealed class StaticEngine(
    string name,
    IReadOnlyList<MemoryItem> items,
    MemorySources ran = MemorySources.Lexical,
    MemoryGrades grades = MemoryGrades.Associative) : IMemoryEngine
{
    public string Name { get; } = name;

    public MemoryGrades Supported => grades;

    public Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        throw new NotSupportedException($"'{Name}' is a read-only test engine.");

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count == 0 ? MemoryRecall.Empty : new MemoryRecall(items, ran));
    }
}

/// <summary>An engine that throws from every path — for the fail-open assertions. A BYO engine that
/// ignores the fail-open contract must not sink a caller's prompt.</summary>
internal sealed class FaultingEngine(string name) : IMemoryEngine
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative;

    public Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        throw new InvalidOperationException("boom");

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
        throw new InvalidOperationException("boom");
}

/// <summary>An engine that records what it was asked to store, and declares which grades it accepts.</summary>
internal sealed class RecordingEngine(string name, MemoryGrades grades) : IMemoryEngine
{
    public List<MemoryWrite> Writes { get; } = [];

    public string Name { get; } = name;

    public MemoryGrades Supported => grades;

    public Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        Writes.Add(write);
        return Task.FromResult(new MemoryRef(Name, Writes.Count.ToString()));
    }

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryRecall.Empty);
    }
}

/// <summary>An engine that implements the optional expansion capability — the one a composite must not
/// hide behind itself.</summary>
internal sealed class ExpandableEngine(string name) : IMemoryEngine, IExpandableMemory
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    public Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        Task.FromResult(new MemoryRef(Name, write.Content));

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryRecall.Empty);
    }

    public Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        var expanded = new MemoryItem(reference, $"expanded {reference.Id}", $"expanded {reference.Id}",
            MemoryGrade.Associative, 1, 1, 1);
        return Task.FromResult(new MemoryRecall([expanded], MemorySources.Graph));
    }
}

/// <summary>A graph store that refuses to LEARN but still remembers — for the read-only-database case,
/// where recall must degrade to "no learning" rather than to "no memory".</summary>
internal sealed class TouchHostileGraphStore : IMemoryGraphStore
{
    private readonly Lyntai.Storage.InMemory.InMemoryMemoryGraphStore _inner = new();

    public Task<long> UpsertAsync(GraphNodeWrite write, DateTimeOffset now, CancellationToken ct = default) =>
        _inner.UpsertAsync(write, now, ct);

    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, double? maxAgeOverStability, int limit, DateTimeOffset now,
        CancellationToken ct = default) =>
        _inner.SeedAsync(engine, taskKey, scope, query, maxAgeOverStability, limit, now, ct);

    public Task<IReadOnlyList<GraphNode>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids,
        int limit, DateTimeOffset now, CancellationToken ct = default) =>
        _inner.NeighboursAsync(engine, ids, limit, now, ct);

    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default) =>
        _inner.GetAsync(engine, id, ct);

    public Task TouchAsync(IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default) =>
        throw new InvalidOperationException("attempt to write to a read-only database");

    public Task LinkAsync(long from, long to, string? kind, double weight, bool symmetric,
        DateTimeOffset now, CancellationToken ct = default) =>
        throw new InvalidOperationException("attempt to write to a read-only database");

    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, DateTimeOffset now,
        CancellationToken ct = default) =>
        _inner.PruneAsync(engine, taskKey, scope, maxAgeOverStability, olderThan, now, ct);

    public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default) =>
        _inner.ForgetAsync(engine, taskKey, scope, ct);
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
