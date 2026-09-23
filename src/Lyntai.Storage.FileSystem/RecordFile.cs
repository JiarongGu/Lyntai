using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lyntai.Storage.FileSystem;

/// <summary>
/// One record on disk: a front-matter header of <c>name: JSON</c> lines between <c>---</c> fences, then the
/// record's text VERBATIM. JSON values keep every header one line and are also valid YAML, so a Markdown tool
/// reads the header; the body is the value a person came to read, byte for byte.
/// <code>
/// ---
/// lyntai: 1
/// key: "user.prefs"
/// ---
/// the value itself
/// </code>
/// <para><b><c>lyntai</c> is the schema version and always the first field.</b> There is no migrator here, so
/// a file written by a NEWER schema is refused rather than read wrongly.</para>
/// </summary>
internal static class RecordFile
{
    public const int Schema = 1;

    private static readonly JsonWriterOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The whole file for a record. Header lines are LF; the body is appended untouched.</summary>
    public static string Write(RecordHeader header, string body)
    {
        var text = new StringBuilder("---\n").Append("lyntai: ").Append(Schema).Append('\n');
        foreach (var (name, json) in header.Fields) text.Append(name).Append(": ").Append(json).Append('\n');
        return text.Append("---\n").Append(body).ToString();
    }

    /// <summary>Parses a record, or throws <see cref="FormatException"/> for a file that is not one and
    /// <see cref="InvalidDataException"/> for one a newer schema wrote.</summary>
    public static (RecordFields Header, string Body) Parse(string text)
    {
        var at = 0;
        if (ReadLine(text, ref at) != "---") throw new FormatException("a record starts with a '---' line");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            var line = ReadLine(text, ref at) ?? throw new FormatException("the header has no closing '---'");
            if (line == "---") break;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) throw new FormatException($"a header line is not 'name: value': {line}");
            fields[line[..colon]] = line[(colon + 1)..].Trim();
        }

        var header = new RecordFields(fields);
        var schema = header.Long("lyntai") ?? throw new FormatException("the header carries no 'lyntai' schema version");
        if (schema > Schema)
            throw new InvalidDataException($"this record was written by schema {schema}; this Lyntai reads up to {Schema}");
        return (header, text[at..]);
    }

    /// <summary>The next line without its terminator (LF or CRLF, so a file saved by a Windows editor still
    /// parses), or null at the end.</summary>
    private static string? ReadLine(string text, ref int at)
    {
        if (at >= text.Length) return null;
        var end = text.IndexOf('\n', at);
        var line = end < 0 ? text[at..] : text[at..end];
        at = end < 0 ? text.Length : end + 1;
        return line.EndsWith('\r') ? line[..^1] : line;
    }

    internal static string Encode(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Json)) write(writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>A record's header fields in write order. Every value is JSON, so a key carrying a newline or a
/// quote still occupies one header line.</summary>
internal sealed class RecordHeader
{
    private readonly List<(string Name, string Json)> _fields = [];

    public IReadOnlyList<(string Name, string Json)> Fields => _fields;

    public RecordHeader Add(string name, string? value) =>
        Put(name, RecordFile.Encode(w => { if (value is null) w.WriteNullValue(); else w.WriteStringValue(value); }));

    public RecordHeader Add(string name, long value) => Put(name, value.ToString(CultureInfo.InvariantCulture));

    public RecordHeader Add(string name, bool value) => Put(name, value ? "true" : "false");

    public RecordHeader Add(string name, DateTimeOffset value) =>
        Add(name, value.ToString("O", CultureInfo.InvariantCulture));

    public RecordHeader Add(string name, DateTimeOffset? value) =>
        value is { } v ? Add(name, v) : Add(name, (string?)null);

    public RecordHeader Add(string name, IReadOnlyDictionary<string, string>? map) =>
        Put(name, RecordFile.Encode(w =>
        {
            if (map is null) { w.WriteNullValue(); return; }
            w.WriteStartObject();
            foreach (var (k, v) in map.OrderBy(p => p.Key, StringComparer.Ordinal)) w.WriteString(k, v);
            w.WriteEndObject();
        }));

    private RecordHeader Put(string name, string json)
    {
        _fields.Add((name, json));
        return this;
    }
}

/// <summary>A parsed header. Every accessor returns null for a field that is absent or JSON <c>null</c>, and
/// throws <see cref="FormatException"/> for one that is present with the wrong type.</summary>
internal sealed class RecordFields(IReadOnlyDictionary<string, string> fields)
{
    public string? String(string name) => Read<string?>(name, e => e.ValueKind == JsonValueKind.String
        ? e.GetString() : throw Wrong(name, "a string"));

    public string RequiredString(string name) => String(name) ?? throw new FormatException($"'{name}' is required");

    public long? Long(string name) => Read<long?>(name, e => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n)
        ? n : throw Wrong(name, "an integer"));

    public bool? Bool(string name) => Read<bool?>(name, e => e.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? e.GetBoolean() : throw Wrong(name, "true or false"));

    public DateTimeOffset? Time(string name) => String(name) is { } s
        ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture)
        : null;

    public IReadOnlyDictionary<string, string>? Map(string name) => Read<IReadOnlyDictionary<string, string>?>(name, e =>
    {
        if (e.ValueKind != JsonValueKind.Object) throw Wrong(name, "an object of strings");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
            map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : throw Wrong(name, "an object of strings");
        return map.Count == 0 ? null : map;
    });

    // T is always a NULLABLE type at the call site, so `default` is null for an absent or JSON-null field.
    private T Read<T>(string name, Func<JsonElement, T> read)
    {
        if (!fields.TryGetValue(name, out var json)) return default!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Null ? default! : read(doc.RootElement);
        }
        catch (JsonException ex) { throw new FormatException($"'{name}' is not valid JSON", ex); }
    }

    private static FormatException Wrong(string name, string what) => new($"'{name}' must be {what}");
}
