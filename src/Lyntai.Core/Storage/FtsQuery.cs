using System.Text;

namespace Lyntai.Storage;

/// <summary>
/// Builds an FTS5 <c>trigram</c> MATCH string from raw user text: the terms of
/// <see cref="SearchTerms.Extract"/>, each double-quoted (which neutralizes FTS query syntax; embedded
/// quotes are doubled) and OR-joined. Returns null when the query yields no term long enough to match —
/// the caller falls back to a LIKE substring scan.
///
/// <para><b>The tokenization is deliberately NOT owned here.</b> It lives in <see cref="SearchTerms"/>
/// because every other backend needs the identical split: when only this class knew how to break a query
/// into words, SQLite's FTS path was the one place a multi-word recall worked, and the same
/// <see cref="Lyntai.Memory.IMemoryGraphStore"/> contract behaved differently depending on where it was stored. This
/// class now owns only the FTS5 <i>syntax</i> — quoting and OR-joining.</para>
/// </summary>
public static class FtsQuery
{
    public static string? Build(string? raw)
    {
        var terms = SearchTerms.Extract(raw);
        return terms.Count == 0 ? null : string.Join(" OR ", terms.Select(Quote));
    }

    private static string Quote(string term) =>
        new StringBuilder("\"", term.Length + 2).Append(term.Replace("\"", "\"\"")).Append('"').ToString();
}
