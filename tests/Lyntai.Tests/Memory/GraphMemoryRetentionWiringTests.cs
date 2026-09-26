using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Retention reaches the engine the way every other plural domain does — as a registered, composed
/// collection the ENGINE owns, not pre-wrapped inside somebody else's constructor.
///
/// <para><b>The failure this prevents.</b> <c>docs/DECISIONS.md</c> D48 declares age, salience and retention
/// plural, each owning a composition policy. Retention that arrives only inside a hand-built
/// <see cref="ModulatedRetrievability"/> is applied by a DI-built engine and silently skipped by a hand-built
/// one — and every bench sweep hand-builds (<c>docs/FIXES.md</c> 2026-08-30, <c>memory-salience</c>, is a
/// divergence of that class that published wrong figures).</para>
///
/// <para><b>Why it is a modelling rule rather than a convenience.</b> A domain reaching the engine through
/// ANOTHER domain's constructor is a modelling error whatever it costs to use. Making the engine the single
/// composition root also puts the <see cref="ModulatedRetrievability"/> invariant somewhere it cannot be
/// reached wrongly: the composed maximum and the per-entry clamp must be computed from the same enumeration,
/// because a product smaller than the clamp narrows
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> — and that narrowing
/// DELETES.</para>
/// </summary>
public class GraphMemoryRetentionWiringTests
{
    /// <summary>A multiplier combination the library does not ship, so a test can tell which rule ran.</summary>
    private sealed class MaxRetentionComposition : IMemoryRetentionCompositionPolicy
    {
        public double StabilityFactor(IReadOnlyList<double> factors) => factors.Count == 0 ? 1 : factors.Max();
    }

    /// <summary>The retrievability one aged entry recalls at under <paramref name="seams"/> — the age policy is
    /// fixed, so the only thing that differs between two calls is the retention wiring under test.</summary>
    private static async Task<double> RecalledRetrievability(GraphMemorySeams seams)
    {
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            seams: seams with { AgePolicies = [new PerWriteAgePolicy()] });
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping around"));
        for (var i = 0; i < 40; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"filler {i}"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "a fact worth keeping around", Limit: 10));
        return Assert.Single(recall.Items, i => i.Headline.Contains("worth keeping", StringComparison.Ordinal))
            .Retrievability;
    }

    /// <summary>A hand-built engine handed retention policies APPLIES them, with no decorator in sight.</summary>
    [Fact]
    public async Task Retention_passed_to_the_ENGINE_lengthens_retrievability_without_a_decorator()
    {
        var plain = await RecalledRetrievability(new GraphMemorySeams());
        var retained = await RecalledRetrievability(new GraphMemorySeams
            {
                RetentionPolicies = [new FixedRetentionPolicy(4)],
            });

        Assert.True(retained > plain, $"retention did not reach the engine: {retained} vs {plain}");
    }

    /// <summary>
    /// Supplying BOTH a pre-modulated curve and retention policies is refused at construction, not applied
    /// twice.
    ///
    /// <para><b>ModulatedRetrievability stays PUBLIC</b>, because it implements a public seam and a consumer
    /// composing their own curve — or not using this engine at all — has a legitimate reason to build one.
    /// Hiding a type to remove an ambiguity fixes the wrong thing. What is refused is the one combination
    /// that cannot be meant: modulation applied twice multiplies stability twice, and the entry would then
    /// outlive what any retention policy declared, breaking
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/>'s superset
    /// guarantee — whose only consumer DELETES.</para>
    ///
    /// <para>Reported at WIRING time (<c>docs/DECISIONS.md</c> D85's idiom) rather than silently preferring
    /// one, because either silent choice is a stability figure the caller did not ask for.</para>
    /// </summary>
    [Fact]
    public void Supplying_a_pre_modulated_curve_AND_retention_policies_is_refused()
    {
        var preWrapped = new ModulatedRetrievability(
            new Lyntai.Memory.Forgetting.DsrRetrievability(), [new FixedRetentionPolicy(2)]);

        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Retrievability = preWrapped,
                RetentionPolicies = [new FixedRetentionPolicy(3)],
            }));

        Assert.Contains("twice", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A pre-modulated curve ALONE is still accepted AND applied — that is the composition route the
    /// seam's publicness exists for, and only the double-application is refused.</summary>
    [Fact]
    public async Task A_pre_modulated_curve_on_its_own_is_still_accepted()
    {
        var plain = await RecalledRetrievability(new GraphMemorySeams());
        var preModulated = await RecalledRetrievability(new GraphMemorySeams
            {
                Retrievability = new ModulatedRetrievability(
                    new Lyntai.Memory.Forgetting.DsrRetrievability(), [new FixedRetentionPolicy(2)]),
            });

        Assert.True(preModulated > plain, $"the pre-modulated curve was not applied: {preModulated} vs {plain}");
    }

    /// <summary>The composition policy is the engine's too, so several coexisting retention dimensions
    /// combine by a rule the caller can replace — the second half of what D48 calls a plural domain. Two
    /// rules over the same factors (2 and 3) must recall differently: 6× multiplied, 3× by maximum.</summary>
    [Fact]
    public async Task An_engine_accepts_a_retention_COMPOSITION_alongside_the_policies()
    {
        GraphMemorySeams Composed(IMemoryRetentionCompositionPolicy composition) => new()
        {
            RetentionPolicies = [new FixedRetentionPolicy(2), new FixedRetentionPolicy(3)],
            RetentionComposition = composition,
        };

        var multiplied = await RecalledRetrievability(Composed(new MultiplicativeRetentionCompositionPolicy()));
        var maximum = await RecalledRetrievability(Composed(new MaxRetentionComposition()));

        Assert.True(multiplied > maximum, $"the composition passed was not the one applied: {multiplied} vs {maximum}");
    }
}
