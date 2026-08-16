using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <see cref="IMemoryAgeCompositionPolicy"/> — the seam that combines several coexisting
/// <see cref="IMemoryAgePolicy"/>s' own ticks and ages into ONE (2026-08-10 memory-policy-seams plan, Task 3,
/// Steps 1-3). <see cref="SummedAgeCompositionPolicy"/> is pinned in isolation, and a mutation-check proves the
/// seam is genuinely load-bearing by swapping it for a different combination rule and showing the result
/// changes end to end, through a real <see cref="GraphMemoryEngine"/> — a seam nothing can vary is
/// decoration.
/// </summary>
public class MemoryAgeCompositionTests
{
    private static MemoryWrite Write(string content = "a note") => new("t", "s", content);
    private const string Engine = "e";

    [Fact]
    public void SummedAgeComposition_sums_ticks_and_ages_and_the_empty_case_is_the_identity()
    {
        var composition = new SummedAgeCompositionPolicy();

        // empty: no age policy registered is never reached in practice (the engine always falls back to a
        // default), but the seam still has to answer something sane rather than throw
        Assert.Equal(MemoryTick.One, composition.Advance([]));
        Assert.Equal(0, composition.Age([]));

        // singleton: the identity the shipped engine default relies on
        var tick = new MemoryTick(3, 0.7);
        Assert.Equal(tick, composition.Advance([tick]));
        Assert.Equal(5, composition.Age([5]), 9);

        // several: position sums, encoding multiplies, age sums
        Assert.Equal(new MemoryTick(3 + 4, 0.5 * 0.25),
            composition.Advance([new MemoryTick(3, 0.5), new MemoryTick(4, 0.25)]));
        Assert.Equal(2 + 3 + 5, composition.Age([2, 3, 5]), 9);
    }

    /// <summary>A composition policy nothing can vary is decoration — this swaps <see cref="SummedAgeCompositionPolicy"/>
    /// for a MAX-based one over the SAME two coexisting Derivable policies and shows retrievability actually
    /// moves, through a real engine rather than the composition type in isolation.</summary>
    private sealed class MaxAgeComposition : IMemoryAgeCompositionPolicy
    {
        public MemoryTick Advance(IReadOnlyList<MemoryTick> ticks) =>
            ticks.Count == 0
                ? MemoryTick.One
                : new MemoryTick(ticks.Max(t => t.Position), ticks.Min(t => t.Encoding));

        public double Age(IReadOnlyList<double> ages) => ages.Count == 0 ? 0 : ages.Max();
    }

    private static GraphMemoryEngine Build(IMemoryAgeCompositionPolicy composition) =>
        new(Engine, new InMemoryMemoryGraphStore(),
            // two coexisting Derivable dimensions with genuinely different scales: PerWrite counts writes,
            // ContentSize (perUnit: 1) counts raw characters — a long write crowds far more on the volume
            // axis than on the ordinal one, which is exactly what makes Sum and Max disagree
            agePolicies: [new PerWriteAgePolicy(), new ContentSizeAgePolicy(perUnit: 1)],
            ageComposition: composition);

    [Fact]
    public async Task Swapping_the_age_composition_policy_changes_retrievability()
    {
        var summed = Build(new SummedAgeCompositionPolicy());
        var maxed = Build(new MaxAgeComposition());

        // long content: VolumeAge (chars) grows far faster than OrdinalAge (write count), so Sum's
        // Ordinal+Volume noticeably exceeds Max's Volume alone once enough writes accumulate
        var longContent = new string('x', 200);
        foreach (var engine in new[] { summed, maxed })
        {
            await engine.RememberAsync(new MemoryWrite("t", "s", "the seed fact"));
            for (var i = 0; i < 5; i++)
                await engine.RememberAsync(new MemoryWrite("t", "s", $"{longContent} filler {i}"));
        }

        var summedHit = (await summed.RecallAsync(new MemoryQuery("t", "s", "seed"))).Items.Single();
        var maxedHit = (await maxed.RecallAsync(new MemoryQuery("t", "s", "seed"))).Items.Single();

        Assert.NotEqual(summedHit.Retrievability, maxedHit.Retrievability);
        // Sum's composed age (Ordinal + Volume) is strictly larger than Max's (Volume alone) whenever both
        // primitives are positive, so Sum must never rate the entry MORE retrievable than Max does
        Assert.True(summedHit.Retrievability < maxedHit.Retrievability,
            $"summed={summedHit.Retrievability:F6} should be lower (more aged) than maxed={maxedHit.Retrievability:F6}");
    }

    /// <summary>Echoes <see cref="MemoryDecayState.Age"/> straight back as "retrievability" so a test can read
    /// the engine's COMPOSED age directly off <c>MemoryItem.Retrievability</c>, rather than reverse-engineering
    /// it from a real curve's formula.</summary>
    private sealed class AgeEchoRetrievability : Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy
    {
        public double InitialStability => 20;

        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        public Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance Provenance =>
            (Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance)(1L << 32);
        public double Retrievability(in MemoryDecayState state) => state.Age;
        public MemoryDecayState Reinforce(in MemoryDecayState state) => state;
        public double CandidateCutoff(double minRetrievability) => double.PositiveInfinity;
    }

    /// <summary>Fix round 1, C-1 — reproduces the review's own measured scenario EXACTLY: seed, +10 days,
    /// filler, +10 days, filler, with <see cref="BurstDampenedAgePolicy"/> (Accumulating) and
    /// <see cref="ElapsedAgePolicy"/> (Derivable) registered together. Before the fix, the accumulator was
    /// inflated to 22 by ALSO summing in Elapsed's own tick at write time, and the composed READ age then
    /// added Elapsed's 20-day projection a SECOND time on top of that already-inflated 22, reading 42. The
    /// correct, unit-clean composed age is 22: the accumulator holds ONLY the Accumulating (burst) share (2 —
    /// exactly the ordinal primitive, since 10-day gaps never trigger actual bursting) and Elapsed's own
    /// projection (20) is added ONCE, not twice.</summary>
    [Fact]
    public async Task Mixed_Accumulating_and_Derivable_composes_the_derivable_contribution_exactly_once()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset Clock() => now;
        var burst = new BurstDampenedAgePolicy(new PerWriteAgePolicy(), TimeSpan.FromSeconds(5), Clock);
        var elapsed = new ElapsedAgePolicy(Clock);
        // the STORE's own clock (for EncodingAt / the ElapsedAge primitive) must be the SAME simulated one,
        // or the primitive advances by real wall-clock microseconds instead of the simulated 10-day gaps
        var store = new InMemoryMemoryGraphStore(Clock);
        var engine = new GraphMemoryEngine("mixed", store, agePolicies: [burst, elapsed],
            retrievability: new AgeEchoRetrievability());

        var seed = await engine.RememberAsync(new MemoryWrite("t", "s", "the seed fact"));
        now = now.AddDays(10);
        await engine.RememberAsync(new MemoryWrite("t", "s", "filler one"));
        now = now.AddDays(10);
        await engine.RememberAsync(new MemoryWrite("t", "s", "filler two"));

        var id = long.Parse(seed.Id, System.Globalization.CultureInfo.InvariantCulture);
        var node = await store.GetAsync("mixed", id);
        Assert.NotNull(node);

        // the WRITE-side fix, checked directly: the accumulator holds ONLY the accumulating share
        Assert.Equal(2, node!.Age, precision: 9);
        Assert.Equal(2, node.AgeSample.Ordinal, precision: 9); // unconditional primitive, unaffected either way
        Assert.Equal(20, node.AgeSample.ElapsedDays, precision: 9); // unconditional primitive, unaffected either way

        // the READ-side result, via the echo retrievability: composed age is 22, never 42
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "seed"));
        var item = Assert.Single(recall.Items);
        Assert.Equal(22, item.Retrievability, precision: 9);
    }

    [Fact]
    public void Registering_two_Accumulating_policies_throws_at_construction()
    {
        // fix round 1, C-1, rule 3: the store's position accumulator is a single number and cannot hold two
        // path-dependent quantities distinguishably — a silent sum would be exactly the quiet wrongness this
        // domain rejects everywhere else, so this must fail loudly rather than blend two burst histories.
        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine(
            "e", new InMemoryMemoryGraphStore(),
            agePolicies: [new BurstDampenedAgePolicy(), new BurstDampenedAgePolicy()]));
        Assert.Contains("Accumulating", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_Accumulating_policy_alongside_any_number_of_Derivable_ones_is_still_allowed()
    {
        // the rejection is specifically about TWO OR MORE Accumulating policies — mixing one Accumulating
        // with several coexisting Derivable ones (the whole point of Steps 1-2) must not be swept up in it
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            agePolicies: [new BurstDampenedAgePolicy(), new PerWriteAgePolicy(), new ElapsedAgePolicy()]);
        Assert.NotNull(engine);
    }
}
