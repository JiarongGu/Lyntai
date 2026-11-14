using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Lyntai.Storage;

/// <summary>
/// Text as every backend stores it. A lone surrogate (a reply cut inside a pair carries one) has no UTF-8 spelling,
/// and Postgres refuses U+0000 in text, so each store replaces either with U+FFFD, at the write and on the lookup,
/// and a key written with one is found by the same key (<c>docs/DECISIONS.md</c> D262). Linked into the in-process,
/// file and SQL stores.
/// </summary>
internal static class Utf8Text
{
    /// <summary>The text with each lone surrogate and each U+0000 replaced by U+FFFD, or the same instance when it
    /// holds neither.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Scrub(string? text) => Replace(text, nul: true);

    /// <summary>A search query with each lone surrogate replaced by U+FFFD but each U+0000 KEPT: <c>SearchTerms</c>
    /// splits a query at a NUL as at whitespace (D326), which a U+FFFD would not.</summary>
    [return: NotNullIfNotNull(nameof(query))]
    public static string? ScrubQuery(string? query) => Replace(query, nul: false);

    private static string? Replace(string? text, bool nul)
    {
        if (text is null) return null;
        var span = text.AsSpan();
        if (span.IndexOfAnyInRange('\uD800', '\uDFFF') < 0 && !(nul && span.Contains('\0'))) return text;
        var scrubbed = new StringBuilder(text.Length);
        while (!span.IsEmpty)
        {
            // an unpaired surrogate decodes as the replacement rune, having consumed its one char
            Rune.DecodeFromUtf16(span, out var rune, out var consumed);
            scrubbed.Append(nul && rune.Value == 0 ? Rune.ReplacementChar.ToString() : rune.ToString());
            span = span[consumed..];
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
