using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>The rulings of D255: what a blend lets through, what a prune refuses, what a budget of zero keeps, and
/// what a recall reinforces and links.</summary>
public class MemoryCoreRulingsTests
{
    private const string Name = "core";

    private static (GraphMemoryEngine Engine, InMemoryMemoryGraphStore Store) Build(GraphMemoryOptions? options = null)
    {
        var store = new InMemoryMemoryGraphStore();
        return (new GraphMemoryEngine(Name, store, options), store);
    }

    private static long Id(MemoryRef reference) => long.Parse(reference.Id, CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_blend_lets_an_unknown_ranking_name_through_as_the_callers_mistake()
    {
        var (engine, _) = Build();
        await engine.RememberAsync(new MemoryWrite("t", "s", "console alpha note"));
        var blend = new CompositeMemoryEngine("project", [engine]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => blend.RecallAsync(
            new MemoryQuery("t", "s", "console", RankingPolicyName: "nope")));
    }

    [Fact]
    public async Task A_memory_walk_lets_an_unknown_ranking_name_through_too()
    {
        var (engine, _) = Build();
        await engine.RememberAsync(new MemoryWrite("t", "s", "console alpha note"));

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await foreach (var _ in engine.WalkAsync(new MemoryQuery("t", "s", "console", RankingPolicyName: "nope"))) { }
        });
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public async Task A_prune_floor_outside_zero_to_one_is_refused(double floor)
    {
        var (engine, _) = Build();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.PruneAsync("t", "s", minRetrievability: floor));
    }

    [Fact]
    public async Task A_negative_age_to_prune_past_is_refused_and_one_past_every_entry_removes_nothing()
    {
        var (engine, _) = Build();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            engine.PruneAsync("t", "s", minRetrievability: 0, olderThan: TimeSpan.FromDays(-1)));
        Assert.Equal(0, await engine.PruneAsync("t", "s", minRetrievability: 0, olderThan: TimeSpan.MaxValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_write_is_refused(string content)
    {
        var (engine, store) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(new MemoryWrite("t", "s", content)));

        Assert.Equal(0, (await store.CountAsync(Name, "t", null)).Entries);
    }

    [Fact]
    public async Task A_character_budget_of_zero_keeps_the_first_item_and_an_expansions_keeps_the_entry_alone()
    {
        var (engine, _) = Build();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "harbour alpha"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "harbour beta"));
        await engine.LinkAsync(a.Reference, b.Reference, weight: 1, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "harbour", CharBudget: 0));
        var expanded = await engine.ExpandAsync(a.Reference, charBudget: 0);

        Assert.Single(recall.Items);
        Assert.Single(expanded.Items);
    }

    [Fact]
    public async Task A_negative_expansion_budget_is_refused_by_the_option()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { ExpandCharBudget = -1 });

        Assert.Contains("entry alone", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_co_activation_cap_past_what_its_pairs_can_hold_is_refused()
    {
        Assert.Equal(100, new GraphMemoryOptions { CoActivationCap = 100 }.CoActivationCap);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { CoActivationCap = 101 });
    }

    [Fact]
    public async Task An_entry_the_character_budget_cut_is_not_reinforced()
    {
        var (engine, store) = Build();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "harbour alpha"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "harbour beta"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "harbour", CharBudget: 1, Detail: MemoryDetail.Full));

        var kept = Assert.Single(recall.Items).Reference;
        var cut = kept == a.Reference ? b.Reference : a.Reference;
        Assert.Equal(1, (await store.GetAsync(Name, Id(kept)))!.RecallCount);
        Assert.Equal(0, (await store.GetAsync(Name, Id(cut)))!.RecallCount);
    }

    [Fact]
    public async Task A_signal_bag_of_only_non_finite_values_keeps_the_stored_bag_as_an_absent_one_does()
    {
        var store = new InMemoryMemoryGraphStore();
        GraphNodeWrite Write(MemorySignals signals) => new(Name, "t", "s", "h", "same content", MemoryGrade.Associative,
            InitialStability: 5, Advance: 1, Metadata: null, Signals: signals);
        var id = await store.UpsertAsync(Write(MemorySignals.Empty.With("salience", 3)));

        await store.UpsertAsync(Write(MemorySignals.Empty.With("salience", double.NaN)));

        Assert.Equal(3, (await store.GetAsync(Name, id))!.Signals.Get("salience"));
    }

    [Fact]
    public async Task A_history_recall_links_no_ended_entry()
    {
        var (engine, store) = Build();
        var paris = (await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"))).Reference;
        var berlin = (await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Berlin") { Supersedes = [paris] })).Reference;
        double Weight(IReadOnlyList<GraphNeighbour> n) => n.Sum(x => x.EdgeWeight);
        var before = Weight(await store.NeighboursAsync(Name, "t", [Id(berlin)], 10));

        await engine.RecallAsync(new MemoryQuery("t", "s", "lives") { Validity = MemoryValidityReading.History });

        Assert.Equal(before, Weight(await store.NeighboursAsync(Name, "t", [Id(berlin)], 10)));
    }
}
