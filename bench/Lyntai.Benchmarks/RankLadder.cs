using Lyntai.Memory;
using Lyntai.Memory.Ranking;

namespace Lyntai.Benchmarks;

/// <summary>The RRF scoring replica the `--ranks` ladders share: given the candidate pool one recall saw, what
/// would <c>ReciprocalRankFusionPolicy</c> have returned at some other <c>K</c>?
///
/// <para><b>Scored OFFLINE from one candidate set, which is not a shortcut but the only clean way.</b>
/// Re-running an arm per K would need a fresh store per K, because a recall reinforces what it returns and
/// that contaminates every later arm (<c>docs/task-archive.md</c> Part 110). RRF at another K is a pure
/// function of ranks already in hand, so nothing is lost.</para>
///
/// <para><b>Shared because the alternative is two replicas of one formula.</b> Two benchmarks score
/// different METRICS over the same ranking — LongMemEval asks whether the current fact outranks the
/// superseded one, LoCoMo whether the evidence turn is in the page — and a second copy of the scoring is a
/// second chance to get the tiebreak or the rank definition wrong in only one of them.</para></summary>
internal sealed class RankLadder
{
    /// <summary>The K values a ladder scores; 60 is the shipped default and doubles as the CONTROL.
    /// <para>It runs UPWARD as well as down because the downward half refuted the obvious reading. As
    /// K → ∞, <c>1/(K+r) ≈ (1/K)(1 − r/K)</c>, so the order tends to the SUM OF RANKS — Borda count. Low K
    /// is the opposite regime: being top-few on one signal outweighs being mediocre on the rest.</para>
    /// </summary>
    internal static readonly double[] K = [1, 3, 10, 30, 60, 120, 300, 1000];

    /// <summary>The shipped <c>ReciprocalRankFusionOptions.K</c>, and the row every ladder is checked at.</summary>
    internal const double Shipped = 60;

    private readonly int[] _rel;
    private readonly int[] _ret;
    private readonly int[] _hop;

    /// <summary>Positions are computed ONCE and reused across the ladder — they do not depend on K.</summary>
    internal RankLadder(IReadOnlyList<MemoryCandidate> candidates)
    {
        // Mirror MemoryRankingContract.Rankable: a non-finite Relevance or Retrievability is dropped BEFORE
        // positions are computed, so ranking over the unfiltered set would shift every position below it.
        Pool = [.. candidates.Where(x => double.IsFinite(x.Node.Relevance) && double.IsFinite(x.Retrievability))];
        _rel = Positions(Pool, x => x.Node.Relevance, ascending: false);
        _ret = Positions(Pool, x => x.Retrievability, ascending: false);
        _hop = Positions(Pool, x => x.Hop, ascending: true);
    }

    /// <summary>The candidates that survived the finite filter, in the order the engine supplied them.</summary>
    internal IReadOnlyList<MemoryCandidate> Pool { get; }

    /// <summary>What the fused ranking would return at <paramref name="k"/>.
    /// <para>The tiebreak is DESCENDING id, matching <c>MemoryRankingContract.Finish</c> — so on a score tie
    /// the newer entry wins. Getting this backwards moved a shipped row by 4 points while looking entirely
    /// plausible, which is the whole reason <see cref="AgreesWithShipped"/> exists.</para></summary>
    internal List<MemoryCandidate> TopAt(double k, int limit) =>
        [.. Enumerable.Range(0, Pool.Count)
            .OrderByDescending(i => 1 / (k + _rel[i]) + 1 / (k + _ret[i]) + 1 / (k + _hop[i]))
            .ThenByDescending(i => Pool[i].Node.Id)
            .Take(limit)
            .Select(i => Pool[i])];

    /// <summary>The CONTROL: at the shipped K this replica must reproduce the real policy's own top-N, or the
    /// ladder is a table about a formula this library does not run. Compared as a SET, since the metrics
    /// above it ask membership rather than order.</summary>
    internal static bool AgreesWithShipped(
        IReadOnlyList<MemoryCandidate> top, IReadOnlyList<RankedMemory> real, int limit) =>
        top.Select(c => c.Node.Id).OrderBy(x => x)
            .SequenceEqual(real.Take(limit).Select(r => r.Candidate.Node.Id).OrderBy(x => x));

    /// <summary>Where one value sits among the pool, by the same competition rule as
    /// <see cref="Positions"/> — for a diagnostic that reports a single candidate's rank.</summary>
    internal static int RankOf(IReadOnlyList<MemoryCandidate> all, Func<MemoryCandidate, double> of, double v)
        => 1 + all.Count(x => of(x) > v);

    /// <summary>Competition ranking, matching <c>ReciprocalRankFusionPolicy.RankPositions</c>: every member
    /// of a tie block takes the block's first position.
    /// <para>Competition rather than positional because ties are the norm here, not the exception: every
    /// walked candidate reports <c>Relevance 0</c> with <c>Matched null</c> (<b>D97</b>), so a positional
    /// rank would depend on sort order among equals and mean nothing.</para></summary>
    internal static int[] Positions(
        IReadOnlyList<MemoryCandidate> all, Func<MemoryCandidate, double> of, bool ascending)
    {
        var v = all.Select(of).ToArray();
        var r = new int[v.Length];
        for (var i = 0; i < v.Length; i++)
            r[i] = 1 + v.Count(x => ascending ? x < v[i] : x > v[i]);
        return r;
    }
}
