using Lyntai.Cortex;
using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="ICuratedMemoryStore"/> as <c>curated/000001.md</c> — one entry per file, its content as
/// the body and kind, task, scope, enabled and metadata in the header.
/// <para><b>Flat by id, never nested by kind or scope</b>, because an update re-categorises and re-scopes in
/// place: a path built from either would turn every such edit into a move across directories, which is two
/// operations where the contract promises one. The semantics are the in-memory store's, including the
/// identity-collision refusal and the empty-string clear sentinel.</para></summary>
internal sealed class FileSystemCuratedMemoryStore(FileSystemRoot root, Func<DateTimeOffset>? clock = null)
    : ICuratedMemoryStore
{
    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("curated");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private List<CuratedMemory>? _entries;
    private long _nextId;

    private List<CuratedMemory> Entries()
    {
        if (_entries is not null) return _entries;
        var entries = root.Load(_directory, (_, h, body) => new CuratedMemory(
            h.Long("id") ?? throw new FormatException("'id' is required"), h.RequiredString("kind"), body,
            h.Bool("enabled") ?? true, h.Time("created") ?? throw new FormatException("'created' is required"),
            h.Time("updated") ?? h.Time("created")!.Value, h.String("task"), h.String("scope"), h.Map("metadata")));
        _nextId = Math.Max(entries.Select(e => e.Id).DefaultIfEmpty(0).Max(), FileSystemRoot.MaxId(_directory)) + 1;
        return _entries = entries;
    }

    private void Write(CuratedMemory e) => root.Write(Path.Combine(_directory, FileSystemRoot.IdFile(e.Id)),
        RecordFile.Write(new RecordHeader().Add("id", e.Id).Add("kind", e.Kind).Add("enabled", e.Enabled)
            .Add("task", e.TaskKey).Add("scope", e.Scope).Add("created", e.CreatedAt).Add("updated", e.UpdatedAt)
            .Add("metadata", e.Metadata), e.Content));

    private static IReadOnlyDictionary<string, string>? Copy(IReadOnlyDictionary<string, string>? m)
        => m is null || m.Count == 0 ? null : new Dictionary<string, string>(m, StringComparer.Ordinal);

    public Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(content);
        var now = _clock();
        lock (_lock)
        {
            var entries = Entries();
            if (dedup && entries.FirstOrDefault(e => e.Kind == kind && e.Content == content
                    && e.TaskKey == taskKey && e.Scope == scope) is { } hit)
                return Task.FromResult(hit.Id);

            var entry = new CuratedMemory(_nextId, kind, content, enabled, now, now, taskKey, scope, Copy(metadata));
            Write(entry);
            entries.Add(entry);
            _nextId++;
            return Task.FromResult(entry.Id);
        }
    }

    // null = leave unchanged; the EMPTY string clears to null (the interface's update-only sentinel)
    private static string? Rescope(string? argument, string? current)
        => argument is null ? current : argument.Length == 0 ? null : argument;

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var entries = Entries();
            var i = entries.FindIndex(e => e.Id == id);
            if (i < 0) return Task.FromResult(false);
            var e = entries[i];

            var newKind = kind ?? e.Kind;
            var newContent = content ?? e.Content;
            var newTask = Rescope(taskKey, e.TaskKey);
            var newScope = Rescope(scope, e.Scope);

            // refuse an identity move onto an identity another entry holds, writing nothing
            if ((newKind != e.Kind || newContent != e.Content || newTask != e.TaskKey || newScope != e.Scope)
                && entries.Any(o => o.Id != id && o.Kind == newKind && o.Content == newContent
                                     && o.TaskKey == newTask && o.Scope == newScope))
                return Task.FromResult(false);

            var updated = e with
            {
                Content = newContent,
                Enabled = enabled ?? e.Enabled,
                Kind = newKind,
                TaskKey = newTask,
                Scope = newScope,
                Metadata = metadata is null ? e.Metadata : Copy(metadata),
                UpdatedAt = now,
            };
            Write(updated);
            entries[i] = updated;
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entries = Entries();
            var i = entries.FindIndex(e => e.Id == id);
            if (i < 0) return Task.FromResult(false);
            FileSystemRoot.Delete(Path.Combine(_directory, FileSystemRoot.IdFile(id)));
            entries.RemoveAt(i);
            return Task.FromResult(true);
        }
    }

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Entries().FirstOrDefault(e => e.Id == id));
    }

    private static bool Matches(CuratedMemory e, IReadOnlyDictionary<string, string>? match)
        => match is null || match.Count == 0
           || (e.Metadata is { } md && match.All(kv => md.TryGetValue(kv.Key, out var v) && v == kv.Value));

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = Entries();
            if (kind is not null) q = q.Where(e => e.Kind == kind);
            if (taskKey is not null) q = q.Where(e => e.TaskKey == taskKey);
            if (scope is not null) q = q.Where(e => e.Scope == scope);
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => Matches(e, metadataMatch))
                .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
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
        var terms = SearchTerms.SubstringTerms(needle);
        try
        {
            lock (_lock)
            {
                IEnumerable<CuratedMemory> q = Entries().Where(e => terms.Count == 0
                    ? e.Content.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    : terms.Any(t => e.Content.Contains(t, StringComparison.OrdinalIgnoreCase)));
                if (kind is not null) q = q.Where(e => e.Kind == kind);
                if (taskKey is not null) q = q.Where(e => e.TaskKey == taskKey);
                if (scope is not null) q = q.Where(e => e.Scope == scope);
                if (enabledOnly) q = q.Where(e => e.Enabled);
                q = q.Where(e => Matches(e, metadataMatch))
                    .OrderByDescending(e => SearchTerms.MatchCount(e.Content, terms, needle))
                    .ThenByDescending(e => e.CreatedAt)
                    .ThenByDescending(e => e.Id);
                if (limit is { } n) q = q.Take(n);
                IReadOnlyList<CuratedMemory> result = [.. q];
                return Task.FromResult(result);
            }
        }
        // FAIL-OPEN, as the contract requires of the search path: an unreadable directory degrades to nothing.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            root.Logger.LogWarning(ex, "curated search failed; returning no entries");
            return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);
        }
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeSet = scopes as IReadOnlyCollection<string> ?? [.. scopes];
        lock (_lock)
        {
            IEnumerable<CuratedMemory> q = Entries();
            if (enabledOnly) q = q.Where(e => e.Enabled);
            q = q.Where(e => CuratedMemorySections.AppliesTo(e, taskKey, scopeSet))
                .OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id);
            IReadOnlyList<CuratedMemory> result = [.. q];
            return Task.FromResult(result);
        }
    }
}
