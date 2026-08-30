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
/// <para><b>The defect this closes.</b> <c>docs/DECISIONS.md</c> D48 declares age, salience and retention
/// plural, each owning a composition policy. Age and salience were engine constructor parameters; retention
/// was not, and arrived only inside a hand-built <see cref="ModulatedRetrievability"/>. So a DI-built engine
/// applied retention and a hand-built one silently did not — and every bench sweep hand-builds. A divergence
/// of exactly that class produced a measurement defect on 2026-08-30 that published wrong figures for
/// days.</para>
///
/// <para><b>Why it is a modelling fix rather than a convenience.</b> A domain reaching the engine through
/// ANOTHER domain's constructor is a modelling error whatever it costs to use. Making the engine the single
/// composition root also puts the <see cref="ModulatedRetrievability"/> invariant somewhere it cannot be
/// reached wrongly: the composed maximum and the per-entry clamp must be computed from the same enumeration,
/// because a product smaller than the clamp narrows
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> — and that narrowing
/// DELETES.</para>
/// </summary>
public class GraphMemoryRetentionWiringTests
{
    private sealed class FixedRetentionPolicy(double factor) : IMemoryRetentionPolicy
    {
        public string Name => "fixed";
        public double MaxStabilityFactor => factor;
        public double StabilityFactor(in MemoryDecayState state) => factor;
    }

    private static GraphMemoryEngine Engine(IMemoryRetentionPolicy[]? retention) =>
        new("e", new InMemoryMemoryGraphStore(),
            agePolicies: [new PerWriteAgePolicy()],
            retentionPolicies: retention);

    /// <summary>A hand-built engine handed retention policies APPLIES them, with no decorator in sight.
    /// <para>Before this, the only way to get retention into a hand-built engine was to know that
    /// <c>retrievability:</c> secretly wanted a <c>ModulatedRetrievability</c> — knowledge nothing on the
    /// constructor hinted at.</para></summary>
    [Fact]
    public async Task Retention_passed_to_the_ENGINE_lengthens_retrievability_without_a_decorator()
    {
        var write = new MemoryWrite("t", "s", "a fact worth keeping around");

        var plain = Engine(null);
        var retained = Engine([new FixedRetentionPolicy(4)]);
        await plain.RememberAsync(write);
        await retained.RememberAsync(write);

        // Age the engines identically, so the only difference is the stability multiplier retention applies.
        for (var i = 0; i < 40; i++)
        {
            await plain.RememberAsync(new MemoryWrite("t", "s", $"filler {i}"));
            await retained.RememberAsync(new MemoryWrite("t", "s", $"filler {i}"));
        }

        var query = new MemoryQuery("t", "s", "a fact worth keeping around", Limit: 10);
        var plainItem = Assert.Single((await plain.RecallAsync(query)).Items,
            i => i.Headline.Contains("worth keeping", StringComparison.Ordinal));
        var retainedItem = Assert.Single((await retained.RecallAsync(query)).Items,
            i => i.Headline.Contains("worth keeping", StringComparison.Ordinal));

        Assert.True(retainedItem.Retrievability > plainItem.Retrievability,
            $"retention did not reach the engine: {retainedItem.Retrievability} vs {plainItem.Retrievability}");
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

        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e",
            new InMemoryMemoryGraphStore(),
            retrievability: preWrapped,
            retentionPolicies: [new FixedRetentionPolicy(3)]));

        Assert.Contains("twice", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A pre-modulated curve ALONE is still accepted — that is the composition route the seam's
    /// publicness exists for, and only the double-application is refused.</summary>
    [Fact]
    public void A_pre_modulated_curve_on_its_own_is_still_accepted() =>
        Assert.NotNull(new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            retrievability: new ModulatedRetrievability(
                new Lyntai.Memory.Forgetting.DsrRetrievability(), [new FixedRetentionPolicy(2)])));

    /// <summary>The composition policy is the engine's too, so several coexisting retention dimensions
    /// combine by a rule the caller can replace — the second half of what D48 calls a plural domain, and the
    /// half a decorator-only path made reachable exclusively through a constructor overload.</summary>
    [Fact]
    public void An_engine_accepts_a_retention_COMPOSITION_alongside_the_policies() =>
        Assert.NotNull(new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            agePolicies: [new PerWriteAgePolicy()],
            retentionPolicies: [new FixedRetentionPolicy(2), new FixedRetentionPolicy(3)],
            retentionComposition: new MultiplicativeRetentionCompositionPolicy()));
}
