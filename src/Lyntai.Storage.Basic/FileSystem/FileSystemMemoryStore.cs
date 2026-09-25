using Lyntai.Storage.InMemory;
using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IMemoryStore"/> as <c>memory/&lt;task&gt;/&lt;scope&gt;/000001.md</c> — one fact per
/// file, its content as the body. The semantics are <see cref="MemoryEntryLog"/>'s, the in-memory store's own
/// core, so a query finds the same entries here as on every other backend; every change is written through
/// before it is visible.</summary>
internal sealed class FileSystemMemoryStore(FileSystemRoot root, LyntaiOptions options, Func<DateTimeOffset>? clock = null)
    : IMemoryStore
{
    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("memory");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private MemoryEntryLog? _log;
    private readonly Dictionary<long, string> _files = []; // the file each entry was LOADED from

    private MemoryEntryLog Log()
    {
        if (_log is not null) return _log;
        var loaded = new List<MemoryLogEntry>();
        if (Directory.Exists(_directory))
            foreach (var scopeDir in Directory.EnumerateDirectories(_directory).SelectMany(Directory.EnumerateDirectories))
                foreach (var (file, entry) in root.Load(scopeDir, (file, h, body) =>
                         {
                             var created = h.Time("created") ?? throw new FormatException("'created' is required");
                             return (File: file, Entry: new MemoryLogEntry(h.Long("id") ?? throw new FormatException("'id' is required"),
                                 h.RequiredString("task"), h.RequiredString("scope"), body, created,
                                 h.Time("accessed") ?? created, h.Time("expires"), body.EnumerateRunes().Count()));
                         }))
                {
                    _files[entry.Id] = file;
                    loaded.Add(entry);
                }
        var log = new MemoryEntryLog(options);
        // past every numbered FILE too — one that did not parse still holds its number
        log.Load(loaded, FileSystemRoot.MaxId(_directory, SearchOption.AllDirectories));
        return _log = log;
    }

    private string FileOf(MemoryLogEntry e) => _files.GetValueOrDefault(e.Id)
        ?? root.Combine("memory", RecordName.For(e.TaskKey), RecordName.For(e.Scope), FileSystemRoot.IdFile(e.Id));

    // Written, then applied: a write that fails leaves the log as it was.
    private void WriteThenApply(MemoryEntryLog log, MemoryLogEntry e)
    {
        root.Write(FileOf(e), RecordFile.Write(new RecordHeader()
            .Add("id", e.Id).Add("task", e.TaskKey).Add("scope", e.Scope).Add("created", e.CreatedAt)
            .Add("accessed", e.LastAccessedAt).Add("expires", e.ExpiresAt), e.Content));
        log.Apply(e);
    }

    // Deleted, then removed from view — a delete that fails leaves the entry visible rather than resurrected.
    private void Remove(MemoryEntryLog log, IEnumerable<long> ids)
    {
        foreach (var id in ids)
        {
            if (log.Get(id) is not { } e) continue;
            FileSystemRoot.Delete(FileOf(e));
            log.Remove(id);
            _files.Remove(id);
        }
    }

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(taskKey);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(content);
        var now = _clock();
        lock (_lock)
        {
            var log = Log();
            var (write, evicted) = log.PlanRemember(taskKey, scope, content, ttl, now);
            WriteThenApply(log, write);
            Remove(log, evicted);
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
                var log = Log();
                var hits = log.Recall(taskKey, scope, query, limit, now);
                if (log.Touches(query))
                    foreach (var hit in hits)
                    {
                        // an access time only orders eviction, so failing to record one must not cost the recall
                        try { WriteThenApply(log, hit with { LastAccessedAt = now }); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            root.Logger.LogWarning(ex, "could not record a recall's access time for {File}", FileOf(hit));
                        }
                    }

                IReadOnlyList<MemoryEntry> result = [.. hits.Select(e => e.ToEntry())];
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
        lock (_lock)
        {
            var log = Log();
            Remove(log, log.ForgetIds(taskKey, scope));
        }
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var log = Log();
            var ids = log.PruneIds(taskKey, olderThan, now);
            Remove(log, ids);
            return Task.FromResult(ids.Count);
        }
    }
}
