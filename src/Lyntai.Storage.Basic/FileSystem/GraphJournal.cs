using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary>
/// One append-only JSONL file: a header line (<see cref="GraphLines.Header"/>), then records.
/// <para><b>What it will not read, it will not rewrite.</b> Bytes after the last newline can only be a crash's
/// torn tail and are cut on load; an unreadable line anywhere else is skipped, logged, and blocks
/// <see cref="Rewrite"/> until someone repairs or removes it (<c>docs/DECISIONS.md</c> D171, D174).</para>
/// </summary>
internal sealed class GraphJournal(FileSystemRoot root, string path, int compactionFloor)
{
    public string Path => path;

    /// <summary>The engine the header names, when it could be read.</summary>
    public string? Engine { get; private set; }

    /// <summary>The id high-water the header carries — ids are never reused.</summary>
    public long HighWater { get; private set; }

    /// <summary>Record lines in the file, header excluded — unreadable ones included.</summary>
    public int Lines { get; private set; }

    public int Unreadable { get; private set; }

    /// <summary>Live records at the last load or rewrite — what <see cref="NeedsCompaction"/> measures growth against.</summary>
    public int Baseline { get; set; }

    public bool NeedsCompaction => Lines > 2 * Baseline + compactionFloor;

    /// <summary>Reads every record line, handing each to <paramref name="read"/> with its raw text.</summary>
    /// <exception cref="InvalidDataException">A newer schema wrote this file.</exception>
    public void Load(Action<GraphLine, string> read)
    {
        if (!File.Exists(path)) return;
        var bytes = File.ReadAllBytes(path);
        var end = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        if (end < bytes.Length)
        {
            root.Logger.LogWarning("cutting {Bytes} torn byte(s) from the end of {File}", bytes.Length - end, path);
            FileSystemRoot.Truncate(path, end);
        }

        var first = true;
        for (var start = 0; start < end;)
        {
            var stop = Array.IndexOf(bytes, (byte)'\n', start);
            var span = bytes.AsSpan(start, stop - start);
            start = stop + 1;
            if (span.Trim((byte)'\r').IsEmpty) continue;
            try
            {
                var text = FileSystemRoot.Decode(span).TrimEnd('\r');
                using var doc = JsonDocument.Parse(text);
                var line = GraphLines.Parse(doc.RootElement);
                if (first && line is HeaderLine header)
                {
                    if (header.Schema > RecordFile.Schema)
                        throw new InvalidDataException(
                            $"{path} was written by schema {header.Schema}; this Lyntai reads up to {RecordFile.Schema}");
                    (Engine, HighWater) = (header.Engine, header.Ids);
                }
                else
                {
                    Lines++;
                    read(line, text);
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or DecoderFallbackException)
            {
                Lines++;
                Unreadable++;
                root.Logger.LogWarning(ex, "skipping an unreadable line of {File}; the file is not rewritten while it remains", path);
            }
            first = false;
        }
    }

    public void Append(IReadOnlyCollection<string> lines, string engine, long highWater)
    {
        if (lines.Count == 0) return;
        if (File.Exists(path) && new FileInfo(path).Length > 0) root.Append(path, Text(lines));
        else
        {
            root.Write(path, GraphLines.Header(engine, highWater) + "\n" + Text(lines));
            (Engine, HighWater) = (engine, highWater);
        }
        Lines += lines.Count;
    }

    /// <summary>Replaces the file with <paramref name="lines"/> atomically — false, touching nothing, while it
    /// holds a line that could not be read.</summary>
    public bool Rewrite(IReadOnlyCollection<string> lines, string engine, long highWater)
    {
        if (Unreadable > 0)
        {
            root.Logger.LogWarning("not rewriting {File}: {Count} line(s) in it could not be read — repair or remove them",
                path, Unreadable);
            Baseline = Lines; // try again only after it doubles once more, not on every append
            return false;
        }
        root.Write(path, GraphLines.Header(engine, highWater) + "\n" + Text(lines));
        (Engine, HighWater, Lines, Baseline) = (engine, highWater, lines.Count, lines.Count);
        return true;
    }

    private static string Text(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(line).Append('\n');
        return text.ToString();
    }
}
