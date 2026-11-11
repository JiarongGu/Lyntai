namespace Lyntai.Memory;

/// <summary>What counts as an IDENTIFIER in a query, and how one is found whole in a text — the one rule the
/// identifier channel, a recall's match score and its matching span share, so they cannot disagree about which
/// entries hold <c>D123</c>.
/// <para>A token is identifier-shaped when it mixes letters and digits (<c>D123</c>, <c>ABC1d</c>,
/// <c>ISSUE-42</c>, <c>v3.5.3</c>), joins words with an underscore (<c>snake_case</c>), has an inner capital
/// after a lowercase letter (<c>GraphMemoryEngine</c>), or is a dotted name (<c>Type.Member</c>,
/// <c>README.md</c>). A bare number is not one, and neither is a plain word.</para>
/// <para>A match is CASE-SENSITIVE and WHOLE: the characters either side must not be a letter, a digit or an
/// underscore, so <c>D12</c> is found in <c>D12 chose</c> and not in <c>D123</c>. Base-62 row ids tell
/// <c>ABC1d</c> from <c>ABC1D</c>, so folding case would match the wrong row.</para></summary>
internal static class MemoryIdentifiers
{
    /// <summary>The identifier-shaped tokens of <paramref name="query"/>, in order and de-duplicated, at most
    /// <paramref name="max"/>. Sentence punctuation around a token (<c>(D123),</c>) is trimmed.</summary>
    internal static IReadOnlyList<string> Extract(string? query, int max)
    {
        if (string.IsNullOrWhiteSpace(query) || max <= 0) return [];

        var found = new List<string>();
        foreach (var raw in query.Replace('\0', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Trim(raw);
            if (token.Length < 2 || !Shaped(token) || found.Contains(token, StringComparer.Ordinal)) continue;
            found.Add(token);
            if (found.Count >= max) break;
        }
        return found;
    }

    /// <summary>Every position where <paramref name="id"/> stands whole in <paramref name="text"/>.</summary>
    internal static IEnumerable<int> Find(string text, string id)
    {
        if (id.Length == 0) yield break;
        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(id, from, StringComparison.Ordinal);
            if (at < 0) yield break;
            var end = at + id.Length;
            if ((at == 0 || !IsWord(text[at - 1])) && (end == text.Length || !IsWord(text[end]))) yield return at;
            from = at + 1;
        }
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string Trim(string token)
    {
        var start = 0;
        var end = token.Length;
        while (start < end && !IsWord(token[start])) start++;
        while (end > start && !IsWord(token[end - 1])) end--;
        return token[start..end];
    }

    private static bool Shaped(string token)
    {
        var letter = false;
        var digit = false;
        var hump = false;
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (char.IsLetter(c)) letter = true;
            else if (char.IsDigit(c)) digit = true;
            if (i > 0 && char.IsLower(token[i - 1]) && char.IsUpper(c)) hump = true;
        }
        if (!letter) return false;
        if (digit || hump || token.Contains('_')) return true;

        // a dotted name, each side at least two characters: Type.Member and README.md, never e.g. or i.e.
        var dot = token.IndexOf('.');
        return token.Length >= 5 && dot >= 2 && token.Length - dot - 1 >= 2;
    }
}
