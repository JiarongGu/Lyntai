namespace Lyntai.Memory;

/// <summary>Renders one recalled memory as exactly ONE line, so nothing it carries can begin a line.
/// <para>Recalled content is consumer-authored, and therefore attacker-influenceable: whatever reached a
/// remember call is rendered back into a prompt. Content carrying its own newlines escapes the bullet it
/// is written into and can then write whatever structure it likes — including the very headings a composer
/// uses to tell a model which material is exact. A grade is the renderer's to state and never the
/// content's, so every rendered memory passes through here first.</para>
/// <para>Collapsing is not truncation — no content is dropped: line breaks become spaces and the ends are
/// trimmed. What a multi-line fact does lose is its line breaks, which is the price of the one-line
/// invariant.</para>
/// <para><see cref="string.ReplaceLineEndings(string)"/> folds <c>\r</c>, <c>\n</c>, <c>\f</c> and U+0085,
/// U+2028, U+2029; <c>\v</c> and the FS/GS/RS separators (U+001C–U+001E) it leaves, so they are folded here.
/// Each of them starts a line for some reader downstream.</para></summary>
internal static class MemoryLine
{
    public static string Flatten(string content) => content.ReplaceLineEndings(" ")
        .Replace('\v', ' ').Replace('\u001C', ' ').Replace('\u001D', ' ').Replace('\u001E', ' ').Trim();

    /// <summary>What a rendered memory says about when it held, so an ended fact is not read as a current one: empty
    /// for an untimed entry, otherwise <c> (valid from X until Y, superseded)</c> with only the parts it has.
    /// Written from instants alone, so nothing a writer authored reaches the line through it.</summary>
    public static string ValidityNote(MemoryValidity? validity)
    {
        if (validity is null) return "";
        var parts = new List<string>(2);
        if (validity.From is { } from) parts.Add($"from {Instant(from)}");
        if (validity.End is { } end) parts.Add($"until {Instant(end)}");   // a supersession is an end, so it is always here
        if (parts.Count == 0) return "";
        return $" (valid {string.Join(' ', parts)}{(validity.SupersededAt is null ? "" : ", superseded")})";
    }

    // a date where the instant is midnight UTC, the instant otherwise — always UTC, always invariant
    private static string Instant(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        return utc.TimeOfDay == TimeSpan.Zero
            ? utc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
