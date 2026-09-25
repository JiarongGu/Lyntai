using System.Text;

namespace Lyntai.Storage;

/// <summary>
/// Builds an FTS5 <c>trigram</c> MATCH string from raw user text: the terms of
/// <see cref="SearchTerms.Extract"/>, each double-quoted (which neutralizes FTS query syntax; embedded
/// quotes are doubled) and OR-joined. Returns null when the query yields no term long enough to match —
/// the caller falls back to a LIKE substring scan.
/// <para>Only the FTS5 <i>syntax</i> lives here. The split is <see cref="SearchTerms"/>'s, shared with every
/// backend, so that which entries a query finds does not depend on where they are stored.</para>
/// </summary>
public static class FtsQuery
{
    /// <summary>The MATCH expression over every indexed column, or null when nothing usable remains.</summary>
    /// <param name="raw">The caller's query text.</param>
    public static string? Build(string? raw)
    {
        var terms = SearchTerms.Extract(raw);
        return terms.Count == 0 ? null : string.Join(" OR ", terms.Select(Quote));
    }

    private static string Quote(string term) =>
        new StringBuilder("\"", term.Length + 2).Append(term.Replace("\"", "\"\"")).Append('"').ToString();
}
