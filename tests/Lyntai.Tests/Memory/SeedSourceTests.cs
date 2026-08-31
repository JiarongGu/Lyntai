using System.Globalization;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>Pins <see cref="LexicalSeedSource"/>'s whole contract: it is a thin pass-through to
/// <see cref="IMemoryGraphStore.SeedAsync"/>, so the source's output must equal a direct store call
/// entry-for-entry and in the SAME order — a PASS-THROUGH guarantee about what this source returns, not a
/// ranking one: <see cref="IMemorySeedSource"/>'s own contract ranks by <c>Relevance</c>, never by list
/// position.
///
/// <para><see cref="SemanticSeedSource"/>'s tests below pin the same "own order" contract for the vector
/// channel, plus the two behaviours moved verbatim out of <c>GraphMemoryEngine.SemanticScoresAsync</c> /
/// <c>AcrossScopesAsync</c>: the best-effort catch (<see cref="SemanticSeedProbeTests"/> records two
/// implementations debugged blind because a swallowed fault reads as an empty result) and the null-scope
/// span across collections (<see cref="GraphSemanticScopeTests"/>).</para></summary>
public sealed class SeedSourceTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task The_lexical_source_returns_the_stores_own_order_untouched()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        await engine.RememberAsync(new MemoryWrite("task", "scope", "alpha beta gamma"));
        await engine.RememberAsync(new MemoryWrite("task", "scope", "beta gamma delta"));
        await engine.RememberAsync(new MemoryWrite("task", "scope", "unrelated content"));

        var source = new LexicalSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "beta"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);
        var direct = await store.SeedAsync("seedtest", "task", "scope", "beta", 10);

        Assert.Equal("lexical", source.Name);
        Assert.Equal(direct.Select(n => n.Id), seeded.Select(n => n.Id));
        Assert.NotEmpty(seeded);
    }

    /// <summary>Returns a fixed vector for every text it is asked to embed — the query text never matters to
    /// these tests, only the vectors seeded directly into the store.</summary>
    private sealed class FixedEmbedder(float[] vector) : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => vector)]);
    }

    /// <summary>Faults on every call, so the source's own catch is what a test observes rather than the
    /// double's plumbing.</summary>
    private sealed class ThrowingEmbedder : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new InvalidOperationException("embedder unavailable");
    }

    /// <summary>Mirrors <c>SemanticSeedProbeTests.CapturingLogger</c>: a swallowed fault reads as an empty
    /// result unless something is listening, so the warning list is what tells the two apart.</summary>
    private sealed class CapturingLogger : ILogger<SemanticSeedSource>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Warning) Warnings.Add(formatter(state, ex));
        }
    }

    /// <summary>Returns exactly the matches it is given, in the CALLER'S order, regardless of score —
    /// <see cref="IVectorStore.SearchAsync"/>'s own contract leaves ties between backends unspecified and
    /// says a SQL-backed store need not break them, so this is a legitimate shape for a real backend to have.
    /// Used to prove <see cref="SemanticSeedSource"/> imposes its OWN order rather than merely forwarding
    /// whatever the store already returned — <see cref="InMemoryVectorStore"/> cannot stand in for this
    /// because it already sorts (descending score, id-ordinal tiebreak) before this source ever sees it.</summary>
    private sealed class UnsortedVectorStore(IReadOnlyList<VectorMatch> matches) : IVectorStore
    {
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorMatch>>([.. matches.Take(k)]);

        public Task UpsertAsync(string collection, string id, float[] vector, string payload,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Returns every seeded match regardless of the requested <c>k</c>, but RECORDS it — so a test
    /// can observe what width <see cref="SemanticSeedSource"/> actually asked the store for, independent of
    /// what came back.</summary>
    private sealed class RecordingVectorStore(IReadOnlyList<VectorMatch> matches) : IVectorStore
    {
        public int? RequestedK { get; private set; }

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
            CancellationToken ct = default)
        {
            RequestedK = k;
            return Task.FromResult(matches);
        }

        public Task UpsertAsync(string collection, string id, float[] vector, string payload,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Faults on every subject-index READ — delegates everything else to a real in-process store, so
    /// a test using this can tell "the index read is broken" from "nothing matched" by watching that the rest
    /// of the store still works.</summary>
    private sealed class SubjectIndexHostileGraphStore : IMemoryGraphStore
    {
        private readonly InMemoryMemoryGraphStore _inner = new();

        public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
            int limit, CancellationToken ct = default) =>
            throw new InvalidOperationException("the subject index is unavailable");

        public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default) =>
            _inner.UpsertAsync(write, ct);
        public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
            string? query, int limit, CancellationToken ct = default) =>
            _inner.SeedAsync(engine, taskKey, scope, query, limit, ct);
        public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
            IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default) =>
            _inner.NeighboursAsync(engine, taskKey, ids, limit, ct);
        public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default) =>
            _inner.GetAsync(engine, id, ct);
        public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
            CancellationToken ct = default) => _inner.TouchAsync(engine, touches, ct);
        public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
            CancellationToken ct = default) => _inner.LinkAsync(engine, from, to, kind, weight, symmetric, ct);
        public Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
            TimeSpan? olderThan, CancellationToken ct = default) =>
            _inner.PruneAsync(engine, taskKey, scope, maxAgeOverStability, olderThan, ct);
        public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
            _inner.DeleteAsync(engine, ids, ct);
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
    }

    /// <summary>Records the <c>limit</c> <see cref="SubjectSeedSource"/> actually asks
    /// <see cref="IMemoryGraphStore.NodesBySubjectAsync"/> for, independent of what comes back — so a test can
    /// tell "the fetch used K" from "the fetch used Limit" purely from the recorded value. Also COUNTS calls
    /// to both subject-index reads, so a test can assert the COST-avoidance half of a guard (no call at all)
    /// rather than only its output shape, which a downstream guard the store already carries can satisfy on
    /// its own. Everything else delegates to a real in-process store, so
    /// <see cref="GraphMemoryEngine.RememberAsync"/> works normally against it.</summary>
    private sealed class RecordingSubjectGraphStore : IMemoryGraphStore
    {
        private readonly InMemoryMemoryGraphStore _inner = new();

        public int? RequestedNodesLimit { get; private set; }
        public int KnownSubjectsCalls { get; private set; }
        public int NodesBySubjectCalls { get; private set; }

        public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
            int limit, CancellationToken ct = default)
        {
            KnownSubjectsCalls++;
            return _inner.KnownSubjectsAsync(engine, taskKey, scope, limit, ct);
        }

        public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
            string subject, int limit, CancellationToken ct = default)
        {
            NodesBySubjectCalls++;
            RequestedNodesLimit = limit;
            return _inner.NodesBySubjectAsync(engine, taskKey, scope, subject, limit, ct);
        }

        public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default) =>
            _inner.UpsertAsync(write, ct);
        public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
            string? query, int limit, CancellationToken ct = default) =>
            _inner.SeedAsync(engine, taskKey, scope, query, limit, ct);
        public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
            IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default) =>
            _inner.NeighboursAsync(engine, taskKey, ids, limit, ct);
        public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default) =>
            _inner.GetAsync(engine, id, ct);
        public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
            CancellationToken ct = default) => _inner.TouchAsync(engine, touches, ct);
        public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
            CancellationToken ct = default) => _inner.LinkAsync(engine, from, to, kind, weight, symmetric, ct);
        public Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
            TimeSpan? olderThan, CancellationToken ct = default) =>
            _inner.PruneAsync(engine, taskKey, scope, maxAgeOverStability, olderThan, ct);
        public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
            _inner.DeleteAsync(engine, ids, ct);
        public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default) =>
            _inner.ForgetAsync(engine, taskKey, scope, ct);
        public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
            CancellationToken ct = default) => _inner.RecordReviewsAsync(engine, reviews, cap, ct);
        public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default) =>
            _inner.ReviewsAsync(engine, ct);
        public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
            CancellationToken ct = default) => _inner.RecordSubjectsAsync(engine, nodeId, subjects, ct);
    }

    /// <summary>The same capture as <see cref="CapturingLogger"/>, typed for <see cref="SubjectSeedSource"/> —
    /// <see cref="ILogger{TCategoryName}"/>'s generic parameter is the category, so the two cannot share a
    /// type.</summary>
    private sealed class SubjectCapturingLogger : ILogger<SubjectSeedSource>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Warning) Warnings.Add(formatter(state, ex));
        }
    }

    [Fact]
    public async Task The_semantic_source_returns_nodes_in_descending_cosine_order()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var one = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry one"));
        var two = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry two"));
        var three = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry three"));

        // Seeded DELIBERATELY out of score order (two, three, one) — a store need not sort its own matches,
        // so this test must observe the SOURCE'S order, never the store's. A real cosine geometry would let
        // a tie-break-only regression slip through unnoticed if the store happened to already sort.
        var vectors = new UnsortedVectorStore([
            new VectorMatch(two.Id, "entry two", 0.1),
            new VectorMatch(three.Id, "entry three", 0.9),
            new VectorMatch(one.Id, "entry one", 0.5),
        ]);

        var source = new SemanticSeedSource(new FixedEmbedder([1f, 0f]), vectors);
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery(TaskKey: "task", Scope: "scope", Query: "anything"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Equal("semantic", source.Name);
        Assert.Equal([three.Id, one.Id, two.Id],
            seeded.Select(n => n.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task The_search_width_is_Ks_own_bound_while_the_return_is_capped_by_the_requests_limit()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var written = await engine.RememberAsync(
                new MemoryWrite(TaskKey: "task", Scope: "scope", Content: $"entry {i}"));
            ids.Add(written.Id);
        }

        // Every match a real store COULD return at K=5 — RecordingVectorStore hands all of them back
        // regardless of the requested k, so the test can tell "search asked for 5" from "search asked for 2"
        // purely from the recorded value, independent of what the source does with the result afterwards.
        var matches = ids.Select((id, i) => new VectorMatch(id, $"entry {i}", 1.0 - i * 0.1)).ToList();
        var vectors = new RecordingVectorStore(matches);

        var source = new SemanticSeedSource(new FixedEmbedder([1f, 0f]), vectors, new SemanticSeedOptions { K = 5 });
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery(TaskKey: "task", Scope: "scope", Query: "anything"), Limit: 2);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        // SEARCH was not narrowed: the store was asked for K (5), never Math.Min(K, Limit) (2).
        Assert.Equal(5, vectors.RequestedK);
        // RETURN honours Limit's own "may return at most" contract, regardless of how wide the search was.
        Assert.Equal(2, seeded.Count);
    }

    [Fact]
    public async Task The_semantic_source_returns_empty_rather_than_throwing_when_the_embedder_faults()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var log = new CapturingLogger();
        var source = new SemanticSeedSource(new ThrowingEmbedder(), new InMemoryVectorStore(), logger: log);
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery(TaskKey: "task", Scope: "scope", Query: "anything"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Empty(seeded);   // empty because the fault was swallowed, never because nothing matched
        Assert.Single(log.Warnings);   // the assertion that tells the two apart
    }

    [Fact]
    public async Task A_null_scope_spans_every_collection_under_the_task()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var home = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "home", Content: "plumbing arrangements"));
        var garden = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "garden", Content: "gardening notes"));

        var vectors = new InMemoryVectorStore();
        await vectors.UpsertAsync("seedtest|task|home", home.Id, [1f, 0f], "plumbing arrangements", CancellationToken.None);
        await vectors.UpsertAsync("seedtest|task|garden", garden.Id, [1f, 0f], "gardening notes", CancellationToken.None);

        var source = new SemanticSeedSource(new FixedEmbedder([1f, 0f]), vectors);
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery(TaskKey: "task", Scope: null, Query: "anything"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Equal(2, seeded.Count);
        Assert.Contains(seeded, n => n.Id.ToString(CultureInfo.InvariantCulture) == home.Id);
        Assert.Contains(seeded, n => n.Id.ToString(CultureInfo.InvariantCulture) == garden.Id);
    }

    [Fact]
    public async Task The_subject_source_returns_a_matched_subjects_nodes_in_the_stores_own_order()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var one = await engine.RememberAsync(new MemoryWrite("task", "scope", "first fact about the topic"));
        var two = await engine.RememberAsync(new MemoryWrite("task", "scope", "second fact about the topic"));
        var three = await engine.RememberAsync(new MemoryWrite("task", "scope", "third fact about the topic"));

        // Every write is tagged the SAME subject, so the only thing that can produce this sequence is the
        // store's own newest-first order (highest id first) — a re-sort (by id ascending, say) would diverge
        // from it immediately, since three writes never sort ascending and descending the same way.
        foreach (var id in new[] { one.Id, two.Id, three.Id })
            await store.RecordSubjectsAsync("seedtest", long.Parse(id, CultureInfo.InvariantCulture), ["topic"],
                CancellationToken.None);

        var source = new SubjectSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "topic"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);
        var direct = await store.NodesBySubjectAsync("seedtest", "task", "scope", "topic", 10,
            CancellationToken.None);

        Assert.Equal("subject", source.Name);
        Assert.Equal(direct, seeded.Select(n => n.Id));
        Assert.NotEmpty(seeded);
    }

    [Fact]
    public async Task The_subject_source_de_duplicates_a_node_shared_across_matched_subjects()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var shared = await engine.RememberAsync(new MemoryWrite("task", "scope", "the shared fact"));
        var urgentOnly = await engine.RememberAsync(new MemoryWrite("task", "scope", "an urgent-only fact"));
        var billingOnly = await engine.RememberAsync(new MemoryWrite("task", "scope", "a billing-only fact"));

        var sharedId = long.Parse(shared.Id, CultureInfo.InvariantCulture);
        // The shared fact carries BOTH handles the query below names — the fixture a missing `seen` guard
        // cannot survive, since each matched subject's own NodesBySubjectAsync call would report it again.
        await store.RecordSubjectsAsync("seedtest", sharedId, ["urgent", "billing"], CancellationToken.None);
        await store.RecordSubjectsAsync("seedtest", long.Parse(urgentOnly.Id, CultureInfo.InvariantCulture),
            ["urgent"], CancellationToken.None);
        await store.RecordSubjectsAsync("seedtest", long.Parse(billingOnly.Id, CultureInfo.InvariantCulture),
            ["billing"], CancellationToken.None);

        var source = new SubjectSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "urgent billing report"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        // Three DISTINCT nodes, never four: without the `seen` guard the shared fact would be counted once
        // per matched subject.
        Assert.Equal(3, seeded.Count);
        Assert.Equal(3, seeded.Select(n => n.Id).Distinct().Count());
        Assert.Contains(seeded, n => n.Id == sharedId);
    }

    [Fact]
    public async Task The_subject_source_returns_empty_rather_than_throwing_when_the_subject_index_faults()
    {
        var store = new SubjectIndexHostileGraphStore();
        var log = new SubjectCapturingLogger();
        var source = new SubjectSeedSource(logger: log);
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "anything"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Empty(seeded);   // empty because the fault was swallowed, never because nothing matched
        Assert.Single(log.Warnings);   // the assertion that tells the two apart
    }

    /// <summary>Writes one fact tagged "topic" into a fresh <see cref="RecordingSubjectGraphStore"/> — the
    /// shared setup for every guard test below, each of which needs a store that genuinely HAS a matching
    /// subject so "zero calls" cannot be mistaken for "nothing to find".</summary>
    private static async Task<RecordingSubjectGraphStore> SubjectSeededStoreAsync()
    {
        var store = new RecordingSubjectGraphStore();
        var engine = new GraphMemoryEngine("seedtest", store);
        var written = await engine.RememberAsync(new MemoryWrite("task", "scope", "a fact about the topic"));
        await store.RecordSubjectsAsync("seedtest", long.Parse(written.Id, CultureInfo.InvariantCulture),
            ["topic"], CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task With_K_or_Scan_at_zero_the_subject_source_makes_no_store_calls()
    {
        // K=0: the guard is one combined early return (K, Scan, Limit and Query all checked together), so
        // NEITHER store call runs — not even KnownSubjectsAsync, despite Scan being untouched.
        var kOffStore = await SubjectSeededStoreAsync();
        var kOff = new SubjectSeedSource(new SubjectSeedOptions { K = 0 });
        var kOffRequest = new MemorySeedRequest("seedtest", kOffStore,
            new MemoryQuery("task", Scope: "scope", Query: "topic"), Limit: 10);

        Assert.Empty(await kOff.SeedAsync(kOffRequest, CancellationToken.None));
        Assert.Equal(0, kOffStore.KnownSubjectsCalls);
        Assert.Equal(0, kOffStore.NodesBySubjectCalls);

        // Scan=0: the handle SCAN never happens at all — the one grouped read SubjectSeedOptions.Scan's own doc
        // says this knob exists to stop paying for, so no call reaches either method.
        var scanOffStore = await SubjectSeededStoreAsync();
        var scanOff = new SubjectSeedSource(new SubjectSeedOptions { Scan = 0 });
        var scanOffRequest = new MemorySeedRequest("seedtest", scanOffStore,
            new MemoryQuery("task", Scope: "scope", Query: "topic"), Limit: 10);

        Assert.Empty(await scanOff.SeedAsync(scanOffRequest, CancellationToken.None));
        Assert.Equal(0, scanOffStore.KnownSubjectsCalls);
        Assert.Equal(0, scanOffStore.NodesBySubjectCalls);
    }

    [Fact]
    public async Task A_blank_query_makes_no_subject_store_calls()
    {
        var store = await SubjectSeededStoreAsync();
        var source = new SubjectSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "   "), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Empty(seeded);
        Assert.Equal(0, store.KnownSubjectsCalls);
        Assert.Equal(0, store.NodesBySubjectCalls);
    }

    [Fact]
    public async Task A_non_positive_limit_makes_no_subject_store_calls()
    {
        var store = await SubjectSeededStoreAsync();
        var source = new SubjectSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "topic"), Limit: 0);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Empty(seeded);
        Assert.Equal(0, store.KnownSubjectsCalls);
        Assert.Equal(0, store.NodesBySubjectCalls);
    }

    [Fact]
    public async Task The_subject_fetch_is_Ks_own_bound_while_the_return_is_capped_by_the_requests_limit()
    {
        var store = new RecordingSubjectGraphStore();
        var engine = new GraphMemoryEngine("seedtest", store);

        for (var i = 0; i < 5; i++)
        {
            var written = await engine.RememberAsync(new MemoryWrite("task", "scope", $"entry {i}"));
            await store.RecordSubjectsAsync("seedtest", long.Parse(written.Id, CultureInfo.InvariantCulture),
                ["topic"], CancellationToken.None);
        }

        var source = new SubjectSeedSource(new SubjectSeedOptions { K = 5 });
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "topic"), Limit: 2);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        // FETCH was not narrowed: the store was asked for K (5), never Math.Min(K, Limit) (2).
        Assert.Equal(5, store.RequestedNodesLimit);
        // RETURN honours Limit's own "may return at most" contract, regardless of how wide the fetch was.
        Assert.Equal(2, seeded.Count);
    }
}
