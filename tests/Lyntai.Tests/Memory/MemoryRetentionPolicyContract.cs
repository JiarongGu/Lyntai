using Lyntai.Memory;
using Lyntai.Memory.Modulation;

namespace Lyntai.Tests.Memory;

/// <summary>Policy-agnostic facts every <see cref="IMemoryRetentionPolicy"/> satisfies.
///
/// <para><b>Why this file exists.</b> Retention was one of four seams with no contract while
/// <c>PolicyContractCoverageTests</c> made coverage structural for three others. Added 2026-08-17 by the
/// pre-3.0 sweep (archive Part 86).</para>
///
/// <para><b>What a violation costs is DELETION, not a short recall.</b> <c>CandidateCutoff</c> is expressed
/// against the STORED stability, which modulation does not change, so the cutoff widens by the product of
/// every registered policy's declared maximum. Seeding applies no faintness bound at all, so that cutoff's
/// one consumer is <c>PruneAsync</c>. A policy that exceeds its own declared bound therefore makes entries
/// the modulated curve still rates perfectly retrievable permanently removable. The decorator clamps rather
/// than trusts — "soundness must not depend on an implementation being honest about itself" — and this
/// contract is what checks the honesty the clamp is covering for.</para></summary>
public static class MemoryRetentionPolicyContract
{
    /// <summary>States spanning the axes a policy may legitimately read — signals and grade — plus the one it
    /// may not: age.</summary>
    private static MemoryDecayState[] States() =>
    [
        new(Age: 0, RecallCount: 0, Stability: 10),
        new(Age: 1000, RecallCount: 50, Stability: 10),
        new(Age: 0, RecallCount: 0, Stability: 10,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 1)),
        new(Age: 0, RecallCount: 0, Stability: 10,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 3)),
        new(Age: 0, RecallCount: 0, Stability: 10,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 1e9)),
        new(Age: 0, RecallCount: 0, Stability: 10,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN)),
        new(Age: 0, RecallCount: 0, Stability: 10,
            Signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, -4)),
    ];

    /// <summary>The declared bound is itself finite and at least 1 — stated on the member. A bound below the
    /// neutral value is not a smaller ceiling, it is a contradiction; an infinite one makes NO finite
    /// <c>CandidateCutoff</c> correct, which is the unbounded case the interface's remarks name.</summary>
    public static void MaxStabilityFactor_is_finite_and_at_least_one(IMemoryRetentionPolicy policy)
    {
        Assert.True(double.IsFinite(policy.MaxStabilityFactor),
            $"{policy.GetType().Name} declares a non-finite MaxStabilityFactor ({policy.MaxStabilityFactor})");
        Assert.True(policy.MaxStabilityFactor >= 1,
            $"{policy.GetType().Name} declares MaxStabilityFactor {policy.MaxStabilityFactor}, below the neutral 1");
    }

    /// <summary>The factor only ever LENGTHENS: at or above 1, and finite. Floors and unconditional admission
    /// come from <see cref="MemoryGrade"/> alone, which is what keeps every storage-side decision indexable on
    /// the grade column rather than on a value buried in a signal bag — a policy that shortened a half-life
    /// would be making a decision this seam deliberately does not own.</summary>
    public static void The_factor_is_finite_and_never_shortens_a_half_life(IMemoryRetentionPolicy policy)
    {
        foreach (var state in States())
        {
            var factor = policy.StabilityFactor(state);
            Assert.True(double.IsFinite(factor),
                $"{policy.GetType().Name} returned a non-finite factor ({factor}) for {state}");
            Assert.True(factor >= 1,
                $"{policy.GetType().Name} returned {factor} for {state}, which would SHORTEN a half-life");
        }
    }

    /// <summary>The factor never exceeds the bound the policy itself declared — the obligation the whole
    /// cutoff widening rests on. Includes a state whose salience signal is absurd (1e9) and one whose signal
    /// is <see cref="double.NaN"/>, because a policy reading a signal it did not write must bound what it
    /// found rather than trust it.</summary>
    public static void The_factor_never_exceeds_the_declared_maximum(IMemoryRetentionPolicy policy)
    {
        foreach (var state in States())
        {
            var factor = policy.StabilityFactor(state);
            Assert.True(factor <= policy.MaxStabilityFactor,
                $"{policy.GetType().Name} returned {factor} for {state}, above its own declared "
                + $"MaxStabilityFactor of {policy.MaxStabilityFactor} — CandidateCutoff is widened by the "
                + "DECLARED value, so the excess is entries PruneAsync deletes while they are still retrievable");
        }
    }

    /// <summary><b>The factor must not key on AGE</b> — stated on the member: "decide the factor from
    /// <c>state</c>'s signals and the entry's grade, never from its age", because
    /// <c>Retrievability</c> must never increase with age and a factor that grew with it would break that
    /// guarantee once run through <see cref="ModulatedRetrievability"/>. Asserted by holding everything else
    /// fixed and moving age alone, which is the only way to see it from outside.</summary>
    public static void The_factor_does_not_depend_on_age(IMemoryRetentionPolicy policy)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2.5);
        var young = new MemoryDecayState(Age: 0, RecallCount: 3, Stability: 10, Signals: signals);
        var old = young with { Age = 10_000 };

        Assert.Equal(policy.StabilityFactor(young), policy.StabilityFactor(old), precision: 12);
    }

    /// <summary>PURE: the same state yields the same factor however often it is asked. <c>ModulatedRetrievability</c>
    /// calls every registered policy on every retrievability evaluation — which happens per candidate, per
    /// recall — so a policy with hidden state would make one recall's ranking depend on how many recalls
    /// preceded it.</summary>
    public static void It_is_a_pure_function_of_the_state(IMemoryRetentionPolicy policy)
    {
        foreach (var state in States())
            Assert.Equal(policy.StabilityFactor(state), policy.StabilityFactor(state), precision: 12);
    }

    /// <summary><see cref="IMemoryRetentionPolicy.Name"/> identifies the dimension in diagnostics, so an
    /// empty one makes a composed factor unattributable — the only thing the member is for.</summary>
    public static void It_has_a_name(IMemoryRetentionPolicy policy) =>
        Assert.False(string.IsNullOrWhiteSpace(policy.Name),
            $"{policy.GetType().Name} has no Name, so its contribution cannot be attributed in diagnostics");
}
