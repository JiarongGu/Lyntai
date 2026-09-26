using Lyntai.Tests.Fakes;
using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>The graph's SEMANTIC half must agree with its LEXICAL half about what an unscoped recall means.
///
/// <para>A write always names a scope, so a literal collection <c>{Name}|{task}|</c> built from a null
/// <see cref="MemoryQuery.Scope"/> can never exist — while the store's own seed spans scopes normally
/// (<c>@scope IS NULL OR n.scope = @scope</c>). Searched literally, the same query answers when a scope is
/// named and returns nothing when it is not, which is the COMMON case.</para>
///
/// <para><b>The vector backend is scripted, not fuzzy</b>, because the subject is the collection the search runs
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
    private sealed class ScriptedVectorProvider : FakeVectorProviderBase
    {
        private static readonly Dictionary<string, float[]> Map = new(StringComparer.Ordinal)
        {
            [Target] = [1f, 0f, 0f],
            [Query] = [1f, 0f, 0f],   // cosine 1 against Target, 0 against Other
            [Other] = [0f, 1f, 0f],
        };

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                [.. texts.Select(t => Map.TryGetValue(t, out var v) ? v : [0f, 0f, 1f])]);
    }

    /// <summary><paramref name="seedK"/> of 0 leaves the vector CHANNEL unregistered, which is what
    /// "seeding off" means — the source's own <see cref="SemanticSeedOptions.K"/> refuses a non-positive
    /// value, because a channel that can never search is indistinguishable from an outage.
    /// <para>The log listens to BOTH the engine and <see cref="SemanticSeedSource"/>: `RecallAsync` converts
    /// anything `GatherAsync` throws into an empty result, and the semantic channel's own best-effort catch
    /// reports through the SOURCE's logger — so an empty warning list is what keeps the NEGATIVE control below
    /// from passing for the wrong reason.</para></summary>
    private static (GraphMemoryEngine Engine, CapturingLogger Log) Build(int seedK)
    {
        var log = new CapturingLogger();
        var vectorProvider = new ScriptedVectorProvider();
        var vectors = new InMemoryVectorStore();
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Providers = [vectorProvider],
                Vectors = vectors,
                SeedSources = seedK <= 0
                    ? [new LexicalSeedSource()]
                    : [new LexicalSeedSource(),
                        new SemanticSeedSource([vectorProvider], vectors, new SemanticSeedOptions { K = seedK },
                            log.For<SemanticSeedSource>())],
            }, logger: log.For<GraphMemoryEngine>());
        return (engine, log);
    }

    private static async Task SeedAsync(GraphMemoryEngine engine)
    {
        await engine.RememberAsync(new MemoryWrite("household", "home", Target));
        await engine.RememberAsync(new MemoryWrite("household", "garden", Other));
    }

    /// <summary>Scope omitted: a semantic half searching a collection no write can create would contribute
    /// nothing.</summary>
    [Fact]
    public async Task An_unscoped_recall_reaches_a_semantically_near_entry_in_some_scope()
    {
        var (engine, log) = Build(seedK: 3);
        await SeedAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("household", Scope: null, Query: Query));

        Assert.Contains(recall.Items, i => i.Headline.Contains("plumbing"));
        Assert.Empty(log.Warnings);
    }

    /// <summary>The control that makes the fixture trustworthy: the scoped query reaches the same entry, so
    /// the null scope is the only variable in the case above.</summary>
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
    /// the unscoped path rather than throwing, and the scoped path is untouched.</summary>
    [Fact]
    public async Task A_store_that_cannot_list_leaves_the_unscoped_path_empty_and_the_scoped_path_working()
    {
        var log = new CapturingLogger();
        var vectorProvider = new ScriptedVectorProvider();
        var vectors = new UnlistableVectorStore();
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Providers = [vectorProvider],
                Vectors = vectors,
                SeedSources = [new LexicalSeedSource(),
                    new SemanticSeedSource([vectorProvider], vectors, new SemanticSeedOptions { K = 3 },
                        log.For<SemanticSeedSource>())],
            }, logger: log.For<GraphMemoryEngine>());
        await SeedAsync(engine);

        Assert.Empty((await engine.RecallAsync(new MemoryQuery("household", null, Query))).Items);
        Assert.Contains((await engine.RecallAsync(new MemoryQuery("household", "home", Query))).Items,
            i => i.Headline.Contains("plumbing"));
        Assert.Empty(log.Warnings);
    }
}
