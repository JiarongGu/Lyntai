namespace Lyntai.Memory.Ranking;

/// <summary>Constants of <see cref="CompositeRankingPolicy"/>'s fusion, in the same <c>score = Σₛ wₛ / (K +
/// rankₛ)</c> shape as <see cref="ReciprocalRankFusionOptions"/> — except here the two "signals" being fused
/// are two WHOLE ranking policies' own RANK POSITIONS on the candidate set, never their raw scores. See
/// <see cref="CompositeRankingPolicy"/>'s own remarks for why: <see cref="MultiplicativeRankingPolicy"/>'s
/// score is a product roughly in <c>[0,1]</c>, <see cref="ReciprocalRankFusionPolicy"/>'s sums to around
/// <c>0.06</c> at its own shipped defaults, and <see cref="IMemoryRankingPolicy"/>'s own contract already says
/// a score means nothing outside the policy that produced it — averaging the two numbers directly would be
/// arithmetic over quantities that share no scale.</summary>
public sealed record CompositeRankingOptions
{
    private readonly double _k = 60;
    private readonly double _primaryWeight = 1;
    private readonly double _secondaryWeight = 1;
    private readonly double _relativeFloor = 0;

    /// <summary>The fusion constant, added to every rank before it is inverted — identical role and default to
    /// <see cref="ReciprocalRankFusionOptions.K"/>, so the same "how much rank 1 dominates" reasoning applies
    /// here with two signals instead of four.
    /// <para><b>Must be FINITE and <c>&gt; 0</c>.</b> Zero abandons the deliberate flattening the constant
    /// exists for; a negative value flips the sign of a term's denominator once a rank exceeds <c>-K</c>, so a
    /// candidate ranked FARTHER down one member can score HIGHER on it than one ranked near the top — the
    /// same failure <see cref="ReciprocalRankFusionOptions.K"/>'s own guard prevents.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double K
    {
        get => _k;
        init => _k = MemoryOption.Require(value, MemoryOptionRange.Positive, nameof(CompositeRankingOptions),
            "zero abandons the deliberate flattening the constant exists for, and a negative value makes "
            + "a candidate ranked farther down a member score HIGHER on it than one ranked near the top "
            + "once rank exceeds -K.");
    }

    /// <summary>The PRIMARY member's contribution to the fused score — the numerator of its own <c>w / (K +
    /// rank)</c> term, where <c>rank</c> is that member's own 1-based RANK POSITION for a candidate (see
    /// <see cref="CompositeRankingPolicy"/>'s own remarks on how that position is derived from the member's
    /// output).
    /// <para><b>Must be FINITE and <c>&gt;= 0</c>.</b> A negative weight would not merely weaken the primary
    /// member's pull, it would INVERT it: a smaller (better) rank produces a LARGER term, and multiplying
    /// that by a negative weight SUBTRACTS more from the fused score the BETTER a candidate ranks under the
    /// primary member — the opposite of what the weight promises.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double PrimaryWeight
    {
        get => _primaryWeight;
        init => _primaryWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(CompositeRankingOptions), Inverts);
    }

    /// <summary>The SECONDARY member's contribution to the fused score — identical domain and failure mode to
    /// <see cref="PrimaryWeight"/>'s own doc, for the other wrapped policy.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double SecondaryWeight
    {
        get => _secondaryWeight;
        init => _secondaryWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(CompositeRankingOptions), Inverts);
    }

    /// <summary>How far below the fused set's own STRONGEST score an entry may fall before this policy drops
    /// it, as a fraction of that best score — identical semantics to
    /// <see cref="ReciprocalRankFusionOptions.RelativeFloor"/>, and the same reason it defaults to <c>0</c>
    /// rather than <see cref="MultiplicativeRankingOptions.RelativeFloor"/>'s <c>0.02</c>: fusing by rank
    /// position compresses the achievable score range far tighter than a product of near-independent
    /// <c>[0,1]</c> factors, so a floor copied from the multiplicative policy would rarely cross a single
    /// fused score and burial would go silently inert rather than merely weaker.
    /// <para><b>Must be FINITE and in <c>[0, 1)</c>.</b> <c>0</c> means the floor is off — every
    /// non-negative-scoring candidate survives, the shipped default, not an error. At <c>1</c> or above the
    /// floor equals or exceeds the very score that defines it, so only a candidate tied exactly with the
    /// maximum survives.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value, a value of 1 or above, or a
    /// non-finite value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double RelativeFloor
    {
        get => _relativeFloor;
        init => _relativeFloor = MemoryOption.Require(value, MemoryOptionRange.FromInclusive(0, 1), nameof(CompositeRankingOptions),
            "a value of 1 or above collapses the floor into \"keep almost nothing\" with no signal "
            + "anywhere, and this policy's own compressed score range (see the property's own doc) makes "
            + "even a small nonzero floor bite far harder than the same number would under "
            + "MultiplicativeRankingOptions.");
    }

    // The two weights share one reason because they have one failure mode; the guard itself is shared with
    // every other memory options record through MemoryOption.
    private const string Inverts =
        "a negative weight would invert this member's pull (a candidate ranking BETTER under it scoring "
        + "LOWER overall) rather than merely weakening it.";
}

/// <summary>
/// Fuses two OTHER ranking policies into one order, so a consumer can blend two notions of "best" without
/// either policy knowing the other exists.
/// <para><b>Fuses by RANK POSITION, never raw score — the only sound way to combine two policies' output.</b>
/// <see cref="IMemoryRankingPolicy.Rank"/>'s contract already says a score means nothing outside the policy
/// that produced it: <see cref="MultiplicativeRankingPolicy"/> reports a bounded product roughly in
/// <c>[0,1]</c>, <see cref="ReciprocalRankFusionPolicy"/> a sum of reciprocal ranks around <c>0.06</c> at its
/// defaults. Combining those arithmetically would be a plausible-looking computation over quantities sharing
/// no scale, so this class re-derives each member's own COMPETITION rank position (see
/// <see cref="CompetitionRanks"/>) and fuses THOSE: <c>score = wₚ / (K + rankₚ) + wₛ / (K + rankₛ)</c>.</para>
/// <para><b>A candidate either member's floor drops is not excluded — it is ranked WORST for that
/// member</b>, contributing one past that member's worst returned rank (<c>member.Count + 1</c>), tied with
/// anything else that member dropped and never fabricated as better. A member dropping EVERY candidate
/// contributes rank 1 to everyone: a signal with no information must not move the ordering.</para>
/// <para><b>A candidate whose OWN <see cref="GraphNode.Relevance"/> or
/// <see cref="MemoryCandidate.Retrievability"/> is non-finite is excluded before either member sees it</b> —
/// kept here as well as in both members because this class never reads either raw field, so it cannot catch
/// a poisoned candidate through arithmetic the way <see cref="MultiplicativeRankingPolicy"/> does.</para>
/// <para><b>Owns the floor, not the grade exemption</b> — the same division of responsibility every ranking
/// policy in this domain documents: an authoritative candidate this policy drops is
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s job to re-admit, never this class's.</para>
/// </summary>
/// <param name="primary">The first member. Weighted by <see cref="CompositeRankingOptions.PrimaryWeight"/>.
/// </param>
/// <param name="secondary">The second member. Weighted by <see cref="CompositeRankingOptions.SecondaryWeight"/>.
/// Which policy is "primary" and which is "secondary" is meaningful whenever the two weights differ — swap
/// them to swap which member's rank position the larger weight amplifies.</param>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class CompositeRankingPolicy(
    IMemoryRankingPolicy primary,
    IMemoryRankingPolicy secondary,
    CompositeRankingOptions? options = null) : IMemoryRankingPolicy
{
    private readonly IMemoryRankingPolicy _primary = primary ?? throw new ArgumentNullException(nameof(primary));
    private readonly IMemoryRankingPolicy _secondary =
        secondary ?? throw new ArgumentNullException(nameof(secondary));
    private readonly CompositeRankingOptions _options = Validated(options ?? new CompositeRankingOptions());

    /// <summary>Guards the one invariant no single property's own <c>init</c> can enforce, because it spans
    /// both: at least one weight must be above zero. Both at zero would score every candidate exactly
    /// <c>0</c> and hand ordering entirely to the id tiebreak — not an error, not an empty result, just a
    /// ranking that silently stopped reading either member at all. The same reasoning, and the same
    /// construction-time (not property-level) placement, as
    /// <see cref="ReciprocalRankFusionPolicy"/>'s own four-weight guard.</summary>
    /// <exception cref="ArgumentException">Both <see cref="CompositeRankingOptions.PrimaryWeight"/> and
    /// <see cref="CompositeRankingOptions.SecondaryWeight"/> are zero.</exception>
    private static CompositeRankingOptions Validated(CompositeRankingOptions options)
    {
        if (options.PrimaryWeight <= 0 && options.SecondaryWeight <= 0)
            throw new ArgumentException(
                "CompositeRankingOptions must set PrimaryWeight or SecondaryWeight above zero — with both " +
                "at zero every candidate scores exactly 0 and ordering falls entirely to the id tiebreak, a " +
                "silent failure rather than a loud one.", nameof(options));
        return options;
    }

    /// <inheritdoc />
    public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
        in MemoryRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return [];

        // The shared non-finite exclusion both members already apply on their own inputs — applied here too
        // because this class never touches Relevance/Retrievability itself, so it cannot rely on the
        // arithmetic side effect MultiplicativeRankingPolicy gets for free.
        var rankable = MemoryRankingContract.Rankable(candidates);
        if (rankable.Count == 0) return [];

        var primaryRanked = _primary.Rank(rankable, context);
        var secondaryRanked = _secondary.Rank(rankable, context);

        var primaryRank = CompetitionRanks(primaryRanked);
        var secondaryRank = CompetitionRanks(secondaryRanked);
        // one past the worst rank a member actually handed out — ties every candidate THAT member dropped,
        // never invented as better or worse than "worse than everything it kept". A member that dropped
        // EVERYONE (Count == 0) resolves this to 1, so a wholly uninformative member contributes the SAME
        // constant to every candidate rather than distorting the order.
        var primaryWorst = primaryRanked.Count + 1;
        var secondaryWorst = secondaryRanked.Count + 1;

        var scored = new List<RankedMemory>(rankable.Count);
        foreach (var c in rankable)
        {
            var r1 = primaryRank.TryGetValue(c.Node.Id, out var v1) ? v1 : primaryWorst;
            var r2 = secondaryRank.TryGetValue(c.Node.Id, out var v2) ? v2 : secondaryWorst;
            var score = _options.PrimaryWeight / (_options.K + r1) + _options.SecondaryWeight / (_options.K + r2);
            // The shared post-hoc guard, for the reason this class shares with ReciprocalRankFusionPolicy:
            // both weights are validated finite and >= 0 with NO upper bound and K may be any finite positive
            // number, so two terms of double.MaxValue / 1.5 overflow their own sum. RelativeFloor ships at 0
            // here too, so the failure is +Infinity × 0 = NaN and a COMPLETELY empty recall — every healthy
            // candidate cut by one overflowed score.
            MemoryRankingContract.AddIfFinite(scored, c, score);
        }

        // The deterministic id tiebreak and the buried-not-cut floor, shared with every policy in this domain.
        return MemoryRankingContract.Finish(scored, _options.RelativeFloor);
    }

    /// <summary>Each candidate's 1-based RANK POSITION within one member's own OUTPUT, keyed by
    /// <see cref="GraphNode.Id"/> — COMPETITION ranking, exactly like
    /// <see cref="ReciprocalRankFusionPolicy"/>'s own signal ranking: a tied group (identical
    /// <see cref="RankedMemory.Score"/>) shares one rank, and the next distinct score skips ahead by the
    /// group's width.
    /// <para><b>Why this reads the member's own SCORE rather than trusting its output's list position.</b> A
    /// member's <c>Rank</c> already breaks ties by id internally to produce a deterministic list — so
    /// position 0, 1, 2, … in that list is always a distinct integer even when every candidate scored
    /// IDENTICALLY. Treating list position as rank would silently re-introduce the exact bug
    /// <see cref="ReciprocalRankFusionPolicy.RankPositions"/> exists to avoid: a member that is fully
    /// uninformative for this candidate set (ties everyone) would still hand out 1..n in full, contributing a
    /// FULL, distorting share of the fused score as a pure proxy for node id. Reading the member's own SCORE
    /// and grouping by equality is what makes a uniformly-tied member actually uninformative here too.</para>
    /// </summary>
    private static Dictionary<long, int> CompetitionRanks(IReadOnlyList<RankedMemory> ranked)
    {
        var ranks = new Dictionary<long, int>(ranked.Count);
        var currentRank = 1;
        for (var i = 0; i < ranked.Count; i++)
        {
            if (i > 0 && ranked[i].Score.CompareTo(ranked[i - 1].Score) != 0) currentRank = i + 1;
            ranks[ranked[i].Candidate.Node.Id] = currentRank;
        }
        return ranks;
    }
}
