using Lyntai;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The <c>AddSemanticMemory</c> wiring seam: one builder call turns semantic recall on, states the
/// intent so the embedder-less case is a startup FAILURE rather than a silent skip, and leaves every piece
/// (embedder, vector store, the service itself) substitutable. Pins that no consumer has to <c>new</c> up a
/// vector store, a connection factory, or an embedder to get persistent semantic recall.</summary>
public sealed class SemanticMemoryWiringTests : IDisposable
{
    private readonly TempDbPath _db = new("semantic-wiring");
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AddSemanticMemory_with_an_embedder_wires_recall_in_one_call()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddingProvider(_ => new FakeEmbedder()).AddSemanticMemory());
        using var sp = services.BuildServiceProvider();

        var mem = sp.GetRequiredService<ISemanticMemory>();
        await mem.RememberAsync("t", "s", "cancel subscription anytime");
        var hits = await mem.RecallAsync("t", "s", "how to cancel", k: 1);

        Assert.Single(hits);
        Assert.Contains("cancel", hits[0].Content);
    }

    /// <summary>The whole point of naming the feature: without this call, an app that forgets the embedder
    /// gets NO <see cref="ISemanticMemory"/> registration at all and every recall path quietly skips
    /// semantic memory. Stating the intent turns that into a composition-time throw.</summary>
    [Fact]
    public void AddSemanticMemory_without_any_embedder_fails_fast_instead_of_silently_doing_nothing()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddSemanticMemory()));

        Assert.Contains("AddSemanticMemory", ex.Message);
        Assert.Contains("ProviderKinds.Vector", ex.Message);
    }

    /// <summary>The no-argument overload is the "my backend comes from somewhere else" path — a provider
    /// package's <c>Add…Provider</c>, <c>AddEmbeddingProvider</c>, or a host registration made before
    /// <c>AddLyntai</c>. All three satisfy the intent.</summary>
    [Fact]
    public void An_embedder_registered_by_any_route_satisfies_the_intent()
    {
        var viaBuilder = new ServiceCollection();
        viaBuilder.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddingProvider(_ => new FakeEmbedder())
            .AddSemanticMemory());
        Assert.NotNull(viaBuilder.BuildServiceProvider().GetService<ISemanticMemory>());

        var viaHost = new ServiceCollection();
        viaHost.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddingProvider(_ => new FakeEmbedder())
            .AddSemanticMemory());
        Assert.NotNull(viaHost.BuildServiceProvider().GetService<ISemanticMemory>());
    }

    [Fact]
    public async Task The_factory_and_generic_overloads_register_the_embedder_too()
    {
        var byFactory = new ServiceCollection();
        byFactory.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddingProvider(_ => new FakeEmbedder()).AddSemanticMemory());
        await using var fromFactory = byFactory.BuildServiceProvider();
        Assert.NotNull(fromFactory.GetService<ISemanticMemory>());

        var byType = new ServiceCollection();
        byType.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddingProvider(_ => new DiConstructedEmbedder()).AddSemanticMemory());
        await using var fromType = byType.BuildServiceProvider();
        Assert.NotNull(fromType.GetService<ISemanticMemory>());
    }

    /// <summary>Substitutable: a host that registers its own <see cref="IVectorStore"/> (or its own
    /// <see cref="ISemanticMemory"/>) before <c>AddLyntai</c> keeps it — the helper only ever
    /// <c>TryAdd</c>s, per the repo's single-dependency convention.</summary>
    [Fact]
    public void A_host_registered_vector_store_still_wins()
    {
        var mine = new CountingVectorStore();
        var services = new ServiceCollection();
        services.AddSingleton<IVectorStore>(mine);
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).AddEmbeddingProvider(_ => new FakeEmbedder()).AddSemanticMemory());
        using var sp = services.BuildServiceProvider();

        Assert.Same(mine, sp.GetRequiredService<IVectorStore>());
    }

    /// <summary>The persistent path end to end, with ZERO hand-construction: storage, the SQLite vector
    /// store and the embedder all arrive through builder calls, and the vectors survive a fresh container
    /// over the same database file.</summary>
    [Fact]
    public async Task Persistent_semantic_recall_needs_no_hand_constructed_types()
    {
        await using (var first = Compose().BuildServiceProvider())
            await first.GetRequiredService<ISemanticMemory>()
                .RememberAsync("t", "s", "cancel subscription anytime");

        await using var second = Compose().BuildServiceProvider();
        var hits = await second.GetRequiredService<ISemanticMemory>().RecallAsync("t", "s", "how to cancel", k: 1);

        Assert.Single(hits);                          // recalled from SQLite, not from a warm in-memory store
        Assert.Contains("cancel", hits[0].Content);

        ServiceCollection Compose()
        {
            var services = new ServiceCollection();
            services.AddLyntai(b => b
                .AddProvider(_ => new FakeLlmProvider("p"))
                .UseSqliteStorage(_db.Path)
                .UseSqliteVectorStore()
                .AddEmbeddingProvider(_ => new FakeEmbedder()).AddSemanticMemory());
            return services;
        }
    }

    /// <summary>Parameterless so the generic overload's DI construction has nothing to resolve.</summary>
    private sealed class DiConstructedEmbedder : EmbeddingBackend
    {
        private readonly FakeEmbedder _inner = new();
        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            _inner.EmbedAsync(texts, ct);
    }

    private sealed class CountingVectorStore : IVectorStore
    {
        public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorMatch>>([]);
        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) => Task.CompletedTask;
    }
}
