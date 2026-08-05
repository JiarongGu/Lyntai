using Lyntai.Cortex;

namespace Lyntai.Storage.InMemory;

/// <summary>In-memory <see cref="ICuratedMemoryStore"/> — a small managed catalog under one lock, for
/// tests and ephemeral use.</summary>
public sealed class InMemoryCuratedMemoryStore(Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private readonly Lock _lock = new();
    private readonly List<CuratedMemory> _entries = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private long _nextId = 1;

    // defensive copy so a caller mutating its dict can't reach into the store (ordinal keys, like the
    // SQL backends' codec round-trip); null/empty collapses to null so "no metadata" is uniform.
    private static IReadOnlyDictionary<string, string>? Copy(IReadOnlyDictionary<string, string>? m)
        => m is null || m.Count == 0 ? null : new Dictionary<string, string>(m, StringComparer.Ordinal);

    public Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (dedup)
            {
                // idempotent on the (kind, content, taskKey, scope) identity — return the existing row's id.
                // Metadata is display/payload (like source/title were): OUT of the identity; matched row kept.
                var hit = _entries.FirstOrDefault(e =>
                    e.Kind == kind && e.Content == content && e.TaskKey == taskKey && e.Scope == scope);
                if (hit is not null) return Task.FromResult(hit.Id);
            }
            var id = _nextId++;
            _entries.Add(new CuratedMemory(id, kind, content, enabled, now, now, taskKey, scope, Copy(metadata)));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var i = _entries.FindIndex(e => e.Id == id);
            if (i < 0) return Task.FromResult(false);
            var e = _entries[i];
            _entries[i] = e with
            {
                Content = content ?? e.Content,        // null = unchanged (COALESCE)
                Enabled = enabled ?? e.Enabled,
                Kind = kind ?? e.Kind,                  // null = unchanged; re-categorises in place
                Metadata = metadata is null ? e.Metadata : Copy(metadata), // null = unchanged; non-null REPLACES
                UpdatedAt = now,
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);
    }

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
    }

    // an entry satisfies metadataMatch when it carries every requested pair exactly (AND); null/empty = no filter
    private static bool Matches(CuratedMemory e, IReadOnlyDictionary<string, string>? match)
        => match is null || match.Count == 0
           || (e.Metadata is { } md && match.All(kv => md.TryGetValue(kv.Key, out var v) && v == kv.Value));

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = _entries;
            if (kind is not null) q = q.Where(e => e.Kind == kind);
            if (taskKey is not null) q = q.Where(e => e.TaskKey == taskKey); // strict equality (admin filter)
            if (scope is not null) q = q.Where(e => e.Scope == scope); // strict equality (admin filter)
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => Matches(e, metadataMatch));
            q = q.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
            if (limit is { } n) q = q.Take(n);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);
        var needle = query.Trim();
        lock (_lock)
        {
            // contiguous-substring match over CONTENT, recency-ranked — the same semantics as
            // InMemoryMemoryStore.RecallAsync (see the divergence note on ICuratedMemoryStore.SearchAsync)
            IEnumerable<CuratedMemory> q = _entries.Where(e =>
                e.Content.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (kind is not null) q = q.Where(e => e.Kind == kind);
            if (taskKey is not null) q = q.Where(e => e.TaskKey == taskKey); // strict equality (admin filter)
            if (scope is not null) q = q.Where(e => e.Scope == scope);
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => Matches(e, metadataMatch));
            q = q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id);
            if (limit is { } n) q = q.Take(n);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeSet = scopes as IReadOnlyCollection<string> ?? [.. scopes];
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = _entries;
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => CuratedMemorySections.AppliesTo(e, taskKey, scopeSet)) // shared task/scope predicate
                 .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }
}
