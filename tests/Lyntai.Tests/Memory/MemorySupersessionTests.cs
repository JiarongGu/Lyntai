using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A supersession can apply as one change</b> (<c>docs/DECISIONS.md</c> D242): the successor is stored and every predecessor ended,
/// each guarded by the revision its approver read, or nothing changed and the call says which moved. The stores' own rules are
/// <c>MemoryGraphStoreContract</c>'s; these are the engine's refusals, its derived steps and the blend's routing.
/// </summary>
public class MemorySupersessionTests
{
    private const string Name = "supersede";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jul = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, string name = Name, IVectorStore? vectors = null,
        bool embeds = false) =>
        new(name, store ?? new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()], Clock = () => Jul,
                Providers = embeds ? [new FakeVectorProvider()] : null, Vectors = vectors,
            });

    private static MemoryWrite Fact(string content, string task = "t", DateTimeOffset? from = null) =>
        new(task, "s", content) { ValidFrom = from ?? Jan };

    private static MemoryQuery Ask(MemoryValidityReading? reading = null, string task = "t") =>
        new(task, "s", "lives works", Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static string[] Contents(MemoryRecall recall) =>
        [.. recall.Items.Select(i => i.Content!).Order(StringComparer.Ordinal)];

    private static async Task<MemorySupersededEntry> Read(GraphMemoryEngine engine, MemoryRef entry) =>
        new(entry, (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single(i => i.Reference == entry).Revision!);

    [Fact]
    public async Task One_call_stores_the_successor_and_ends_every_predecessor_guarded_by_its_revision()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var london = (await engine.RememberAsync(Fact("works in London"))).Reference;

        var result = await engine.SupersedeAsync(new MemorySupersession(Fact("lives and works in Rome", from: Mar),
            [await Read(engine, paris), await Read(engine, london)], "relocated"));

        Assert.True(result.Applied);
        Assert.Equal(["lives and works in Rome"], Contents(await engine.RecallAsync(Ask())));
        Assert.All(result.Predecessors, p => Assert.Equal(MemoryPredecessorOutcome.Ended, p.Outcome));
        var history = (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items;
        Assert.Equal(3, history.Count);
        foreach (var id in new[] { paris, london })
        {
            var ended = history.Single(i => i.Reference == id);
            Assert.Equal(("relocated", Mar, result.Successor), (ended.Validity!.EndedReason, ended.Validity.End, ended.Validity.SupersededBy));
        }
        Assert.Equal(["lives in Paris", "works in London"],
            Contents(await engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Jan.AddDays(5))))));
        Assert.True(result.Ran.HasFlag(MemorySources.Graph));
    }

    [Fact]
    public async Task One_stale_revision_stores_nothing_and_says_which_predecessor_moved()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var london = (await engine.RememberAsync(Fact("works in London"))).Reference;
        var reviewed = new[] { await Read(engine, paris), await Read(engine, london) };
        await engine.RememberAsync(Fact("works in London") with { Headline = "reworded after the review" });   // moves london's revision

        var result = await engine.SupersedeAsync(new MemorySupersession(Fact("lives and works in Rome", from: Mar), reviewed, "relocated"));

        Assert.False(result.Applied);
        Assert.Null(result.Successor);
        Assert.Equal([MemoryPredecessorOutcome.Unmoved, MemoryPredecessorOutcome.Changed], result.Predecessors.Select(p => p.Outcome));
        Assert.Equal((await Read(engine, london)).ExpectedRevision, result.Predecessors[1].Revision);   // the revision a fresh review reads
        Assert.Equal(["lives in Paris", "works in London"], Contents(await engine.RecallAsync(Ask())));
        Assert.Equal(2, (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Count);
    }

    [Fact]
    public async Task A_keyed_supersession_repeated_answers_that_it_applied_naming_the_successor()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var reviewed = await Read(engine, paris);
        var request = new MemorySupersession(Fact("lives in Rome", from: Mar), [reviewed], "moved") { OperationKey = "sup-1" };

        var first = await engine.SupersedeAsync(request);
        var again = await engine.SupersedeAsync(request);

        Assert.False(first.Receipt!.Replayed);
        Assert.Equal((MemoryOperationKind.Supersede, first.Successor), (first.Receipt.Kind, first.Receipt.Reference));
        Assert.True(again.Applied);
        Assert.True(again.Receipt!.Replayed);
        Assert.Equal((first.Successor, first.Revision), (again.Successor, again.Revision));
        Assert.Equal(MemoryPredecessorOutcome.Ended, again.Predecessors.Single().Outcome);
        Assert.Equal(MemorySources.None, again.Ran);
        Assert.Equal(2, (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Count);
        // the same call without a key is a stale review, which is what the key is for
        Assert.False((await engine.SupersedeAsync(request with { OperationKey = null })).Applied);
        await Assert.ThrowsAsync<MemoryKeyConflictException>(() =>
            engine.SupersedeAsync(request with { Reason = "a different reason" }));
        Assert.Equal("sup-1", (await engine.ReceiptAsync("t", "sup-1"))!.Key);
    }

    [Fact]
    public async Task A_keyed_supersession_repeated_after_a_predecessor_was_removed_still_answers_its_receipt()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var london = (await engine.RememberAsync(Fact("works in London"))).Reference;
        var request = new MemorySupersession(Fact("lives and works in Rome", from: Mar),
            [await Read(engine, paris), await Read(engine, london)], "moved") { OperationKey = "sup-gone" };
        var first = await engine.SupersedeAsync(request);
        await store.DeleteAsync(Name, [long.Parse(london.Id, CultureInfo.InvariantCulture)]);

        var again = await engine.SupersedeAsync(request);

        Assert.True(again.Applied);
        Assert.True(again.Receipt!.Replayed);
        Assert.Equal(first.Successor, again.Successor);
        Assert.Equal([MemoryPredecessorOutcome.Ended, MemoryPredecessorOutcome.Missing], again.Predecessors.Select(p => p.Outcome));
    }

    [Fact]
    public async Task A_refused_supersession_does_not_move_the_age_policy()
    {
        var age = new CountingAgePolicy();
        var engine = new GraphMemoryEngine(Name, new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams { AgePolicies = [age], Clock = () => Jul });
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var reviewed = await Read(engine, paris);
        await engine.RememberAsync(Fact("lives in Paris") with { Headline = "reworded after the review" });   // moves paris's revision
        var before = age.Advances;

        var stale = await engine.SupersedeAsync(new MemorySupersession(Fact("lives in Rome", from: Mar), [reviewed], "moved"));

        Assert.False(stale.Applied);
        Assert.Equal(before, age.Advances);
        // the control: a call that applies does advance it
        var fresh = await engine.SupersedeAsync(new MemorySupersession(Fact("lives in Rome", from: Mar), [await Read(engine, paris)], "moved"));
        Assert.True(fresh.Applied);
        Assert.Equal(before + 1, age.Advances);
    }

    private sealed class CountingAgePolicy : IMemoryAgePolicy
    {
        public int Advances { get; private set; }
        public MemoryAgeKind Kind => MemoryAgeKind.Derivable;
        public MemoryTick Advance(MemoryWrite write, string engine)
        {
            Advances++;
            return MemoryTick.One;
        }
        public double Age(MemoryAgeSample sample) => 0;
    }

    [Fact]
    public async Task It_refuses_what_a_stated_supersession_refuses_and_writes_nothing()
    {
        var engine = Build();
        var paris = await engine.RememberAsync(Fact("lives in Paris"));
        var other = await engine.RememberAsync(Fact("lives in Oslo", task: "u"));
        var reviewed = await Read(engine, paris.Reference);
        MemorySupersession Call(MemoryWrite? successor = null, IReadOnlyList<MemorySupersededEntry>? named = null, string reason = "moved") =>
            new(successor ?? Fact("lives in Rome", from: Mar), named ?? [reviewed], reason);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.SupersedeAsync(Call(reason: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.SupersedeAsync(Call(named: [])));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.SupersedeAsync(Call(Fact("lives in Rome", from: Mar) with { Supersedes = [paris.Reference] })));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.SupersedeAsync(Call(Fact("lives in Rome", from: Mar) with { OperationKey = "inside" })));
        await Assert.ThrowsAsync<ArgumentException>(() =>   // another task's entry
            engine.SupersedeAsync(Call(named: [new MemorySupersededEntry(other.Reference, "0")])));
        await Assert.ThrowsAsync<ArgumentException>(() =>   // another engine's entry
            engine.SupersedeAsync(Call(named: [new MemorySupersededEntry(new MemoryRef("elsewhere", "1"), "0")])));
        await Assert.ThrowsAsync<ArgumentException>(() =>   // a revision this engine did not issue
            engine.SupersedeAsync(Call(named: [reviewed with { ExpectedRevision = "not-a-number" }])));
        await Assert.ThrowsAsync<ArgumentException>(() =>   // a successor that starts before the entry it ends
            engine.SupersedeAsync(Call(Fact("lives in Rome", from: Jan.AddDays(-5)))));

        Assert.Equal(["lives in Paris"], Contents(await engine.RecallAsync(Ask())));
        Assert.Single((await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items);
    }

    [Fact]
    public async Task A_predecessor_that_already_has_a_different_live_successor_refuses_before_anything_is_stored()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var rome = (await engine.RememberAsync(Fact("lives in Rome", from: Mar) with { Supersedes = [paris] })).Reference;

        var reviewed = await Read(engine, paris);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.SupersedeAsync(
            new MemorySupersession(Fact("lives in Oslo", from: Mar.AddDays(30)), [reviewed], "moved")));

        Assert.Contains($"'{rome.Id}'", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["lives in Rome"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task A_store_that_cannot_apply_the_pair_atomically_refuses_and_never_stores_then_ends()
    {
        var store = new DelegatingGraphStore();
        var engine = Build(store);
        Assert.False(engine.HoldsSupersession);
        var paris = (await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"))).Reference;

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.SupersedeAsync(new MemorySupersession(Fact("lives in Rome"), [new MemorySupersededEntry(paris, "0")], "moved")));

        Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "lives"))).Items);
        Assert.True(Build().HoldsSupersession);
    }

    [Fact]
    public async Task A_failed_index_write_leaves_the_supersession_applied()
    {
        var engine = Build(vectors: new WriteHostileVectorStore(), embeds: true);
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;

        var result = await engine.SupersedeAsync(
            new MemorySupersession(Fact("lives in Rome", from: Mar), [await Read(engine, paris)], "moved"));

        Assert.True(result.Applied);
        Assert.False(result.Ran.HasFlag(MemorySources.Similarity));   // the vector never reached the index
        Assert.Equal(["lives in Rome"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task The_blend_routes_a_supersession_to_the_member_that_holds_the_predecessors()
    {
        var graph = Build(name: "blend/graph");
        var lexical = new LexicalMemoryEngine("blend/lex", new FakeMemoryStore());
        var blend = new CompositeMemoryEngine("blend", [lexical, graph]);
        Assert.True(blend.HoldsSupersession);
        var paris = (await graph.RememberAsync(Fact("lives in Paris"))).Reference;
        var other = (await graph.RememberAsync(Fact("works in London"))).Reference;

        var result = await blend.SupersedeAsync(new MemorySupersession(Fact("lives in Rome", from: Mar),
            [await Read(graph, paris)], "moved") { OperationKey = "sup-blend" });

        Assert.True(result.Applied);
        Assert.Equal("blend/graph", result.Successor!.Value.Engine);
        Assert.Equal(["lives in Rome", "works in London"], Contents(await blend.RecallAsync(Ask())));
        Assert.True((await blend.ReceiptAsync("t", "sup-blend")) is { Kind: MemoryOperationKind.Supersede });

        await Assert.ThrowsAsync<ArgumentException>(() => blend.SupersedeAsync(new MemorySupersession(Fact("lives in Oslo", from: Mar),
            [new MemorySupersededEntry(paris, "0"), new MemorySupersededEntry(new MemoryRef("blend/lex", "1"), "0")], "moved")));
        await Assert.ThrowsAsync<NotSupportedException>(() => blend.SupersedeAsync(new MemorySupersession(Fact("lives in Oslo", from: Mar),
            [new MemorySupersededEntry(new MemoryRef("blend/lex", "1"), "0")], "moved")));
        Assert.NotNull(other.Id);
    }

    [Fact]
    public async Task The_gates_store_first_path_is_unchanged_by_the_new_call()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;

        // an un-keyed remember that supersedes still stores then ends, in two steps, exactly as before
        var rome = await engine.RememberAsync(Fact("lives in Rome", from: Mar) with { Supersedes = [paris] });

        Assert.Equal(["lives in Rome"], Contents(await engine.RecallAsync(Ask())));
        Assert.Null(rome.Receipt);
        Assert.Equal(long.Parse(rome.Reference.Id, CultureInfo.InvariantCulture) - 1, long.Parse(paris.Id, CultureInfo.InvariantCulture));
    }
}
