using Lyntai.Memory;
using Lyntai.Memory.Ranking;

namespace Lyntai.Tests.Memory;

/// <summary>This domain's first rank formula, given a name and a construction-time guard rather than
/// changed — no longer the REGISTERED default as of 3.0 (owner ruling, 2026-08-11;
/// <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> is, see that class's own remarks), but
/// unchanged, still shipped, and still registerable in one line. Every number and every comment in
/// <see cref="MultiplicativeRankingPolicy"/> was ported verbatim in behaviour from
/// <c>GraphMemoryEngine.RecallAsync</c>'s own hardcoded projection (still there — Task 3 of this plan wires
/// the engine to call this seam instead) — this file pins that the port kept it that way, not that the
/// formula is newly correct.</summary>
public class MultiplicativeRankingPolicyTests
{
    private static GraphNode Node(long id, double relevance = 1, MemorySignals signals = default) =>
        new(id, "e", "t", "s", $"headline {id}", $"content {id}", MemoryGrade.Associative,
            DateTimeOffset.UnixEpoch, RecallCount: 0, Stability: 20, Age: 0, Relevance: relevance,
            Degree: 0, Metadata: null, Signals: signals);

    private static MemoryCandidate Candidate(long id, double relevance = 1, double retrievability = 1,
        int hop = 0, MemorySignals signals = default) =>
        new(Node(id, relevance, signals), retrievability, hop);

    private static readonly MemoryRankingContext Context = new(Limit: 10, Engine: "test");

    // a nonzero weight so the shared contract facts actually exercise the salience-boost path, rather than
    // leaving it permanently inert at the shipped default (0) — see MultiplicativeRankingOptions
    // .SalienceRankWeight's own doc for why 0 makes the whole ln(salience) term dead code.
    private static MultiplicativeRankingPolicy Default() =>
        new(new MultiplicativeRankingOptions { SalienceRankWeight = 0.3 });

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

    /// <summary>The SECOND route into an overflowed score, and the one no input filter can reach at all
    /// because it does not come from a candidate: <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/>
    /// is validated finite and <c>&gt;= 0</c> but deliberately carries NO upper bound (its own doc argues a
    /// larger weight is only ever a monotone strengthening — true of the ORDER it produces, not of the
    /// arithmetic). A huge weight times a salience above <c>e</c> overflows <c>boost</c>, and the score with
    /// it. The candidate whose salience did it is not even the victim: it becomes <c>best</c>, and the
    /// unsalient-but-healthy candidate beside it is what gets cut.</summary>
    [Fact]
    public void An_overflowing_salience_boost_does_not_empty_a_healthy_recall()
    {
        var policy = new MultiplicativeRankingPolicy(
            new MultiplicativeRankingOptions { SalienceRankWeight = double.MaxValue });
        // ln(10) ≈ 2.3, so boost = 1 + MaxValue × 2.3 overflows to +Infinity while every INPUT stays finite.
        var salient = Candidate(1, signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 10));
        var healthy = Candidate(2, relevance: 0.9, retrievability: 0.9);

        var ranked = policy.Rank([salient, healthy], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        Assert.Contains(ranked, r => r.Candidate.Node.Id == 2);
    }

    // ---- the formula itself ----

    [Fact]
    public void The_score_is_the_product_of_all_four_terms()
    {
        var policy = new MultiplicativeRankingPolicy(
            new MultiplicativeRankingOptions { HopAttenuation = 0.5, SalienceRankWeight = 0 });
        var candidate = Candidate(1, relevance: 0.8, retrievability: 0.5, hop: 2);

        var ranked = policy.Rank([candidate], in Context);

        // 0.8 (relevance) * 0.5 (retrievability) * 1 (boost, weight 0) * 0.5^2 (hop attenuation) = 0.1
        Assert.Equal(0.1, ranked.Single().Score, precision: 9);
    }

    [Fact]
    public void Hop_attenuation_compounds_per_hop()
    {
        var policy = new MultiplicativeRankingPolicy(
            new MultiplicativeRankingOptions { HopAttenuation = 0.5, RelativeFloor = 0 });
        var direct = Candidate(1, hop: 0);
        var twoHops = Candidate(2, hop: 2);

        var ranked = policy.Rank([direct, twoHops], in Context);

        Assert.Equal(1.0, ranked.Single(r => r.Candidate.Node.Id == 1).Score, precision: 9);
        Assert.Equal(0.25, ranked.Single(r => r.Candidate.Node.Id == 2).Score, precision: 9);
    }

    [Fact]
    public void The_salience_boost_is_inert_at_the_shipped_default_weight()
    {
        // SalienceRankWeight's own shipped default (0) — a change to MultiplicativeRankingOptions's default
        // cannot silently drift from that without failing this.
        var policy = new MultiplicativeRankingPolicy();
        var salient = Candidate(1, retrievability: 0.5,
            signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 4));
        var neutral = Candidate(2, retrievability: 0.5);

        var ranked = policy.Rank([salient, neutral], in Context);

        Assert.Equal(ranked.Single(r => r.Candidate.Node.Id == 1).Score,
            ranked.Single(r => r.Candidate.Node.Id == 2).Score, precision: 9);
    }

    [Fact]
    public void An_opted_in_salience_weight_lifts_a_more_salient_candidate()
    {
        var policy = new MultiplicativeRankingPolicy(
            new MultiplicativeRankingOptions { SalienceRankWeight = 1.0 });
        var salient = Candidate(1, retrievability: 0.5,
            signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 4));
        var neutral = Candidate(2, retrievability: 0.5);

        var ranked = policy.Rank([salient, neutral], in Context);

        Assert.Equal(1, ranked[0].Candidate.Node.Id);
    }

    [Fact]
    public void The_relative_floor_buries_what_falls_far_enough_below_the_best()
    {
        // THE target of mutation #3 (returning the unfloored list): "weak" scores 1% of "strong", well
        // below a 10% floor.
        var policy = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { RelativeFloor = 0.1 });
        var strong = Candidate(1, retrievability: 1.0);
        var weak = Candidate(2, retrievability: 0.01);

        var ranked = policy.Rank([strong, weak], in Context);

        Assert.Contains(ranked, r => r.Candidate.Node.Id == 1);
        Assert.DoesNotContain(ranked, r => r.Candidate.Node.Id == 2);
    }

    [Fact]
    public void A_zero_floor_keeps_every_positively_scored_candidate()
    {
        var policy = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { RelativeFloor = 0 });
        var strong = Candidate(1, retrievability: 1.0);
        var faint = Candidate(2, retrievability: 0.0001);

        var ranked = policy.Rank([strong, faint], in Context);

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void The_shipped_defaults_match_todays_hardcoded_formula()
    {
        // pins the port's numeric fidelity against the values GraphMemoryEngine.cs / GraphMemoryOptions.cs
        // still hardcode as of this task — a silent drift here would change recall order for every consumer
        // who never configured ranking explicitly, once Task 3 wires this seam in.
        var options = new MultiplicativeRankingOptions();

        Assert.Equal(0.5, options.HopAttenuation);
        Assert.Equal(0.02, options.RelativeFloor);
        Assert.Equal(0, options.SalienceRankWeight);
    }

    // ---- construction guards ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_HopAttenuation_is_rejected_at_construction(double hopAttenuation)
    {
        // 0 makes every candidate beyond hop 0 score exactly zero — not attenuated, GONE, however well it
        // matched. Above 1, a candidate FARTHER from the seed scores higher than a direct hit, inverting the
        // one thing this knob promises. Negative flips the result's sign by hop parity instead of merely
        // weakening it.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiplicativeRankingOptions { HopAttenuation = hopAttenuation });
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1.0)]
    public void The_HopAttenuation_bounds_themselves_are_accepted(double hopAttenuation) =>
        // inclusive at both ends: a near-zero value is a steep but valid attenuation, and 1 is "hop doesn't
        // matter", a deliberate and safe setting
        Assert.Equal(hopAttenuation,
            new MultiplicativeRankingOptions { HopAttenuation = hopAttenuation }.HopAttenuation);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_RelativeFloor_is_rejected_at_construction(double relativeFloor)
    {
        // at 1 or above, the floor equals or exceeds the very score that defines it, so only candidates
        // tied exactly with the maximum survive — "bury what falls far enough below the best" collapsing
        // into "keep almost nothing" with no exception or empty-result signal pointing at the cause. A
        // negative value is refused rather than silently clamped by the ranking loop's own defensive
        // Math.Max(0, .) forever.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiplicativeRankingOptions { RelativeFloor = relativeFloor });
    }

    [Fact]
    public void A_zero_RelativeFloor_is_accepted_because_off_is_not_a_mistake() =>
        Assert.Equal(0, new MultiplicativeRankingOptions { RelativeFloor = 0 }.RelativeFloor);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_SalienceRankWeight_is_rejected_at_construction(double weight)
    {
        // salience is coerced to >= 1 before this formula ever sees it, so ln(salience) >= 0 always — a
        // negative weight therefore makes 1 + weight*ln(salience) DROP below 1 as salience rises: a MORE
        // salient entry would rank BELOW a neutral one, the exact inverse of "does not fade away".
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiplicativeRankingOptions { SalienceRankWeight = weight });
    }

    [Fact]
    public void A_zero_SalienceRankWeight_is_accepted_as_the_shipped_default() =>
        Assert.Equal(0, new MultiplicativeRankingOptions().SalienceRankWeight);
}
