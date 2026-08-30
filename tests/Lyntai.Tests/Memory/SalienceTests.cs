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
    public void A_negative_novelty_weight_is_INERT_rather_than_inverting()
    {
        // `SalienceOptions.NoveltyWeight` claimed "a negative weight legitimately inverts the effect" until
        // 2026-08-29, when a bench arm came back byte-identical to the weight-zero arm. It cannot invert:
        // the policy clamps to [1, MaxSalience], so any negative weight floors at the neutral value and the
        // bag comes back EMPTY. Pinned here rather than measured, because it is arithmetic — a 30-seed
        // paired sweep is an expensive way to observe a clamp, and a corrected doc with no gate behind it
        // is one edit from being wrong again.
        var inverting = new StructuralSaliencePolicy(new SalienceOptions { NoveltyWeight = -1.5 });
        var scaling = new StructuralSaliencePolicy(new SalienceOptions { NoveltyWeight = 1.5 });

        foreach (var novelty in new[] { 0.01, 0.5, 1d })
        {
            var context = new SalienceContext("e", novelty, ComparableCount: 50);

            // the assertion that matters: nothing is written, so no consumer can read an inverted preference
            Assert.Equal(0, inverting.Signals(Write(), context).Count);
            // ...and the control, without which the test would also pass on a policy that judges NOTHING
            Assert.Equal(1, scaling.Signals(Write(), context).Count);
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

/// <summary>Every <see cref="MemorySaliencePolicyContract"/> fact against BOTH shipped salience policies.
/// Derive a class per policy so a new implementation gets the whole contract by adding one file — the same
/// shape <see cref="MemoryAgePolicyContractFacts"/> uses.</summary>
public abstract class MemorySaliencePolicyContractFacts
{
    protected abstract IMemorySaliencePolicy New();

    /// <summary>The <c>MaxSalience</c> the implementation was built with — the contract cannot read a ceiling
    /// off the interface, because the seam deliberately does not expose one (a consumer's policy may bound
    /// itself however it likes). The driver knows what it configured.</summary>
    protected virtual double Ceiling => new SalienceOptions().MaxSalience;

    [Fact] public void Finite_signals() => MemorySaliencePolicyContract.Every_signal_it_writes_is_finite(New());
    [Fact] public void At_least_neutral() => MemorySaliencePolicyContract.A_reported_salience_is_at_least_neutral(New());
    [Fact] public void Bounded() =>
        MemorySaliencePolicyContract.A_reported_salience_never_exceeds_its_configured_ceiling(New(), Ceiling);
    [Fact] public void One_provenance_bit() => MemorySaliencePolicyContract.Provenance_is_exactly_one_bit(New());
    [Fact] public void Pure() => MemorySaliencePolicyContract.It_is_a_pure_function_of_the_write_and_context(New());
    [Fact] public void Neutral_is_written_as_nothing() =>
        MemorySaliencePolicyContract.A_neutral_judgement_is_written_as_nothing_rather_than_as_one(New());
}

public class StructuralSaliencePolicyContractTests : MemorySaliencePolicyContractFacts
{
    protected override IMemorySaliencePolicy New() => new StructuralSaliencePolicy();
}

/// <summary>The supported way to turn salience OFF. It satisfies the same contract by declining on every
/// input — including <see cref="MemorySaliencePolicyContract.Provenance_is_exactly_one_bit"/>, which it meets
/// by declaring the bit of the policy it REPLACES rather than a bit of its own: honest, because a policy that
/// never returns a signal never contributes provenance to any row.</summary>
public class NeutralSaliencePolicyContractTests : MemorySaliencePolicyContractFacts
{
    protected override IMemorySaliencePolicy New() => new NeutralSaliencePolicy();
}

/// <summary>Every <see cref="MemoryRetentionPolicyContract"/> fact against the shipped retention policy.</summary>
public abstract class MemoryRetentionPolicyContractFacts
{
    protected abstract IMemoryRetentionPolicy New();

    [Fact] public void Bound_is_sane() => MemoryRetentionPolicyContract.MaxStabilityFactor_is_finite_and_at_least_one(New());
    [Fact] public void Never_shortens() => MemoryRetentionPolicyContract.The_factor_is_finite_and_never_shortens_a_half_life(New());
    [Fact] public void Within_its_bound() => MemoryRetentionPolicyContract.The_factor_never_exceeds_the_declared_maximum(New());
    [Fact] public void Not_age_keyed() => MemoryRetentionPolicyContract.The_factor_does_not_depend_on_age(New());
    [Fact] public void Pure() => MemoryRetentionPolicyContract.It_is_a_pure_function_of_the_state(New());
    [Fact] public void Named() => MemoryRetentionPolicyContract.It_has_a_name(New());
}

public class SalienceRetentionPolicyContractTests : MemoryRetentionPolicyContractFacts
{
    protected override IMemoryRetentionPolicy New() => new SalienceRetentionPolicy();
}
