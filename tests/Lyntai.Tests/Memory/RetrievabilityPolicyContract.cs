using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Facts every <see cref="IRetrievabilityPolicy"/> satisfies, run against the default curve so a
/// custom policy cannot quietly break the store's candidate query.</summary>
public static class RetrievabilityPolicyContract
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Retrievability_is_a_probability(IRetrievabilityPolicy policy)
    {
        foreach (var days in new[] { 0, 1, 7, 30, 365, 3650 })
        {
            var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
            Assert.InRange(policy.Retrievability(state, T0.AddDays(days)), 0, 1);
        }
    }

    public static void It_is_one_at_zero_elapsed_time(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);

        Assert.Equal(1.0, policy.Retrievability(state, T0), precision: 9);
    }

    public static void It_never_increases_with_age(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
        var previous = 1.0;

        for (var days = 0; days <= 400; days += 5)
        {
            var r = policy.Retrievability(state, T0.AddDays(days));
            Assert.True(r <= previous + 1e-12, $"retrievability rose at day {days}: {r} > {previous}");
            previous = r;
        }
    }

    public static void Reinforcement_never_shortens_a_memory(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);

        Assert.True(policy.Reinforce(state, T0.AddDays(1)) >= state.Stability);
    }

    /// <summary>THE load-bearing one. The store filters candidates with a plain
    /// <c>age_days / stability &lt;= cutoff</c> comparison and never evaluates the curve, so a policy whose
    /// cutoff excluded a node it would have kept would silently lose memories. Stated as a SUPERSET
    /// property rather than exact equivalence, because a custom curve is allowed to be looser.</summary>
    public static void CandidateCutoff_is_a_conservative_superset(IRetrievabilityPolicy policy)
    {
        const double minR = 0.05;
        var cutoff = policy.CandidateCutoff(minR);

        foreach (var stability in new[] { 0.5, 1, 7, 30, 365, 3650.0 })
        foreach (var days in new[] { 0, 1, 3, 7, 14, 30, 90, 365, 1000, 5000.0 })
        {
            var state = new MemoryDecayState(T0, T0, 0, stability);
            var r = policy.Retrievability(state, T0.AddDays(days));
            if (r < minR) continue; // may be excluded; only the keepers matter

            Assert.True(days / stability <= cutoff,
                $"a node with r={r:F4} (age {days}d, stability {stability}) falls outside cutoff {cutoff}");
        }
    }

    public static void An_unbounded_policy_is_still_correct(IRetrievabilityPolicy policy)
    {
        // returning infinity is the documented escape hatch: correct, at the cost of a full in-scope scan
        Assert.True(policy.CandidateCutoff(0) > 0);
    }
}
