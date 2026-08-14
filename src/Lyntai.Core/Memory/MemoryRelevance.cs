namespace Lyntai.Memory;

/// <summary>
/// The ONE rule every <see cref="IMemoryGraphStore"/> reports <see cref="GraphNode.Relevance"/> by, for the
/// backends that have a rank ORDER to report and no comparable score to normalize.
///
/// <para><b>Why a shared rule and not three private copies.</b> Both SQL backends carried this expression
/// verbatim — SQLite twice (its single-query path and its two-query merge), Postgres once — and one clause
/// of it is a CONTRACT fact rather than a backend's own taste: <b>an authoritative entry the query did not
/// match reports 0</b>. All three backends used to disagree about that, and SQLite disagreed with itself
/// (the FTS path put such a node at the TAIL of the gradient, the substring path at the HEAD), which is the
/// divergence 3.0 closed. Three copies is three chances to reopen it, and the reopening would be silent —
/// the same reasoning <see cref="MemorySignals.Salience"/> and <see cref="MemorySubject"/> already carry for
/// their own rules.</para>
///
/// <para><b>What this is NOT.</b> It is not a similarity score and not comparable across backends or across
/// queries — <see cref="IMemoryGraphStore.SeedAsync"/> makes the reported ordering explicitly
/// backend-specific. It is a monotone transform of ONE query's own result order, which is all the ranking
/// policies need. <c>InMemoryMemoryGraphStore</c> has no rank order to transform, so it does not use this —
/// it reports <c>1</c> for a match and <c>0</c> for a grade-admitted non-match directly. (This sentence
/// said "reports a flat 1" when this type was introduced, repeating a stale claim from that store's own
/// class doc without checking it against its <c>SeedAsync</c>; the "flat 1" holds only for a query-less
/// enumeration. Corrected in the 3.0 pre-freeze review — and worth keeping as a note, because copying a
/// neighbouring doc's wording is exactly how one wrong sentence becomes three.)</para>
///
/// <para><b>The known limit, recorded rather than hidden</b> (<c>.claude/knowledge/pitfalls.md</c>
/// §Storage): because this is normalized by rank POSITION and not by score MARGIN, the gap between
/// consecutive positions is CANDIDATE-COUNT DEPENDENT — with two candidates it is exactly 1.0 and 0.5, a
/// fixed 2× spread however close the underlying scores are; with ten, the same one-place gap is 10%. Any
/// test for a rank-lifting signal measured against this needs a result set large enough that the gap it
/// fights is smaller than the boost it is proving. Two candidates is the WORST case, never a
/// representative one.</para>
/// </summary>
public static class MemoryRelevance
{
    /// <summary>This row's relevance from its own position in a query's result order.</summary>
    /// <param name="index">Zero-based position in the returned order, best first.</param>
    /// <param name="count">How many rows the query returned.</param>
    /// <param name="matched">Whether the query actually matched this row, or <c>null</c> where the query did
    /// not ask. <b><c>false</c> is the load-bearing case</b>: a row admitted purely by the
    /// <see cref="MemoryGrade.Authoritative"/> carve-out, which the query never matched, reports <c>0</c> —
    /// because 0 is what "matched the query this well" honestly says about something admitted by GRADE. Its
    /// admission is guaranteed by that carve-out and by the engine's own re-admission, never by borrowing a
    /// relevance it did not earn.</param>
    /// <returns>0 for an admitted non-match; otherwise a value in <c>(0,1]</c>, and exactly <c>1</c> for a
    /// single-row result (there is no gradient to place it on).</returns>
    public static double ByRankPosition(int index, int count, bool? matched = null) =>
        matched == false ? 0
        : count <= 1 ? 1
        : 1 - (double)index / count;
}
