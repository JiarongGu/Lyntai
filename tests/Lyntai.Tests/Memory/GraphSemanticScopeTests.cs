using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>The graph's SEMANTIC half must agree with its LEXICAL half about what an unscoped recall means.
///
/// <para>It did not. A write always names a scope, so the literal collection <c>{Name}|{task}|</c> that a
/// null <see cref="MemoryQuery.Scope"/> produced could never exist — while the store's own seed spans scopes
/// normally (<c>@scope IS NULL OR n.scope = @scope</c>). An adopter measured the consequence on 3.0.1: the
/// same query answered when a scope was named and returned nothing when it was not, which is the COMMON
/// case.</para>
///
/// <para><b>The embedder is scripted, not fuzzy</b>, because the subject is the collection the search runs
/// against and not similarity quality. A word-overlap double could not tell "found semantically" apart from
/// "found lexically" — the query below shares no term with any content, so the lexical seed is empty by
/// construction and every hit here is the semantic path or nothing.</para></summary>
public class GraphSemanticScopeTests
{
    private const string Target = "zzqq plumbing arrangements";
    private const string Other = "yypp gardening notes";

    // shares no >=3-char term with either content, so the lexical seed cannot reach them
    private const string Query = "vvww trades contact";

    /// <summary>Exact text to exact vector. Anything unscripted is orthogonal to both, so an accidental
    /// match cannot pass this test.</summary>
    private sealed class ScriptedEmbedder : IEmbedder
    {
        private static readonly Dictionary<string, float[]> Map = new(StringComparer.Ordinal)
        {
            [Target] = [1f, 0f, 0f],
            [Query] = [1f, 0f, 0f],   // cosine 1 against Target, 0 against Other
            [Other] = [0f, 1f, 0f],
        };

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                [.. texts.Select(t => Map.TryGetValue(t, out var v) ? v : [0f, 0f, 1f])]);
    }

    /// <summary>`RecallAsync` converts anything `GatherAsync` throws into an empty result, so a swallowed
    /// defect is indistinguishable from "nothing matched" — the trap `SemanticSeedProbeTests` records having
    /// been debugged blind twice. Asserting the warning list stayed empty is what keeps the NEGATIVE control
    /// below from passing for the wrong reason.
    /// <para>Wired to BOTH loggers, because the semantic channel's own best-effort catch now lives in
    /// <see cref="SemanticSeedSource"/> and reports through that type's logger — listening only to the
    /// engine's would leave exactly the fault this fixture exists to hear inaudible.</para></summary>
    private sealed class CapturingLogger : ILogger<GraphMemoryEngine>, ILogger<SemanticSeedSource>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Warning) Warnings.Add(formatter(state, ex));
        }
    }

    /// <summary><paramref name="seedK"/> of 0 leaves the vector CHANNEL unregistered, which is now what
    /// "seeding off" means — the source's own <see cref="SemanticSeedOptions.K"/> refuses a non-positive
    /// value, because a channel that can never search is indistinguishable from an outage.</summary>
    private static (GraphMemoryEngine Engine, CapturingLogger Log) Build(int seedK)
    {
        var log = new CapturingLogger();
        var embedder = new ScriptedEmbedder();
        var vectors = new InMemoryVectorStore();
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            logger: log, embedder: embedder, vectors: vectors,
            seedSources: seedK <= 0
                ? [new LexicalSeedSource()]
                : [new LexicalSeedSource(),
                    new SemanticSeedSource(embedder, vectors, new SemanticSeedOptions { K = seedK }, log)]);
        return (engine, log);
    }

    private static async Task SeedAsync(GraphMemoryEngine engine)
    {
        await engine.RememberAsync(new MemoryWrite("household", "home", Target));
        await engine.RememberAsync(new MemoryWrite("household", "garden", Other));
    }

    /// <summary>The reported defect: scope omitted, so the semantic half searched a collection no write can
    /// create and contributed nothing.</summary>
    [Fact]
    public async Task An_unscoped_recall_reaches_a_semantically_near_entry_in_some_scope()
    {
        var (engine, log) = Build(seedK: 3);
        await SeedAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("household", Scope: null, Query: Query));

        Assert.Contains(recall.Items, i => i.Headline.Contains("plumbing"));
        Assert.Empty(log.Warnings);
    }

    /// <summary>The control that makes the fixture trustworthy: naming the scope already worked before the
    /// fix, so the null-scope case above is the only thing that changed.</summary>
    [Fact]
    public async Task Naming_the_scope_reaches_the_same_entry()
    {
        var (engine, log) = Build(seedK: 3);
        await SeedAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("household", Scope: "home", Query: Query));

        Assert.Contains(recall.Items, i => i.Headline.Contains("plumbing"));
        Assert.Empty(log.Warnings);
    }

    /// <summary>The NEGATIVE control, and the one that makes the two above mean anything: with seeding off
    /// the query reaches nothing at all, which proves the hits are the semantic path rather than a lexical
    /// match the fixture failed to exclude.</summary>
    [Fact]
    public async Task With_seeding_off_the_query_reaches_nothing_so_the_hits_above_are_semantic()
    {
        var (engine, log) = Build(seedK: 0);
        await SeedAsync(engine);

        var scoped = await engine.RecallAsync(new MemoryQuery("household", Scope: "home", Query: Query));
        var unscoped = await engine.RecallAsync(new MemoryQuery("household", Scope: null, Query: Query));

        Assert.Empty(scoped.Items);
        Assert.Empty(unscoped.Items);
        Assert.Empty(log.Warnings);   // empty because nothing matched, never because something was swallowed
    }

    /// <summary>Spanning needs <see cref="IListableVectorStore"/>. A BYO store without it yields nothing on
    /// the unscoped path — exactly what that path did before — rather than throwing, and the scoped path is
    /// untouched.</summary>
    [Fact]
    public async Task A_store_that_cannot_list_leaves_the_unscoped_path_empty_and_the_scoped_path_working()
    {
        var log = new CapturingLogger();
        var embedder = new ScriptedEmbedder();
        var vectors = new UnlistableVectorStore();
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            logger: log, embedder: embedder, vectors: vectors,
            seedSources: [new LexicalSeedSource(),
                new SemanticSeedSource(embedder, vectors, new SemanticSeedOptions { K = 3 }, log)]);
        await SeedAsync(engine);

        Assert.Empty((await engine.RecallAsync(new MemoryQuery("household", null, Query))).Items);
        Assert.Contains((await engine.RecallAsync(new MemoryQuery("household", "home", Query))).Items,
            i => i.Headline.Contains("plumbing"));
        Assert.Empty(log.Warnings);
    }

    /// <summary>A vector store with only the required half of the seam.</summary>
    private sealed class UnlistableVectorStore : IVectorStore
    {
        private readonly InMemoryVectorStore _inner = new();
        public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default) =>
            _inner.UpsertAsync(collection, id, vector, payload, ct);
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            _inner.SearchAsync(collection, query, k, ct);
        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
            _inner.DeleteAsync(collection, id, ct);
        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
            _inner.RemoveCollectionAsync(collection, ct);
    }
}
