using Lyntai.Memory;
using Lyntai.Inference;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>A declared vector space stamps every collection it writes, and refuses to mix another space's vectors
/// into one — the same-dimension embedder swap no store can see on its own (<c>docs/DECISIONS.md</c> <b>D199</b>).
/// Two guards over ONE inner store stand in for the deployment before and after the swap.</summary>
public class VectorSpaceTests
{
    private static readonly float[] Probe = [1f, 0f, 0f];

    private static IVectorStore Guard(IVectorStore inner, string space) => VectorSpaceStore.Wrap(inner, space, owned: false);

    [Fact]
    public async Task A_collection_written_under_one_space_refuses_a_write_from_another()
    {
        var inner = new InMemoryVectorStore();
        await Guard(inner, "model-a").UpsertAsync("c", "1", Probe, "first");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Guard(inner, "model-b").UpsertAsync("c", "2", Probe, "second"));
        Assert.Contains("model-a", ex.Message);
        Assert.Contains("model-b", ex.Message);
        Assert.Single(await inner.SearchAsync("c", Probe, 10));   // nothing mixed in
    }

    [Fact]
    public async Task A_search_of_another_space_s_collection_finds_nothing()
    {
        var inner = new InMemoryVectorStore();
        await Guard(inner, "model-a").UpsertAsync("c", "1", Probe, "first");

        Assert.Empty(await Guard(inner, "model-b").SearchAsync("c", Probe, 10));
        Assert.Empty(await Guard(inner, "model-b").SearchAsync("c", Probe, 10, new VectorSearchFilter()));
        Assert.Single(await Guard(inner, "model-a").SearchAsync("c", Probe, 10));
    }

    [Fact]
    public async Task An_unstamped_collection_is_searched_and_adopted_by_its_first_guarded_write()
    {
        // a collection written before the space was declared carries no stamp: it is read as before, and the
        // first write under the declared space claims it
        var inner = new InMemoryVectorStore();
        await inner.UpsertAsync("c", "1", Probe, "legacy");
        var guarded = Guard(inner, "model-a");

        Assert.Single(await guarded.SearchAsync("c", Probe, 10));
        await guarded.UpsertAsync("c", "2", Probe, "new");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Guard(inner, "model-b").UpsertAsync("c", "3", Probe, "other"));
    }

    [Fact]
    public async Task Removing_a_collection_removes_its_stamp()
    {
        var inner = new InMemoryVectorStore();
        await Guard(inner, "model-a").UpsertAsync("c", "1", Probe, "first");
        await Guard(inner, "model-a").RemoveCollectionAsync("c");

        await Guard(inner, "model-b").UpsertAsync("c", "2", Probe, "second");   // a forgotten collection is free
        Assert.Single(await Guard(inner, "model-b").SearchAsync("c", Probe, 10));
    }

    [Fact]
    public async Task Reading_back_another_space_s_vectors_throws_rather_than_returning_them()
    {
        var inner = new InMemoryVectorStore();
        await Guard(inner, "model-a").UpsertAsync("c", "1", Probe, "first");

        var readable = Assert.IsAssignableFrom<IReadableVectorStore>(Guard(inner, "model-b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readable.GetAsync("c", ["1"]));
    }

    [Fact]
    public async Task The_stamps_are_never_listed_as_a_collection()
    {
        var inner = new InMemoryVectorStore();
        var guarded = Assert.IsAssignableFrom<IListableVectorStore>(Guard(inner, "model-a"));
        await guarded.UpsertAsync("c", "1", Probe, "first");

        Assert.Equal(["c"], await guarded.ListCollectionsAsync(""));
    }

    [Fact]
    public void The_guard_declares_exactly_the_optional_halves_its_store_has()
    {
        // claiming IListableVectorStore over a store that cannot list would make "cannot list" read as "holds
        // nothing" — the silent shape the optional interfaces exist to end
        var bare = Guard(new ParkingVectorStoreWithoutListing(), "model-a");
        Assert.IsNotAssignableFrom<IListableVectorStore>(bare);
        Assert.IsAssignableFrom<IReadableVectorStore>(bare);

        var full = Guard(new InMemoryVectorStore(), "model-a");
        Assert.IsAssignableFrom<IListableVectorStore>(full);
        Assert.IsAssignableFrom<IReadableVectorStore>(full);
    }

    [Fact]
    public async Task A_re_embed_moves_a_graph_s_collections_to_the_declared_space()
    {
        var store = new InMemoryMemoryGraphStore();
        var inner = new InMemoryVectorStore();
        var before = GraphOver(store, Guard(inner, "model-a"));
        await before.RememberAsync(new MemoryWrite("t", "s", "the recovery key is written on the blue card"));

        var after = GraphOver(store, Guard(inner, "model-b"));
        var refused = await after.RememberAsync(new MemoryWrite("t", "s", "the spare key is under the mat"));
        Assert.False(refused.Ran.HasFlag(MemorySources.Similarity));   // not mixed into model-a's collection

        var result = await after.ReindexAsync("t");
        Assert.Equal(2, result.Indexed);
        var written = await after.RememberAsync(new MemoryWrite("t", "s", "the door code is four digits"));
        Assert.True(written.Ran.HasFlag(MemorySources.Similarity));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(inner, "model-a")
            .UpsertAsync(MemoryVectorCollection.For("project/graph", "t", "s"), "99", Probe, "stale"));
    }

    [Fact]
    public void UseVectorSpace_wraps_the_registered_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModelProvider>(new FakeVectorProvider());
        services.AddLyntai(cfg => cfg.UseVectorSpace("model-a"));

        using var sp = services.BuildServiceProvider();
        Assert.IsType<VectorSpaceStore.Full>(sp.GetRequiredService<IVectorStore>());
    }

    [Fact]
    public void UseVectorSpace_refuses_a_blank_id() =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(cfg => cfg.UseVectorSpace(" ")));

    private static GraphMemoryEngine GraphOver(IMemoryGraphStore store, IVectorStore vectors) =>
        new("project/graph", store, seams: new GraphMemorySeams
        {
            AgePolicies = [new PerWriteAgePolicy()],
            Providers = [new FakeVectorProvider()],
            Vectors = vectors,
        });

    /// <summary>A store that can read back but cannot list its collections.</summary>
    private sealed class ParkingVectorStoreWithoutListing : IReadableVectorStore
    {
        private readonly InMemoryVectorStore _inner = new();

        public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default) =>
            _inner.UpsertAsync(collection, id, vector, payload, ct);

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            _inner.SearchAsync(collection, query, k, ct);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => _inner.DeleteAsync(collection, id, ct);

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) => _inner.RemoveCollectionAsync(collection, ct);

        public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
            CancellationToken ct = default) => _inner.GetAsync(collection, ids, ct);
    }
}
