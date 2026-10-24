using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="GraphMemoryEngine.CountAsync"/>: the store's counts, plus how many entries are BURIED — a
/// figure the store cannot produce, because it never evaluates the curve.</summary>
public class GraphMemoryCountTests
{
    private const string Task = "project";

    // a recall that changes nothing, so reading retrievability through it cannot move what is counted
    private static readonly GraphMemoryOptions Still = new()
    {
        ReinforceOn = MemoryReinforcementActs.None,
        CoActivationCap = 0,
        LogReviews = false,
    };

    private static async Task<GraphMemoryEngine> SeededAsync(IMemoryGraphStore store)
    {
        var engine = new GraphMemoryEngine("project/graph", store, Still);
        for (var i = 0; i < 6; i++)
            await engine.RememberAsync(new MemoryWrite(Task, "s", $"associative fact number {i} about the deploy"));
        await engine.RememberAsync(new MemoryWrite(Task, "s", "the production database is db-prod-2",
            Grade: MemoryGrade.Authoritative));
        return engine;
    }

    /// <summary>Buried means what a recall would report: an associative entry whose retrievability, as the
    /// engine computes it, is below the floor. Read back through a recall, so the two cannot disagree.</summary>
    [Fact]
    public async Task Buried_counts_the_associative_entries_a_recall_rates_below_the_floor()
    {
        var engine = await SeededAsync(new InMemoryMemoryGraphStore());
        var recall = await engine.RecallAsync(new MemoryQuery(Task, "s", Limit: 100));
        Assert.Equal(7, recall.Items.Count);
        var associative = recall.Items.Where(i => i.Grade == MemoryGrade.Associative)
            .Select(i => i.Retrievability).Order().ToList();
        var floor = (associative[1] + associative[2]) / 2;
        var expected = associative.Count(r => r < floor);
        Assert.InRange(expected, 1, associative.Count - 1);   // a floor that buries some, not none or all

        var counts = await engine.CountAsync(Task, "s", minRetrievability: floor);

        Assert.NotNull(counts);
        Assert.Equal(expected, counts.Buried);
        Assert.Equal(7, counts.Entries);
        Assert.Equal(1, counts.Authoritative);
    }

    /// <summary>An authoritative entry is never buried, at any floor — its retrievability is fixed at 1.</summary>
    [Fact]
    public async Task An_authoritative_entry_is_never_buried()
    {
        var engine = await SeededAsync(new InMemoryMemoryGraphStore());

        var counts = await engine.CountAsync(Task, minRetrievability: 1.5);

        Assert.Equal(6, counts!.Buried);
    }

    /// <summary>A floor of zero is PruneAsync's opt-out, and buries nothing — retrievability is never below zero.</summary>
    [Fact]
    public async Task A_zero_floor_buries_nothing()
    {
        var engine = await SeededAsync(new InMemoryMemoryGraphStore());

        Assert.Equal(0, (await engine.CountAsync(Task, minRetrievability: 0))!.Buried);
    }

    /// <summary>The store's own figures pass through untouched; only the engine fills in Buried.</summary>
    [Fact]
    public async Task The_store_reports_no_buried_figure_and_the_engine_supplies_one()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = await SeededAsync(store);

        var stored = await store.CountAsync("project/graph", Task, null);
        var counted = await engine.CountAsync(Task);

        Assert.Null(stored.Buried);
        Assert.NotNull(counted!.Buried);
        Assert.Equal(stored, counted with { Buried = null });
    }

    /// <summary>A store that cannot count answers null — never a zero that reads as an empty memory.</summary>
    [Fact]
    public async Task A_store_that_cannot_count_answers_null_rather_than_zero()
    {
        var engine = await SeededAsync(new DelegatingGraphStore());

        Assert.Null(await engine.CountAsync(Task));
    }
}
