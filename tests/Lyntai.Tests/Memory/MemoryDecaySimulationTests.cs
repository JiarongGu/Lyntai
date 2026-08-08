using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// MEM-TUNE: the measurement that turns the decay constants from guesses into values chosen against a
/// stated criterion — and then keeps them there.
/// <para>Nine constants govern forgetting, and they INTERACT: edge decay erodes the strength that feeds the
/// connection boost, reinforcement fights the interference the same writes cause, and burst damping changes
/// what a write costs. Measuring them one at a time would measure the wrong thing, so this drives a corpus
/// with a KNOWN split through the whole model at once and asserts outcomes rather than values.</para>
/// <para><b>What this proves, stated plainly:</b> it measures the DYNAMICS — that reuse outruns
/// interference by the intended margin — and it runs in CI, which a production corpus cannot. It does NOT
/// establish that real usage has the reuse-to-noise ratio modelled here. So the constants are "measured
/// against a stated model", not "measured", and the XML docs say exactly that. Replacing the model with a
/// real corpus later is a strict improvement, not a prerequisite.</para>
/// <para>Deliberately uses an undamped <see cref="PerWriteClock"/>: the simulation writes as fast as the
/// test runs, so burst damping would make every round one enormous burst and measure the damping instead
/// of the decay. Damping has its own tests, and its own scenario at the bottom of this file.</para>
/// </summary>
public class MemoryDecaySimulationTests
{
    private const int Rounds = 20;
    private const int NoisePerRound = 10;
    private const int DurableFacts = 10;

    private static GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("sim", new InMemoryMemoryGraphStore(), options, memoryClock: new PerWriteClock());

    private static string Durable(int i) => $"durable{i} is a fact worth keeping about the system";
    private static string Noise(int round, int i) => $"noise{round}x{i} was mentioned once and never again";

    /// <summary>Write the durable set, then alternate: a round of one-off noise, then a pass that uses each
    /// durable fact (which reinforces it). Returns the engine.</summary>
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

    private static async Task<bool> RecallableAsync(GraphMemoryEngine engine, string token) =>
        (await engine.RecallAsync(new MemoryQuery("t", "s", token))).Items.Count > 0;

    [Fact]
    public async Task Reused_material_survives()
    {
        var engine = await RunAsync();

        var kept = 0;
        for (var i = 0; i < DurableFacts; i++)
            if (await RecallableAsync(engine, $"durable{i}")) kept++;

        Assert.True(kept >= DurableFacts * 0.9,
            $"only {kept}/{DurableFacts} reused facts survived — reinforcement is not outrunning interference");
    }

    [Fact]
    public async Task One_off_material_from_early_in_the_run_is_gone()
    {
        // deliberately measured over the FIRST HALF only: noise written moments ago SHOULD still be
        // recallable — it is recent. What must fade is what was mentioned once, long ago.
        var engine = await RunAsync();

        var surviving = 0;
        var total = 0;
        for (var round = 0; round < Rounds / 2; round++)
            for (var i = 0; i < NoisePerRound; i++)
            {
                total++;
                if (await RecallableAsync(engine, $"noise{round}x{i}")) surviving++;
            }

        Assert.True(surviving <= total * 0.1,
            $"{surviving}/{total} one-off items from the first half of the run are still recallable");
    }

    [Fact]
    public async Task Reused_material_outranks_what_survives_of_the_noise()
    {
        var engine = await RunAsync();

        var durable = await engine.RecallAsync(new MemoryQuery("t", "s", "durable", Limit: 50));
        var worstDurable = durable.Items.Min(i => i.Retrievability);

        var noise = await engine.RecallAsync(new MemoryQuery("t", "s", "noise0x", Limit: 50));
        var bestOldNoise = noise.Items.Count == 0 ? 0 : noise.Items.Max(i => i.Retrievability);

        Assert.True(worstDurable > bestOldNoise,
            $"the weakest reused fact ({worstDurable:F3}) does not beat the strongest old one-off " +
            $"({bestOldNoise:F3})");
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

        var kept = 0;
        for (var i = 0; i < DurableFacts; i++)
            if (await RecallableAsync(engine, $"durable{i}")) kept++;

        Assert.True(kept >= DurableFacts * 0.9,
            $"only {kept}/{DurableFacts} reused facts survived the longer run");
    }

    /// <summary>The scenario burst damping exists for, end to end: bulk-ingesting a large document must not
    /// erase what the memory already held.
    /// <para>This one uses the DEFAULT clock — damped — because that is the thing under test. Undamped, 500
    /// writes advance the position by 500 and every prior entry falls far past the floor.</para></summary>
    [Fact]
    public async Task A_bulk_ingest_does_not_erase_what_was_already_known()
    {
        var engine = new GraphMemoryEngine("sim", new InMemoryMemoryGraphStore());
        for (var i = 0; i < DurableFacts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Durable(i)));

        // a 500-page document arriving at once
        for (var i = 0; i < 500; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s",
                $"document paragraph {i.ToString(CultureInfo.InvariantCulture)} of a long ingested file"));

        var kept = 0;
        for (var i = 0; i < DurableFacts; i++)
            if (await RecallableAsync(engine, $"durable{i}")) kept++;

        Assert.True(kept >= DurableFacts * 0.9,
            $"a bulk ingest erased the prior memory — only {kept}/{DurableFacts} survived");
    }

    /// <summary>...and the same ingest, undamped, to show the damping is doing the work rather than the
    /// numbers happening to be forgiving. A regression that silently disabled damping would otherwise pass
    /// the test above only until the corpus grew.</summary>
    [Fact]
    public async Task The_same_ingest_undamped_would_erase_it
        ()
    {
        var engine = Engine(); // PerWriteClock, no damping
        for (var i = 0; i < DurableFacts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", Durable(i)));

        for (var i = 0; i < 500; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s",
                $"document paragraph {i.ToString(CultureInfo.InvariantCulture)} of a long ingested file"));

        var kept = 0;
        for (var i = 0; i < DurableFacts; i++)
            if (await RecallableAsync(engine, $"durable{i}")) kept++;

        Assert.Equal(0, kept);
    }
}
