namespace Lyntai.Memory.Ranking;

/// <summary>Constants of <see cref="ReciprocalRankFusionPolicy"/>'s formula: <c>score = Σₛ wₛ / (K +
/// rankₛ)</c>, summed over five signals — relevance, retrievability, salience, hop and diagnosticity — each
/// contributing its own 1-based RANK POSITION within the candidate set, never its raw value. Ranking by
/// POSITION rather
/// than value is the whole reason this policy exists beside <see cref="MultiplicativeRankingPolicy"/>: a
/// bm25-derived relevance, a [0,1] retrievability, an unbounded salience and an integer hop count share no
/// numeric scale, so a product of them (Multiplicative's own formula) implicitly assumes one that fusing by
/// rank never has to.</summary>
public sealed record ReciprocalRankFusionOptions
{
    private readonly double _k = 60;
    private readonly double _relevanceWeight = 1;
    private readonly double _retrievabilityWeight = 1;
    private readonly double _salienceWeight = 0;   // D89 — measured; see the property's own doc
    private readonly double _hopWeight = 1;
    // 0 = off. Unmeasured, so it ships inert — see DiagnosticityWeight's own remarks.
    private readonly double _diagnosticityWeight;
    private readonly double _relativeFloor = 0;

    /// <summary>The fusion constant, added to every rank before it is inverted. <c>60</c> — the default here
    /// — is Cormack, Clarke &amp; Buettcher's own published value (2009): large enough that rank 1 and rank 40
    /// stay within a narrow band of each other (a <c>100/61 ≈ 1.639×</c> spread — see
    /// <see cref="ReciprocalRankFusionPolicy"/>'s remarks on why <see cref="RelativeFloor"/> defaults to 0
    /// because of it), rather than the near-total dominance a small constant would hand the top rank.
    /// <para><b>It selects a REGIME, not a sharpness, and the two ends want different things.</b> At a small
    /// <c>K</c> a candidate that is top-few on ONE signal outscores one that is merely good on all of them.
    /// As <c>K</c> grows the curve flattens toward <c>(1/K)(1 − rank/K)</c>, so the order tends to the SUM of
    /// ranks — Borda count — which rewards the opposite. Tune it for which of those a workload wants; a
    /// larger value is not "more of" a smaller one.</para>
    /// <para><b>Must be FINITE and <c>&gt; 0</c>.</b> At <c>K = 0</c> the curve stops being Cormack et al.'s
    /// measured one at all — rank 1 scores exactly <c>1</c> per signal while rank 2 already halves to
    /// <c>0.5</c>, the deliberate flattening gone. A NEGATIVE <c>K</c> is worse than merely a different curve:
    /// once a candidate's rank on some signal exceeds <c>-K</c>, that signal's denominator changes SIGN, so a
    /// candidate ranked FARTHER DOWN a signal can score HIGHER on it than one ranked near the top — ranking
    /// stops being monotone in rank position at all, the same shape of failure
    /// <see cref="MultiplicativeRankingOptions.HopAttenuation"/>'s own negative-base guard exists to
    /// prevent.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double K
    {
        get => _k;
        init => _k = MemoryOption.Require(value, MemoryOptionRange.Positive, nameof(ReciprocalRankFusionOptions),
            "see the property's XML doc for why zero abandons the curve's deliberate flattening and a "
            + "negative value makes a candidate ranked farther down a signal score HIGHER on it than one "
            + "ranked near the top once rank exceeds -K. 60 is Cormack, Clarke & Buettcher's published "
            + "value.");
    }

    /// <summary>This signal's contribution to the fused score, as the numerator of its own <c>wₛ / (K +
    /// rankₛ)</c> term — <see cref="GraphNode.Relevance"/>, ranked DESCENDING (a higher relevance is a
    /// better, numerically SMALLER rank).
    /// <para><b>Must be FINITE and <c>&gt;= 0</c>.</b> A negative weight would not merely weaken this
    /// signal's pull, it would INVERT it: a smaller (better) <c>rankₛ</c> produces a LARGER <c>1/(K +
    /// rankₛ)</c> term, and multiplying that by a negative weight SUBTRACTS more from the fused score the
    /// BETTER a candidate ranks on this signal — the exact opposite of what the weight promises.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double RelevanceWeight
    {
        get => _relevanceWeight;
        init => _relevanceWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(ReciprocalRankFusionOptions), Inverts);
    }

    /// <summary>This signal's contribution to the fused score — the candidate's already-evaluated
    /// <see cref="MemoryCandidate.Retrievability"/>, ranked DESCENDING (more retrievable is better).
    /// <para>Domain and failure mode identical to <see cref="RelevanceWeight"/>'s own doc: FINITE and
    /// <c>&gt;= 0</c>, and a negative value inverts rather than weakens the signal's pull.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double RetrievabilityWeight
    {
        get => _retrievabilityWeight;
        init => _retrievabilityWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(ReciprocalRankFusionOptions), Inverts);
    }

    /// <summary>This signal's contribution to the fused score — <see cref="MemorySignals.Salience"/>, ranked
    /// DESCENDING (more salient is better), the same direction
    /// <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/>'s own boost pulls, though the shape here
    /// is rank position rather than a logarithm of the raw value.
    ///
    /// <para><b>Defaults to 0 — salience does not vote on ranking</b>, matching
    /// <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/>, which has defaulted off since
    /// <b>D45</b> on the argument that salience means "does not fade away" rather than "first priority".
    /// Measured across two embedding models and five writing systems (<b>D89</b>): a non-zero weight costs
    /// recall in every cell tested. Set it above 0 for a deployment whose salience signal is known to track
    /// relevance — the other two consumers of salience, decay resistance and store admission, are
    /// unaffected either way.</para>
    ///
    /// <para>Domain and failure mode identical to <see cref="RelevanceWeight"/>'s own doc: FINITE and
    /// <c>&gt;= 0</c>, and a negative value inverts rather than weakens the signal's pull.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double SalienceWeight
    {
        get => _salienceWeight;
        init => _salienceWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(ReciprocalRankFusionOptions), Inverts);
    }

    /// <summary>This signal's contribution to the fused score — <see cref="MemoryCandidate.Hop"/>, ranked
    /// ASCENDING (nearer the seed is better: hop 0 is rank 1). <b>This is the one deliberate deviation from
    /// the design spec's three-signal list</b>: taken literally, ignoring hop would let a hop-2 match
    /// outrank a direct hit purely because rank fusion has no other mechanism to prefer nearer material —
    /// a behaviour change with nothing to do with fusing relevance, retrievability and salience. Every other
    /// signal here ranks DESCENDING (bigger is better); hop is the only one that ranks ASCENDING (smaller —
    /// nearer — is better), because it is a distance, not a strength.
    /// <para>Domain and failure mode identical to <see cref="RelevanceWeight"/>'s own doc: FINITE and
    /// <c>&gt;= 0</c>, and a negative value inverts rather than weakens the signal's pull — here, a negative
    /// weight would make a FARTHER candidate outrank a nearer one.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double HopWeight
    {
        get => _hopWeight;
        init => _hopWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(ReciprocalRankFusionOptions), Inverts);
    }

    /// <summary>How much a candidate's DIAGNOSTICITY counts — its <see cref="GraphNode.Degree"/>, ranked
    /// ASCENDING, so a node with few connections outranks an otherwise identical hub. <b>Defaults to 0
    /// (off)</b>, like <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/> before it: the term
    /// contributes exactly zero when unset, so adding it to the registered default policy changed no
    /// existing arm.
    ///
    /// <para><b>This is ACT-R's fan effect, and the measurement REFUSED it — the default stays 0</b>
    /// (<c>docs/DECISIONS.md</c> D62; re-run with <c>node devtools/dev.mjs memory-fan</c>). On
    /// <c>topical</c> the damage is cleanly monotonic in the weight; on <c>critical-rare</c> the response is
    /// non-monotonic, which reads as noise.</para>
    ///
    /// <para><b>Why it fails HERE, which is the part worth keeping.</b> The fan effect assumes degree
    /// measures how INDISCRIMINATE a node is. In this engine most edges come from CO-ACTIVATION, so degree
    /// ALSO measures how often an entry has been useful — penalising it penalises exactly the entries a
    /// caller keeps coming back to. The mechanism is not wrong; the proxy is, in a graph built this way.</para>
    ///
    /// <para><b>When it might still earn its keep</b>, so the option is not dead weight: a deployment whose
    /// edges come predominantly from SUBJECT ANNOTATION has a degree meaning "shares a handle with many
    /// things" rather than "has been recalled a lot" — the condition the fan effect actually describes.</para>
    /// <para>Domain and failure mode identical to <see cref="RelevanceWeight"/>: FINITE and <c>&gt;= 0</c>.
    /// A negative weight would invert the signal and promote hubs, which is the opposite of the point.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double DiagnosticityWeight
    {
        get => _diagnosticityWeight;
        init => _diagnosticityWeight = MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(ReciprocalRankFusionOptions), Inverts);
    }

    /// <summary>How far below the STRONGEST candidate's score an entry may fall before this policy drops it,
    /// as a fraction of that best score — the identical semantics to
    /// <see cref="MultiplicativeRankingOptions.RelativeFloor"/>, but a DIFFERENT default: <b><c>0</c> here,
    /// not <c>0.02</c></b>, and <c>0</c> is what ships.
    /// <para><b>Multiplicative's <c>0.02</c> would be INERT here, not merely gentle</b>, so burial under this
    /// policy has to be chosen deliberately rather than inherited: rank fusion compresses its own score range.
    /// With equal weights the tightest ratio a fused set of <c>n</c> candidates can produce is
    /// <c>(K + 1) / (K + n)</c> — worst-on-every-signal against best-on-every-signal — so at the shipped
    /// <see cref="K"/> of <c>60</c> with <c>n = 40</c> no floor below <c>0.61</c> can cut anything at all.
    /// Recompute <c>(K+1)/(K+n)</c> for your own values: a floor meant to bite here lives at a VERY different
    /// point of <c>[0,1)</c> than Multiplicative's.</para>
    /// <para><b>Must be FINITE and in <c>[0, 1)</c>.</b> <c>0</c> means the floor is off — every
    /// non-negative-scoring candidate survives, which this policy's shipped default IS, not an error. At
    /// <c>1</c> or above the floor equals or exceeds the very score that defines it, so only a candidate tied
    /// exactly with the maximum survives, the same collapse
    /// <see cref="MultiplicativeRankingOptions.RelativeFloor"/>'s own doc describes.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value, a value of 1 or above, or a
    /// non-finite value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double RelativeFloor
    {
        get => _relativeFloor;
        init => _relativeFloor = MemoryOption.Require(value, MemoryOptionRange.FromInclusive(0, 1), nameof(ReciprocalRankFusionOptions),
            "see the property's XML doc for why this policy's compressed score range makes even a small "
            + "nonzero floor collapse toward \"keep almost nothing\" long before 1, why 0 (off) is the "
            + "shipped default rather than a value copied from MultiplicativeRankingOptions, and what value "
            + "would actually bite if you want burial under this policy.");
    }

    // All five signal weights share one reason because they have one failure mode; the guard itself is
    // shared with every other memory options record through MemoryOption.
    private const string Inverts =
        "a negative weight would invert this signal's pull (a candidate ranking BETTER on it scoring LOWER "
        + "overall) rather than merely weakening it.";
}

/// <summary>
/// Reciprocal rank fusion — this ranking domain's second implementation, given a name and a swap point
/// beside <see cref="MultiplicativeRankingPolicy"/> rather than replacing it (implementations of a domain's
/// seam accumulate; which one is the DEFAULT is a separate, versioned decision).
/// <b>This is the registered default as of 3.0</b> (<c>docs/DECISIONS.md</c> D49);
/// <see cref="MultiplicativeRankingPolicy"/> stays shipped, unchanged and registerable in one line. See
/// <c>MemoryEngineRegistration.AddMemoryEngine</c>'s own remarks for the full reasoning.
/// <c>Score = Σₛ wₛ / (K + rankₛ)</c>, summed over retrievability, salience, hop and diagnosticity by rank
/// POSITION within the whole candidate set — see <see cref="ReciprocalRankFusionOptions"/>'s own remarks for
/// why that is the point of fusing by rank at all, and for hop's deliberate ascending direction, the one
/// deviation from the design spec's three-signal list.
/// <para><b>Relevance is the one term fused per SOURCE, not per candidate.</b> One <c>w/(K+rank)</c> term
/// sums for every <see cref="MemoryCandidate.Ranks"/> entry that matched — two sources agreeing earns two
/// terms rather than one averaged value, the formula this class is named for applied to actual ranked lists.
/// With NO candidate in the set carrying a rank — a BYO gather, or a caller constructing
/// <see cref="MemoryCandidate"/> directly — this falls back to <see cref="GraphNode.Relevance"/> ranked by
/// position, the pooled-field behaviour this class shipped with before
/// <see cref="MemoryCandidate.Ranks"/> existed.</para>
/// <para><see cref="ReciprocalRankFusionOptions.RelativeFloor"/> ships at <c>0</c> here, not
/// <see cref="MultiplicativeRankingOptions.RelativeFloor"/>'s <c>0.02</c> — see its own doc for why.</para>
/// <para><b>Owns the floor, not the grade exemption</b> — the same division of responsibility
/// <see cref="MultiplicativeRankingPolicy"/> documents: an <see cref="MemoryGrade.Authoritative"/> candidate
/// this policy drops is the caller's (<see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s) job to
/// re-admit, never this class's.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class ReciprocalRankFusionPolicy(ReciprocalRankFusionOptions? options = null) : IMemoryRankingPolicy
{
    private readonly ReciprocalRankFusionOptions _options = Validated(options ?? new ReciprocalRankFusionOptions());

    /// <summary>Guards the one invariant no single property's own <c>init</c> can enforce, because it spans
    /// all four: at least one weight must be above zero. All four at zero would score EVERY candidate exactly
    /// <c>0</c> and hand ordering entirely to the id tiebreak — not an error, not an empty result, just a
    /// ranking that silently stopped reading any signal at all. Checked here, once, against the fully
    /// constructed options object, rather than in any one property's <c>init</c> — a cross-property
    /// invariant checked from inside a single property's own accessor would depend on C# object-initializer
    /// ORDER, which is exactly the kind of guard that passes by accident depending on how a caller happens to
    /// list the properties.</summary>
    /// <exception cref="ArgumentException">Every one of <see cref="ReciprocalRankFusionOptions.RelevanceWeight"/>,
    /// <see cref="ReciprocalRankFusionOptions.RetrievabilityWeight"/>,
    /// <see cref="ReciprocalRankFusionOptions.SalienceWeight"/>,
    /// <see cref="ReciprocalRankFusionOptions.HopWeight"/> and
    /// <see cref="ReciprocalRankFusionOptions.DiagnosticityWeight"/> is zero.</exception>
    private static ReciprocalRankFusionOptions Validated(ReciprocalRankFusionOptions options)
    {
        // DiagnosticityWeight joined this list when it was added (2026-08-15). The guard's subject is "does
        // ANY signal contribute", so a new signal that is not listed makes the guard WRONG in the refusing
        // direction — it would reject a perfectly coherent diagnosticity-only configuration. A weight added
        // to the score without being added here is the mirror defect, and just as silent: the guard would
        // pass while the score was still identically zero.
        if (options.RelevanceWeight <= 0 && options.RetrievabilityWeight <= 0 &&
            options.SalienceWeight <= 0 && options.HopWeight <= 0 && options.DiagnosticityWeight <= 0)
            throw new ArgumentException(
                "ReciprocalRankFusionOptions must set at least one of RelevanceWeight, RetrievabilityWeight, " +
                "SalienceWeight, HopWeight or DiagnosticityWeight above zero — with all five at zero every " +
                "candidate scores exactly 0 and ordering falls entirely to the id tiebreak, a silent failure " +
                "rather than a loud one.", nameof(options));
        return options;
    }

    /// <inheritdoc />
    public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
        in MemoryRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return [];

        // A candidate whose OWN Relevance or Retrievability is non-finite cannot be placed in a rank
        // ordering at all — there is no valid position for "not a number" among 1..n — and this policy's
        // fusion never multiplies a raw signal into the score, so it would not even sort itself to the
        // bottom the way it does under a product. MemoryRankingContract.Rankable is the shared filter, and
        // its own remarks carry that reasoning. MemorySignals.Salience already coerces a non-finite salience
        // to the neutral value, and Hop is an int, so Relevance and Retrievability are the only two raw
        // doubles it needs to cover.
        var rankable = MemoryRankingContract.Rankable(candidates);
        if (rankable.Count == 0) return [];

        // Does ANY candidate carry per-source ranks? Asked once, over the whole set, because the
        // answer decides which relevance term the whole set is scored by and a per-candidate test
        // would silently mix two scales — the very defect this change removes.
        //
        // FALSE is the compatibility path: a hand-built engine, a BYO gather, or any caller
        // constructing MemoryCandidate directly never populates Ranks, and must rank exactly as it
        // did before this member existed.
        //
        // The MIXED case is normal, not a fallback trigger: hop neighbours carry no ranks and sit
        // beside seeds that do. They contribute no relevance term, which is what Matched null/false
        // means (D97) — not a score of zero, which would be a claim nobody measured.
        var anyRanks = false;
        foreach (var candidate in rankable)
            if (!candidate.Ranks.IsEmpty) { anyRanks = true; break; }

        int[] relevanceRank = anyRanks ? [] : RankPositions(rankable, static c => c.Node.Relevance, ascending: false);
        var retrievabilityRank = RankPositions(rankable, static c => c.Retrievability, ascending: false);
        var salienceRank = RankPositions(rankable, static c => MemorySignals.Salience(c.Node.Signals),
            ascending: false);
        // hop is a DISTANCE, not a strength — nearer (numerically smaller) is better, the one signal here
        // that ranks ascending. See ReciprocalRankFusionOptions.HopWeight's own doc for why this is a
        // deliberate deviation from the design spec's three-signal list rather than an oversight.
        var hopRank = RankPositions(rankable, static c => (double)c.Hop, ascending: true);
        // Degree ranks ASCENDING for the same reason hop does: it is a count to be small, not a strength to
        // be large. A node adjacent to everything discriminates nothing (ACT-R's fan effect), and degree 0 —
        // an entry that reached the set on its own textual merits with no hub inflating it — is therefore the
        // most diagnostic rather than a case to exclude.
        var diagnosticityRank = RankPositions(rankable, static c => (double)c.Node.Degree, ascending: true);

        var scored = new List<RankedMemory>(rankable.Count);
        for (var i = 0; i < rankable.Count; i++)
        {
            double relevance;
            if (anyRanks)
            {
                // One term per source that MATCHED this candidate — Cormack, Clarke & Buettcher's
                // formula over ranked lists, which is what this policy is named for and had never
                // been handed. A candidate two sources agree on earns two terms, so agreement is
                // rewarded rather than averaged away.
                relevance = 0;
                foreach (var entry in rankable[i].Ranks.Span)
                    relevance += _options.RelevanceWeight / (_options.K + entry.Rank);
            }
            else
            {
                relevance = _options.RelevanceWeight / (_options.K + relevanceRank[i]);
            }

            var score =
                relevance +
                _options.RetrievabilityWeight / (_options.K + retrievabilityRank[i]) +
                _options.SalienceWeight / (_options.K + salienceRank[i]) +
                _options.HopWeight / (_options.K + hopRank[i]) +
                _options.DiagnosticityWeight / (_options.K + diagnosticityRank[i]);
            // The shared post-hoc guard, and NOT because this formula is shaped like the multiplicative one.
            // The sum is NOT finite by construction — true at the
            // shipped weights, false in general: every weight is validated finite and >= 0 with NO upper
            // bound, and K may be any finite positive number, so two terms of double.MaxValue / 1.5 overflow
            // their own sum from four perfectly legal option values. The consequence is worse here than under
            // a product, because RelativeFloor ships at 0: +Infinity × 0 is NaN, every `Score >= NaN` is
            // false, and the recall comes back COMPLETELY empty rather than collapsed to the poisoned entry.
            MemoryRankingContract.AddIfFinite(scored, rankable[i], score);
        }

        // The deterministic id tiebreak and the buried-not-cut floor, shared with every policy in this
        // domain. The floor is off by DEFAULT here — see ReciprocalRankFusionOptions.RelativeFloor's own doc
        // for why 0, not Multiplicative's 0.02.
        return MemoryRankingContract.Finish(scored, _options.RelativeFloor);
    }

    /// <summary>Each candidate's 1-based RANK POSITION on one signal — COMPETITION ranking: a tied group
    /// shares one rank number, and the next distinct value skips ahead by the width of that group (two
    /// candidates tied for best both score rank 1, and the next candidate is rank 3, never rank 2 — "1, 1,
    /// 3", never "1, 1, 2" and never "1, 2, 3").
    /// <para><b>A signal on which EVERY candidate ties therefore cannot move the ordering at all</b>: every
    /// candidate takes the same rank, so the signal contributes the same constant term to every score —
    /// equivalent to setting its weight to 0, without a consumer having to notice its discriminating power
    /// vanished and disable it by hand. That case is ordinary here rather than exotic, because this policy
    /// fuses SIGNALS rather than already-total ranked lists: <see cref="MemorySignals.Salience"/> reports the
    /// identical neutral value for every candidate whenever nothing has judged any of them (no embedder, no
    /// vector store — the library's own default deployment), and every direct hit shares hop 0 on a fresh
    /// graph or with <c>Hops = 0</c>. A PARTIALLY tied signal degrades proportionally rather than totally:
    /// only the tied subset shares a rank, and everyone past them still pays for the width of the group they
    /// skipped over.</para>
    /// <para><b>Determinism is unaffected.</b> Equal values always produce equal ranks regardless of how the
    /// (unstable) sort below happens to order a tied group internally — the loop only advances
    /// <c>currentRank</c> when the value actually changes, never based on position — and the caller's own
    /// final sort still breaks ties in the FUSED score by <c>Node.Id</c> descending, so the overall result
    /// remains a total order.</para></summary>
    private static int[] RankPositions(IReadOnlyList<MemoryCandidate> candidates,
        Func<MemoryCandidate, double> key, bool ascending)
    {
        var n = candidates.Count;
        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = key(candidates[i]);

        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (x, y) => ascending ? values[x].CompareTo(values[y]) : values[y].CompareTo(values[x]));

        var rank = new int[n];
        var currentRank = 1;
        for (var i = 0; i < n; i++)
        {
            if (i > 0 && values[order[i]].CompareTo(values[order[i - 1]]) != 0) currentRank = i + 1;
            rank[order[i]] = currentRank;
        }
        return rank;
    }
}
