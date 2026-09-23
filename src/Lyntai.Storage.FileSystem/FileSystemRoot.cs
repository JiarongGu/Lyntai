using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.FileSystem;

/// <summary>
/// The directory every store of one wiring writes under, OWNED by this process for its lifetime.
///
/// <para><b>Why ownership.</b> A scan-per-read store misses the recall contract's cost by an order of
/// magnitude (<c>storage-scan</c>), so each store holds its records in memory and writes through; that view
/// is coherent only while nothing else writes the directory. An exclusive lock file enforces it — a second
/// owner fails at construction rather than serving stale reads.</para>
///
/// <para><b>Every write is BOM-less UTF-8, written to a temporary sibling, flushed to disk and renamed over
/// the target</b>, so a crash leaves the old record or the new one, never half of one. A leftover temporary
/// file is deleted the next time its directory is read.</para>
/// </summary>
internal sealed class FileSystemRoot : IDisposable
{
    public const string LockName = ".lyntai.lock";
    private const string Temporary = ".tmp";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly FileStream _lock;

    public FileSystemRoot(string path, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        Logger = logger ?? NullLogger.Instance;
        Directory.CreateDirectory(Path);
        try
        {
            _lock = new FileStream(System.IO.Path.Combine(Path, LockName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"'{Path}' is already owned by another file-system store — one process, one wiring per root.", ex);
        }
    }

    /// <summary>The root's full path.</summary>
    public string Path { get; }

    public ILogger Logger { get; }

    /// <summary>A path under the root. Every segment must already be a <see cref="RecordName"/> or a fixed
    /// literal — nothing a consumer typed reaches the file system unencoded.</summary>
    public string Combine(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

    public static string IdFile(long id) => id.ToString("D6", CultureInfo.InvariantCulture) + ".md";

    public void Write(string file, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var temp = file + Temporary;
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Utf8.GetBytes(text));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, file, overwrite: true);
    }

    public static void Delete(string file)
    {
        if (File.Exists(file)) File.Delete(file);
    }

    public static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <summary>Every record file directly in <paramref name="directory"/>, projected by
    /// <paramref name="project"/>, in file-name order.
    /// <para>A file that does not parse, or lacks a field its domain needs, is SKIPPED and logged — never
    /// deleted: it may be a person's edit in progress, and losing their text is worse than not loading it. A
    /// file written by a NEWER schema throws, because reading it wrongly is worse than refusing.</para></summary>
    public List<T> Load<T>(string directory, Func<string, RecordFields, string, T> project)
    {
        var loaded = new List<T>();
        if (!Directory.Exists(directory)) return loaded;
        foreach (var stray in Directory.EnumerateFiles(directory, "*" + Temporary)) File.Delete(stray);

        foreach (var file in Directory.EnumerateFiles(directory, "*.md").Order(StringComparer.Ordinal))
            if (TryLoad(file, project, out var record)) loaded.Add(record);
        return loaded;
    }

    /// <summary>One record file, projected — false when it is absent or unreadable, by the same rules as
    /// <see cref="Load"/>.</summary>
    public bool TryLoad<T>(string file, Func<string, RecordFields, string, T> project, out T record)
    {
        record = default!;
        if (!File.Exists(file)) return false;
        try
        {
            var (header, body) = RecordFile.Parse(File.ReadAllText(file, Utf8));
            record = project(file, header, body);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            Logger.LogWarning(ex, "skipping {File}: it is not a readable record", file);
            return false;
        }
    }

    /// <summary>The ids already taken by numbered files in <paramref name="directory"/>, whether or not they
    /// parse — so a new record never lands on the name of a file that was skipped.</summary>
    public static long MaxId(string directory, SearchOption search = SearchOption.TopDirectoryOnly) =>
        !Directory.Exists(directory) ? 0 : Directory.EnumerateFiles(directory, "*.md", search)
            .Select(f => long.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), NumberStyles.None,
                CultureInfo.InvariantCulture, out var id) ? id : 0)
            .DefaultIfEmpty(0).Max();

    public void Dispose() => _lock.Dispose();
}
