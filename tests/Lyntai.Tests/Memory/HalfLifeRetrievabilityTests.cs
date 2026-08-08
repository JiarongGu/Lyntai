using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class HalfLifeRetrievabilityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IRetrievabilityPolicy Default() => new HalfLifeRetrievability();

    [Fact] public void Probability() => RetrievabilityPolicyContract.Retrievability_is_a_probability(Default());
    [Fact] public void One_at_zero() => RetrievabilityPolicyContract.It_is_one_at_zero_elapsed_time(Default());
    [Fact] public void Monotone() => RetrievabilityPolicyContract.It_never_increases_with_age(Default());
    [Fact] public void Reinforce_grows() => RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory(Default());
    [Fact] public void Cutoff_superset() => RetrievabilityPolicyContract.CandidateCutoff_is_a_conservative_superset(Default());
    [Fact] public void Unbounded_ok() => RetrievabilityPolicyContract.An_unbounded_policy_is_still_correct(Default());

    [Fact]
    public void One_half_life_halves_retrievability()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { InitialStability = TimeSpan.FromDays(7) });
        var state = new MemoryDecayState(T0, T0, 0, 7);

        Assert.Equal(0.5, policy.Retrievability(state, T0.AddDays(7)), precision: 6);
        Assert.Equal(0.25, policy.Retrievability(state, T0.AddDays(14)), precision: 6);
    }

    [Fact]
    public void Stability_stops_growing_at_the_ceiling()
    {
        // Unbounded `stability *= 1 + Reinforce` compounds: at the default factor roughly twenty recalls
        // turn a 7-day half-life into 64 YEARS, so a hot ASSOCIATIVE node would silently acquire
        // authoritative durability without any of its guarantees. The ceiling is what stops that.
        var policy = new HalfLifeRetrievability(new HalfLifeOptions
        {
            InitialStability = TimeSpan.FromDays(7),
            ReinforceFactor = 0.5,
            MaxStability = TimeSpan.FromDays(365),
        });

        var stability = 7.0;
        for (var i = 0; i < 100; i++)
            stability = policy.Reinforce(new MemoryDecayState(T0, T0, i, stability), T0.AddDays(i));

        Assert.Equal(365, stability, precision: 6);
    }

    [Fact]
    public void A_recall_makes_the_next_forgetting_slower()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { ReinforceFactor = 0.5 });
        var before = new MemoryDecayState(T0, T0, 0, 7);
        var after = before with { Stability = policy.Reinforce(before, T0), LastRecalledAt = T0 };

        var at30 = T0.AddDays(30);

        Assert.True(policy.Retrievability(after, at30) > policy.Retrievability(before, at30));
    }

    [Fact]
    public void The_cutoff_is_the_exact_inverse_of_the_curve()
    {
        // r = 2^(-age/stability) = minR  <=>  age/stability = -log2(minR)
        Assert.Equal(Math.Log2(1 / 0.05), new HalfLifeRetrievability().CandidateCutoff(0.05), precision: 9);
    }

    [Fact]
    public void A_zero_or_negative_minimum_means_no_bound()
    {
        Assert.True(double.IsPositiveInfinity(new HalfLifeRetrievability().CandidateCutoff(0)));
    }
}
