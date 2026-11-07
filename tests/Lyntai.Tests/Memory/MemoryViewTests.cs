using System.Reflection;
using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A read-only view of another task's memory</b> (<c>docs/DECISIONS.md</c> D242): no write path in its type, a read that moves
/// nothing of the owner's, a reference outside its task refused, each result naming its source task, revision, relations and
/// standing, a host's check before an expansion reads anything, and a blend that routes it by <see cref="MemoryRef.Engine"/>.
/// The stores' own reads are <c>MemoryGraphStoreContract.Reads_move_nothing_the_owner_holds</c>.
/// </summary>
public class MemoryViewTests
{
    private const string Owner = "shared/graph";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jul = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore store, string name = Owner) =>
        new(name, store, null, new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()], Clock = () => Jul });

    private static MemoryWrite Fact(string content, string task = "owned", string scope = "s") =>
        new(task, scope, content) { ValidFrom = Jan };

    private static MemoryViewQuery Ask(MemoryValidityReading? reading = null) =>
        new("lives works", Limit: 20, Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static async Task<string> Held(IPortableMemory engine, string task)
    {
        var text = new StringWriter();
        await engine.ExportAsync(task, null, text);
        return text.ToString();
    }

    private static async Task<(InMemoryMemoryGraphStore Store, GraphMemoryEngine Owner, MemoryRef Paris, MemoryRef London)> Seed()
    {
        var store = new InMemoryMemoryGraphStore();
        var owner = Build(store);
        var paris = (await owner.RememberAsync(Fact("lives in Paris"))).Reference;
        var london = (await owner.RememberAsync(Fact("works in London"))).Reference;
        await owner.RememberAsync(Fact("lives in Oslo", task: "other"));
        return (store, owner, paris, london);
    }

    [Fact]
    public void The_view_s_type_has_no_write_path()
    {
        Assert.Equal(["ExpandAsync", "RecallAsync", "RevisionOfAsync"],
            typeof(IMemoryView).GetMethods().Where(m => !m.IsSpecialName).Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var view = Build(new InMemoryMemoryGraphStore()).ViewOf("owned");
        Type[] writes =
        [
            typeof(IMemoryEngine), typeof(ILinkableMemory), typeof(IEndableMemory), typeof(IConfirmableMemory), typeof(IRelatableMemory),
            typeof(IKeyedMemory), typeof(ISupersedingMemory), typeof(IForgettableMemory), typeof(IPrunableMemory),
            typeof(IReindexableMemory), typeof(IPortableMemory),
        ];
        Assert.All(writes, t => Assert.False(t.IsInstanceOfType(view), $"the view is a {t.Name}"));
    }

    [Fact]
    public async Task A_recall_and_an_expansion_through_the_view_leave_the_owner_s_reinforcement_links_and_age_as_they_were()
    {
        var (store, owner, paris, _) = await Seed();
        var reader = Build(store);   // a second instance over the owner's store and name: the reader's
        var view = reader.ViewOf("owned");
        var before = await Held(owner, "owned");

        var recall = await view.RecallAsync(Ask());
        var expansion = await view.ExpandAsync(paris, hops: 2, detail: MemoryDetail.Full);
        await view.RevisionOfAsync(paris);

        Assert.Equal(2, recall.Items.Count);
        Assert.Equal(paris, expansion.Items[0].Item.Reference);
        Assert.Equal(before, await Held(owner, "owned"));

        // the control: the same reads through the engine DO learn, so the equality above is not vacuous
        await reader.RecallAsync(new MemoryQuery("owned", "s", "lives works", Limit: 20));
        await reader.ExpandAsync(paris);
        Assert.NotEqual(before, await Held(owner, "owned"));
    }

    [Fact]
    public async Task Each_result_names_its_source_task_reference_revision_relations_and_standing()
    {
        var (store, owner, paris, london) = await Seed();
        var reader = Build(store);
        var rome = (await owner.SupersedeAsync(new MemorySupersession(Fact("lives in Rome") with { ValidFrom = Mar },
            [new MemorySupersededEntry(paris, (await reader.ViewOf("owned").RevisionOfAsync(paris))!)], "moved"))).Successor!.Value;
        await owner.RelateAsync("owned", rome, london, MemoryRelation.Disputes);
        var view = reader.ViewOf("owned");

        var now = await view.RecallAsync(Ask());
        var history = await view.RecallAsync(Ask(MemoryValidityReading.History));

        Assert.Equal([london, rome], now.Items.Select(i => i.Item.Reference).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray());
        Assert.All(history.Items, i => Assert.Equal("owned", i.SourceTask));
        Assert.All(history.Items, i => Assert.Equal(Owner, i.Item.Reference.Engine));
        var old = history.Items.Single(i => i.Item.Reference == paris);
        Assert.Equal(MemoryStanding.Ended, old.Standing);
        Assert.Equal(rome, old.Item.Validity!.SupersededBy);
        Assert.All(history.Items.Where(i => i.Item.Reference != paris), i => Assert.Equal(MemoryStanding.Current, i.Standing));
        Assert.All(history.Items, i => Assert.False(string.IsNullOrEmpty(i.Item.Revision)));
        var disputing = history.Items.Single(i => i.Item.Reference == rome).Item.Relations!.Single();
        Assert.Equal((MemoryRelation.Disputes, london, true), (disputing.Relation, disputing.Other, disputing.AssertedByThis));
    }

    [Fact]
    public async Task RevisionOf_is_one_read_by_id_that_moves_nothing_and_follows_the_owner()
    {
        var (store, owner, paris, _) = await Seed();
        var view = Build(store).ViewOf("owned");
        var before = await Held(owner, "owned");

        var revision = await view.RevisionOfAsync(paris);

        Assert.Equal(before, await Held(owner, "owned"));
        Assert.Equal((await view.RecallAsync(Ask())).Items.Single(i => i.Item.Reference == paris).Item.Revision, revision);
        await owner.RememberAsync(Fact("lives in Paris") with { Headline = "reworded" });
        Assert.NotEqual(revision, await view.RevisionOfAsync(paris));
        Assert.Null(await view.RevisionOfAsync(new MemoryRef(Owner, "999")));
        Assert.Null(await view.RevisionOfAsync(new MemoryRef("elsewhere", paris.Id)));
        Assert.Null(await view.RevisionOfAsync(new MemoryRef(Owner, "not-a-number")));
    }

    [Fact]
    public async Task A_reference_of_another_task_is_refused_and_reads_as_a_missing_one()
    {
        var (store, owner, _, _) = await Seed();
        var other = (await Build(store).ViewOf("other").RecallAsync(Ask())).Items.Single().Item.Reference;
        var view = Build(store).ViewOf("owned");
        var before = await Held(owner, "other");

        Assert.Equal(MemoryViewRecall.Empty, await view.ExpandAsync(other));
        Assert.Null(await view.RevisionOfAsync(other));
        Assert.Equal(before, await Held(owner, "other"));
    }

    [Fact]
    public async Task A_reference_the_check_refuses_reads_as_a_missing_one_for_its_revision_too()
    {
        var (store, _, paris, london) = await Seed();
        var view = Build(store).ViewOf("owned", new Check(r => r != paris));

        Assert.Null(await view.RevisionOfAsync(paris));
        Assert.NotNull(await view.RevisionOfAsync(london));
    }

    [Fact]
    public async Task A_blend_s_due_sweep_skips_a_graph_member_whose_store_cannot_confirm()
    {
        var bare = Build(ReadSpy.Bare(new InMemoryMemoryGraphStore()), "b/bare");
        var graph = Build(new InMemoryMemoryGraphStore(), "b/graph");
        var blend = new CompositeMemoryEngine("b", [bare, graph]);
        await graph.RememberAsync(Fact("lives in Paris") with { Recheck = "ask" });

        var due = await blend.DueAsync("owned", null, Jul);

        Assert.Equal(["lives in Paris"], due.Select(i => i.Content));
    }

    [Fact]
    public async Task A_refused_expansion_reads_no_entry()
    {
        var (store, owner, paris, london) = await Seed();
        var spy = ReadSpy.Over(store, out var calls);
        var asked = new List<MemoryRef>();
        var view = Build(spy).ViewOf("owned", new Check(r => { asked.Add(r); return r == london; }));
        calls.Clear();

        Assert.Equal(MemoryViewRecall.Empty, await view.ExpandAsync(paris));

        Assert.Equal([paris], asked);
        Assert.Empty(calls);
        var permitted = await view.ExpandAsync(london);
        Assert.Equal(london, permitted.Items[0].Item.Reference);
        Assert.Contains("GetAsync", calls);
    }

    [Fact]
    public async Task The_owner_s_end_is_read_at_the_reader_s_next_recall_through_a_blend_that_routes_by_engine()
    {
        var (store, owner, paris, london) = await Seed();
        var mine = Build(store, "reader/graph");
        var blend = new CompositeMemoryEngine("reader", [mine]) { Views = [Build(store).ViewOf("owned")] };
        var query = new MemoryQuery("mine", null, "lives works", Limit: 20, Detail: MemoryDetail.Full);

        Assert.Equal([paris, london], (await blend.RecallAsync(query)).Items.Select(i => i.Reference).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray());

        await owner.EndAsync("owned", paris, new MemoryEnding("moved away") { At = Mar });

        Assert.Equal([london], (await blend.RecallAsync(query)).Items.Select(i => i.Reference).ToArray());
        var expanded = await blend.ExpandAsync(london, hops: 1, detail: MemoryDetail.Full);
        Assert.Equal(london, expanded.Items[0].Reference);
        Assert.Equal("works in London", expanded.Items[0].Content);
        // the blend's own member stays the reader's: a write lands there and never in the owner's task
        var held = await Held(owner, "owned");
        var written = await blend.RememberAsync(new MemoryWrite("mine", "s", "lives in a tent"));
        Assert.Equal("reader/graph", written.Reference.Engine);
        Assert.Equal(held, await Held(owner, "owned"));
    }

    [Fact]
    public async Task A_write_naming_a_view_s_reference_is_refused_through_the_blend()
    {
        var (store, _, paris, london) = await Seed();
        var blend = new CompositeMemoryEngine("reader", [Build(store, "reader/graph")]) { Views = [Build(store).ViewOf("owned")] };

        await Assert.ThrowsAsync<NotSupportedException>(() => blend.EndAsync("owned", paris, new MemoryEnding("gone")));
        await Assert.ThrowsAsync<NotSupportedException>(() => blend.LinkAsync(paris, london));
        await Assert.ThrowsAsync<NotSupportedException>(() => blend.RelateAsync("owned", paris, london, MemoryRelation.Amends));
        await Assert.ThrowsAsync<NotSupportedException>(() => blend.ConfirmAsync("owned", paris, MemoryCheckOutcome.Held));
        await Assert.ThrowsAsync<ArgumentException>(() => blend.RememberAsync(   // names no member that holds the entry
            new MemoryWrite("owned", "s", "lives in Rome") { Supersedes = [paris] }));
        Assert.Empty((await blend.ExpandInTaskAsync(paris, "mine", 1, null, MemoryDetail.Full, default)).Items);
        Assert.NotEmpty((await blend.ExpandInTaskAsync(paris, "owned", 1, null, MemoryDetail.Full, default)).Items);
    }

    [Fact]
    public void A_view_of_an_engine_a_blend_already_has_is_refused()
    {
        var store = new InMemoryMemoryGraphStore();
        var view = Build(store, "reader/graph").ViewOf("owned");
        Assert.Throws<InvalidOperationException>(() => new CompositeMemoryEngine("reader", [Build(store, "reader/graph")]) { Views = [view] });
        Assert.Throws<InvalidOperationException>(() => new CompositeMemoryEngine("reader", [Build(store, "reader/graph")])
            { Views = [Build(store).ViewOf("a"), Build(store).ViewOf("b")] });
    }

    [Fact]
    public async Task A_blend_holds_a_view_exactly_when_a_member_does_and_otherwise_says_so()
    {
        var withGraph = new CompositeMemoryEngine("g", [Build(new InMemoryMemoryGraphStore(), "g/graph")]);
        var lexicalOnly = new CompositeMemoryEngine("l", [new LexicalMemoryEngine("l/lexical", new FakeMemoryStore())]);

        Assert.True(withGraph.HoldsView);
        Assert.False(lexicalOnly.HoldsView);
        var ex = Assert.Throws<NotSupportedException>(() => lexicalOnly.ViewOf("owned"));
        Assert.Contains("holds no read-only view", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IViewableMemory>(lexicalOnly);

        var view = withGraph.ViewOf("owned");
        await withGraph.RememberAsync(Fact("lives in Paris"));
        Assert.Single((await view.RecallAsync(Ask())).Items);
        Assert.Equal(["g/graph"], view.Engines);
    }

    [Fact]
    public void A_store_without_revisions_holds_no_view()
    {
        var engine = Build(ReadSpy.Bare(new InMemoryMemoryGraphStore()));

        Assert.False(engine.HoldsView);
        Assert.Throws<NotSupportedException>(() => engine.ViewOf("owned"));
    }

    [Fact]
    public async Task UseView_wires_a_reader_s_blend_from_the_container()
    {
        var store = new InMemoryMemoryGraphStore();
        var owner = Build(store);
        var paris = (await owner.RememberAsync(Fact("lives in Paris"))).Reference;
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(store);
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeTextProvider("p"));
            b.AddMemoryEngine("reader", e => e.UseLexical().UseView(Owner, "owned"));
        });
        using var sp = services.BuildServiceProvider();

        var reader = sp.GetRequiredService<IMemoryEngineFactory>().Get("reader");
        var recall = await reader.RecallAsync(new MemoryQuery("mine", Query: "lives", Detail: MemoryDetail.Full));

        Assert.Equal([paris], recall.Items.Select(i => i.Reference).ToArray());
        Assert.DoesNotContain(Owner, sp.GetRequiredService<IMemoryEngineFactory>().Names);   // a view is not a registered engine
    }

    private sealed class Check(Func<MemoryRef, bool> permits) : IMemoryViewCheck
    {
        public Task<bool> PermitsAsync(MemoryRef reference, CancellationToken ct = default) => Task.FromResult(permits(reference));
    }

    /// <summary>Forwards every call to a store and records its name — to say what a refused expansion did not read. With
    /// <c>asBare</c> it implements only <see cref="IMemoryGraphStore"/>, as a store without revisions does.</summary>
    public class ReadSpy : DispatchProxy
    {
        private readonly List<string> _calls = [];
        private object _inner = null!;

        public static IEndableMemoryGraphStore Over(InMemoryMemoryGraphStore store, out List<string> calls)
        {
            var proxy = Create<IEndableMemoryGraphStore, ReadSpy>();
            var spy = (ReadSpy)(object)proxy;
            spy._inner = store;
            calls = spy._calls;
            return proxy;
        }

        public static IMemoryGraphStore Bare(InMemoryMemoryGraphStore store)
        {
            var proxy = Create<IMemoryGraphStore, ReadSpy>();
            ((ReadSpy)(object)proxy)._inner = store;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add(targetMethod!.Name);
            try { return targetMethod.Invoke(_inner, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException!; }
        }
    }
}
