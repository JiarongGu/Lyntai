using Lyntai.Memory;
using Lyntai.Memory.Ranking;

namespace Lyntai.Tests.Memory;

/// <summary>This ranking domain's SECOND implementation, chronologically — and, as of 3.0, the REGISTERED
/// default (owner ruling, 2026-08-11; see <c>docs/DECISIONS.md</c>): this library's own measurement
/// (<c>local/superpowers/records/2026-08-09-memory-policy-measurement.md</c>, fsrs-properly plan Task 4) found it beating
/// <see cref="MultiplicativeRankingPolicy"/> on the corpus's `topical` class in every shape tested, over two
/// independent runs. This file's job is only to pin what the formula itself does, exactly like
/// <c>MultiplicativeRankingPolicyTests</c> pins its sibling — default status is a separate, versioned fact
/// this file does not itself assert.</summary>
public class ReciprocalRankFusionPolicyTests
{
    private static GraphNode Node(long id, double relevance = 1, MemorySignals signals = default,
        int degree = 0) =>
        new(id, "e", "t", "s", $"headline {id}", $"content {id}", MemoryGrade.Associative,
            DateTimeOffset.UnixEpoch, RecallCount: 0, Stability: 20, Age: 0, Relevance: relevance,
            Degree: degree, Metadata: null, Signals: signals);

    private static MemoryCandidate Candidate(long id, double relevance = 1, double retrievability = 1,
        int hop = 0, MemorySignals signals = default, int degree = 0) =>
        new(Node(id, relevance, signals, degree), retrievability, hop);

    private static MemorySignals Salience(double value) =>
        MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, value);

    private static readonly MemoryRankingContext Context = new(Limit: 10, Engine: "test");

    private static ReciprocalRankFusionPolicy Default() => new();

    // ---- the shared contract every policy must satisfy ----

    [Fact] public void Deterministic() => MemoryRankingPolicyContract.Ordering_is_deterministic(Default());
    [Fact] public void Best_first() => MemoryRankingPolicyContract.Scores_are_ordered_best_first(Default());

    [Fact]
    public void Subset_no_duplicates() =>
        MemoryRankingPolicyContract.It_returns_a_subset_without_duplicates(Default());

    [Fact]
    public void Empty_in_empty_out() =>
        MemoryRankingPolicyContract.An_empty_candidate_set_ranks_to_empty(Default());

    [Fact]
    public void No_non_finite_score() =>
        MemoryRankingPolicyContract.No_returned_score_is_non_finite(Default());

    [Fact]
    public void Infinite_relevance_does_not_empty_a_healthy_recall() =>
        MemoryRankingPolicyContract.A_non_finite_relevance_that_would_otherwise_be_best_does_not_empty_a_healthy_recall(
            Default());

    [Fact]
    public void An_overflowing_product_of_finite_inputs_does_not_empty_a_healthy_recall() =>
        MemoryRankingPolicyContract.A_finite_input_whose_score_overflows_does_not_empty_a_healthy_recall(
            Default());

    /// <summary><b>This policy's own "finite by construction" claim was FALSE, and this fact is what makes it
    /// true.</b> <c>Rank</c>'s own comment argued that a sum of positive, bounded reciprocal terms can never
    /// turn non-finite — sound for the shipped weights, and wrong in general: every weight is validated finite
    /// and <c>&gt;= 0</c> with no upper bound, and <see cref="ReciprocalRankFusionOptions.K"/> may be any
    /// finite positive number, so two terms of <c>double.MaxValue / 1.5</c> overflow their own SUM. The
    /// consequence here is worse than <see cref="MultiplicativeRankingPolicy"/>'s, because this policy's
    /// shipped <see cref="ReciprocalRankFusionOptions.RelativeFloor"/> is <c>0</c>: <c>+Infinity × 0</c> is
    /// <c>NaN</c>, every <c>Score &gt;= NaN</c> is false, and the recall comes back COMPLETELY EMPTY rather
    /// than merely collapsed to the poisoned entry.</summary>
    [Fact]
    public void An_overflowing_weighted_sum_does_not_empty_a_healthy_recall()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            K = 0.5,
            RelevanceWeight = double.MaxValue,
            RetrievabilityWeight = double.MaxValue,
            SalienceWeight = 0,
            HopWeight = 0,
        });
        // best on both weighted signals: rank 1 on each, so MaxValue/1.5 + MaxValue/1.5 = +Infinity.
        var top = Candidate(1, relevance: 0.9, retrievability: 0.9);
        // rank 2 on each: MaxValue/2.5 + MaxValue/2.5 ≈ 1.44e308 — still finite, and the candidate an
        // unguarded NaN floor silently takes down with the overflowing one.
        var healthy = Candidate(2, relevance: 0.5, retrievability: 0.5);

        var ranked = policy.Rank([top, healthy], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        Assert.Contains(ranked, r => r.Candidate.Node.Id == 2);
    }

    // ---- the formula itself ----

    [Fact]
    public void An_exact_score_is_the_weighted_sum_of_reciprocal_ranks()
    {
        // Three candidates, each best on two signals and worst on the other two, so the fused score
        // genuinely exercises all four terms rather than one candidate simply dominating everywhere:
        //
        //              Relevance  Retrievability  Salience  Hop  |  ranks (rel, retr, sal, hop)
        //   A (id 1)       0.9           0.3           5     2   |   1     3      1     3
        //   B (id 2)       0.6           0.9           3     0   |   2     1      2     1
        //   C (id 3)       0.1           0.5           1     1   |   3     2      3     2
        //
        // (relevance/retrievability/salience rank DESCENDING — bigger is better; hop ranks ASCENDING —
        // smaller is better.) At the shipped defaults (K=60, every weight=1), candidate B's fused score is
        //   1/(60+2) + 1/(60+1) + 1/(60+2) + 1/(60+1) = 2/61 + 2/62 ≈ 0.0650449498
        var a = Candidate(1, relevance: 0.9, retrievability: 0.3, hop: 2, signals: Salience(5));
        var b = Candidate(2, relevance: 0.6, retrievability: 0.9, hop: 0, signals: Salience(3));
        var c = Candidate(3, relevance: 0.1, retrievability: 0.5, hop: 1, signals: Salience(1));

        var ranked = Default().Rank([a, b, c], in Context);

        var scoreB = ranked.Single(r => r.Candidate.Node.Id == 2).Score;
        Assert.Equal(2.0 / 61 + 2.0 / 62, scoreB, precision: 9);
        // and it is the best of the three — no single signal dominates, but B's mix does
        Assert.Equal(2, ranked[0].Candidate.Node.Id);
    }

    [Fact]
    public void Rank_compression_over_forty_candidates_keeps_the_score_span_under_a_factor_of_two()
    {
        // THE reason RelativeFloor defaults to 0 for this policy (see
        // ReciprocalRankFusionOptions.RelativeFloor's own doc): forty candidates, fully discriminated and
        // identically ordered on all four signals (candidate i ranks i-th on every one of them), so the
        // fused score is exactly 4/(60+i). Best (i=1) is 4/61; worst (i=40) is 4/100 — a ratio of 100/61 ≈
        // 1.6393, nowhere near the wide spread MultiplicativeRankingPolicy's product of near-independent
        // [0,1] factors produces. A 2% relative floor (Multiplicative's own default) would never cross a
        // single one of these forty scores.
        var candidates = Enumerable.Range(1, 40)
            .Select(i => Candidate(i, relevance: 41 - i, retrievability: 41 - i, hop: i - 1,
                signals: Salience(41 - i)))
            .ToArray();

        var ranked = Default().Rank(candidates, in Context);

        Assert.Equal(40, ranked.Count); // the default floor (0) cuts nothing
        var ratio = ranked[0].Score / ranked[^1].Score;
        Assert.InRange(ratio, 1.5, 2.0);
    }

    [Fact]
    public void The_shipped_default_RelativeFloor_is_zero_because_compression_would_make_a_nonzero_one_inert() =>
        // paired with the compression fact above: a 2% floor (Multiplicative's default) over a span this
        // tight would never cut anything, so burial would silently become inert rather than merely weaker.
        Assert.Equal(0, new ReciprocalRankFusionOptions().RelativeFloor);

    [Fact]
    public void A_candidate_winning_every_signal_ranks_first_and_one_losing_every_signal_ranks_last()
    {
        var winner = Candidate(1, relevance: 0.9, retrievability: 0.9, hop: 0, signals: Salience(5));
        var middle = Candidate(2, relevance: 0.5, retrievability: 0.5, hop: 3, signals: Salience(3));
        var loser = Candidate(3, relevance: 0.1, retrievability: 0.1, hop: 6, signals: Salience(1));

        var ranked = Default().Rank([loser, middle, winner], in Context); // deliberately not pre-sorted

        Assert.Equal(1, ranked[0].Candidate.Node.Id);
        Assert.Equal(3, ranked[^1].Candidate.Node.Id);
    }

    [Fact]
    public void Hop_ranks_ascending_so_the_nearer_candidate_outranks_the_farther_one()
    {
        // Isolates hop from the other three signals (their weight is 0, so their values cannot affect the
        // outcome) — the only fact in this file that discriminates purely on hop direction. THE target of
        // mutation #1: ranking hop DESCENDING instead of ASCENDING must flip this outcome, or hop is not
        // actually doing any work in the fixture.
        var options = new ReciprocalRankFusionOptions
        {
            HopWeight = 1, RelevanceWeight = 0, RetrievabilityWeight = 0, SalienceWeight = 0,
        };
        var near = Candidate(1, hop: 0);
        var far = Candidate(2, hop: 5);

        var ranked = new ReciprocalRankFusionPolicy(options).Rank([far, near], in Context);

        Assert.Equal(1, ranked[0].Candidate.Node.Id);
    }

    [Fact]
    public void Uniformly_tied_signals_contribute_nothing_to_the_ordering()
    {
        // THE fact that would have caught F1 — and it needs TWO signals tied at once to actually
        // discriminate the bug, mirroring the exact adversarial shape the fix report worked out by hand.
        // The library's own DEFAULT deployment ties exactly these two together: no embedder/vector store
        // makes StructuralSaliencePolicy report the identical neutral salience for every node, AND a
        // fresh graph (or Hops = 0) makes every hit hop 0 — salience and hop tie SIMULTANEOUSLY, not in
        // isolation. Five candidates, real signals (relevance, retrievability) assigned the OPPOSITE way
        // round from id (id 1 is the best performer, id 5 the worst) so a leaked id bias has to fight the
        // real signals rather than agree with them by accident.
        //
        // Under the OLD position-based ranking this fixture produces an EXACT tie between id 1 (best on
        // both real signals) and id 5 (worst on both), and between id 2 and id 4 — the two uniformly-tied
        // signals hand out ranks 1..5 by id descending, exactly cancelling the two real signals' own 1..5
        // — so the FINAL id-descending tiebreak alone decides, and the worst candidate (id 5) beats the
        // best one (id 1). Under competition ranking every candidate gets the SAME rank (1) on both tied
        // signals, contributing an identical constant each — provable by comparing against both weights
        // explicitly set to 0, which must produce the exact same ORDER (not the same scores — the constant
        // itself differs, only its effect on ordering must vanish).
        var candidates = Enumerable.Range(1, 5)
            .Select(id => Candidate(id, relevance: 6 - id, retrievability: 6 - id, hop: 0))
            .ToArray(); // id 1 is best on relevance/retrievability, id 5 is worst; hop and salience tied for all

        var rankedAtDefault = Default().Rank(candidates, in Context);
        var rankedWithTiedSignalsOff = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            RelevanceWeight = 1, RetrievabilityWeight = 1, SalienceWeight = 0, HopWeight = 0,
        }).Rank(candidates, in Context);

        var defaultOrder = rankedAtDefault.Select(r => r.Candidate.Node.Id).ToArray();
        Assert.Equal([1L, 2, 3, 4, 5], defaultOrder); // the real signals' own order, untouched by the tied pair
        Assert.Equal(rankedWithTiedSignalsOff.Select(r => r.Candidate.Node.Id).ToArray(), defaultOrder);
    }

    [Fact]
    public void A_partial_tie_shares_one_rank_and_the_next_candidate_skips_ahead_by_the_group_width()
    {
        // Isolates relevance (the other three signals carry weight 0). A and B tie for the BEST relevance —
        // competition ranking gives them BOTH rank 1, so their scores are EXACTLY equal, not merely close.
        // C is next, distinct — but the tied group ahead of it is WIDTH 2, so competition ranking skips
        // rank 2 entirely and C lands on rank 3 (score 1/63). D, the sole loser, is rank 4 (score 1/64).
        // A "dense" ranking (which increments by exactly 1 per distinct value, ignoring group width) would
        // wrongly give C rank 2 (score 1/62) instead — the skip is the whole point of COMPETITION ranking,
        // not just "ties share a number".
        var options = new ReciprocalRankFusionOptions
        {
            RelevanceWeight = 1, RetrievabilityWeight = 0, SalienceWeight = 0, HopWeight = 0,
        };
        var a = Candidate(1, relevance: 0.9);
        var b = Candidate(2, relevance: 0.9); // tied with a for rank 1
        var c = Candidate(3, relevance: 0.5); // rank 3 — skips rank 2, the tied group's own width
        var d = Candidate(4, relevance: 0.1); // rank 4

        var ranked = new ReciprocalRankFusionPolicy(options).Rank([a, b, c, d], in Context);

        var scoreA = ranked.Single(r => r.Candidate.Node.Id == 1).Score;
        var scoreB = ranked.Single(r => r.Candidate.Node.Id == 2).Score;
        var scoreC = ranked.Single(r => r.Candidate.Node.Id == 3).Score;
        var scoreD = ranked.Single(r => r.Candidate.Node.Id == 4).Score;

        Assert.Equal(scoreA, scoreB); // the tied pair share EXACTLY one rank, not two distinct ones
        Assert.Equal(1.0 / 63, scoreC, precision: 9); // rank 3 — would be 1/62 under a dense (non-skipping) ranking
        Assert.Equal(1.0 / 64, scoreD, precision: 9);
    }

    [Fact]
    public void A_candidate_with_non_finite_retrievability_is_excluded_even_at_the_default_zero_floor()
    {
        // Unlike MultiplicativeRankingPolicy, this policy's fusion never multiplies a raw signal into the
        // score, so a NaN retrievability does not corrupt the fused score into NaN — it would otherwise just
        // rank "worst" on that one signal and still come back with a small but finite, returnable score.
        // Ranking.Rank filters such a candidate out explicitly (see its own remarks) rather than relying on
        // RelativeFloor, which defaults to 0 here and would not exclude it by magnitude alone.
        var bad = Candidate(1, retrievability: double.NaN);
        var good = Candidate(2, retrievability: 1.0);

        var ranked = Default().Rank([bad, good], in Context);

        Assert.Equal([2L], ranked.Select(r => r.Candidate.Node.Id).ToArray());
        Assert.True(double.IsFinite(ranked.Single().Score));
    }

    [Fact]
    public void A_candidate_with_non_finite_relevance_is_excluded_even_at_the_default_zero_floor()
    {
        var bad = Candidate(1, relevance: double.NaN);
        var good = Candidate(2, relevance: 0.5);

        var ranked = Default().Rank([bad, good], in Context);

        Assert.Equal([2L], ranked.Select(r => r.Candidate.Node.Id).ToArray());
    }

    [Fact]
    public void The_shipped_defaults_are_K60_and_unit_weights()
    {
        var options = new ReciprocalRankFusionOptions();

        Assert.Equal(60, options.K);
        Assert.Equal(1, options.RelevanceWeight);
        Assert.Equal(1, options.RetrievabilityWeight);
        Assert.Equal(1, options.SalienceWeight);
        Assert.Equal(1, options.HopWeight);
        Assert.Equal(0, options.RelativeFloor);
    }

    // ---- construction guards ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_K_is_rejected_at_construction(double k)
    {
        // zero abandons Cormack/Clarke/Buettcher's deliberate flattening; negative flips the sign of the
        // denominator once rank exceeds -K, so a candidate ranked FARTHER down a signal can score HIGHER on
        // it than one ranked near the top.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusionOptions { K = k });
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1000.0)]
    public void K_accepts_any_finite_positive_value(double k) =>
        Assert.Equal(k, new ReciprocalRankFusionOptions { K = k }.K);

    public static readonly TheoryData<double> InvalidWeights =
        [-0.1, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_RelevanceWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusionOptions { RelevanceWeight = weight });

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_RetrievabilityWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReciprocalRankFusionOptions { RetrievabilityWeight = weight });

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_SalienceWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusionOptions { SalienceWeight = weight });

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_HopWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReciprocalRankFusionOptions { HopWeight = weight });

    [Fact]
    public void A_single_weight_may_individually_be_zero_without_throwing() =>
        // only the CONSTRUCTION-TIME composite guard (below) cares whether EVERY weight is zero at once —
        // any one property on its own accepts 0, the same "off" meaning HopWeight etc. carry individually.
        Assert.Equal(0, new ReciprocalRankFusionOptions { SalienceWeight = 0 }.SalienceWeight);

    [Fact]
    public void All_four_weights_at_zero_is_rejected_at_policy_construction()
    {
        // NOT a property-level guard: each of the four weights is individually valid at 0, so this can only
        // be caught once the whole options object is assembled — checked in the POLICY's own constructor
        // rather than any one property's init, which would depend on object-initializer order.
        var options = new ReciprocalRankFusionOptions
        {
            RelevanceWeight = 0, RetrievabilityWeight = 0, SalienceWeight = 0, HopWeight = 0,
        };

        Assert.Throws<ArgumentException>(() => new ReciprocalRankFusionPolicy(options));
    }

    [Fact]
    public void A_single_nonzero_weight_is_sufficient_at_policy_construction() =>
        // three of four at zero does not trip the "all zero" guard — only all four together do
        new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            RelevanceWeight = 0, RetrievabilityWeight = 0, SalienceWeight = 0, HopWeight = 1,
        }).Rank([Candidate(1)], in Context);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_RelativeFloor_is_rejected_at_construction(double relativeFloor) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReciprocalRankFusionOptions { RelativeFloor = relativeFloor });

    [Fact]
    public void A_zero_RelativeFloor_is_accepted_as_the_shipped_default() =>
        Assert.Equal(0, new ReciprocalRankFusionOptions { RelativeFloor = 0 }.RelativeFloor);

    // ── the fan effect (DiagnosticityWeight) ────────────────────────────────────────────────────────────

    /// <summary><b>OFF by default, and byte-identical when unset.</b> The whole reason this can be added to a
    /// registered default policy without a measurement first: at weight 0 the new term contributes exactly
    /// zero and every existing arm is unchanged. Asserted rather than assumed, because "additive and
    /// defaulted" is a claim about arithmetic, not an intention.</summary>
    [Fact]
    public void Diagnosticity_is_off_by_default_and_changes_nothing_when_unset()
    {
        Assert.Equal(0, new ReciprocalRankFusionOptions().DiagnosticityWeight);

        var policy = new ReciprocalRankFusionPolicy();
        var hub = Candidate(1, degree: 50);
        var leaf = Candidate(2, degree: 1);

        var ranked = policy.Rank([hub, leaf], in Context);

        // identical on every other signal, so with the fan term off the two are tied and neither is demoted
        Assert.Equal(2, ranked.Count);
        Assert.Equal(ranked[0].Score, ranked[1].Score, precision: 12);
    }

    /// <summary>ACT-R's FAN EFFECT: a node associated with many things is less diagnostic of any one cue, so
    /// it should spread less. Lyntai builds hubs deliberately (subject annotation exists to produce shared
    /// handles), and nothing anywhere consulted <see cref="GraphNode.Degree"/> — a node with fifty
    /// neighbours contributed exactly as much as a node with one.
    /// <para>The argument for adopting it is information-theoretic rather than biomimetic: a node adjacent to
    /// everything discriminates nothing. That is why it is worth having even though this library is not
    /// trying to be a cognitive model.</para></summary>
    [Fact]
    public void A_high_fan_node_ranks_below_an_otherwise_identical_low_fan_one()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            DiagnosticityWeight = 1, RelevanceWeight = 0, RetrievabilityWeight = 0,
            SalienceWeight = 0, HopWeight = 0,
        });
        var hub = Candidate(1, degree: 50);
        var leaf = Candidate(2, degree: 1);

        var ranked = policy.Rank([hub, leaf], in Context);

        Assert.Equal(2, ranked[0].Candidate.Node.Id);   // the LEAF leads — it is the diagnostic one
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    /// <summary>An unconnected node (degree 0) is the MOST diagnostic, not a special case to exclude. It
    /// reached the candidate set on its own textual merits and has no hub inflating it.</summary>
    [Fact]
    public void An_unconnected_node_is_treated_as_maximally_diagnostic()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            DiagnosticityWeight = 1, RelevanceWeight = 0, RetrievabilityWeight = 0,
            SalienceWeight = 0, HopWeight = 0,
        });

        var ranked = policy.Rank([Candidate(1, degree: 9), Candidate(2, degree: 0)], in Context);

        Assert.Equal(2, ranked[0].Candidate.Node.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_negative_or_non_finite_diagnosticity_weight_is_refused(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReciprocalRankFusionOptions { DiagnosticityWeight = weight });
}
