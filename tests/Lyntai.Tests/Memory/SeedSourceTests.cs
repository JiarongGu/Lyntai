using System.Globalization;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>Pins <see cref="LexicalSeedSource"/>'s whole contract: it is a thin pass-through to
/// <see cref="IMemoryGraphStore.SeedAsync"/>, so the source's output must equal a direct store call
/// entry-for-entry and in the SAME order — position is the rank this seam exists to carry.
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
}
