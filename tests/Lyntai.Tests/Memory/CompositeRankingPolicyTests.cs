using Lyntai.Memory;
using Lyntai.Memory.Ranking;

namespace Lyntai.Tests.Memory;

/// <summary>This ranking domain's first COMPOSITE — fuses two OTHER <see cref="IMemoryRankingPolicy"/>
/// members by rank position, never raw score (see the class's own remarks for why averaging
/// <see cref="MultiplicativeRankingPolicy"/>'s product against <see cref="ReciprocalRankFusionPolicy"/>'s sum
/// would be arithmetic over quantities that share no scale). Three concerns get their own facts here rather
/// than being assumed: (1) the fused output genuinely differs from EITHER member alone — a composite that
/// quietly reproduces one member is not a composite; (2) which member is PRIMARY is not cosmetic — swapping
/// the two, weights held fixed but unequal, changes the result; (3) a fully tied input (this project's own
/// shipped trap, `.claude/knowledge/pitfalls.md`) must not let either member's tiebreak-broken OUTPUT LIST
/// leak in as if it were a real, distinguishing rank.</summary>
public class CompositeRankingPolicyTests
{
    private static GraphNode Node(long id, double relevance = 1, MemorySignals signals = default) =>
        new(id, "e", "t", "s", $"headline {id}", $"content {id}", MemoryGrade.Associative,
            DateTimeOffset.UnixEpoch, RecallCount: 0, Stability: 20, Age: 0, Relevance: relevance,
            Degree: 0, Metadata: null, Signals: signals);

    private static MemoryCandidate Candidate(long id, double relevance = 1, double retrievability = 1,
        int hop = 0, MemorySignals signals = default) =>
        new(Node(id, relevance, signals), retrievability, hop);

    private static readonly MemoryRankingContext Context = new(Limit: 10, Engine: "test");

    private static CompositeRankingPolicy Default() =>
        new(new MultiplicativeRankingPolicy(), new ReciprocalRankFusionPolicy());

    /// <summary>A ranking policy under FULL manual control — returns exactly the (id, score) pairs given, in
    /// that order, filtered to whichever candidates were actually passed in. Lets the mutation-check facts
    /// below assert precise, hand-computed outcomes instead of reasoning about the real formulas.</summary>
    private sealed class FakeRankingPolicy(IReadOnlyList<(long Id, double Score)> order) : IMemoryRankingPolicy
    {
        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context)
        {
            var byId = candidates.ToDictionary(c => c.Node.Id);
            return order
                .Where(o => byId.ContainsKey(o.Id))
                .Select(o => new RankedMemory(byId[o.Id], o.Score))
                .ToList();
        }
    }

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

    /// <summary>This class carried the SAME "sum of positive, bounded reciprocal terms, so <c>best</c> can
    /// never turn non-finite" claim <see cref="ReciprocalRankFusionPolicy"/> did, and it was false here for
    /// the same reason: both weights are validated finite and <c>&gt;= 0</c> with no upper bound, and
    /// <see cref="CompositeRankingOptions.K"/> may be any finite positive number, so two terms of
    /// <c>double.MaxValue / 1.5</c> overflow their sum. This policy's own
    /// <see cref="CompositeRankingOptions.RelativeFloor"/> also defaults to <c>0</c>, so — exactly like
    /// reciprocal rank fusion — the failure is <c>+Infinity × 0 = NaN</c> and a COMPLETELY empty recall, not
    /// merely a collapsed one.
    /// <para>The members here are FAKES with hand-picked ranks, because the overflow being exercised is this
    /// class's OWN fusion arithmetic — nothing about either real member is involved, and using them would
    /// only make the numbers harder to read.</para></summary>
    [Fact]
    public void An_overflowing_fused_sum_does_not_empty_a_healthy_recall()
    {
        var byIdOrder = new (long, double)[] { (1, 1.0), (2, 0.5) };
        var policy = new CompositeRankingPolicy(
            new FakeRankingPolicy(byIdOrder), new FakeRankingPolicy(byIdOrder),
            new CompositeRankingOptions
            {
                K = 0.5, PrimaryWeight = double.MaxValue, SecondaryWeight = double.MaxValue,
            });
        // rank 1 under both members: MaxValue/1.5 + MaxValue/1.5 = +Infinity.
        var top = Candidate(1);
        // rank 2 under both: MaxValue/2.5 + MaxValue/2.5 ≈ 1.44e308 — finite, and the candidate a NaN floor
        // takes down alongside the overflowing one.
        var healthy = Candidate(2);

        var ranked = policy.Rank([top, healthy], in Context);

        Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score),
            $"non-finite score returned for candidate {r.Candidate.Node.Id}"));
        Assert.Contains(ranked, r => r.Candidate.Node.Id == 2);
    }

    // ---- mutation-check #1: a genuine composite, not a relabeling of either member ----

    [Fact]
    public void The_fused_order_differs_from_both_members_taken_alone()
    {
        // memberA ranks 1 > 2 > 3; memberB ranks the exact OPPOSITE, 3 > 2 > 1 — genuinely disagreeing
        // members, so a composite that just forwarded one of them could never match the fused order below.
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3) };
        var memberA = new FakeRankingPolicy([(1, 100), (2, 50), (3, 10)]);
        var memberB = new FakeRankingPolicy([(3, 100), (2, 50), (1, 10)]);
        var composite = new CompositeRankingPolicy(memberA, memberB);

        var fused = composite.Rank(candidates, in Context).Select(r => r.Candidate.Node.Id).ToArray();
        var fromA = memberA.Rank(candidates, in Context).Select(r => r.Candidate.Node.Id).ToArray();
        var fromB = memberB.Rank(candidates, in Context).Select(r => r.Candidate.Node.Id).ToArray();

        Assert.NotEqual(fromA, fused);
        Assert.NotEqual(fromB, fused);
    }

    // ---- mutation-check #2: which member is PRIMARY is not cosmetic ----

    [Fact]
    public void Swapping_which_member_is_primary_changes_the_result()
    {
        // Equal weights would make the fusion sum symmetric under a swap (w/(K+r1) + w/(K+r2) is the same
        // value as w/(K+r2) + w/(K+r1)) — this fact deliberately uses UNEQUAL weights (2:1) so which member
        // the larger weight amplifies is observable. Hand-computed at K=60:
        //   id1: rankA=1, rankB=3 -> 2/61 + 1/63 ≈ 0.048660   id3: rankA=3, rankB=1 -> 2/63 + 1/61 ≈ 0.048139
        //   id2: rankA=2, rankB=2 -> 2/62 + 1/62 = 3/62        ≈ 0.048387 (unaffected by the swap either way)
        // A-primary orders [1, 2, 3]; swapping to B-primary (same weights, same members, opposite roles)
        // flips it to [3, 2, 1].
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3) };
        var memberA = new FakeRankingPolicy([(1, 100), (2, 50), (3, 10)]);
        var memberB = new FakeRankingPolicy([(3, 100), (2, 50), (1, 10)]);
        var weighted = new CompositeRankingOptions { PrimaryWeight = 2, SecondaryWeight = 1 };

        var aPrimary = new CompositeRankingPolicy(memberA, memberB, weighted)
            .Rank(candidates, in Context).Select(r => r.Candidate.Node.Id).ToArray();
        var bPrimary = new CompositeRankingPolicy(memberB, memberA, weighted)
            .Rank(candidates, in Context).Select(r => r.Candidate.Node.Id).ToArray();

        Assert.Equal([1L, 2, 3], aPrimary);
        Assert.Equal([3L, 2, 1], bPrimary);
    }

    // ---- mutation-check #3: a fully tied input is decided ONLY by the id tiebreak ----

    [Fact]
    public void A_fully_tied_input_is_decided_only_by_the_id_tiebreak()
    {
        // Every candidate identical on every real signal (relevance, retrievability, hop, no salience
        // signals) — the exact shape that once made a position-based rank (rather than competition rank)
        // hand a fully-uninformative signal FULL weight as a covert proxy for node id
        // (`.claude/knowledge/pitfalls.md`). Both real members already use competition ranking internally
        // and so tie EVERY candidate at rank 1 on their own output; the composite must preserve that rather
        // than reading a member's tiebreak-broken LIST POSITION as if it were a genuine distinction.
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3), Candidate(4) };
        var policy = Default();

        var ranked = policy.Rank(candidates, in Context);

        Assert.Equal(4, ranked.Count);
        Assert.All(ranked, r => Assert.Equal(ranked[0].Score, r.Score, precision: 12));
        Assert.Equal([4L, 3, 2, 1], ranked.Select(r => r.Candidate.Node.Id).ToArray());
    }

    // ---- the real shipped members genuinely both reach the fused score ----

    [Fact]
    public void The_secondary_member_can_break_a_tie_the_primary_member_cannot_see()
    {
        // MultiplicativeRankingPolicy's SalienceRankWeight defaults to 0, so with relevance/retrievability/
        // hop tied it cannot distinguish these two candidates AT ALL — its OWN tiebreak (id DESCENDING)
        // would rank id 2 first. The RRF member is given a salience weight EXPLICITLY so it CAN, and the
        // fused order follows THAT preference — proving the secondary member's signal genuinely reaches the
        // composite rather than the primary member's id tiebreak silently deciding everything.
        //
        // Explicit because BOTH shipped policies now default salience to 0 in ranking (D89 measured what D45
        // argued). Inherited, this fact would turn on the id tiebreak alone and prove nothing about
        // composition — the subject is the WIRING, so the signal it rides on is switched on deliberately.
        var salient = Candidate(1, signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 10));
        var neutral = Candidate(2);
        var policy = new CompositeRankingPolicy(
            new MultiplicativeRankingPolicy(),
            new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { SalienceWeight = 1 }));

        var ranked = policy.Rank([salient, neutral], in Context);

        Assert.Equal(1, ranked[0].Candidate.Node.Id);
    }

    [Fact]
    public void A_candidate_a_members_own_floor_drops_is_not_excluded_by_the_composite()
    {
        // Multiplicative's own floor buries the weak candidate outright when asked alone — the composite
        // must still return it, ranked worse rather than dropped, because floor semantics are the FUSED
        // score's own business, never a single member's veto.
        var strong = Candidate(1, retrievability: 1.0);
        var weak = Candidate(2, retrievability: 0.001);
        var multiplicative = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions
        {
            RelativeFloor = 0.5,
        });
        var rrf = new ReciprocalRankFusionPolicy(); // RelativeFloor 0 — keeps everything

        Assert.DoesNotContain(multiplicative.Rank([strong, weak], in Context),
            r => r.Candidate.Node.Id == 2); // sanity: Multiplicative alone really does drop it

        var fused = new CompositeRankingPolicy(multiplicative, rrf).Rank([strong, weak], in Context);

        Assert.Contains(fused, r => r.Candidate.Node.Id == 2);
        Assert.Equal(1, fused[0].Candidate.Node.Id); // still ranked worse, just not gone
    }

    [Fact]
    public void A_member_that_drops_every_candidate_contributes_nothing_rather_than_corrupting_the_fusion()
    {
        // A member with literally no opinion (drops everyone) must not out-rank or bury anyone — every
        // dropped candidate falls back to the SAME rank (1) on that signal, so it contributes an identical
        // constant to every candidate rather than distorting the order. The surviving member (plain id
        // ranking here) decides the whole outcome.
        var dropsEverything = new FakeRankingPolicy([]);
        var byIdDescending = new FakeRankingPolicy([(1, 100), (2, 50), (3, 10)]);
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3) };

        var fused = new CompositeRankingPolicy(dropsEverything, byIdDescending).Rank(candidates, in Context);

        Assert.Equal([1L, 2, 3], fused.Select(r => r.Candidate.Node.Id).ToArray());
    }

    // ---- construction guards ----

    [Fact]
    public void A_null_primary_is_rejected_at_construction() =>
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeRankingPolicy(null!, new ReciprocalRankFusionPolicy()));

    [Fact]
    public void A_null_secondary_is_rejected_at_construction() =>
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeRankingPolicy(new MultiplicativeRankingPolicy(), null!));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_K_is_rejected_at_construction(double k) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeRankingOptions { K = k });

    [Theory]
    [InlineData(0.001)]
    [InlineData(1000.0)]
    public void K_accepts_any_finite_positive_value(double k) =>
        Assert.Equal(k, new CompositeRankingOptions { K = k }.K);

    public static readonly TheoryData<double> InvalidWeights =
        [-0.1, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_PrimaryWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeRankingOptions { PrimaryWeight = weight });

    [Theory, MemberData(nameof(InvalidWeights))]
    public void An_out_of_domain_SecondaryWeight_is_rejected_at_construction(double weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeRankingOptions { SecondaryWeight = weight });

    [Fact]
    public void A_single_weight_may_individually_be_zero_without_throwing() =>
        Assert.Equal(0, new CompositeRankingOptions { SecondaryWeight = 0 }.SecondaryWeight);

    [Fact]
    public void Both_weights_at_zero_is_rejected_at_policy_construction()
    {
        var options = new CompositeRankingOptions { PrimaryWeight = 0, SecondaryWeight = 0 };

        Assert.Throws<ArgumentException>(() =>
            new CompositeRankingPolicy(new MultiplicativeRankingPolicy(), new ReciprocalRankFusionPolicy(),
                options));
    }

    [Fact]
    public void A_single_nonzero_weight_is_sufficient_at_policy_construction() =>
        new CompositeRankingPolicy(new MultiplicativeRankingPolicy(), new ReciprocalRankFusionPolicy(),
            new CompositeRankingOptions { PrimaryWeight = 0, SecondaryWeight = 1 })
            .Rank([Candidate(1)], in Context);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_RelativeFloor_is_rejected_at_construction(double relativeFloor) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompositeRankingOptions { RelativeFloor = relativeFloor });

    [Fact]
    public void The_shipped_defaults_are_K60_unit_weights_and_a_zero_floor()
    {
        var options = new CompositeRankingOptions();

        Assert.Equal(60, options.K);
        Assert.Equal(1, options.PrimaryWeight);
        Assert.Equal(1, options.SecondaryWeight);
        Assert.Equal(0, options.RelativeFloor);
    }
}
