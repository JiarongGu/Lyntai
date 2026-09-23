namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IKeyValueStore"/> as <c>kv/&lt;name&gt;.md</c>, one file per key: the key in the
/// header, the value as the body.</summary>
internal sealed class FileSystemKeyValueStore(FileSystemRoot root) : IKeyValueStore
{
    private sealed record Entry(string Value, string File);

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("kv");
    private Dictionary<string, Entry>? _entries;

    // Loaded once, on first use. A key held by two files (a hand-made copy) keeps the later one by file name,
    // and a write goes back to the file the key was LOADED from, so a hand-renamed file is updated in place
    // rather than shadowed by a second one.
    private Dictionary<string, Entry> Entries() => _entries ??= root.Load(_directory,
            (file, header, body) => (Key: header.RequiredString("key"), Entry: new Entry(body, file)))
        .GroupBy(r => r.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last().Entry, StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Entries().TryGetValue(key, out var e) ? e.Value : null);
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_lock)
        {
            var entries = Entries();
            var file = entries.TryGetValue(key, out var e) ? e.File : Path.Combine(_directory, RecordName.For(key) + ".md");
            root.Write(file, RecordFile.Write(new RecordHeader().Add("key", key), value));
            entries[key] = new Entry(value, file);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // the file first: a delete that fails must leave the key visible, not gone until the next start
            if (Entries().TryGetValue(key, out var e))
            {
                FileSystemRoot.Delete(e.File);
                Entries().Remove(key);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<string> keys = [.. Entries().Keys
                .Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
            return Task.FromResult(keys);
        }
    }
}
