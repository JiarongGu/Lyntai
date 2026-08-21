using Lyntai;
using Lyntai.Memory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>Semantic memory: the in-memory vector store's cosine ranking + dedup/forget, and the
/// SemanticMemory service's meaning-based recall / scope isolation / no-embedder guard — deterministic via
/// the feature-hashed <see cref="FakeEmbedder"/>.</summary>
public class SemanticMemoryTests
{
    // ---- vector store --------------------------------------------------------------------------------

    [Fact]
    public async Task Vector_store_ranks_by_cosine_and_respects_k()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync("c", "a", [1f, 0f, 0f], "A");
        await store.UpsertAsync("c", "b", [0f, 1f, 0f], "B");
        await store.UpsertAsync("c", "c", [0f, 0f, 1f], "C");

        var hits = await store.SearchAsync("c", [0.9f, 0.1f, 0f], k: 2);

        Assert.Equal(2, hits.Count);
        Assert.Equal("A", hits[0].Payload);   // nearest to the query direction
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public async Task Vector_store_upsert_dedups_by_id_and_forget_clears()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync("c", "same", [1f, 0f], "first");
        await store.UpsertAsync("c", "same", [1f, 0f], "second"); // same id → overwrite

        var hits = await store.SearchAsync("c", [1f, 0f], k: 5);
        Assert.Single(hits);
        Assert.Equal("second", hits[0].Payload);

        await store.RemoveCollectionAsync("c");
        Assert.Empty(await store.SearchAsync("c", [1f, 0f], k: 5));
    }

    [Fact]
    public async Task Vector_store_delete_removes_one_by_id_and_absent_is_a_no_op()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync("c", "keep", [1f, 0f], "KEEP");
        await store.UpsertAsync("c", "drop", [0f, 1f], "DROP");

        await store.DeleteAsync("c", "drop");         // remove one by id
        await store.DeleteAsync("c", "never");        // absent id → no-op, no throw

        var hits = await store.SearchAsync("c", [1f, 1f], k: 5);
        Assert.Single(hits);
        Assert.Equal("KEEP", hits[0].Payload);        // only the un-deleted vector remains
    }

    [Fact]
    public async Task Vector_store_tolerates_a_dimension_mismatch_scoring_it_zero()
    {
        // a stray wrong-dimension row (e.g. from a prior embedding model) must NOT throw and sink the whole
        // search — it scores 0 and ranks last, consistent with the SQLite/Postgres vector stores.
        var store = new InMemoryVectorStore();
        await store.UpsertAsync("c", "match", [1f, 0f], "MATCH");     // 2-dim (same as the query)
        await store.UpsertAsync("c", "stale", [1f, 0f, 0f], "STALE"); // 3-dim (mismatched)

        var hits = await store.SearchAsync("c", [1f, 0f], k: 5);

        Assert.Equal("MATCH", hits[0].Payload);                       // matching dim ranks first
        Assert.Equal(0, hits.Single(h => h.Payload == "STALE").Score); // mismatched row scored 0, not thrown
    }

    // ---- semantic memory service ---------------------------------------------------------------------

    private static SemanticMemory NewMemory() => new(new FakeEmbedder(), new InMemoryVectorStore());

    [Fact]
    public async Task Recalls_the_semantically_closest_fact_first()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "s", "cancel my subscription anytime");
        await mem.RememberAsync("t", "s", "the refund policy details");
        await mem.RememberAsync("t", "s", "our pizza menu today");

        var hits = await mem.RecallAsync("t", "s", "how do I cancel", k: 3, minScore: 0.0001);

        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);           // the query shares "cancel" → top
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza")); // no word overlap → filtered by minScore
    }

    [Fact]
    public async Task Remembering_identical_content_dedups()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "s", "same fact");
        await mem.RememberAsync("t", "s", "same fact");

        var hits = await mem.RecallAsync("t", "s", "same fact", k: 10);
        Assert.Single(hits);
    }

    [Fact]
    public async Task Recall_is_scoped_by_task_and_scope()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "scopeA", "alpha content");
        await mem.RememberAsync("t", "scopeB", "beta content");

        var a = await mem.RecallAsync("t", "scopeA", "alpha content", k: 10);
        Assert.Single(a);
        Assert.Contains("alpha", a[0].Content);               // scopeB's entry is not visible
    }

    [Fact]
    public async Task Forget_clears_a_scope()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "s", "forget me");
        await mem.ForgetAsync("t", "s");
        Assert.Empty(await mem.RecallAsync("t", "s", "forget me", k: 10));
    }

    [Fact]
    public async Task Empty_query_returns_nothing()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "s", "something");
        Assert.Empty(await mem.RecallAsync("t", "s", "   ", k: 5));
    }

    [Fact] // T7: recall is fail-open when the vector backend throws (e.g. pgvector on a dimension mismatch)
    public async Task Recall_is_fail_open_when_the_vector_store_throws()
    {
        var mem = new SemanticMemory(new FakeEmbedder(), new ThrowingVectorStore());
        var hits = await mem.RecallAsync("t", "s", "query", k: 5); // must NOT throw
        Assert.Empty(hits);
    }

    private sealed class ThrowingVectorStore : IVectorStore
    {
        public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            throw new InvalidOperationException("different vector dimensions"); // mimics pgvector's error
        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ---- a scope-OPTIONAL recall ---------------------------------------------------------------------

    /// <summary>The adopter's shape: scope is a FILTER, so the ordinary path passes none. Through 3.0.0 that
    /// searched the collection <c>task + separator + ""</c>, which never exists — and an absent collection and
    /// one holding no match are the same empty list, so the semantic member contributed exactly zero and
    /// nothing said so.</summary>
    [Fact]
    public async Task A_null_scope_searches_every_scope_of_the_task()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "preference", "the children go to sleep at eight on school nights");
        await mem.RememberAsync("t", "contact", "the plumber is on speed dial");
        await mem.RememberAsync("other", "preference", "belongs to a different task");

        var hits = await mem.RecallAsync("t", scope: null, "children sleep nights", k: 5, minScore: 0.0001);

        Assert.Contains(hits, h => h.Content.Contains("children"));
        Assert.DoesNotContain(hits, h => h.Content.Contains("different task"));   // task isolation survives
    }

    /// <summary>Each hit names the scope it came from, which is the only way a caller can act on a
    /// cross-scope result — and what lets the engine mint the same id the write did.</summary>
    [Fact]
    public async Task A_cross_scope_hit_carries_the_scope_it_came_from()
    {
        var mem = NewMemory();
        await mem.RememberAsync("t", "preference", "the children go to sleep at eight");

        var hit = Assert.Single(await mem.RecallAsync("t", scope: null, "children sleep", k: 5, minScore: 0.0001));

        Assert.Equal("preference", hit.Scope);
        Assert.Null((await mem.RecallAsync("t", "preference", "children sleep", k: 5))[0].Scope);
    }

    [Fact]
    public async Task A_cross_scope_recall_is_still_bounded_by_k()
    {
        var mem = NewMemory();
        for (var i = 0; i < 6; i++) await mem.RememberAsync("t", $"scope{i}", $"shared subject entry {i}");

        Assert.Equal(2, (await mem.RecallAsync("t", scope: null, "shared subject", k: 2)).Count);
    }

    /// <summary>A store that cannot enumerate its collections yields nothing — exactly what this path did
    /// before the capability existed — rather than throwing. Fail-open, like every other recall here.</summary>
    [Fact]
    public async Task A_store_that_cannot_list_its_collections_yields_nothing_rather_than_throwing()
    {
        var mem = new SemanticMemory(new FakeEmbedder(), new UnlistableVectorStore());

        Assert.Empty(await mem.RecallAsync("t", scope: null, "anything", k: 5));
    }

    /// <summary>A BYO store with only the required half of the seam — it forwards every
    /// <see cref="IVectorStore"/> member to a real store and simply does not implement
    /// <see cref="IListableVectorStore"/>.</summary>
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

    [Fact]
    public async Task Without_an_embedder_a_call_throws_a_clear_error()
    {
        var mem = new SemanticMemory(embedder: null, new InMemoryVectorStore());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mem.RememberAsync("t", "s", "x"));
        Assert.Contains("AddEmbeddings", ex.Message);
    }

    // ---- the engine over it --------------------------------------------------------------------------

    /// <summary>The engine used to early-return on a null scope, so a composite whose graph member is scoped
    /// by kind and whose semantic member should search everything could not be expressed at all — one
    /// <see cref="MemoryQuery"/> travels to every member, so the consumer could not work around it either.</summary>
    [Fact]
    public async Task The_engine_passes_a_null_scope_through_instead_of_returning_early()
    {
        var semantic = new FakeSemanticMemory();
        await semantic.RememberAsync("t", "preference", "the children sleep at eight");
        var engine = new Lyntai.Memory.Engines.SemanticMemoryEngine("e", semantic);

        var recall = await engine.RecallAsync(new MemoryQuery("t", Scope: null, Query: "children"));

        Assert.Equal("the children sleep at eight", Assert.Single(recall.Items).Headline);
    }

    /// <summary>The id must be the one a write minted for (task, scope, content), or the reference addresses
    /// nothing — which is why a cross-scope hit has to carry its own scope back.</summary>
    [Fact]
    public async Task A_cross_scope_recall_reports_the_same_reference_the_write_returned()
    {
        var engine = new Lyntai.Memory.Engines.SemanticMemoryEngine("e", new FakeSemanticMemory());
        var written = await engine.RememberAsync(new MemoryWrite("t", "preference", "the children sleep at eight"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", Scope: null, Query: "children"));

        Assert.Equal(written.Id, Assert.Single(recall.Items).Reference.Id);
    }

    // ---- DI wiring -----------------------------------------------------------------------------------

    [Fact]
    public async Task AddEmbeddings_wires_semantic_memory_end_to_end()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddEmbeddings(new FakeEmbedder()));
        using var sp = services.BuildServiceProvider();

        var mem = sp.GetRequiredService<ISemanticMemory>();
        await mem.RememberAsync("t", "s", "cancel subscription anytime");
        await mem.RememberAsync("t", "s", "pizza menu today");

        var hits = await mem.RecallAsync("t", "s", "how to cancel", k: 1);
        Assert.Single(hits);
        Assert.Contains("cancel", hits[0].Content);
    }
}
