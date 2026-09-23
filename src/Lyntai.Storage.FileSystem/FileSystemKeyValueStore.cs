namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IKeyValueStore"/> as <c>kv/&lt;name&gt;.md</c>, one file per key: the key in the
/// header, the value as the body.</summary>
internal sealed class FileSystemKeyValueStore(FileSystemRoot root) : IKeyValueStore
{
    // `Copies` are the other files holding the same key, so a delete removes every one of them
    private sealed record Entry(string Value, string File, IReadOnlyList<string> Copies);

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("kv");
    private Dictionary<string, Entry>? _entries;

    // Loaded once, on first use. A key held by two files (a hand-made copy) keeps the later one by file name,
    // and a write goes back to the file the key was LOADED from, so a hand-renamed file is updated in place
    // rather than shadowed by a second one.
    private Dictionary<string, Entry> Entries() => _entries ??= root.Load(_directory,
            (file, header, body) => (Key: header.RequiredString("key"), Value: body, File: file))
        .GroupBy(r => r.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => new Entry(g.Last().Value, g.Last().File, [.. g.SkipLast(1).Select(r => r.File)]),
            StringComparer.Ordinal);

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
            var e = entries.GetValueOrDefault(key);
            var file = e?.File ?? Path.Combine(_directory, RecordName.For(key) + ".md");
            // a file already at a new key's own name is this key broken, or another key edited in by hand —
            // either way, never written over
            if (e is null && File.Exists(file))
                throw new InvalidOperationException(
                    $"'{file}' exists but holds no readable '{key}' — repair, rename or remove it");
            root.Write(file, RecordFile.Write(new RecordHeader().Add("key", key), value));
            entries[key] = new Entry(value, file, e?.Copies ?? []);
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
                foreach (var file in e.Copies.Append(e.File)) FileSystemRoot.Delete(file);
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
