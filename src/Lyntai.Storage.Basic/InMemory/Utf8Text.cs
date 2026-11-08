using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Lyntai.Storage;

/// <summary>
/// Text as a UTF-8 database would hold it. A string whose UTF-16 has a lone surrogate (a reply cut inside a
/// pair is one) has no UTF-8 spelling: SQLite stores U+FFFD for each, so the in-process and file stores replace
/// it the same way, at the write and on the lookup, and a key written with one is found by the same key.
/// </summary>
internal static class Utf8Text
{
    /// <summary>The text with each lone surrogate replaced by U+FFFD, or the same instance when it holds none.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Scrub(string? text)
    {
        if (text is null || text.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0) return text;
        var scrubbed = new StringBuilder(text.Length);
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            // an unpaired surrogate decodes as the replacement rune, having consumed its one char
            Rune.DecodeFromUtf16(rest, out var rune, out var consumed);
            scrubbed.Append(rune.ToString());
            rest = rest[consumed..];
        }
        return scrubbed.ToString();
    }

    /// <summary><see cref="Scrub(string?)"/> over a map's keys and values; null stays null, and a map with nothing to
    /// replace is returned as it is.</summary>
    [return: NotNullIfNotNull(nameof(map))]
    public static IReadOnlyDictionary<string, string>? Scrub(IReadOnlyDictionary<string, string>? map) =>
        map is null || !map.Any(p => Scrub(p.Key) != p.Key || Scrub(p.Value) != p.Value)
            ? map
            : map.GroupBy(p => Scrub(p.Key)!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Scrub(g.Last().Value)!, StringComparer.Ordinal);
}
