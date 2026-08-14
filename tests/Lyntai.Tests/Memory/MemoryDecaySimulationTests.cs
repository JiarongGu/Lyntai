using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// MEM-TUNE: the measurement that turns the decay constants from guesses into values chosen against a
/// stated criterion — and then keeps them there.
/// <para>Nine constants govern forgetting, and they INTERACT: edge decay erodes the strength that feeds the
/// connection boost, reinforcement fights the interference the same writes cause, and burst damping changes
/// what a write costs. Measuring them one at a time would measure the wrong thing, so this drives a corpus
/// with a KNOWN split through the whole model at once and asserts outcomes rather than values.</para>
/// <para><b>Nothing here asserts that a memory is GONE</b>, because nothing in this model removes one.
/// Decay buries: a faint entry is hidden by being outranked, and a targeted query for it still returns it.
/// So "forgotten" is measured as <i>absent from a broad recall it has to compete in</i>, or as a collapsed
/// retrievability — never as a missing row.</para>
/// <para><b>What this proves, stated plainly:</b> it measures the DYNAMICS — that reuse outruns
/// interference by the intended margin — and it runs in CI, which a production corpus cannot. It does NOT
/// establish that real usage has the reuse-to-noise ratio modelled here. So the constants are "measured
/// against a stated model", not "measured", and the XML docs say exactly that.</para>
/// <para>Deliberately uses an undamped <see cref="PerWriteAgePolicy"/> for the interference runs: the
/// simulation writes as fast as the test executes, so burst damping would make every round one enormous
/// burst and measure the damping instead of the decay. Damping has its own scenario at the bottom.</para>
/// </summary>
public class MemoryDecaySimulationTests
{
    private const int Rounds = 20;
    private const int NoisePerRound = 10;
    private const int DurableFacts = 10;

    private static GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("sim", new InMemoryMemoryGraphStore(), options, agePolicies: [new PerWriteAgePolicy()]);

    // every entry carries "item" so a BROAD recall makes them compete — which is where burial shows up
    private static string Durable(int i) => $"item durable{i} is a fact worth keeping about the system";
    private static string Noise(int round, int i) => $"item noise{round}x{i} was mentioned once, never again";
    private static string Document(int i) =>
        $"item paragraph {i.ToString(CultureInfo.InvariantCulture)} of a long ingested file";

    /// <summary>Write the durable set, then alternate: a round of one-off noise, then a pass that uses each
    /// durable fact (which reinforces it).</summary>
    private static async Task<GraphMemoryEngine> RunAsync(GraphMemoryOptions? options = null)
    {
        var engine = Engine(options);
        for (var i = 0; i < DurableFacts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Durable(i)));

        for (var round = 0; round < Rounds; round++)
        {
            for (var i = 0; i < NoisePerRound; i++)
                await engine.RememberAsync(new MemoryWrite("t", "s", Noise(round, i)));

            // the durable facts are USED each round — that is what makes them durable
            for (var i = 0; i < DurableFacts; i++)
                await engine.RecallAsync(new MemoryQuery("t", "s", $"durable{i}"));
        }

        return engine;
    }

    /// <summary>What everything has to compete in. Burial is relative, so it is only observable here.</summary>
    private static Task<MemoryRecall> BroadRecallAsync(GraphMemoryEngine engine, int limit = 30) =>
        engine.RecallAsync(new MemoryQuery("t", "s", "item", Limit: limit));

    [Fact]
    public async Task Reused_material_holds_its_place_in_a_crowded_recall()
    {
        var engine = await RunAsync();

        var recall = await BroadRecallAsync(engine);
        var kept = Enumerable.Range(0, DurableFacts)
            .Count(i => recall.Items.Any(x => x.Headline.Contains($"durable{i}", StringComparison.Ordinal)));

        Assert.True(kept >= DurableFacts * 0.9,
            $"only {kept}/{DurableFacts} reused facts made a crowded recall — reinforcement is not "
            + "outrunning interference");
    }

    [Fact]
    public async Task One_off_material_from_early_in_the_run_is_buried()
    {
        // measured over the FIRST HALF only: material written moments ago SHOULD still surface — it is
        // recent. What must fall behind is what was mentioned once, long ago.
        var engine = await RunAsync();

        var recall = await BroadRecallAsync(engine);
        var surfacing = 0;
        for (var round = 0; round < Rounds / 2; round++)
            for (var i = 0; i < NoisePerRound; i++)
                if (recall.Items.Any(x => x.Headline.Contains($"noise{round}x{i}", StringComparison.Ordinal)))
                    surfacing++;

        Assert.Equal(0, surfacing);
    }

    [Fact]
    public async Task What_is_buried_is_still_there_when_asked_for_directly()
    {
        // the whole distinction: buried, not cut. It lost the competition; it was not deleted.
        var engine = await RunAsync();

        var targeted = await engine.RecallAsync(new MemoryQuery("t", "s", "noise0x0"));

        Assert.Single(targeted.Items);
        // Threshold loosened for DsrRetrievability (2026-08-10, fsrs-properly plan Task 1): this corpus's own
        // 210-write run only ages noise0x0 to age/InitialStability ~10, where DSR's heavier tail (the reason
        // it was adopted) sits at r≈0.18 (MEASURED, fix round 1: 0.180041) — the deleted exponential curve
        // fell under 0.05 at the same age. 0.3 still pins "faint" (well under a fresh recall's r=1, and with
        // real headroom over the measured 0.18) without asking this corpus's own constants, shared with
        // several other facts in this file, to change.
        Assert.True(targeted.Items[0].Retrievability < 0.3, "and it comes back faint, which is the point");
    }

    [Fact]
    public async Task Reused_material_outranks_what_survives_of_the_noise()
    {
        var engine = await RunAsync();

        var durable = await engine.RecallAsync(new MemoryQuery("t", "s", "durable", Limit: 50));
        var worstDurable = durable.Items.Min(i => i.Retrievability);

        var oldNoise = await engine.RecallAsync(new MemoryQuery("t", "s", "noise0x", Limit: 50));
        var bestOldNoise = oldNoise.Items.Count == 0 ? 0 : oldNoise.Items.Max(i => i.Retrievability);

        Assert.True(worstDurable > bestOldNoise,
            $"the weakest reused fact ({worstDurable:F4}) does not beat the strongest old one-off "
            + $"({bestOldNoise:F4})");
    }

    [Fact]
    public async Task The_separation_holds_at_twice_the_run_length()
    {
        // guards against constants tuned to one point on the curve: the same corpus, driven twice as long
        var engine = await RunAsync();
        for (var round = Rounds; round < Rounds * 2; round++)
        {
            for (var i = 0; i < NoisePerRound; i++)
                await engine.RememberAsync(new MemoryWrite("t", "s", Noise(round, i)));
            for (var i = 0; i < DurableFacts; i++)
                await engine.RecallAsync(new MemoryQuery("t", "s", $"durable{i}"));
        }

        var recall = await BroadRecallAsync(engine);
        var kept = Enumerable.Range(0, DurableFacts)
            .Count(i => recall.Items.Any(x => x.Headline.Contains($"durable{i}", StringComparison.Ordinal)));

        Assert.True(kept >= DurableFacts * 0.9, $"only {kept}/{DurableFacts} survived the longer run");
    }

    /// <summary>The scenario burst damping exists for: bulk-ingesting a large document must not wash out
    /// what the memory already held.
    /// <para>Measured as RETRIEVABILITY, not presence — 500 fresher paragraphs legitimately outrank
    /// everything in a top-30, and that is not forgetting. What damping protects is how retrievable the
    /// prior material remains, which the control below shows collapsing without it.</para></summary>
    [Fact]
    public async Task A_bulk_ingest_does_not_wash_out_what_was_already_known()
    {
        var engine = new GraphMemoryEngine("sim", new InMemoryMemoryGraphStore()); // the DAMPED default
        for (var i = 0; i < DurableFacts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Durable(i)));

        for (var i = 0; i < 500; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Document(i)));

        for (var i = 0; i < DurableFacts; i++)
        {
            var hit = (await engine.RecallAsync(new MemoryQuery("t", "s", $"durable{i}"))).Items.Single();
            // MEASURED (fix round 1, confirming this threshold is still correct for DsrRetrievability):
            // durable0 reads 0.730903 here — comfortably above 0.25, and the pair below (the undamped
            // control) reads 0.113702 at the identical scenario, so the two thresholds still BRACKET rather
            // than having drifted together in the same direction.
            Assert.True(hit.Retrievability > 0.25,
                $"durable{i} was washed out by the ingest (r={hit.Retrievability:F4})");
        }
    }

    /// <summary>...and the same ingest undamped, so the damping is shown to be doing the work rather than
    /// the numbers happening to be forgiving. A regression that silently disabled it would otherwise pass
    /// the test above only until the corpus grew.</summary>
    [Fact]
    public async Task The_same_ingest_undamped_washes_it_out()
    {
        var engine = Engine(); // PerWriteAgePolicy, no damping
        for (var i = 0; i < DurableFacts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Durable(i)));

        for (var i = 0; i < 500; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Document(i)));

        for (var i = 0; i < DurableFacts; i++)
        {
            var hit = (await engine.RecallAsync(new MemoryQuery("t", "s", $"durable{i}"))).Items.Single();
            // Threshold loosened for DsrRetrievability (2026-08-10, fsrs-properly plan Task 1): the deleted
            // exponential curve's 2^(-age/S) fell under 0.001 at this scenario's age/S≈25; DSR's heavier tail
            // — the reason it was adopted — sits at r≈0.11 there instead (MEASURED, fix round 1: durable0
            // reads 0.113702), so "washed out" can no longer mean "near zero." The fair, still-discriminating
            // bar is the SAME 0.25 the damped control above clears comfortably (MEASURED: 0.730903 there):
            // this asserts the undamped case falls BELOW that bar, which is exactly the contrast this pair
            // of facts exists to show, from the other direction — the two thresholds bracket
            // (0.1137 < 0.25 < 0.7309), not two numbers that drifted together.
            Assert.True(hit.Retrievability < 0.25,
                $"durable{i} should have been washed out undamped (r={hit.Retrievability:F6})");
        }
    }
}
