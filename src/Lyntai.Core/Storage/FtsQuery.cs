namespace Lyntai.Storage;

/// <summary>
/// Builds an FTS5 <c>trigram</c> MATCH string from raw user text: tokens shorter than 3 chars are
/// dropped (a trigram index can't match them), the rest are double-quoted (neutralizing FTS query
/// syntax; embedded quotes doubled) and OR-joined. Returns null when nothing usable remains — the
/// caller falls back to LIKE.
/// </summary>
public static class FtsQuery
{
    public static string? Build(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var quoted = raw
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Select(t => "\"" + t.Replace("\"", "\"\"") + "\"")
            .ToList();

        return quoted.Count == 0 ? null : string.Join(" OR ", quoted);
    }
}
