using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="GraphMemoryOptions.VectorProviderId"/> pins which backend embeds a graph engine — its
/// writes, its recall queries and its re-embeds — the way <c>ScoringVerificationOptions.ProviderId</c> pins the
/// reranker (<c>docs/DECISIONS.md</c> <b>D148</b>, <b>D209</b>). Unpinned, embedding routes over every vector
/// backend in registration order, and a failover writes another model's vectors into the same collection.</summary>
public class VectorProviderPinTests
{
    private const string Name = "project/graph";

    /// <summary>An embedding backend that counts what it is asked, by role, and can be made to fail.</summary>
    private sealed class Counting(bool fails) : FakeVectorProviderBase
    {
        private readonly FakeVectorProvider _inner = new();

        public List<EmbeddingRole> Roles { get; } = [];

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            EmbedAsync(texts, EmbeddingRole.Document, ct);

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role,
            CancellationToken ct = default)
        {
            Roles.Add(role);
            if (fails) throw new HttpRequestException("the pinned embedder is down");
            return _inner.EmbedAsync(texts, ct);
        }
    }

    private static Counting Backend(string id, bool fails = false) => new Counting(fails) { Id = id };

    private static GraphMemoryEngine Engine(IReadOnlyList<IModelProvider> providers, string? pin,
        IVectorStore? vectors = null)
    {
        var index = vectors ?? new InMemoryVectorStore();
        return new GraphMemoryEngine(Name, new InMemoryMemoryGraphStore(),
            new GraphMemoryOptions { VectorProviderId = pin },
            new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Providers = providers,
                Vectors = index,
                SeedSources = [new LexicalSeedSource(), new SemanticSeedSource(providers, index)],
            });
    }

    private static async Task ExerciseEveryPathAsync(GraphMemoryEngine engine)
    {
        await engine.RememberAsync(new MemoryWrite("t", "s", "the recovery key is written on the blue card"));
        await engine.RecallAsync(new MemoryQuery("t", "s", "where is the recovery key"));
        await engine.ReindexAsync("t");
    }

    [Fact]
    public async Task A_PINNED_backend_embeds_every_write_query_and_re_embed_even_when_another_was_registered_FIRST()
    {
        var first = Backend("fast");
        var pinned = Backend("accurate");

        await ExerciseEveryPathAsync(Engine([first, pinned], "accurate"));

        Assert.Empty(first.Roles);                                   // registration order no longer decides
        Assert.Contains(EmbeddingRole.Document, pinned.Roles);       // the write and the re-embed
        Assert.Contains(EmbeddingRole.Query, pinned.Roles);          // the recall's semantic seed
        Assert.Equal(3, pinned.Roles.Count);
    }

    [Fact]
    public async Task Pinning_NOTHING_keeps_routing_over_every_vector_backend_in_registration_order()
    {
        var first = Backend("fast");
        var second = Backend("accurate");

        await ExerciseEveryPathAsync(Engine([first, second], pin: null));

        Assert.Equal(3, first.Roles.Count);
        Assert.Empty(second.Roles);
    }

    [Fact]
    public async Task A_failing_pinned_backend_never_FAILS_OVER_to_another_model()
    {
        // The point of the pin: a failover writes a different model's vectors into the same collection, which
        // a declared vector space cannot see (D199 compares declared ids, not models). Pinned, the write is
        // stored without its vector — the best-effort path every embed failure already takes.
        var other = Backend("fast");
        var pinned = Backend("accurate", fails: true);
        var engine = Engine([other, pinned], "accurate");

        var written = await engine.RememberAsync(new MemoryWrite("t", "s", "the spare key is under the mat"));
        await engine.RecallAsync(new MemoryQuery("t", "s", "where is the spare key"));
        var reindexed = await engine.ReindexAsync("t");

        Assert.False(written.Ran.HasFlag(MemorySources.Similarity));
        Assert.Equal(new MemoryReindexResult(0, 1), reindexed);
        Assert.Empty(other.Roles);
    }

    [Fact]
    public void Pinning_a_backend_NOBODY_REGISTERED_throws_where_the_engine_is_composed()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Engine([Backend("accurate")], "acurate"));

        Assert.Contains("acurate", error.Message, StringComparison.Ordinal);
        Assert.Contains("accurate", error.Message, StringComparison.Ordinal);   // says what IS registered
    }

    [Fact]
    public void Pinning_a_backend_that_does_NOT_embed_throws_too()
    {
        // the likelier typo: a real id that happens to be the chat model
        var error = Assert.Throws<InvalidOperationException>(
            () => Engine([Backend("vectors"), new FakeTextProvider("chat")], "chat"));

        Assert.Contains(ProviderKinds.Vector, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_backend_is_matched_case_insensitively_like_every_other_id_lookup_here() =>
        _ = Engine([Backend("Accurate")], "accurate");

    [Fact]
    public void A_mis_pin_fails_the_container_s_engine_resolution_naming_the_option()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddProvider(_ => Backend("accurate"), FakeVectorProvider.Declared)
            .AddSemanticMemory()
            .AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions { VectorProviderId = "acurate" })));
        using var sp = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(
            () => sp.GetRequiredService<IMemoryEngineFactory>().Get("project"));
        Assert.Contains(nameof(GraphMemoryOptions.VectorProviderId), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_container_built_engine_honours_the_pin_on_its_write_path()
    {
        var first = Backend("fast");
        var pinned = Backend("accurate");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddProvider(_ => first, FakeVectorProvider.Declared)
            .AddProvider(_ => pinned, FakeVectorProvider.Declared)
            .AddSemanticMemory()
            .AddMemorySemanticSeeds()
            .AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions { VectorProviderId = "accurate" })));
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project");

        await engine.RememberAsync(new MemoryWrite("t", "s", "the recovery key is written on the blue card"));
        await engine.RecallAsync(new MemoryQuery("t", "s", "where is the recovery key"));

        Assert.Empty(first.Roles);
        Assert.Equal([EmbeddingRole.Document, EmbeddingRole.Query], pinned.Roles);
    }

    [Fact]
    public void A_pin_on_an_engine_with_NO_vector_store_is_a_wiring_finding()
    {
        // certain, so reported (D85): the pin states an intent to embed, and with no index nothing ever is
        var engine = new GraphMemoryEngine(Name, new InMemoryMemoryGraphStore(),
            new GraphMemoryOptions { VectorProviderId = "accurate" },
            new GraphMemorySeams { Providers = [Backend("accurate")] });

        var found = Assert.Single(MemoryWiring.Inspect([engine], verification: false, annotation: false));

        Assert.Equal(MemoryWiringProblem.PinnedVectorProviderWithoutIndex, found.Problem);
        Assert.Equal(Name, found.Engine);
    }

    [Fact]
    public void An_unpinned_engine_with_no_vector_store_reports_nothing()
    {
        var engine = new GraphMemoryEngine(Name, new InMemoryMemoryGraphStore(),
            seams: new GraphMemorySeams { Providers = [Backend("accurate")] });

        Assert.Empty(MemoryWiring.Inspect([engine], verification: false, annotation: false));
    }
}
