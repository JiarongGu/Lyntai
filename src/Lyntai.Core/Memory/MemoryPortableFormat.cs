using System.Text;
using System.Text.Json;

namespace Lyntai.Memory;

/// <summary>One line of an <see cref="IPortableMemory"/> file, checked for the fields every record shares.</summary>
/// <param name="Number">1-based, counting blank lines, so an error names the line an editor shows.</param>
/// <param name="Record">What kind of entry the line carries (<c>curated</c>, <c>graph</c> or <c>link</c>).</param>
/// <param name="Engine">The engine that exported it, or null when the line names none.</param>
/// <param name="Body">The whole object.</param>
/// <param name="Text">The line as read, so a blend can hand it to a member unchanged.</param>
internal sealed record MemoryPortableLine(int Number, string Record, string? Engine, JsonElement Body, string Text);

/// <summary>The JSONL envelope every <see cref="IPortableMemory"/> record shares: <c>v</c>, <c>record</c>,
/// <c>engine</c>. The per-record fields belong to the engine that writes them. Hand-written over
/// <see cref="Utf8JsonWriter"/>/<see cref="JsonDocument"/>, as <c>CuratedMetadataJson</c> is (D14).</summary>
internal static class MemoryPortableFormat
{
    /// <summary>The one version this reader understands. A reader refuses a higher one rather than guessing which
    /// fields it may drop.</summary>
    public const int Version = 1;

    public const string Curated = "curated";

    /// <summary>A graph entry WITH its decay state; its <c>id</c> is the source's, read only to resolve links.</summary>
    public const string Graph = "graph";

    /// <summary>A link between two <see cref="Graph"/> lines of the same file, by their <c>id</c>s.</summary>
    public const string Link = "link";

    /// <summary>One line, newline-terminated with <c>\n</c> on every platform.</summary>
    public static Task WriteLineAsync(TextWriter destination, string record, string engine,
        Action<Utf8JsonWriter> body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteString("record", record);
            writer.WriteString("engine", engine);
            body(writer);
            writer.WriteEndObject();
        }
        return destination.WriteAsync((Encoding.UTF8.GetString(buffer.ToArray()) + "\n").AsMemory(), ct);
    }

    /// <summary>Every non-blank line, each checked for the envelope. Throws on the first that fails.</summary>
    public static async Task<List<MemoryPortableLine>> ReadAsync(TextReader source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = new List<MemoryPortableLine>();
        var number = 0;
        while (await source.ReadLineAsync(ct).ConfigureAwait(false) is { } text)
        {
            number++;
            if (string.IsNullOrWhiteSpace(text)) continue;
            JsonElement body;
            try
            {
                using var doc = JsonDocument.Parse(text);
                body = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw Refuse(number, "is not JSON", ex);
            }
            if (body.ValueKind != JsonValueKind.Object) throw Refuse(number, "is not a JSON object");
            if (!body.TryGetProperty("v", out var v) || !v.TryGetInt32(out var version))
                throw Refuse(number, "carries no format version (\"v\")");
            if (version != Version)
                throw Refuse(number, $"was written in format version {version}; this reader understands version {Version}");
            var record = String(body, "record") ?? throw Refuse(number, "names no \"record\"");
            lines.Add(new MemoryPortableLine(number, record, String(body, "engine"), body, text));
        }
        return lines;
    }

    /// <summary>A string property, or null when it is absent or JSON null. Any other type is refused by the caller
    /// reading it as required.</summary>
    public static string? String(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>An optional string→string map, null when absent, JSON null or empty.</summary>
    public static Dictionary<string, string>? Map(MemoryPortableLine line, string name)
    {
        if (!line.Body.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw Refuse(line.Number, $"carries a \"{name}\" that is not an object");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in element.EnumerateObject())
            map[pair.Name] = pair.Value.ValueKind == JsonValueKind.String
                ? pair.Value.GetString()!
                : throw Refuse(line.Number, $"carries a non-string {name} value for '{pair.Name}'");
        return map.Count == 0 ? null : map;
    }

    /// <summary>A required finite number; <paramref name="nonNegative"/> refuses one below zero too.</summary>
    public static double Number(MemoryPortableLine line, string name, bool nonNegative = false) =>
        line.Body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number) && double.IsFinite(number) && (!nonNegative || number >= 0)
            ? number
            : throw Refuse(line.Number, $"carries no finite{(nonNegative ? " non-negative" : "")} \"{name}\"");

    /// <summary>A required whole number; <paramref name="nonNegative"/> refuses one below zero too.</summary>
    public static long Whole(MemoryPortableLine line, string name, bool nonNegative = false) =>
        line.Body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number) && (!nonNegative || number >= 0)
            ? number
            : throw Refuse(line.Number, $"carries no whole{(nonNegative ? " non-negative" : "")} \"{name}\"");

    public static InvalidDataException Refuse(int line, string problem, Exception? inner = null) =>
        new($"Memory import refused: line {line} {problem}. Nothing was imported.", inner);
}
