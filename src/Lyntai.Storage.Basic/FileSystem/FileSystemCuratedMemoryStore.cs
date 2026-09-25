using Lyntai.Storage.InMemory;
using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="ICuratedMemoryStore"/> as <c>curated/000001.md</c> — one entry per file, its content as
/// the body and kind, task, scope, enabled and metadata in the header.
/// <para><b>Flat by id, never nested by kind or scope</b>, because an update re-categorises and re-scopes in
/// place: a path built from either would turn every such edit into a move across directories, which is two
/// operations where the contract promises one. The semantics are <see cref="CuratedCatalog"/>'s, the
/// in-memory store's own core; every change is written through before it is visible.</para></summary>
internal sealed class FileSystemCuratedMemoryStore(FileSystemRoot root, Func<DateTimeOffset>? clock = null)
    : ICuratedMemoryStore
{
    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("curated");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private CuratedCatalog? _catalog;
    private readonly Dictionary<long, string> _files = []; // the file each entry was LOADED from

    private CuratedCatalog Catalog()
    {
        if (_catalog is not null) return _catalog;
        var loaded = root.Load(_directory, (file, h, body) => (File: file, Entry: new CuratedMemory(
            h.Long("id") ?? throw new FormatException("'id' is required"), h.RequiredString("kind"), body,
            h.Bool("enabled") ?? true, h.Time("created") ?? throw new FormatException("'created' is required"),
            h.Time("updated") ?? h.Time("created")!.Value, h.String("task"), h.String("scope"), h.Map("metadata"))));
        foreach (var (file, entry) in loaded) _files[entry.Id] = file;
        var catalog = new CuratedCatalog();
        // past every numbered FILE too — one that did not parse still holds its number
        catalog.Load(loaded.Select(r => r.Entry), FileSystemRoot.MaxId(_directory));
        return _catalog = catalog;
    }

    // Back to the file the entry was loaded from, so a hand-renamed file is updated in place rather than
    // shadowed by a second one holding the same id.
    private string FileOf(long id) => _files.GetValueOrDefault(id) ?? Path.Combine(_directory, FileSystemRoot.IdFile(id));

    // Written, then applied: a write that fails leaves the catalog as it was.
    private void WriteThenApply(CuratedCatalog catalog, CuratedMemory e)
    {
        root.Write(FileOf(e.Id), RecordFile.Write(new RecordHeader().Add("id", e.Id).Add("kind", e.Kind)
            .Add("enabled", e.Enabled).Add("task", e.TaskKey).Add("scope", e.Scope).Add("created", e.CreatedAt)
            .Add("updated", e.UpdatedAt).Add("metadata", e.Metadata), e.Content));
        catalog.Apply(e);
    }

    public Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(content);
        var now = _clock();
        lock (_lock)
        {
            var catalog = Catalog();
            var (entry, isNew) = catalog.PlanAdd(kind, content, enabled, taskKey, scope, dedup, metadata, now);
            if (isNew) WriteThenApply(catalog, entry);
            return Task.FromResult(entry.Id);
        }
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            var catalog = Catalog();
            if (catalog.PlanUpdate(id, content, enabled, kind, taskKey, scope, metadata, now) is not { } updated)
                return Task.FromResult(false);
            WriteThenApply(catalog, updated);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var catalog = Catalog();
            if (catalog.Get(id) is null) return Task.FromResult(false);
            FileSystemRoot.Delete(FileOf(id)); // the file first: a failed delete leaves the entry visible
            catalog.Remove(id);
            _files.Remove(id);
            return Task.FromResult(true);
        }
    }

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Catalog().Get(id));
    }

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Catalog().List(kind, enabledOnly, taskKey, scope, limit, metadataMatch));
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);
        try
        {
            lock (_lock)
                return Task.FromResult(Catalog().Search(query, kind, enabledOnly, taskKey, scope, limit, metadataMatch));
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
        lock (_lock) return Task.FromResult(Catalog().ForComposition(taskKey, scopeSet, enabledOnly));
    }
}
