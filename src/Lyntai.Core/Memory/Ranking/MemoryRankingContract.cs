namespace Lyntai.Memory.Ranking;

/// <summary>
/// The three obligations <see cref="IMemoryRankingPolicy"/> puts on EVERY implementation, in ONE place —
/// the non-finite input filter, the non-finite score guard, and the deterministic order plus relative
/// floor that finishes a ranking.
///
/// <para><b>Why this exists rather than three copies.</b> All three shipped policies
/// (<see cref="MultiplicativeRankingPolicy"/>, <see cref="ReciprocalRankFusionPolicy"/>,
/// <see cref="CompositeRankingPolicy"/>) carried byte-identical versions of each, every one annotated
/// "same … as the other policies". That is the shape
/// <c>.claude/knowledge/pitfalls.md</c> §Storage names for a stored VALUE — <i>one thing read at N sites
/// grows N rules, and the divergence is silent</i> — applied to a contract RULE instead: the id tiebreak
/// and the buried-not-cut floor are not each policy's own choice, they are what
/// <see cref="IMemoryRankingPolicy.Rank"/> promises its callers, and three copies is three chances for one
/// of them to stop promising it. The same discipline <c>MemorySignals.Salience</c> already applies to the
/// salience coercion.</para>
///
/// <para><b>Internal on purpose.</b> A third-party policy owes the contract on
/// <see cref="IMemoryRankingPolicy"/>, not this class's shape — publishing a helper would make ONE way of
/// meeting the contract into permanent public surface under SemVer, which is a bigger promise than the
/// contract itself needs. Reachable from the tests through <c>InternalsVisibleTo</c>.</para>
/// </summary>
internal static class MemoryRankingContract
{
    /// <summary>The candidates a policy may score at all: those whose OWN <see cref="GraphNode.Relevance"/>
    /// and <see cref="MemoryCandidate.Retrievability"/> are finite.
    /// <para>Both are BYO-supplied — <see cref="Lyntai.Memory.IMemoryGraphStore"/> reports the first and
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> the second, and neither contract
    /// promises finiteness — so a poisoned candidate is reachable without anything this library ships
    /// misbehaving. Dropping it HERE costs the corrupted candidate alone: carried further, a
    /// <c>+Infinity</c> survives ordering as the undisputed best, becomes <c>best</c> in
    /// <see cref="Finish"/>, makes the floor <c>+Infinity</c> too, and then cuts every healthy candidate —
    /// so one bad row empties the whole recall.</para>
    /// <para>A <see cref="double.NaN"/> is excluded here too, rather than left to sort itself to the
    /// bottom: that only happens to work under a policy that multiplies the raw signals into its score. A
    /// rank-fusion policy never multiplies a raw signal in, so a poisoned candidate would merely rank worst
    /// on that ONE signal and still come back with a perfectly finite score — surfacing material the caller
    /// should never have trusted.</para></summary>
    internal static List<MemoryCandidate> Rankable(IReadOnlyList<MemoryCandidate> candidates)
    {
        var rankable = new List<MemoryCandidate>(candidates.Count);
        foreach (var c in candidates)
            if (double.IsFinite(c.Node.Relevance) && double.IsFinite(c.Retrievability))
                rankable.Add(c);
        return rankable;
    }

    /// <summary>Record a scored candidate, unless the SCORE itself came out non-finite.
    /// <para><b><see cref="Rankable"/> genuinely cannot cover this</b>, which is why it is a second guard
    /// and not a redundant one: every input can be finite while the result is not. Two factors of
    /// <c>1e308</c> both pass <see cref="double.IsFinite"/> and multiply to <c>+Infinity</c>; two weighted
    /// reciprocal-rank terms of <c>double.MaxValue / 1.5</c> overflow their own SUM from four perfectly
    /// legal option values (every weight is validated finite and non-negative, deliberately with no upper
    /// bound). So <see cref="IMemoryRankingPolicy.Rank"/>'s "no returned score may be non-finite" has to be
    /// enforced where the score is, not only on what went into it.</para></summary>
    internal static void AddIfFinite(List<RankedMemory> scored, MemoryCandidate candidate, double score)
    {
        if (double.IsFinite(score)) scored.Add(new RankedMemory(candidate, score));
    }

    /// <summary>Put a scored set into its final, contractual order and apply the policy's relative floor.
    ///
    /// <para><b>The id tiebreak is mandatory, not decorative.</b> <see cref="List{T}.Sort(Comparison{T})"/>
    /// is UNSTABLE, so two candidates scored identically can swap places between calls depending on how the
    /// sort happened to partition the input — and the same recall then returns a different page each time,
    /// which <see cref="IMemoryRankingPolicy.Rank"/> forbids.</para>
    ///
    /// <para><b>BURIED, NOT CUT.</b> An entry is dropped because something OUTRANKS it by more than
    /// <paramref name="relativeFloor"/> allows, never because its own score crossed some absolute line —
    /// the floor is always relative to the best score in this same set. A negative floor is clamped to 0
    /// (keep everything) rather than rejected: the policies validate their own option values, and a floor
    /// is a bound, so the safe reading of a nonsensical one is "no bound".</para></summary>
    /// <param name="scored">The scored candidates, in any order; sorted in place.</param>
    /// <param name="relativeFloor">The fraction of the best score a candidate must reach to survive.</param>
    internal static IReadOnlyList<RankedMemory> Finish(List<RankedMemory> scored, double relativeFloor)
    {
        if (scored.Count == 0) return [];

        scored.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : b.Candidate.Node.Id.CompareTo(a.Candidate.Node.Id);
        });

        // `best` is finite because AddIfFinite dropped anything that was not — NOT "by construction", which
        // is what two of these policies' own comments used to claim before an unbounded weight falsified it.
        var floor = scored[0].Score * Math.Max(0, relativeFloor);
        return scored.Where(x => x.Score >= floor).ToList();
    }
}
