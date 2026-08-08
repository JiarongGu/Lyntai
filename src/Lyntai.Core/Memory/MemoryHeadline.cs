namespace Lyntai.Memory;

/// <summary>Derives the one-line form recall returns for ASSOCIATIVE material.
/// <para><b>It does not split on sentences</b>, deliberately. "The build gate is dev.mjs verify" cut at the
/// first period reads "The build gate is dev." — a confidently wrong headline, which is worse than having
/// no memory at all. Cutting on a word boundary and marking the truncation is honest instead: the reader
/// can see that something was elided. Authoritative material never passes through here.</para></summary>
internal static class MemoryHeadline
{
    public static string Derive(string content, int maxChars)
    {
        var text = content.Trim().ReplaceLineEndings(" ");
        if (maxChars <= 0 || text.Length <= maxChars) return text;

        var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
        if (cut <= 0) cut = maxChars; // a single very long token: hard cut rather than no headline
        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
    }
}
