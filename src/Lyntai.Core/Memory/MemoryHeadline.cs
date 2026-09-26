using System.Globalization;

namespace Lyntai.Memory;

/// <summary>Derives the one-line form recall returns for ASSOCIATIVE material.
/// <para><b>It does not split on sentences</b>, deliberately. "The build gate is dev.mjs verify" cut at the
/// first period reads "The build gate is dev." — a confidently wrong headline, which is worse than having
/// no memory at all. Cutting on a word boundary and marking the truncation is honest instead: the reader
/// can see that something was elided. Authoritative material never passes through here.</para>
/// <para>The cut is the last space in the budget's LATTER half, else between text elements, never inside a
/// surrogate pair: in a spaceless script the one space is often the one after a leading date, and a cut there
/// keeps the date alone.</para></summary>
internal static class MemoryHeadline
{
    public static string Derive(string content, int maxChars)
    {
        var text = MemoryLine.Flatten(content);
        if (maxChars <= 0 || text.Length <= maxChars) return text;

        var cut = text.LastIndexOf(' ', maxChars);
        if (cut < Math.Max(1, maxChars / 2)) cut = HardCut(text, maxChars);
        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
    }

    /// <summary>The last text-element boundary within <paramref name="maxChars"/>; when the first element alone
    /// outruns it, a code-point boundary, and a first code point outrunning it is kept whole.</summary>
    private static int HardCut(string text, int maxChars)
    {
        var cut = 0;
        while (cut + StringInfo.GetNextTextElementLength(text.AsSpan(cut)) is var next && next <= maxChars)
            cut = next;
        if (cut > 0) return cut;

        cut = char.IsSurrogatePair(text, maxChars - 1) ? maxChars - 1 : maxChars;
        return cut > 0 ? cut : 2;
    }
}
