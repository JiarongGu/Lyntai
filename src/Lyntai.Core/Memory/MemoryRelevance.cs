namespace Lyntai.Memory;

/// <summary>
/// The ONE rule every <see cref="IMemoryGraphStore"/> reports <see cref="GraphNode.Relevance"/> by, for the
/// backends that have a rank ORDER to report and no comparable score to normalize.
///
/// <para><b>Shared because one clause is a CONTRACT fact rather than a backend's own taste: an
/// authoritative entry the query did not match reports 0.</b> All three backends once disagreed about that,
/// and SQLite disagreed with itself. Three copies is three chances to reopen it, silently — the same
/// reasoning <see cref="MemorySignals.Salience"/> and <see cref="MemorySubject"/> carry for their own
/// rules.</para>
///
/// <para><b>What this is NOT.</b> It is not a similarity score and not comparable across backends or across
/// queries — <see cref="IMemoryGraphStore.SeedAsync"/> makes the reported ordering explicitly
/// backend-specific. It is a monotone transform of ONE query's own result order, which is all the ranking
/// policies need. <c>InMemoryMemoryGraphStore</c> has no rank order to transform, so it does not use this —
/// it reports <c>1</c> for a match and <c>0</c> for a grade-admitted non-match directly. (A flat <c>1</c>
/// only for a query-less enumeration.)</para>
///
/// <para><b>The known limit</b> (<c>.claude/knowledge/pitfalls.md</c> §Storage): normalized by rank
/// POSITION, not score MARGIN, so the gap between consecutive positions is CANDIDATE-COUNT DEPENDENT — with
/// two candidates exactly 1.0 and 0.5, a fixed 2× spread however close the real scores are; with ten, 10%.
/// A test for any rank-lifting signal needs a result set large enough that the gap it fights is smaller
/// than the boost it proves. Two candidates is the WORST case, never a representative one.</para>
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
