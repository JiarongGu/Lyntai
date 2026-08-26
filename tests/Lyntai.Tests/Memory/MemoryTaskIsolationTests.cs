using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A <c>taskKey</c> is the isolation boundary every other guarantee is stated inside</b>, and until
/// 2026-08-26 nothing asserted it as a property — only as a control inside one removal fact.
///
/// <para><c>MemoryGraphStoreContract.No_read_crosses_a_task_key</c> holds the STORE to it on all three
/// backends. These are the ENGINE's own paths, which compose several store reads and one traversal that
/// the store deliberately does not scope.</para>
/// </summary>
public class MemoryTaskIsolationTests
{
    private const string Engine = "isolation";

    private static GraphMemoryEngine NewEngine(IMemoryGraphStore store,
        IEmbedder? embedder = null, IVectorStore? vectors = null, GraphMemoryOptions? options = null) =>
        new(Engine, store, options, agePolicies: [new PerWriteAgePolicy()],
            embedder: embedder, vectors: vectors);

    [Fact]
    public async Task A_recall_never_returns_another_task_s_material()
    {
        // The end-to-end statement of the contract fact, through the engine's own gather: identical content
        // under two task keys, and a query that matches both, so only the scoping can separate them.
        var engine = NewEngine(new InMemoryMemoryGraphStore());
        await engine.RememberAsync(new MemoryWrite("mine", "s", "the deploy pipeline needs approval"));
        await engine.RememberAsync(new MemoryWrite("theirs", "s", "the deploy pipeline needs approval"));

        var mine = await engine.RecallAsync(new MemoryQuery("mine", "s", "deploy pipeline", Limit: 20));
        var theirs = await engine.RecallAsync(new MemoryQuery("theirs", "s", "deploy pipeline", Limit: 20));

        Assert.Single(mine.Items);
        Assert.Single(theirs.Items);
        Assert.NotEqual(mine.Items[0].Reference.Id, theirs.Items[0].Reference.Id);
    }

    [Fact]
    public async Task The_engine_never_links_across_tasks_BY_ITSELF()
    {
        // Traversal is the one read the store does NOT scope -- NeighboursAsync takes ids and an engine, and
        // follows edges wherever they lead. That is only safe while no edge crosses a task, so the property
        // that actually protects isolation is about the WRITERS of edges, not the reader.
        //
        // All three engine-internal linkers are task-scoped by construction: co-activation links what ONE
        // recall returned (and a recall is task-scoped), similarity links within the vector collection
        // (addressed per task and scope), and subject linking looks up NodesBySubjectAsync with the write's
        // own task. This exercises the first two together and requires zero degree across the boundary.
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, new FakeEmbedder(), new InMemoryVectorStore(),
            new GraphMemoryOptions { MinSimilarity = 0.1 });   // link freely — the point is that it still cannot cross

        foreach (var task in new[] { "mine", "theirs" })
        {
            await engine.RememberAsync(new MemoryWrite(task, "s", "cancel your subscription anytime"));
            await engine.RememberAsync(new MemoryWrite(task, "s", "cancel a subscription from settings"));
            // a recall is what writes co-activation edges
            await engine.RecallAsync(new MemoryQuery(task, "s", "cancel subscription", Limit: 10));
        }

        // Every node's neighbours must be inside its own task. Read through the STORE, because the engine's
        // own recall is task-scoped and would hide a cross-task edge rather than reveal one.
        //
        // ONE node at a time, not the whole set: NeighboursAsync EXCLUDES the ids it is given, so asking for
        // the neighbours of every node in a task excludes the entire cluster and comes back empty. The first
        // draft did exactly that and was caught only by the vacuity guard below — which is the argument for
        // writing one, since the fact would otherwise have passed while observing nothing at all.
        var mine = await store.SeedAsync(Engine, "mine", "s", null, 50);
        var mineIds = mine.Select(n => n.Id).ToHashSet();

        var found = 0;
        foreach (var node in mine)
        {
            var neighbours = await store.NeighboursAsync(Engine, [node.Id], 200);
            found += neighbours.Count;
            Assert.All(neighbours, n => Assert.Contains(n.Node.Id, mineIds));
        }

        Assert.True(found > 0, "no edges were written at all, so this fact observed nothing");
    }

    [Fact]
    public async Task An_EXPLICIT_cross_task_link_DOES_carry_a_recall_across_and_that_is_the_documented_hole()
    {
        // THE ONE WAY ACROSS, established rather than assumed. `ILinkableMemory.LinkAsync` is public and
        // documented as "an application can assert structure the library could not infer" -- it validates
        // nothing about the two refs' tasks, and IMemoryGraphStore.NeighboursAsync applies no task
        // predicate, so a link an application asserts across tasks is a link traversal will follow.
        //
        // Recorded as BEHAVIOUR, not endorsed as a design: whether the engine should refuse such a link, or
        // scope traversal, or keep trusting the caller, is a decision nobody has taken. What this fact
        // guarantees is that the answer is written down and that changing it is a deliberate act rather than
        // an accident -- if traversal is ever scoped, this test fails and forces the choice into the open.
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, options: new GraphMemoryOptions { Hops = 2 });

        var mine = await engine.RememberAsync(new MemoryWrite("mine", "s", "the deploy pipeline needs approval"));
        var theirs = await engine.RememberAsync(new MemoryWrite("theirs", "s", "unrelated tenant material"));

        await engine.LinkAsync(mine, theirs, "asserted", weight: 5, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("mine", "s", "deploy pipeline", Limit: 20));

        Assert.Contains(recall.Items, i => i.Reference.Id == theirs.Id);
    }
}
