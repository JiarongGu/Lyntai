using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IMemoryStore"/> as <c>memory/&lt;task&gt;/&lt;scope&gt;/000001.md</c> — one fact per
/// file, its content as the body. The semantics are the in-memory store's (dedup, TTL, eviction through the
/// shared <see cref="MemoryEviction"/>, fail-open recall through the shared <see cref="SearchTerms"/> split), so
/// a query finds the same entries here as on every other backend; every change is written through before it
/// is visible.</summary>
internal sealed class FileSystemMemoryStore(FileSystemRoot root, LyntaiOptions options, Func<DateTimeOffset>? clock = null)
    : IMemoryStore
{
    private sealed record Entry(long Id, string TaskKey, string Scope, string Content, DateTimeOffset CreatedAt,
        DateTimeOffset LastAccessedAt, DateTimeOffset? ExpiresAt, int RuneLength, string File);

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("memory");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private List<Entry>? _entries;
    private long _nextId;

    private List<Entry> Entries()
    {
        if (_entries is not null) return _entries;
        var entries = new List<Entry>();
        if (Directory.Exists(_directory))
            foreach (var scopeDir in Directory.EnumerateDirectories(_directory).SelectMany(Directory.EnumerateDirectories))
                entries.AddRange(root.Load(scopeDir, (file, h, body) =>
                {
                    var created = h.Time("created") ?? throw new FormatException("'created' is required");
                    return new Entry(h.Long("id") ?? throw new FormatException("'id' is required"),
                        h.RequiredString("task"), h.RequiredString("scope"), body, created,
                        h.Time("accessed") ?? created, h.Time("expires"), body.EnumerateRunes().Count(), file);
                }));
        _nextId = Math.Max(entries.Select(e => e.Id).DefaultIfEmpty(0).Max(),
            FileSystemRoot.MaxId(_directory, SearchOption.AllDirectories)) + 1;
        return _entries = entries;
    }

    private void Write(Entry e) => root.Write(e.File, RecordFile.Write(new RecordHeader()
        .Add("id", e.Id).Add("task", e.TaskKey).Add("scope", e.Scope).Add("created", e.CreatedAt)
        .Add("accessed", e.LastAccessedAt).Add("expires", e.ExpiresAt), e.Content));

    // Written, then removed from view — a delete that fails leaves the entry visible rather than resurrected.
    private void Remove(List<Entry> entries, Func<Entry, bool> gone)
    {
        foreach (var e in entries.Where(gone).ToList())
        {
            FileSystemRoot.Delete(e.File);
            entries.Remove(e);
        }
    }

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(taskKey);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(content);
        var now = _clock();
        var policy = options.MemoryEviction;
        var effectiveTtl = ttl ?? policy.DefaultTtl;
        var expiresAt = effectiveTtl is null ? (DateTimeOffset?)null : now + effectiveTtl.Value;
        lock (_lock)
        {
            var entries = Entries();
            var i = entries.FindIndex(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content);
            var entry = i >= 0
                ? entries[i] with { CreatedAt = now, LastAccessedAt = now, ExpiresAt = expiresAt }
                : new Entry(_nextId, taskKey, scope, content, now, now, expiresAt, content.EnumerateRunes().Count(),
                    root.Combine("memory", RecordName.For(taskKey), RecordName.For(scope), FileSystemRoot.IdFile(_nextId)));
            Write(entry);
            if (i >= 0) entries[i] = entry;
            else
            {
                entries.Add(entry);
                _nextId++;
            }

            if (policy.HasSizeBound)
            {
                var keep = MemoryEviction.Survivors(policy, entries.Where(e => e.TaskKey == taskKey && e.Scope == scope)
                    .Select(e => new MemoryEviction.Row(e.Id, e.CreatedAt, e.LastAccessedAt, e.ExpiresAt, e.RuneLength)), now);
                Remove(entries, e => e.TaskKey == taskKey && e.Scope == scope && !keep.Contains(e.Id));
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock();
        var touch = options.MemoryEviction.TracksAccess && !string.IsNullOrWhiteSpace(query);
        try
        {
            lock (_lock)
            {
                var candidates = Entries().Where(e => e.TaskKey == taskKey
                    && (scope is null || e.Scope == scope) && (e.ExpiresAt is null || e.ExpiresAt > now));

                IReadOnlyList<string> terms = [];
                if (!string.IsNullOrWhiteSpace(query))
                {
                    terms = SearchTerms.SubstringTerms(query);
                    candidates = terms.Count == 0
                        ? candidates.Where(e => e.Content.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                        : candidates.Where(e => terms.Any(t => e.Content.Contains(t, StringComparison.OrdinalIgnoreCase)));
                }

                var ordered = candidates
                    .OrderByDescending(e => SearchTerms.MatchCount(e.Content, terms, query))
                    .ThenByDescending(e => e.CreatedAt)
                    .ThenByDescending(e => e.Id)
                    .Take(take)
                    .ToList();

                if (touch)
                    foreach (var hit in ordered)
                    {
                        var touched = hit with { LastAccessedAt = now };
                        // an access time only orders eviction, so failing to record one must not cost the recall
                        try { Write(touched); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            root.Logger.LogWarning(ex, "could not record a recall's access time for {File}", hit.File);
                            continue;
                        }
                        _entries![_entries.IndexOf(hit)] = touched;
                    }

                IReadOnlyList<MemoryEntry> result =
                    [.. ordered.Select(e => new MemoryEntry(e.Id, e.TaskKey, e.Scope, e.Content, e.CreatedAt))];
                return Task.FromResult(result);
            }
        }
        // FAIL-OPEN, as the contract requires: a directory that cannot be read, or a record from a newer
        // schema, degrades to no memories rather than a failed turn.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            root.Logger.LogWarning(ex, "memory recall failed; returning no memories");
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);
        }
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        lock (_lock) Remove(Entries(), e => e.TaskKey == taskKey && (scope is null || e.Scope == scope));
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        var cutoff = olderThan is null ? (DateTimeOffset?)null : now - olderThan.Value;
        lock (_lock)
        {
            var entries = Entries();
            var before = entries.Count;
            Remove(entries, e => (taskKey is null || e.TaskKey == taskKey)
                && ((e.ExpiresAt is not null && e.ExpiresAt <= now) || (cutoff is not null && e.CreatedAt < cutoff)));
            return Task.FromResult(before - entries.Count);
        }
    }
}
