using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Lyntai.Storage;

/// <summary>Canonical <c>string→string</c> map ⇄ JSON-object-string codec for
/// <see cref="CuratedMemory.Metadata"/> — the one converter every backend shares (the JSON is opaque TEXT
/// to the database; only this code parses it). Keys are sorted (ordinal) for a stable, deterministic column
/// form; a null/empty map serializes to <c>null</c> (an empty metadata set is stored as SQL NULL, never
/// <c>"{}"</c>). Hand-written via <see cref="Utf8JsonWriter"/>/<see cref="JsonDocument"/> — no reflection
/// serialization (AOT-clean; see <c>docs/DECISIONS.md</c> D14).</summary>
public static class CuratedMetadataJson
{
    /// <summary>Serialize a metadata map to a canonical (sorted-key) JSON object string, or <c>null</c> when
    /// the map is null/empty.</summary>
    public static string? Serialize(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var key in metadata.Keys.OrderBy(k => k, StringComparer.Ordinal))
                writer.WriteString(key, metadata[key]);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Parse a stored JSON object back into a map, or <c>null</c> when the text is null/blank/empty
    /// or not an object. Non-string values (which this codec never writes) are read as their raw JSON.</summary>
    public static IReadOnlyDictionary<string, string>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()!
                : prop.Value.GetRawText();
        return result.Count == 0 ? null : result;
    }
}
