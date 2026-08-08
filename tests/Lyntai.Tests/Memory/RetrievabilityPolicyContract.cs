using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Facts every <see cref="IRetrievabilityPolicy"/> satisfies, run against the default curve so a
/// custom policy cannot quietly break the store's candidate query.
/// <para>A policy sees no clock: age is a dimensionless quantity the engine's <see cref="IMemoryClock"/>
/// defines, and stability is in the same units.</para></summary>
public static class RetrievabilityPolicyContract
{
    public static void Retrievability_is_a_probability(IRetrievabilityPolicy policy)
    {
        foreach (var age in new[] { 0, 1, 7, 30, 365, 3650.0 })
            Assert.InRange(policy.Retrievability(new MemoryDecayState(age, 0, policy.InitialStability)), 0, 1);
    }

    public static void It_is_one_at_zero_age(IRetrievabilityPolicy policy) =>
        Assert.Equal(1.0, policy.Retrievability(new MemoryDecayState(0, 0, policy.InitialStability)),
            precision: 9);

    public static void It_never_increases_with_age(IRetrievabilityPolicy policy)
    {
        var previous = 1.0;
        for (var age = 0; age <= 400; age += 5)
        {
            var r = policy.Retrievability(new MemoryDecayState(age, 0, policy.InitialStability));
            Assert.True(r <= previous + 1e-12, $"retrievability rose at age {age}: {r} > {previous}");
            previous = r;
        }
    }

    public static void Reinforcement_never_shortens_a_memory(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(1, 0, policy.InitialStability);

        Assert.True(policy.Reinforce(state) >= state.Stability);
    }

    /// <summary>THE load-bearing one. The store filters candidates with a plain
    /// <c>age / stability &lt;= cutoff</c> comparison against the STORED stability and never evaluates the
    /// curve, so a policy whose cutoff excluded an entry it would have kept would silently lose memories.
    /// Stated as a SUPERSET property rather than exact equivalence, because a custom curve is allowed to be
    /// looser.
    /// <para>The strength axis is what makes this sharp: a connectedness boost raises effective stability
    /// above the stored value, so a cutoff derived from the unboosted curve would exclude well-connected
    /// entries that are still perfectly retrievable.</para></summary>
    public static void CandidateCutoff_is_a_conservative_superset(IRetrievabilityPolicy policy)
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

    public static void An_unbounded_policy_is_still_correct(IRetrievabilityPolicy policy) =>
        // returning infinity is the documented escape hatch: correct, at the cost of a full in-scope scan
        Assert.True(policy.CandidateCutoff(0) > 0);

    /// <summary>Connectedness may only ever RAISE retrievability. If it could lower it, the cutoff would
    /// stop being a superset and the store would start losing memories it should have kept.</summary>
    public static void Connectedness_never_lowers_retrievability(IRetrievabilityPolicy policy)
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
