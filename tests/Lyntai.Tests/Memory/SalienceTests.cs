using Lyntai.Memory;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Salience;

namespace Lyntai.Tests.Memory;

/// <summary>The salience policy and the retention policy IN ISOLATION — decay resistance only, which is all either of
/// them does on its own. Salience as a whole is ALSO store admission priority, always on, and MAY additionally
/// be rank priority if a consumer opts in (2026-08-09 — <c>docs/DECISIONS.md</c> D45: "does not fade away",
/// not "first priority" by default) — those two halves live in the store (seed admission) and the engine
/// (<c>GraphMemoryEngine</c>'s opt-in rank boost), covered by <c>MemoryGraphStoreContract</c> and
/// <see cref="Lyntai.Tests.Memory.GraphMemoryRankingTests"/> respectively — neither type under test here.
/// These facts pin the salience policy's curve and the bound the retention policy declares — an unbounded one would break
/// <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/>.</summary>
public class SalienceTests
{
    private static MemoryWrite Write(string content = "a fact") => new("t", "s", content);

    [Fact]
    public void Novel_material_is_judged_more_salient_than_familiar_material()
    {
        var saliencePolicy = new StructuralSaliencePolicy();

        var novel = saliencePolicy.Signals(Write(), new SalienceContext("e", Novelty: 1, ComparableCount: 50));
        var familiar = saliencePolicy.Signals(Write(), new SalienceContext("e", Novelty: 0, ComparableCount: 50));

        Assert.True(novel.Get(MemorySignals.WellKnown.Salience)
            > familiar.Get(MemorySignals.WellKnown.Salience));
    }

    [Fact]
    public void Judged_salience_stays_in_range_for_non_finite_and_out_of_range_novelty()
    {
        // covers the novelty-side clamp and the IsFinite guard only — it does NOT exercise the outer
        // Math.Clamp on the final salience value, because novelty is pre-clamped to [0,1] and the default
        // NoveltyWeight (1.5) can only drive the raw value up to 2.5, well under MaxSalience (4). The outer
        // clamp's own coverage lives in the salience/retention agreement test below, which uses a
        // NoveltyWeight large enough to make it load-bearing.
        var options = new SalienceOptions();
        var saliencePolicy = new StructuralSaliencePolicy(options);

        foreach (var novelty in new[] { -5d, 0, 0.5, 1, 5, double.NaN, double.PositiveInfinity })
        {
            var value = saliencePolicy
                .Signals(Write(), new SalienceContext("e", novelty, ComparableCount: 50))
                .Get(MemorySignals.WellKnown.Salience, fallback: 1);

            Assert.InRange(value, 1, options.MaxSalience);
        }
    }

    [Fact]
    public void An_engine_with_too_few_comparables_judges_at_the_neutral_value()
    {
        // with almost nothing stored, "novel" carries no information — everything is novel — so a first
        // session must not be marked maximally important. Asserting only the returned VALUE would pass
        // even if the guard were removed, so assert the bag is empty: nothing was judged at all.
        var saliencePolicy = new StructuralSaliencePolicy();

        var judged = saliencePolicy.Signals(Write(),
            new SalienceContext("e", Novelty: 1, ComparableCount: 2)); // MinimumComparables is 3

        Assert.Equal(0, judged.Count);
        Assert.Equal(1, judged.Get(MemorySignals.WellKnown.Salience, fallback: 1), 6);
    }

    [Fact]
    public void At_exactly_the_minimum_comparable_count_the_salience_policy_judges_normally()
    {
        // pins the < operator: MinimumComparables is a floor that itself qualifies, not a floor that must
        // be exceeded — off-by-one here would silently withhold judgment for exactly the entries the guard
        // was supposed to start admitting
        var saliencePolicy = new StructuralSaliencePolicy();

        var judged = saliencePolicy.Signals(Write(),
            new SalienceContext("e", Novelty: 1, ComparableCount: 3)); // MinimumComparables is 3

        Assert.Equal(1, judged.Count);
        Assert.True(judged.Get(MemorySignals.WellKnown.Salience) > 1);
    }

    [Fact]
    public void The_retention_policy_lengthens_in_proportion_to_salience_and_stops_at_its_bound()
    {
        var retentionPolicy = new SalienceRetentionPolicy();
        var neutral = new MemoryDecayState(0, 0, 20, Signals: MemorySignals.Empty);
        var salient = new MemoryDecayState(0, 0, 20,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 3));
        var absurd = new MemoryDecayState(0, 0, 20,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 1e9));

        Assert.Equal(1, retentionPolicy.StabilityFactor(neutral), 6);
        Assert.Equal(3, retentionPolicy.StabilityFactor(salient), 6);
        Assert.InRange(retentionPolicy.StabilityFactor(absurd), 1, retentionPolicy.MaxStabilityFactor);
    }

    [Fact]
    public void A_state_with_no_signals_is_left_completely_alone()
    {
        // every entry written before this feature has an empty bag, and none of them may change decay
        Assert.Equal(1, new SalienceRetentionPolicy().StabilityFactor(new MemoryDecayState(10, 0, 20)), 6);
    }

    [Fact]
    public void The_salience_and_retention_policies_derive_their_bound_from_the_SAME_options()
    {
        // THE assertion protecting CandidateCutoff. ModulatedRetrievability widens the cutoff by the
        // retention policy's DECLARED maximum, so a salience policy able to report more than the retention
        // policy declares would make the cutoff too narrow and silently exclude entries that are still retrievable.
        // A CUSTOM instance is what makes the coupling observable — comparing two defaults compares a
        // constant to itself and would pass against a hardcoded literal. And NoveltyWeight is set high
        // enough that the clamp is load-bearing: without it the raw value would be 101.
        var options = new SalienceOptions { MaxSalience = 7, NoveltyWeight = 100 };
        var saliencePolicy = new StructuralSaliencePolicy(options);
        var retentionPolicy = new SalienceRetentionPolicy(options);

        var highest = saliencePolicy
            .Signals(new MemoryWrite("t", "s", "x"),
                new SalienceContext("e", Novelty: 1, ComparableCount: 50))
            .Get(MemorySignals.WellKnown.Salience);

        Assert.Equal(7, retentionPolicy.MaxStabilityFactor, 6);
        Assert.Equal(7, highest, 6); // clamped from 101 — deleting the clamp fails here
        Assert.True(highest <= retentionPolicy.MaxStabilityFactor,
            $"the salience policy reported {highest}, exceeding the retention policy's declared " +
            $"{retentionPolicy.MaxStabilityFactor} — CandidateCutoff would be too narrow");
    }

    [Fact]
    public void A_max_salience_below_the_neutral_value_is_rejected_at_construction()
    {
        // a bound below 1 is a contradiction, not a smaller ceiling — reported here, at the
        // misconfiguration, rather than as an ArgumentException thrown from Math.Clamp deep in the
        // recall/salience-judgement hot path
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalienceOptions { MaxSalience = 0.5 });
    }

    [Fact]
    public void A_max_salience_of_exactly_one_is_accepted()
    {
        // the boundary is valid: it means "no lengthening", not "invalid"
        var options = new SalienceOptions { MaxSalience = 1 };
        Assert.Equal(1, options.MaxSalience);
    }

    [Fact]
    public void A_minimum_comparable_count_below_one_is_rejected_at_construction()
    {
        // mirrors the MaxSalience guard: ComparableCount is never negative, so 0 or below cannot express
        // "wait for comparables" — it reads as a disabled guard while silently admitting the empty-engine
        // case the guard exists to prevent
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalienceOptions { MinimumComparables = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalienceOptions { MinimumComparables = -1 });
    }

    [Fact]
    public void A_minimum_above_the_achievable_comparable_count_disables_salience_judgement_entirely()
    {
        // THE cliff, pinned so the doc that warns about it cannot quietly stop being true. ComparableCount is
        // what the engine's similarity probe found, bounded by GraphMemoryOptions.SimilarityK + 1 — 6 at the
        // defaults — and NOT by how much the engine holds. So a minimum above that bound means the salience
        // policy never judges anything, ever: no error, no log, the feature simply off. Validation cannot catch
        // it (SalienceOptions has no idea what SimilarityK is), which is exactly why it is asserted here.
        var saliencePolicy = new StructuralSaliencePolicy(new SalienceOptions { MinimumComparables = 10 });

        var judged = saliencePolicy.Signals(Write(),
            new SalienceContext("e", Novelty: 1, ComparableCount: new GraphMemoryOptions().SimilarityK + 1));

        Assert.Equal(0, judged.Count);
    }
}
