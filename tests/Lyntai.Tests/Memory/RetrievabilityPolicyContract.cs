using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;

namespace Lyntai.Tests.Memory;

/// <summary>Facts every <see cref="IMemoryRetrievabilityPolicy"/> satisfies, run against the default curve so a
/// custom policy cannot quietly break the store's candidate query.
/// <para>A policy sees no clock: age is a dimensionless quantity the engine's <see cref="IMemoryAgePolicy"/>
/// defines, and stability is in the same units.</para></summary>
public static class RetrievabilityPolicyContract
{
    public static void Retrievability_is_a_probability(IMemoryRetrievabilityPolicy policy)
    {
        foreach (var age in new[] { 0, 1, 7, 30, 365, 3650.0 })
            Assert.InRange(policy.Retrievability(new MemoryDecayState(age, 0, policy.InitialStability)), 0, 1);
    }

    public static void It_is_one_at_zero_age(IMemoryRetrievabilityPolicy policy) =>
        Assert.Equal(1.0, policy.Retrievability(new MemoryDecayState(0, 0, policy.InitialStability)),
            precision: 9);

    public static void It_never_increases_with_age(IMemoryRetrievabilityPolicy policy)
    {
        var previous = 1.0;
        for (var age = 0; age <= 400; age += 5)
        {
            var r = policy.Retrievability(new MemoryDecayState(age, 0, policy.InitialStability));
            Assert.True(r <= previous + 1e-12, $"retrievability rose at age {age}: {r} > {previous}");
            previous = r;
        }
    }

    /// <summary>The seam's own written guarantee: a reinforcement may grow a memory and may leave it alone,
    /// but may never hand back a stability SMALLER than the one it was given.
    /// <para><b>The second half is the whole point (<c>docs/task-archive.md</c> Part 54, DSR2, closed
    /// 2026-08-11).</b>
    /// The ordinary fixture below starts at <see cref="IMemoryRetrievabilityPolicy.InitialStability"/>, which
    /// sits far under any ceiling a policy would impose, so it can never exercise what a ceiling does to a
    /// stability that is ALREADY past it — and for two years that was exactly where the guarantee failed:
    /// <see cref="DsrRetrievability"/> ended in a bare <c>Math.Min(grown, MaxStability)</c>, so a stored
    /// <c>100000</c> came back as <c>2000</c>, a 50× SHORTENING, and the interface documented the exception
    /// rather than closing it. A ceiling must cap GROWTH, never act as a CUT — an over-ceiling entry is
    /// FROZEN (it can no longer grow), not truncated. Reachable by lowering a ceiling under an existing
    /// corpus, or by any stability written outside the policy, so it is a contract fact rather than a
    /// DSR-only one: any future curve with a ceiling has the identical trap available.</para></summary>
    public static void Reinforcement_never_shortens_a_memory(IMemoryRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(1, 0, policy.InitialStability);

        Assert.True(policy.Reinforce(state).Stability >= state.Stability);

        // far past any ceiling a shipped or custom policy would plausibly configure, at several ages so the
        // check does not depend on where the curve happens to put r for one of them
        foreach (var age in new[] { 0, 1, 30, 5000.0 })
        {
            var overCeiling = new MemoryDecayState(age, 0, 1e6);
            var reinforced = policy.Reinforce(overCeiling).Stability;

            Assert.True(reinforced >= overCeiling.Stability,
                $"reinforcement SHORTENED a stability stored above the policy's ceiling at age {age}: " +
                $"{overCeiling.Stability} came back as {reinforced} — a ceiling must cap growth, never cut " +
                "what is already stored");
        }
    }

    /// <summary>THE unit contract (2026-08-10 memory-policy-seams plan, Task 5, Step 1).
    /// <c>Stability</c> means exactly one thing across every implementation: the position delta at which
    /// retrievability is 0.5. <see cref="DsrRetrievability"/> anchors FSRS's 90%-retention convention back
    /// onto this one by deriving its curve factor from it (<c>F = 0.5^(1/decay) - 1</c>), precisely so this
    /// holds — so this fact PINS existing behaviour rather than changing it.
    /// <para>This is what let the first draft of the design (~200 lines: policies declaring stability
    /// conventions, reconstructing foreign state, error bounds, a three-step fallback) be deleted entirely.
    /// All of it existed to let two conventions coexist; one enforced fact here makes a second convention
    /// impossible to ship, so nothing ever needs converting between two.</para>
    /// <para><b>A claim about the CURVE's own unit, not about a decorator that reads other state too</b>
    /// (fix round 2, cheap minor). <c>ModulatedRetrievability</c> satisfies this only when every registered
    /// retention policy reports its NEUTRAL factor for the state given — the model-free default, no signals
    /// judged (<c>ModulatedRetrievabilityTests.NeutralDefault</c> runs this fact against it under exactly
    /// that condition). A NON-neutral retention policy moving where retrievability crosses 0.5 for an entry is
    /// modulation working as intended, not a violation of this fact — do not run this against a policy whose
    /// factor for the fixture state is anything other than 1.</para></summary>
    public static void Stability_is_the_position_delta_at_which_retrievability_is_half(
        IMemoryRetrievabilityPolicy policy)
    {
        foreach (var stability in new[] { 0.5, 1, 7, 20, 30, 365, 3650.0 })
            Assert.Equal(0.5, policy.Retrievability(new MemoryDecayState(stability, 0, stability)),
                precision: 9);
    }

    /// <summary>The other half of the contract a state-returning <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>
    /// makes possible (2026-08-10 memory-policy-seams plan, Task 5, Step 2 — widened Task 2 of the
    /// fsrs-properly plan, when <see cref="DsrRetrievability"/> became the first policy to claim a SECOND
    /// field): a policy may grow <see cref="MemoryDecayState.Stability"/> (see
    /// <see cref="Reinforcement_never_shortens_a_memory"/> above) and may move
    /// <see cref="MemoryDecayState.Difficulty"/>, but must leave every field it does not own EXACTLY as
    /// given. Nothing today claims anything beyond those two, so this is what makes it safe for a caller to
    /// extract just those fields rather than special-casing which ones a particular policy set —
    /// <see cref="GraphMemoryEngine"/> does exactly that.
    /// <para>Deliberately does NOT assert <c>Difficulty</c> is unchanged, the same way it has never asserted
    /// <c>Stability</c> is unchanged: a policy that owns a field is free to move it, and this fact's whole
    /// job is to pin the fields NEITHER shipped policy owns.</para></summary>
    public static void Reinforcement_leaves_every_field_it_does_not_own_unchanged(
        IMemoryRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(30, 3, policy.InitialStability, Strength: 12, StrengthAge: 5,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 3));

        var reinforced = policy.Reinforce(state);

        Assert.Equal(state.Age, reinforced.Age);
        Assert.Equal(state.RecallCount, reinforced.RecallCount);
        Assert.Equal(state.Strength, reinforced.Strength);
        Assert.Equal(state.StrengthAge, reinforced.StrengthAge);
        Assert.Equal(state.Signals, reinforced.Signals);
    }

    /// <summary>THE load-bearing one. The store filters candidates with a plain
    /// <c>age / stability &lt;= cutoff</c> comparison against the STORED stability and never evaluates the
    /// curve, so a policy whose cutoff excluded an entry it would have kept would silently lose memories.
    /// Stated as a SUPERSET property rather than exact equivalence, because a custom curve is allowed to be
    /// looser.
    /// <para>The strength axis is what makes this sharp: a connectedness boost raises effective stability
    /// above the stored value, so a cutoff derived from the unboosted curve would exclude well-connected
    /// entries that are still perfectly retrievable.</para></summary>
    public static void CandidateCutoff_is_a_conservative_superset(IMemoryRetrievabilityPolicy policy)
    {
        const double minR = 0.05;
        var cutoff = policy.CandidateCutoff(minR);

        foreach (var stability in new[] { 0.5, 1, 7, 30, 365, 3650.0 })
        foreach (var age in new[] { 0, 1, 3, 7, 14, 30, 90, 365, 1000, 5000.0 })
        foreach (var strength in new[] { 0, 1, 5, 25, 100, 10_000.0 })
        {
            var state = new MemoryDecayState(age, 0, stability, strength, 0);
            var r = policy.Retrievability(state);
            if (r < minR) continue; // may be excluded; only the keepers matter

            Assert.True(age / stability <= cutoff,
                $"an entry with r={r:F4} (age {age}, stability {stability}, strength {strength}) " +
                $"falls outside cutoff {cutoff}");
        }
    }

    public static void An_unbounded_policy_is_still_correct(IMemoryRetrievabilityPolicy policy) =>
        // returning infinity is the documented escape hatch: correct, at the cost of a full in-scope scan
        Assert.True(policy.CandidateCutoff(0) > 0);

    /// <summary>Connectedness may only ever RAISE retrievability. If it could lower it, the cutoff would
    /// stop being a superset and the store would start losing memories it should have kept.</summary>
    public static void Connectedness_never_lowers_retrievability(IMemoryRetrievabilityPolicy policy)
    {
        foreach (var age in new[] { 1, 7, 30, 365.0 })
        {
            var isolated = new MemoryDecayState(age, 0, policy.InitialStability);
            var connected = isolated with { Strength = 20 };

            Assert.True(policy.Retrievability(connected) >= policy.Retrievability(isolated),
                $"connectedness lowered retrievability at age {age}");
        }
    }
}
