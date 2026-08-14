using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <see cref="IMemorySalienceCompositionPolicy"/> — the seam that combines several coexisting
/// <see cref="IMemorySaliencePolicy"/>s' own bags into ONE (2026-08-10 memory-policy-seams plan, Task 3,
/// Steps 1-3). <see cref="MaximalSalienceComposition"/> is pinned in isolation, and a mutation-check proves
/// the seam is genuinely load-bearing by swapping it for a different combination rule and showing the stored
/// signal changes end to end, through a real <see cref="GraphMemoryEngine"/>.
/// </summary>
public class MemorySalienceCompositionTests
{
    private sealed class FixedSaliencePolicy(double salience) : IMemorySaliencePolicy
    {
        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        // Two INSTANCES of this same type coexist below (Build's own saliencePolicies list) sharing this one
        // bit deliberately — that is not a collision (see MemoryProvenance.EnsureEachBitIsSingleRealAndUnique's
        // own remarks): "did FixedSaliencePolicy run" is unambiguous regardless of how many instances did.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
    }

    [Fact]
    public void MaximalSalienceComposition_takes_the_largest_value_per_name_and_the_singleton_case_is_identity()
    {
        var composition = new MaximalSalienceComposition();
        var only = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);

        Assert.Equal(MemorySignals.Empty, composition.Compose([]));
        Assert.Equal(only, composition.Compose([only])); // singleton is the identity

        var low = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);
        var high = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        var composed = composition.Compose([low, high]);
        Assert.Equal(5, composed.Get(MemorySignals.WellKnown.Salience));

        // different names never collide — each survives untouched
        var novelty = MemorySignals.Empty.With("novelty", 0.9);
        var difficulty = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 3);
        var merged = composition.Compose([novelty, difficulty]);
        Assert.Equal(0.9, merged.Get("novelty"));
        Assert.Equal(3, merged.Get(MemorySignals.WellKnown.Difficulty));
    }

    /// <summary>A composition policy nothing can vary is decoration — swaps <see cref="MaximalSalienceComposition"/>
    /// for a MIN-based one over the SAME two salience policies (both writing the SAME signal name with DIFFERENT
    /// values) and shows the stored salience actually moves.</summary>
    private sealed class MinimalSalienceComposition : IMemorySalienceCompositionPolicy
    {
        public MemorySignals Compose(IReadOnlyList<MemorySignals> signals)
        {
            if (signals.Count == 0) return MemorySignals.Empty;
            if (signals.Count == 1) return signals[0];
            var merged = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var bag in signals)
                foreach (var (name, value) in bag.Values)
                    merged[name] = merged.TryGetValue(name, out var existing) ? Math.Min(existing, value) : value;
            return MemorySignals.From(merged);
        }
    }

    private static GraphMemoryEngine Build(IMemorySalienceCompositionPolicy composition) =>
        new("e", new InMemoryMemoryGraphStore(),
            saliencePolicies: [new FixedSaliencePolicy(2), new FixedSaliencePolicy(5)],
            salienceComposition: composition,
            policy: new ModulatedRetrievability(
                new Lyntai.Memory.Forgetting.DsrRetrievability(), [new SalienceRetentionPolicy()]));

    [Fact]
    public async Task Swapping_the_salience_composition_policy_changes_the_stored_signal()
    {
        var maxed = Build(new MaximalSalienceComposition());
        var minned = Build(new MinimalSalienceComposition());

        await maxed.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping"));
        await minned.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping"));

        // age both entries identically before recall: at age 0 retrievability is 1 regardless of stability,
        // which would hide any difference the composed salience actually made
        for (var i = 0; i < 40; i++)
        {
            await maxed.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler {i}"));
            await minned.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler {i}"));
        }

        var maxedRecall = (await maxed.RecallAsync(new MemoryQuery("t", "s", "fact"))).Items.Single();
        var minnedRecall = (await minned.RecallAsync(new MemoryQuery("t", "s", "fact"))).Items.Single();

        // both salience policies report the same 2 and 5, so max composes to 5 and min composes to 2 — a
        // longer half-life under max, and so a strictly higher retrievability at the same age
        Assert.NotEqual(maxedRecall.Retrievability, minnedRecall.Retrievability);
        Assert.True(maxedRecall.Retrievability > minnedRecall.Retrievability,
            $"maxed={maxedRecall.Retrievability:F6} should exceed minned={minnedRecall.Retrievability:F6}");
    }
}
