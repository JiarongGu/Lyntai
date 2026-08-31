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

    [Fact]
    public async Task The_semantic_source_returns_nodes_in_descending_cosine_order()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        var one = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry one"));
        var two = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry two"));
        var three = await engine.RememberAsync(new MemoryWrite(TaskKey: "task", Scope: "scope", Content: "entry three"));

        // entry three is nearest the [1,0] query, then entry one (45 degrees off), then entry two (orthogonal)
        var vectors = new InMemoryVectorStore();
        await vectors.UpsertAsync("seedtest|task|scope", three.Id, [1f, 0f], "entry three", CancellationToken.None);
        await vectors.UpsertAsync("seedtest|task|scope", one.Id, [0.7f, 0.7f], "entry one", CancellationToken.None);
        await vectors.UpsertAsync("seedtest|task|scope", two.Id, [0f, 1f], "entry two", CancellationToken.None);

        var source = new SemanticSeedSource(new FixedEmbedder([1f, 0f]), vectors);
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery(TaskKey: "task", Scope: "scope", Query: "anything"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);

        Assert.Equal("semantic", source.Name);
        Assert.Equal([three.Id, one.Id, two.Id],
            seeded.Select(n => n.Id.ToString(CultureInfo.InvariantCulture)));
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
