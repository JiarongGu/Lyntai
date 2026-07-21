using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Storage.InMemory;

/// <summary>In-memory <see cref="ICuratedMemoryStore"/> — a small managed catalog under one lock, for
/// tests and ephemeral use.</summary>
public sealed class InMemoryCuratedMemoryStore(Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private readonly Lock _lock = new();
    private readonly List<CuratedMemory> _entries = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private long _nextId = 1;

    public Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? task = null, string? scope = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var id = _nextId++;
            _entries.Add(new CuratedMemory(id, kind, content, source, enabled, now, now, task, scope));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var i = _entries.FindIndex(e => e.Id == id);
            if (i < 0) return Task.FromResult(false);
            var e = _entries[i];
            _entries[i] = e with
            {
                Content = content ?? e.Content,
                Enabled = enabled ?? e.Enabled,
                Source = source ?? e.Source, // null = unchanged; "" clears
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

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? task = null, int? limit = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = _entries;
            if (kind is not null) q = q.Where(e => e.Kind == kind);
            if (task is not null) q = q.Where(e => e.Task == task); // strict equality (admin filter)
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
            if (limit is { } n) q = q.Take(n);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string task, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeSet = scopes as IReadOnlyCollection<string> ?? [.. scopes];
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = _entries;
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => CuratedMemorySections.AppliesTo(e, task, scopeSet)) // shared task/scope predicate
                 .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }
}
