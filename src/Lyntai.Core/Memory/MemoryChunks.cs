namespace Lyntai.Memory;

/// <summary>Splits text too long for one entry into parts that each fit, so a host that writes long records can keep
/// every entry short enough to be ranked and read by passage (<see cref="GraphMemoryOptions.MaxContentChars"/>).
/// <para>The parts are contiguous slices of the text, in order, so <c>string.Concat(parts)</c> is the text again and
/// nothing is dropped or rewritten. Each part ends where a reader would stop, in this order of preference: after a blank
/// line, after a line break, after a sentence's end, after a space, and only then between two characters. The library
/// does not write them for you: which headline each part carries, whether they link to one another and which one a
/// <see cref="MemoryWrite.Supersedes"/> ends are the host's, who knows what the text is.</para></summary>
public static class MemoryChunks
{
    // numeric, never literals: a literal is what a non-UTF-8 console transcodes (SearchTerms keeps its ranges so too)
    private const char IdeographicFullStop = (char)0x3002;
    private const char FullwidthBang = (char)0xFF01;
    private const char FullwidthQuestion = (char)0xFF1F;

    /// <summary>The text as parts of at most <paramref name="maxChars"/> characters; one part when it already fits, none when
    /// it is empty.</summary>
    /// <param name="content">The text.</param>
    /// <param name="maxChars">The longest part, in characters.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxChars"/> is below one.</exception>
    public static IReadOnlyList<string> Split(string content, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);
        if (content.Length == 0) return [];

        var parts = new List<string>();
        var at = 0;
        while (content.Length - at > maxChars)
        {
            var end = At(content, at, maxChars);
            parts.Add(content[at..end]);
            at = end;
        }
        parts.Add(content[at..]);
        return parts;
    }

    /// <summary>Where the part starting at <paramref name="from"/> ends: the latest break of the best kind that leaves the part
    /// at least a quarter of the limit, so a stray early line break does not leave a sliver.</summary>
    private static int At(string text, int from, int max)
    {
        var limit = from + max;
        var floor = from + Math.Max(1, max / 4);

        for (var i = limit; i >= floor; i--)
            if (i >= 2 && text[i - 1] == '\n' && text[i - 2] == '\n') return i;
        for (var i = limit; i >= floor; i--)
            if (text[i - 1] == '\n') return i;
        for (var i = limit; i >= floor; i--)
            if (text[i - 1] is IdeographicFullStop or FullwidthBang or FullwidthQuestion || (i >= 2 && char.IsWhiteSpace(text[i - 1]) && text[i - 2] is '.' or '!' or '?'))
                return i;
        for (var i = limit; i >= floor; i--)
            if (char.IsWhiteSpace(text[i - 1])) return i;

        // between two characters, never inside a surrogate pair
        return char.IsHighSurrogate(text[limit - 1]) && limit - 1 > from ? limit - 1 : limit;
    }
}
