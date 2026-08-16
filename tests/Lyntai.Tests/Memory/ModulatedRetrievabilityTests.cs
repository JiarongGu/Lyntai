using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Salience;

namespace Lyntai.Tests.Memory;

/// <summary>The decorator that makes retention open. Every fact here exists because its absence loses
/// memories silently rather than failing.</summary>
public class ModulatedRetrievabilityTests
{
    /// <summary>A retention policy that lengthens by a fixed factor, optionally LYING about its own bound.</summary>
    private sealed class FixedRetentionPolicy(double factor, double? declaredMax = null) : IMemoryRetentionPolicy
    {
        public string Name => "fixed";
        public double MaxStabilityFactor => declaredMax ?? factor;
        public double StabilityFactor(in MemoryDecayState state) => factor;
    }

    /// <summary>A minimal, purpose-built curve for isolating <see cref="ModulatedRetrievability"/>'s own
    /// behaviour from any shipped curve's arithmetic. <c>HalfLifeRetrievability</c> is deleted (2026-08-10,
    /// fsrs-properly plan Task 1); substituting <see cref="DsrRetrievability"/> here would trade the clean,
    /// hand-checkable expected values below (<c>0.25</c>, <c>0.5</c>, <c>Math.Pow(2, -0.5)</c>, …) for a
    /// power-law formula with no equally simple closed form — exactly the "copy of the deleted curve" the
    /// task brief warns against, wearing a different excuse. This keeps only the plain
    /// <c>r = 2^(-age/effectiveStability)</c> exponential shape both shipped curves' own connection-boost
    /// math already assumed, with no reinforcement growth and no unmeasured tuning constants — the minimum
    /// surface <see cref="IMemoryRetrievabilityPolicy"/> demands, nothing more. Every fact below leaves
    /// <see cref="MemoryDecayState.Strength"/> at its default (0), so the boost/decay arithmetic is never
    /// actually exercised through this type; <c>CandidateCutoff_is_never_narrower_than_the_modulated_curve_requires</c>
    /// below is the one exception, so the boost ceiling here matches both shipped curves' own default
    /// (<c>MaxConnectionBoost = 4</c>) rather than being invented independently.</summary>
    private sealed class SimpleExponentialRetrievability : IMemoryRetrievabilityPolicy
    {
        private const double DefaultInitialStability = 20;
        private const double MaxConnectionBoost = 4;

        public double InitialStability => DefaultInitialStability;
        public MemoryRetrievabilityProvenance Provenance => (MemoryRetrievabilityProvenance)(1L << 32);

        public double Retrievability(in MemoryDecayState state) =>
            state.Age <= 0 ? 1 : Math.Clamp(Math.Pow(2, -state.Age / EffectiveStability(state)), 0, 1);

        public MemoryDecayState Reinforce(in MemoryDecayState state) => state; // never reinforces — irrelevant here

        // a fixed, distinguishable value nothing else in this file could produce — proves DerivedGrade
        // delegation (2026-08-11, fsrs-properly plan Task 3) rather than a coincidental agreement with the
        // interface's own default (null)
        public double? DerivedGrade(in MemoryDecayState state) => state.Age <= 0 ? null : 3.14;

        public double CandidateCutoff(double minRetrievability) =>
            minRetrievability is <= 0 or > 1
                ? double.PositiveInfinity
                : Math.Log2(1 / minRetrievability) * MaxConnectionBoost;

        private double EffectiveStability(in MemoryDecayState state)
        {
            var stability = state.Stability > 0 ? state.Stability : InitialStability;
            if (state.Strength <= 0) return stability;
            var boost = Math.Min(1 + 0.5 * Math.Log(1 + state.Strength), MaxConnectionBoost);
            return stability * boost;
        }
    }

    private static IMemoryRetrievabilityPolicy Inner() => new SimpleExponentialRetrievability();

    private static MemoryDecayState State(double age, double stability) =>
        new(age, 0, stability);

    // ---- the shared RetrievabilityPolicyContract, run against ModulatedRetrievability itself ----
    // (fix round 2, cheap minor) — the policy AddMemoryEngine actually installs via UseGraph/
    // UseBestAvailable (MemoryEngineRegistration), never exercised by this fact set before this addition.
    //
    // NEUTRAL, not empty: SalienceRetentionPolicy is the real, shipped default retention policy, not a
    // stand-in — With_no_retention_policies_it_is_the_inner_policy_exactly (above) already covers the
    // trivially-identical empty case. Every contract state below carries EMPTY Signals (the model-free default
    // deployment, no salience policy has ever run), under which SalienceRetentionPolicy.StabilityFactor reports
    // EXACTLY 1 — Modulated's own `factor == 1` short-circuit — so the wrapped policy is observably identical to
    // the inner curve for every state the contract exercises, while genuinely running the real decision logic
    // (compute the factor, compare to 1) rather than the constructor's own zero-policy shortcut.
    //
    // THIS IS THE ONLY CASE THAT CAN HOLD. The r(S) = 0.5 anchor and "may never SHORTEN a memory" are claims
    // about the CURVE's own unit convention; a retention policy whose whole purpose is to LENGTHEN stability
    // beyond the stored value (what every NON-neutral retention policy does, by design) necessarily moves where
    // retrievability crosses 0.5 for that entry — that is modulation working as intended, not a violation of
    // the anchor. Running the contract under a genuinely non-neutral retention policy would assert the wrong
    // claim, not a stronger one.
    private static IMemoryRetrievabilityPolicy NeutralDefault() =>
        new ModulatedRetrievability(new SimpleExponentialRetrievability(), [new SalienceRetentionPolicy()]);

    [Fact]
    public void Neutral_Probability() =>
        RetrievabilityPolicyContract.Retrievability_is_a_probability(NeutralDefault());

    [Fact]
    public void Neutral_One_at_zero() =>
        RetrievabilityPolicyContract.It_is_one_at_zero_age(NeutralDefault());

    [Fact]
    public void Neutral_Monotone() =>
        RetrievabilityPolicyContract.It_never_increases_with_age(NeutralDefault());

    [Fact]
    public void Neutral_Reinforce_grows() =>
        RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory(NeutralDefault());

    [Fact]
    public void Neutral_Cutoff_superset() =>
        RetrievabilityPolicyContract.CandidateCutoff_is_a_conservative_superset(NeutralDefault());

    [Fact]
    public void Neutral_Unbounded_ok() =>
        RetrievabilityPolicyContract.An_unbounded_policy_is_still_correct(NeutralDefault());

    [Fact]
    public void Neutral_Connectedness() =>
        RetrievabilityPolicyContract.Connectedness_never_lowers_retrievability(NeutralDefault());

    [Fact]
    public void Neutral_Stability_unit() =>
        RetrievabilityPolicyContract.Stability_is_the_position_delta_at_which_retrievability_is_half(
            NeutralDefault());

    [Fact]
    public void Neutral_Reinforce_owns_only_stability_and_difficulty() =>
        RetrievabilityPolicyContract.Reinforcement_leaves_every_field_it_does_not_own_unchanged(NeutralDefault());

    [Fact]
    public void With_no_retention_policies_it_is_the_inner_policy_exactly()
    {
        var inner = Inner();
        var policy = new ModulatedRetrievability(inner, []);
        var state = State(age: 30, stability: 20);

        Assert.Equal(inner.Retrievability(state), policy.Retrievability(state));
        Assert.Equal(inner.CandidateCutoff(0.1), policy.CandidateCutoff(0.1));
        Assert.Equal(inner.InitialStability, policy.InitialStability);
    }

    [Fact]
    public void A_retention_policy_lengthens_the_half_life_and_so_raises_retrievability()
    {
        var state = State(age: 40, stability: 20);
        var plain = Inner().Retrievability(state);
        var modulated = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(2)]).Retrievability(state);

        // 2^(-40/20) = 0.25 unmodulated; 2^(-40/40) = 0.5 with the half-life doubled
        Assert.Equal(0.25, plain, 6);
        Assert.Equal(0.5, modulated, 6);
    }

    [Fact]
    public void Retention_policies_compose_multiplicatively()
    {
        var state = State(age: 40, stability: 20);
        var policy = new ModulatedRetrievability(Inner(),
            [new FixedRetentionPolicy(2), new FixedRetentionPolicy(2)]);

        // half-life 20 → 80, so 2^(-40/80)
        Assert.Equal(Math.Pow(2, -0.5), policy.Retrievability(state), 6);
    }

    /// <summary>Composition itself as the shipped default, given a name rather than hardcoded — pins
    /// <see cref="MultiplicativeRetentionCompositionPolicy"/>'s own arithmetic in isolation from
    /// <see cref="ModulatedRetrievability"/>'s clamping, including the empty-list identity its
    /// <see cref="ModulatedRetrievability"/> constructor call relies on for a retention-policy-free engine.</summary>
    [Fact]
    public void MultiplicativeRetentionComposition_multiplies_and_the_empty_product_is_one()
    {
        var composition = new MultiplicativeRetentionCompositionPolicy();

        Assert.Equal(1, composition.StabilityFactor([]));
        Assert.Equal(6, composition.StabilityFactor([2, 3]), 9);
    }

    /// <summary>A composition policy nothing can vary is decoration (2026-08-10 memory-policy-seams plan,
    /// Task 3, Step 3's mutation-check requirement) — this proves the seam is real by swapping in a
    /// DIFFERENT combination rule (max instead of the shipped multiply) over the SAME two retention policies and
    /// showing the result changes. Multiply gives 2 × 3 = 6× the half-life; max gives 3× — different enough
    /// to be unmistakable, not a rounding difference.</summary>
    private sealed class MaxRetentionComposition : IMemoryRetentionCompositionPolicy
    {
        public double StabilityFactor(IReadOnlyList<double> factors) => factors.Count == 0 ? 1 : factors.Max();
    }

    [Fact]
    public void Swapping_the_composition_policy_changes_the_composed_factor()
    {
        var state = State(age: 40, stability: 20);
        var retentionPolicies = new IMemoryRetentionPolicy[]
        {
            new FixedRetentionPolicy(2, declaredMax: 3), new FixedRetentionPolicy(3, declaredMax: 3),
        };

        var multiplied = new ModulatedRetrievability(Inner(), retentionPolicies, new MultiplicativeRetentionCompositionPolicy());
        var maxed = new ModulatedRetrievability(Inner(), retentionPolicies, new MaxRetentionComposition());

        // half-life 20 × 6 = 120 under multiply, 20 × 3 = 60 under max — genuinely different retrievability,
        // not merely a different internal field with no observable effect
        Assert.Equal(Math.Pow(2, -40.0 / 120), multiplied.Retrievability(state), 9);
        Assert.Equal(Math.Pow(2, -40.0 / 60), maxed.Retrievability(state), 9);
        Assert.NotEqual(multiplied.Retrievability(state), maxed.Retrievability(state));

        // and the cutoff itself, which is what actually protects PruneAsync, moves with it too — both
        // retention policies declare a maximum of 3, so multiply's product is 9 and max's own max is 3
        Assert.Equal(multiplied.CandidateCutoff(0.05), maxed.CandidateCutoff(0.05) * 3, 9);
    }

    [Fact]
    public void A_retention_policy_may_not_SHORTEN_a_half_life()
    {
        // shortening is not in the model: floors and admission come from GRADE, and a retention policy that
        // could shorten would let a signal quietly make material MORE forgettable with no way to see it
        var state = State(age: 40, stability: 20);
        var policy = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(0.25)]);

        Assert.Equal(Inner().Retrievability(state), policy.Retrievability(state), 6);

        // and the CUTOFF must not narrow either: a sub-1 declared maximum shrinking it below the inner
        // policy's would exclude entries the curve still considers retrievable
        Assert.Equal(Inner().CandidateCutoff(0.1), policy.CandidateCutoff(0.1), 6);
    }

    [Fact]
    public void A_retention_policy_that_declares_a_NON_FINITE_maximum_widens_nothing()
    {
        // Math.Max(1, NaN) is NaN by contract, so an unguarded declared maximum would poison _maxFactor to
        // NaN, and a NaN cutoff compares false against every row — the candidate set empties silently,
        // with no error, for every query the store ever runs
        var state = State(age: 40, stability: 20);
        var policy = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(2, declaredMax: double.NaN)]);

        Assert.Equal(Inner().CandidateCutoff(0.1), policy.CandidateCutoff(0.1), 6);
        Assert.True(double.IsFinite(policy.CandidateCutoff(0.1)));

        // and the clamp uses the same coercion, so a NaN-declaring retention policy is inert on retrievability too
        Assert.Equal(Inner().Retrievability(state), policy.Retrievability(state), 6);
    }

    [Fact]
    public void A_retention_policy_that_LIES_about_its_bound_is_clamped_to_what_it_declared()
    {
        // the cutoff widens by the DECLARED maximum, so an undeclared excess would break the superset
        // guarantee and silently drop retrievable entries. Clamp rather than trust.
        var state = State(age: 40, stability: 20);
        var honest = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(2, declaredMax: 2)]);
        var liar = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(8, declaredMax: 2)]);

        Assert.Equal(honest.Retrievability(state), liar.Retrievability(state), 6);
    }

    [Fact]
    public void The_cutoff_widens_by_the_product_of_declared_maxima()
    {
        var policy = new ModulatedRetrievability(Inner(),
            [new FixedRetentionPolicy(1, declaredMax: 2), new FixedRetentionPolicy(1, declaredMax: 3)]);

        Assert.Equal(Inner().CandidateCutoff(0.05) * 6, policy.CandidateCutoff(0.05), 6);
    }

    [Fact]
    public void Reinforce_operates_on_the_STORED_stability_not_the_modulated_one()
    {
        // modulation is a READ-TIME view; reinforcement writes a value back to the store, so compounding
        // the modulated figure would bake a signal's effect into the stored stability permanently
        var state = State(age: 0, stability: 20);
        var policy = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(4)]);

        Assert.Equal(Inner().Reinforce(state), policy.Reinforce(state));
    }

    /// <summary>DerivedGrade (2026-08-11, fsrs-properly plan Task 3) forwards to the wrapped policy
    /// unchanged, on the SAME raw state <see cref="Reinforce"/> itself uses — never the modulated one — for
    /// the identical reason: whichever policy actually computed the grade is the one a review log must
    /// credit, on the state that produced it.</summary>
    [Fact]
    public void DerivedGrade_forwards_to_the_inner_policy_on_the_unmodulated_state()
    {
        var state = State(age: 5, stability: 20);
        var policy = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(4)]);

        var grade = policy.DerivedGrade(state);

        Assert.Equal(Inner().DerivedGrade(state), grade);
        Assert.Equal(3.14, grade); // the fixed, distinguishable value only the INNER policy could produce
    }

    /// <summary>The Δt=0 half of the same guarantee — null must propagate too, not collapse to some other
    /// value on the way through the decorator.</summary>
    [Fact]
    public void DerivedGrade_forwards_a_null_grade_too()
    {
        var state = State(age: 0, stability: 20);
        var policy = new ModulatedRetrievability(Inner(), [new FixedRetentionPolicy(4)]);

        Assert.Null(policy.DerivedGrade(state));
    }

    /// <summary>THE invariant. <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> is what lets a store
    /// bound its candidates with plain arithmetic, and a store filters on the STORED stability while the
    /// engine evaluates the MODULATED curve. If the cutoff is ever narrower than the modulated curve
    /// requires, an entry that is perfectly retrievable is excluded from the candidate set — silently, with
    /// no symptom but a memory that stopped coming back.
    /// <para>Randomized over a fixed seed so a failure is reproducible from the seed alone.</para></summary>
    [Fact]
    public void CandidateCutoff_is_never_narrower_than_the_modulated_curve_requires()
    {
        var random = new Random(20260809); // fixed: a failing case must be reproducible
        var policy = new ModulatedRetrievability(Inner(),
            [new FixedRetentionPolicy(3, declaredMax: 3), new FixedRetentionPolicy(2, declaredMax: 2)]);

        foreach (var floor in new[] { 0.5, 0.25, 0.1, 0.05, 0.01 })
        {
            var cutoff = policy.CandidateCutoff(floor);

            for (var i = 0; i < 2000; i++)
            {
                var stability = 0.5 + random.NextDouble() * 500;
                var age = random.NextDouble() * 5000;
                // Strength/StrengthAge exercise the inner policy's OWN MaxConnectionBoost widening and its
                // MaxStability ceiling composed with modulation, not just the retention policy's factor alone
                var state = new MemoryDecayState(age, random.Next(0, 20), stability,
                    Strength: random.NextDouble() * 50, StrengthAge: random.NextDouble() * 200);

                if (policy.Retrievability(state) < floor) continue;

                // the store's own test: age / STORED stability must fall inside the cutoff
                Assert.True(age / stability <= cutoff,
                    $"floor {floor}: age {age} / stability {stability} = {age / stability} " +
                    $"exceeds cutoff {cutoff}, so a retrievable entry would be excluded");
            }
        }
    }
}
