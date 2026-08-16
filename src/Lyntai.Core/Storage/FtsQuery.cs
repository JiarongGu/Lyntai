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
    public static string? Build(string? raw) => Build(raw, column: null);

    /// <summary>Build the MATCH expression, optionally CONFINED to one indexed column.
    ///
    /// <para><b>Why confining matters, measured 2026-08-15.</b> An FTS5 table indexes every column it
    /// declares, and a bare expression matches ANY of them. <c>lyntai_memory_node_fts</c> declares
    /// <c>headline, content</c>, so SQLite alone answered a headline-only query — while Postgres's trigram
    /// index and the in-process store both match <c>content</c> only. That is a different answer to *is the
    /// fact found*, which `storage.md` calls a defect rather than a divergence, and it contradicted
    /// <c>IMemoryGraphStore.SeedAsync</c>'s written portable guarantee ("a node whose CONTENT contains a
    /// single ≥3-character query token … is found on every backend"). Confining the expression realigns
    /// SQLite with the contract without touching a released migration.</para>
    ///
    /// <para><b>The parentheses are load-bearing.</b> A column filter binds tighter than <c>OR</c>, so
    /// <c>content : "a" OR "b"</c> confines only the first term and leaves the rest matching every column —
    /// which would look correct in a single-term test and be wrong for every real multi-token query.</para>
    /// </summary>
    /// <param name="raw">The caller's query text.</param>
    /// <param name="column">The indexed column to confine to, or null to match any indexed column (correct
    /// for a single-column FTS table, where confining would only add noise).</param>
    public static string? Build(string? raw, string? column)
    {
        var terms = SearchTerms.Extract(raw);
        if (terms.Count == 0) return null;

        var expression = string.Join(" OR ", terms.Select(Quote));
        return string.IsNullOrWhiteSpace(column) ? expression : $"{{{column}}} : ({expression})";
    }

    private static string Quote(string term) =>
        new StringBuilder("\"", term.Length + 2).Append(term.Replace("\"", "\"\"")).Append('"').ToString();
}
