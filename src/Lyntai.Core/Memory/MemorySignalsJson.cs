using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Lyntai.Memory;

/// <summary>Canonical name→double map ⇄ JSON-object-string codec for <see cref="MemorySignals"/> — the
/// SQL backends' materialization of the bag, mirroring <see cref="Lyntai.Storage.CuratedMetadataJson"/>
/// exactly: hand-written via <see cref="Utf8JsonWriter"/>/<see cref="JsonDocument"/>, no reflection
/// serialization (AOT-clean; see <c>docs/DECISIONS.md</c> D14). Keys are sorted (ordinal) for a stable,
/// deterministic column form; an empty bag serializes to <c>null</c> (SQL NULL, never <c>"{}"</c>).</summary>
public static class MemorySignalsJson
{
    /// <summary>Serialize a bag to a canonical (sorted-key) JSON object string, or <c>null</c> when it is
    /// empty — including when every member was skipped as non-finite (see below), so the empty-bag-is-null
    /// convention holds either way.
    /// <para>A non-finite value (<c>NaN</c>/<c>±Infinity</c>) is skipped rather than written: JSON has no
    /// representation for one, and <see cref="Utf8JsonWriter.WriteNumber(string, double)"/> throws
    /// <see cref="ArgumentException"/> for it — uncaught, that would fail the whole write, not just the one
    /// signal. Symmetric with <see cref="Deserialize"/>'s own member-skip: a lost signal is recoverable, a
    /// lost write is not.</para></summary>
    public static string? Serialize(MemorySignals signals)
    {
        if (signals.Count == 0) return null;
        var buffer = new ArrayBufferWriter<byte>();
        var wrote = false;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var key in signals.Values.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var value = signals.Values[key];
                if (!double.IsFinite(value)) continue;
                writer.WriteNumber(key, value);
                wrote = true;
            }
            writer.WriteEndObject();
        }
        return wrote ? Encoding.UTF8.GetString(buffer.WrittenSpan) : null;
    }

    /// <summary>Parse a stored JSON object back into a bag. Malformed, null, blank, or non-object JSON
    /// yields <see cref="MemorySignals.Empty"/> rather than throwing — a row that cannot be parsed must
    /// still be recallable, and a lost signal is recoverable while a lost memory is not. A non-numeric
    /// member is skipped for the same reason: one bad entry must not sink the rest of the bag.</summary>
    public static MemorySignals Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return MemorySignals.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return MemorySignals.Empty;
            // built ONCE via MemorySignals.From, never member-by-member with With(): each With() copies the
            // whole bag into a new FrozenDictionary, so a k-member bag would cost O(k²) — on the read path of
            // every row every seed query returns
            var members = new List<KeyValuePair<string, double>>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var value))
                    members.Add(new KeyValuePair<string, double>(prop.Name, value));
            return MemorySignals.From(members);
        }
        catch (JsonException)
        {
            return MemorySignals.Empty;
        }
    }
}
