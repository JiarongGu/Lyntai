using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class HalfLifeRetrievabilityTests
{
    private static IRetrievabilityPolicy Default() => new HalfLifeRetrievability();

    [Fact] public void Probability() => RetrievabilityPolicyContract.Retrievability_is_a_probability(Default());
    [Fact] public void One_at_zero() => RetrievabilityPolicyContract.It_is_one_at_zero_age(Default());
    [Fact] public void Monotone() => RetrievabilityPolicyContract.It_never_increases_with_age(Default());
    [Fact] public void Reinforce_grows() => RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory(Default());
    [Fact] public void Cutoff_superset() => RetrievabilityPolicyContract.CandidateCutoff_is_a_conservative_superset(Default());
    [Fact] public void Unbounded_ok() => RetrievabilityPolicyContract.An_unbounded_policy_is_still_correct(Default());
    [Fact] public void Connectedness() => RetrievabilityPolicyContract.Connectedness_never_lowers_retrievability(Default());

    [Fact]
    public void One_half_life_halves_retrievability()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { InitialStability = 7 });

        Assert.Equal(0.5, policy.Retrievability(new MemoryDecayState(7, 0, 7)), precision: 6);
        Assert.Equal(0.25, policy.Retrievability(new MemoryDecayState(14, 0, 7)), precision: 6);
    }

    [Fact]
    public void Stability_stops_growing_at_the_ceiling()
    {
        // Unbounded `stability *= 1 + Reinforce` compounds: at the default factor roughly twenty recalls
        // turn a small half-life into an enormous one, so a hot ASSOCIATIVE entry would silently acquire
        // authoritative durability without any of its guarantees. The ceiling is what stops that.
        var policy = new HalfLifeRetrievability(new HalfLifeOptions
        {
            InitialStability = 7,
            ReinforceFactor = 0.5,
            MaxStability = 365,
        });

        var stability = 7.0;
        for (var i = 0; i < 100; i++)
            stability = policy.Reinforce(new MemoryDecayState(1, i, stability));

        Assert.Equal(365, stability, precision: 6);
    }

    [Fact]
    public void A_recall_makes_the_next_forgetting_slower()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { ReinforceFactor = 0.5 });
        var before = new MemoryDecayState(30, 0, 7);
        var after = before with { Stability = policy.Reinforce(before) };

        Assert.True(policy.Retrievability(after) > policy.Retrievability(before));
    }

    [Fact]
    public void A_well_connected_memory_outlives_an_isolated_one()
    {
        var policy = new HalfLifeRetrievability();
        var isolated = new MemoryDecayState(30, 0, 7);
        var connected = isolated with { Strength = 20 };

        Assert.True(policy.Retrievability(connected) > policy.Retrievability(isolated) * 2,
            "connectedness barely mattered");
    }

    [Fact]
    public void Links_that_stop_recurring_stop_propping_a_memory_up()
    {
        // the whole point of edge decay: a neighbourhood that went quiet must not keep a memory alive
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { EdgeHalfLife = 30 });
        var node = new MemoryDecayState(30, 0, 7, Strength: 50, StrengthAge: 0);

        var fresh = policy.Retrievability(node);
        var stale = policy.Retrievability(node with { StrengthAge = 3650 });

        Assert.True(stale < fresh, "a long-stale neighbourhood still boosted the memory");
    }

    [Fact]
    public void The_boost_is_bounded_so_a_hub_does_not_become_immortal()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions
        {
            InitialStability = 7,
            MaxConnectionBoost = 4,
            MaxStability = 365,
        });

        // stability 7 x MaxBoost 4 = an effective half-life of 28, so age 28 must land near 0.5, not near 1
        var hub = new MemoryDecayState(28, 0, 7, Strength: 1_000_000, StrengthAge: 0);

        Assert.InRange(policy.Retrievability(hub), 0.4, 0.6);
    }

    [Fact]
    public void The_cutoff_is_the_exact_inverse_of_the_curve_when_nothing_boosts_it()
    {
        // r = 2^(-age/stability) = minR  <=>  age/stability = -log2(minR)
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { MaxConnectionBoost = 1 });

        Assert.Equal(Math.Log2(1 / 0.05), policy.CandidateCutoff(0.05), precision: 9);
    }

    [Fact]
    public void The_cutoff_widens_by_exactly_the_boost_ceiling()
    {
        // the store filters on STORED stability, so the cutoff must cover the largest boost possible or a
        // well-connected entry gets excluded while still being perfectly retrievable
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { MaxConnectionBoost = 4 });

        Assert.Equal(Math.Log2(1 / 0.05) * 4, policy.CandidateCutoff(0.05), precision: 9);
    }

    [Fact]
    public void A_zero_or_negative_minimum_means_no_bound() =>
        Assert.True(double.IsPositiveInfinity(new HalfLifeRetrievability().CandidateCutoff(0)));
}
