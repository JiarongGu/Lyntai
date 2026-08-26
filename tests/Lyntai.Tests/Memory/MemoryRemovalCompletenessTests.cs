using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Removal reaches the SIMILARITY INDEX, not only the graph store.
///
/// <para><b>What was wrong.</b> <c>EnrichAsync</c> indexes every write as
/// <c>vectors.UpsertAsync(collection, id, vector, write.Content)</c> — the payload is the entry's full
/// content, verbatim — and neither <c>ForgetAsync</c> nor <c>PruneAsync</c> touched that store. So the
/// consent-withdrawal path (<see cref="IForgettableMemory"/>: "the deletion path an application uses when a
/// user withdraws consent") left the content readable in the projection, and pruning left an orphan that
/// still costs a <c>SemanticSeedK</c> slot and a <c>GetAsync</c> round trip on every later recall.</para>
///
/// <para><b>Why the graph store's own contract could not catch it.</b> The identical defect was found for
/// SUBJECT rows and fixed inside <c>IMemoryGraphStore</c>, where a contract fact can see it. Vectors live in
/// a store the graph store does not own, so the removal verbs are a SECOND DOOR onto shared state — the
/// shape <c>.claude/knowledge/pitfalls.md</c> §Second doors records, where the question is not "is this code
/// correct" but "what else can reach these objects, and does it apply the same rules".</para>
///
/// <para><b>The asymmetry these facts pin.</b> Enrichment is best-effort — a failing embedder costs links,
/// never the entry. Removal is not: a forget that silently does less than it says is the failure the whole
/// surface exists to prevent, which is why the vector delete runs BEFORE the store delete and is allowed to
/// throw.</para>
/// </summary>
public class MemoryRemovalCompletenessTests
{
    private static GraphMemoryEngine Engine(IVectorStore vectors, GraphMemoryOptions? options = null,
        IMemoryGraphStore? store = null) =>
        new("project/graph", store ?? new InMemoryMemoryGraphStore(), options,
            agePolicies: [new PerWriteAgePolicy()], embedder: new FakeEmbedder(), vectors: vectors);

    private static string Collection(string taskKey, string scope) => $"project/graph|{taskKey}|{scope}";

    /// <summary>Every payload still retrievable from <paramref name="collection"/>. A zero vector is a legal
    /// probe: <see cref="VectorMath.Cosine"/> returns 0 rather than NaN for one, and a search returns the
    /// collection's top-k whatever the scores are — so this reads what is THERE rather than what matches.</summary>
    private static async Task<IReadOnlyList<string>> PayloadsAsync(IVectorStore vectors, string collection) =>
        [.. (await vectors.SearchAsync(collection, new float[64], 1000)).Select(m => m.Payload)];

    /// <summary>The shipped default age policy, on a clock that advances a minute per read.
    /// <para>The clock is what makes the fixture legible, not what makes it accumulating.
    /// <see cref="BurstDampenedAgePolicy"/> divides each write's position by the burst size, and a test
    /// writing two hundred entries inside one tick is ONE burst — so the position advances by the harmonic
    /// series (~6 for 200 writes) and nothing ever ages past a prune floor. A minute between writes ends
    /// every burst, so each advances by one and the fixture ages the way its reader expects.</para></summary>
    private static IMemoryAgePolicy Accumulating()
    {
        var minute = 0;
        return new BurstDampenedAgePolicy(clock: () => DateTimeOffset.UnixEpoch.AddMinutes(minute++));
    }

    /// <summary>Make everything already stored older by writing unrelated material — the same idiom
    /// <c>GraphMemoryEngineTests.Crowd</c> uses, because that is what ages a memory here.</summary>
    private static async Task Crowd(GraphMemoryEngine engine, string taskKey, int writes)
    {
        for (var i = 0; i < writes; i++)
            await engine.RememberAsync(new MemoryWrite(taskKey, "filler", $"unrelated filler number {i}"));
    }

    [Fact]
    public async Task Forgetting_a_scope_removes_the_vector_payloads_it_indexed()
    {
        var vectors = new InMemoryVectorStore();
        var engine = Engine(vectors);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the recovery key is written on the blue card"));

        // the write really was indexed — otherwise the assertion below passes for the wrong reason
        Assert.NotEmpty(await PayloadsAsync(vectors, Collection("t", "s")));

        await engine.ForgetAsync("t", "s");

        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "s")));
    }

    [Fact]
    public async Task Forgetting_every_scope_of_a_task_removes_every_one_of_its_collections()
    {
        // A null scope forgets across the task's scopes, so the vector cleanup has to span them too. Doing
        // the named-scope case only would leave the DOCUMENTED default shape — ForgetAsync("t") — broken.
        var vectors = new InMemoryVectorStore();
        var engine = Engine(vectors);
        await engine.RememberAsync(new MemoryWrite("t", "one", "the recovery key is on the blue card"));
        await engine.RememberAsync(new MemoryWrite("t", "two", "the spare key is in the top drawer"));

        await engine.ForgetAsync("t", scope: null);

        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "one")));
        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "two")));
    }

    [Fact]
    public async Task Forgetting_one_task_leaves_another_task_s_payloads_alone()
    {
        // The control that keeps the three facts above honest. Deleting MORE than was asked for is the one
        // direction a removal must never err in (IPrunableMemory's own remarks), and a prefix-matched
        // collection sweep is exactly how that happens.
        var vectors = new InMemoryVectorStore();
        var engine = Engine(vectors);
        await engine.RememberAsync(new MemoryWrite("keep", "s", "the retained fact about widgets"));
        await engine.RememberAsync(new MemoryWrite("drop", "s", "the withdrawn fact about widgets"));

        await engine.ForgetAsync("drop", scope: null);

        Assert.Empty(await PayloadsAsync(vectors, Collection("drop", "s")));
        Assert.NotEmpty(await PayloadsAsync(vectors, Collection("keep", "s")));
    }

    [Fact]
    public async Task Forgetting_spans_scopes_even_when_the_vector_store_cannot_enumerate_collections()
    {
        // IListableVectorStore is OPTIONAL — a BYO store need not implement it, and all three shipped stores
        // happen to. Deriving the scopes from the NODES being forgotten is what keeps the promise complete
        // for one that does not, rather than degrading a consent withdrawal to whatever the index could list.
        var vectors = new UnlistableVectorStore();
        var engine = Engine(vectors);
        await engine.RememberAsync(new MemoryWrite("t", "one", "the recovery key is on the blue card"));
        await engine.RememberAsync(new MemoryWrite("t", "two", "the spare key is in the top drawer"));

        await engine.ForgetAsync("t", scope: null);

        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "one")));
        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "two")));
    }

    [Fact]
    public async Task Pruning_removes_the_vector_payloads_of_the_entries_it_removed()
    {
        // An orphaned vector is not merely disk: SearchAsync still returns it, so it consumes a SemanticSeedK
        // slot and a GetAsync round trip on every later recall, and GatherAsync then drops it silently
        // because the id no longer resolves. The seed budget is spent on nothing.
        var vectors = new InMemoryVectorStore();
        var engine = Engine(vectors, new GraphMemoryOptions { MinRetrievability = 0.9 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry about widgets"));
        await Crowd(engine, "t", 40);

        var removed = await engine.PruneAsync("t", "s");

        Assert.True(removed >= 1, $"the configured floor must remove the faded entry; removed {removed}");
        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "s")));
    }

    [Fact]
    public async Task Pruning_leaves_the_payloads_of_the_entries_it_kept()
    {
        // The control. PruneAsync removes a qualifying SUBSET, so the vector cleanup must be keyed on the ids
        // actually removed — dropping the whole collection would erase the entries the floor spared.
        var vectors = new InMemoryVectorStore();
        var engine = Engine(vectors, new GraphMemoryOptions { MinRetrievability = 0 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry about widgets"));
        await Crowd(engine, "t", 40);

        Assert.Equal(0, await engine.PruneAsync("t", "s"));

        Assert.NotEmpty(await PayloadsAsync(vectors, Collection("t", "s")));
    }

    [Fact]
    public async Task Pruning_removes_the_payloads_on_the_store_s_OWN_prune_path_too()
    {
        // THE PATH THAT IS EASY TO MISS. PruneAsync has two: with any Derivable age policy the engine picks
        // the doomed ids itself, and with only Accumulating ones — including the SHIPPED DEFAULT, which is
        // what `agePolicies: null` installs — the store decides alone and reports only a COUNT. Every other
        // fact in this class runs the first path, so without this one the shipped configuration would keep
        // orphaning vectors while the suite went green. A rule enforced on one path is not enforced.
        var vectors = new InMemoryVectorStore();
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store,
            new GraphMemoryOptions { MinRetrievability = 0.9 },
            agePolicies: [Accumulating()], embedder: new FakeEmbedder(), vectors: vectors);

        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry about widgets"));
        await Crowd(engine, "t", 200);

        var removed = await engine.PruneAsync("t", "s");

        Assert.True(removed >= 1, $"the store's own path must remove the faded entry; removed {removed}");
        Assert.Empty(await PayloadsAsync(vectors, Collection("t", "s")));
    }

    [Fact]
    public async Task Pruning_with_no_vector_store_makes_no_extra_store_reads()
    {
        // The cost claim, asserted rather than reasoned. Recovering the removed ids on the store's own path
        // needs a before/after census — two full-scope scans — and a deployment with no similarity index has
        // nothing to keep in step, so it must not pay for one. Counting the reads is the only way to tell
        // "the census was skipped" from "the census happened to find nothing".
        var store = new CountingGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store,
            new GraphMemoryOptions { MinRetrievability = 0.9 }, agePolicies: [Accumulating()]);

        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry about widgets"));
        await Crowd(engine, "t", 200);

        var before = store.Seeds;
        await engine.PruneAsync("t", "s");

        Assert.Equal(before, store.Seeds);
    }

    [Fact]
    public async Task A_failing_index_makes_a_FORGET_fail_loudly_and_leaves_the_nodes_retryable()
    {
        // The order's whole justification, asserted rather than left in prose. A forget clears the index
        // FIRST, so an index outage must surface — and must leave the nodes intact, because a consent
        // withdrawal that reported success over surviving content is the failure this exists to prevent.
        // What the caller gets is an exception and an unchanged store, which is retryable.
        var store = new InMemoryMemoryGraphStore();
        var engine = Engine(new ThrowingVectorStore(), store: store);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the recovery key is on the blue card"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ForgetAsync("t", "s"));

        // nothing was half-removed: the entry is still there to be forgotten on the next attempt
        Assert.NotEmpty(await store.SeedAsync("project/graph", "t", "s", null, 10));
    }

    [Fact]
    public async Task A_failing_index_does_NOT_fail_a_prune_that_already_removed_the_nodes()
    {
        // THE MIRROR IMAGE, and the asymmetry is the point. PruneAsync is best-effort capacity management
        // by its own contract -- "removing fewer entries than hoped is a deferred cost rather than a
        // defect" -- and it clears the index AFTER the store, so by the time the index fails the nodes are
        // already gone. Throwing there would lose the COUNT and leave the caller unable to tell that the
        // prune had in fact succeeded; the honest degradation is an orphaned vector, which is exactly the
        // state the whole store was in before any of this existed.
        //
        // The ORDER was asymmetric from the start and the ERROR HANDLING was not, which is the defect this
        // fact was written to catch.
        var store = new InMemoryMemoryGraphStore();
        var engine = Engine(new ThrowingVectorStore(), new GraphMemoryOptions { MinRetrievability = 0.9 },
            store);
        await engine.RememberAsync(new MemoryWrite("t", "s", "a faint associative entry about widgets"));
        await Crowd(engine, "t", 40);

        var removed = await engine.PruneAsync("t", "s");

        Assert.True(removed >= 1, $"the prune succeeded in the store and must report it; got {removed}");
        Assert.Empty(await store.SeedAsync("project/graph", "t", "s", "faint", 10));
    }

    /// <summary>An <see cref="IVectorStore"/> whose every operation fails — a backend that is down, which is
    /// the condition both removal verbs have to answer for and neither had been asked about.</summary>
    private sealed class ThrowingVectorStore : IVectorStore
    {
        public Task UpsertAsync(string collection, string id, float[] vector, string payload,
            CancellationToken ct = default) => Task.CompletedTask;   // the WRITE succeeds; removal is the subject

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VectorMatch>>([]);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
            throw new InvalidOperationException("the vector store is unavailable");

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
            throw new InvalidOperationException("the vector store is unavailable");
    }

    /// <summary>An <see cref="IVectorStore"/> that is deliberately NOT an
    /// <see cref="IListableVectorStore"/> — the BYO shape the fallback exists for.</summary>
    private sealed class UnlistableVectorStore : IVectorStore
    {
        private readonly InMemoryVectorStore _inner = new();

        public Task UpsertAsync(string collection, string id, float[] vector, string payload,
            CancellationToken ct = default) => _inner.UpsertAsync(collection, id, vector, payload, ct);

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
            CancellationToken ct = default) => _inner.SearchAsync(collection, query, k, ct);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
            _inner.DeleteAsync(collection, id, ct);

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
            _inner.RemoveCollectionAsync(collection, ct);
    }

    /// <summary>A real in-process graph store that counts its <see cref="SeedAsync"/> reads, so the census a
    /// vector store forces can be told apart from one that ran and found nothing.</summary>
    private sealed class CountingGraphStore : IMemoryGraphStore
    {
        private readonly InMemoryMemoryGraphStore _inner = new();

        public int Seeds { get; private set; }

        public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default) =>
            _inner.UpsertAsync(write, ct);

        public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
            string? query, int limit, CancellationToken ct = default)
        {
            Seeds++;
            return _inner.SeedAsync(engine, taskKey, scope, query, limit, ct);
        }

        public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
            IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default) =>
            _inner.NeighboursAsync(engine, taskKey, ids, limit, ct);

        public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default) =>
            _inner.GetAsync(engine, id, ct);

        public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
            CancellationToken ct = default) => _inner.TouchAsync(engine, touches, ct);

        public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
            CancellationToken ct = default) => _inner.LinkAsync(engine, from, to, kind, weight, symmetric, ct);

        public Task<int> PruneAsync(string engine, string taskKey, string? scope,
            double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default) =>
            _inner.PruneAsync(engine, taskKey, scope, maxAgeOverStability, olderThan, ct);

        public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids,
            CancellationToken ct = default) => _inner.DeleteAsync(engine, ids, ct);

        public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default) =>
            _inner.ForgetAsync(engine, taskKey, scope, ct);

        public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
            CancellationToken ct = default) => _inner.RecordReviewsAsync(engine, reviews, cap, ct);

        public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default) =>
            _inner.ReviewsAsync(engine, ct);

        public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
            CancellationToken ct = default) => _inner.RecordSubjectsAsync(engine, nodeId, subjects, ct);

        public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
            string subject, int limit, CancellationToken ct = default) =>
            _inner.NodesBySubjectAsync(engine, taskKey, scope, subject, limit, ct);

        public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
            int limit, CancellationToken ct = default) =>
            _inner.KnownSubjectsAsync(engine, taskKey, scope, limit, ct);
    }
}
