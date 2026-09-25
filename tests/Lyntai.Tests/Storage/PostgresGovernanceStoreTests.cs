using Lyntai.Inference;
using Lyntai;
using Lyntai.Inference.Budgeting;
using Lyntai.Inference.Caching;
using Lyntai.Memory;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>The persistent Postgres backends for the governance + semantic-memory seams against the real
/// container (Testcontainers, pgvector image). Skips when Docker is unavailable. Scopes to unique
/// keys/consumers/collections so they share the one migrated database (hence per-consumer totals only, no
/// global-SUM assertions).</summary>
[Collection("postgres")]
public sealed class PostgresGovernanceStoreTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    // The cross-backend ResponseCacheContract, Uid-scoped for the shared container. The size-cap trim stays
    // per-backend below: it is set at construction and needs a far-future clock here, neither of which a
    // portable fact can express.
    private async Task CachePg(Func<IResponseCache, string, Action<TimeSpan>, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        await body(new PostgresResponseCache(pg.Factory, new LyntaiOptions(), clock.Get), Uid(), clock.Advance);
    }

    [SkippableFact] public Task Cache_round_trip() => CachePg(ResponseCacheContract.A_stored_reply_round_trips_with_its_usage);
    [SkippableFact] public Task Cache_miss() => CachePg(ResponseCacheContract.A_key_that_was_never_set_is_a_MISS);
    [SkippableFact] public Task Cache_ttl() => CachePg(ResponseCacheContract.An_entry_past_its_TTL_is_a_MISS);
    [SkippableFact] public Task Cache_remove_one() => CachePg(ResponseCacheContract.Remove_evicts_ONE_entry_and_leaves_the_others);
    [SkippableFact] public Task Cache_remove_absent() => CachePg(ResponseCacheContract.Removing_a_key_that_is_not_there_is_a_NO_OP);

    /// <summary>A reply survives the INSTANCE that cached it — what a persistent backend is for, and the one
    /// property the portable contract cannot state, since only a persistent cache has two handles over one
    /// store.</summary>
    [SkippableFact]
    public async Task ResponseCache_persists_a_reply_across_store_instances()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        var key = Uid();
        await new PostgresResponseCache(pg.Factory, options)
            .SetAsync(key, new TextResponse("pg cached", ProviderVerdict.Ok, new TextUsage(3, 4, CostUsd: 0.05)));

        var got = await new PostgresResponseCache(pg.Factory, options).GetAsync(key); // fresh instance

        Assert.NotNull(got);
        Assert.Equal("pg cached", got!.Text);
        Assert.Equal(0.05, got.Usage!.CostUsd);
    }

    [SkippableFact]
    public async Task ResponseCache_evicts_the_oldest_beyond_max_entries()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var options = new LyntaiOptions();
        options.Cache.MaxEntries = 2;
        // Clock far in the FUTURE: the trim keeps "the newest @max" TABLE-wide, so this test's three
        // entries must strictly outrank other tests' present-time rows on the shared container (tests in
        // the postgres collection run serially, so evicting older leftovers is harmless).
        var clock = new MutableClock { Now = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var cache = new PostgresResponseCache(pg.Factory, options, clock.Get);
        var (a, b, c) = (Uid(), Uid(), Uid());

        await cache.SetAsync(a, new TextResponse("a", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(b, new TextResponse("b", ProviderVerdict.Ok)); clock.Advance(TimeSpan.FromSeconds(1));
        await cache.SetAsync(c, new TextResponse("c", ProviderVerdict.Ok)); // over cap → oldest (a) trimmed

        Assert.Null(await cache.GetAsync(a));
        Assert.NotNull(await cache.GetAsync(b));
        Assert.NotNull(await cache.GetAsync(c));

        // hygiene: don't leave far-future rows outranking later tests' entries in the shared table
        await cache.RemoveAsync(b);
        await cache.RemoveAsync(c);
    }

    // The cross-backend UsageTrackerContract. It replaces two hand-written facts that asserted a STRICT
    // SUBSET of the SQLite suite's — the global total, an unrecorded consumer, and a scoped reset leaving
    // the other consumers intact were all missing, so a ResetAsync(consumer) that dropped the whole table
    // would have passed here. The two TABLE-WIDE facts still cannot run on a shared container and are
    // excluded by name in PostgresContractCoverageTests, which fails if an exclusion stops matching.
    private async Task UsagePg(Func<IUsageTracker, string, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        await body(new PostgresUsageTracker(pg.Factory), Uid());
    }

    [SkippableFact] public Task Usage_accumulates() => UsagePg(UsageTrackerContract.Records_accumulate_per_consumer);
    [SkippableFact] public Task Usage_unrecorded() => UsagePg(UsageTrackerContract.An_unrecorded_consumer_is_Empty);
    [SkippableFact] public Task Usage_casings() => UsagePg(UsageTrackerContract.Consumer_identity_aggregates_across_casings);
    [SkippableFact] public Task Usage_non_ascii_casings() => UsagePg(UsageTrackerContract.Consumer_identity_aggregates_across_non_ASCII_casings);
    [SkippableFact] public Task Usage_reset_one() => UsagePg(UsageTrackerContract.Resetting_a_consumer_clears_it);
    [SkippableFact] public Task Usage_reset_scoped() => UsagePg(UsageTrackerContract.Resetting_ONE_consumer_leaves_the_others_intact);
    [SkippableFact] public Task Usage_reset_casing() => UsagePg(UsageTrackerContract.Resetting_is_case_insensitive_like_the_totals);

    /// <summary>Totals survive the instance that recorded them — the property the replaced fact carried
    /// inline ("fresh instance reads persisted totals") and the one thing a shared contract cannot express,
    /// since only a persistent backend has two handles over one store.</summary>
    [SkippableFact]
    public async Task UsageTracker_totals_are_read_back_by_a_FRESH_handle_over_the_same_store()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var consumer = Uid();
        await new PostgresUsageTracker(pg.Factory).RecordAsync(consumer, new ProviderUsage(10, 5, CostUsd: 0.10));

        Assert.Equal(15, (await new PostgresUsageTracker(pg.Factory).TotalAsync(consumer)).TotalTokens);
    }

    // The cross-backend VectorStoreContract, wired here because this class owns the container lifetime and
    // NAMESPACED with Uid() so it coexists with the other tests on the shared instance. This is the backend
    // that computes similarity in SQL (pgvector's <=> cosine distance) rather than through VectorMath, so it
    // is where a divergence from the in-process store would actually live — and the pre-existing fixtures
    // could not have seen one: they used unit basis vectors, under which cosine and a raw dot product agree
    // on both ordering and sign. Measured 2026-08-14: swapping VectorMath.Cosine for a bare dot product left
    // all 29 existing vector/semantic tests green, including the one named "ranks_by_cosine".
    private async Task VecPg(Func<IVectorStore, string, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        await body(new PostgresVectorStore(pg.Factory), Uid());
    }

    [SkippableFact] public Task Contract_cosine_not_dot() => VecPg(VectorStoreContract.Ranking_is_by_cosine_so_magnitude_does_not_win);
    [SkippableFact] public Task Contract_score_in_range() => VecPg(VectorStoreContract.A_score_is_a_cosine_in_the_documented_range);
    [SkippableFact] public Task Contract_upsert_replaces() => VecPg(VectorStoreContract.Upserting_the_same_id_replaces_rather_than_duplicating);
    [SkippableFact] public Task Contract_bounded_by_k() => VecPg(VectorStoreContract.Search_returns_at_most_k);
    [SkippableFact] public Task Contract_delete_one() => VecPg(VectorStoreContract.Delete_removes_one_entry_and_leaves_the_others);
    [SkippableFact] public Task Contract_remove_collection() => VecPg(VectorStoreContract.Removing_a_collection_clears_it_and_absent_deletes_are_no_ops);
    [SkippableFact] public Task Contract_isolated() => VecPg(VectorStoreContract.Collections_are_isolated);
    [SkippableFact] public Task Contract_tie_by_id() => VecPg(VectorStoreContract.Equal_scores_are_ordered_by_id);
    [SkippableFact] public Task Contract_tie_at_k() => VecPg(VectorStoreContract.The_k_boundary_keeps_the_same_tied_entries);
    [SkippableFact] public Task Contract_tie_loses_to_score() => VecPg(VectorStoreContract.The_tiebreak_never_outranks_the_score);
    [SkippableFact] public Task Contract_other_dimension() => VecPg(VectorStoreContract.A_vector_of_another_dimension_scores_zero_and_ranks_last);
    [SkippableFact] public Task Contract_zero_vector() => VecPg(VectorStoreContract.A_zero_vector_scores_zero_and_ranks_last);
    [SkippableFact] public Task Contract_list_prefix() => VecPg(VectorStoreContract.Listing_matches_a_prefix_ordinally);
    [SkippableFact] public Task Contract_list_literal() => VecPg(VectorStoreContract.A_listing_prefix_is_never_read_as_a_pattern);
    [SkippableFact] public Task Contract_list_empty() => VecPg(VectorStoreContract.Listing_omits_emptied_collections_and_never_throws);

    [SkippableFact]
    public void Contract_can_list()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        VectorStoreContract.Every_shipped_store_can_list_its_collections(new PostgresVectorStore(pg.Factory));
    }

    [SkippableFact]
    public async Task VectorStore_pgvector_ranks_by_cosine_dedups_and_removes()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var c = Uid();
        var store = new PostgresVectorStore(pg.Factory);
        await store.UpsertAsync(c, "a", [1f, 0f, 0f], "A");
        await store.UpsertAsync(c, "b", [0f, 1f, 0f], "B");
        await store.UpsertAsync(c, "a", [1f, 0f, 0f], "A2"); // same id → dedup (payload updated)

        var hits = await store.SearchAsync(c, [0.9f, 0.1f, 0f], k: 5);
        Assert.Equal(2, hits.Count);            // a (deduped) + b
        Assert.Equal("A2", hits[0].Payload);    // nearest to the query, latest payload
        Assert.True(hits[0].Score > hits[1].Score);

        await store.RemoveCollectionAsync(c);
        Assert.Empty(await store.SearchAsync(c, [1f, 0f, 0f], k: 5));
    }

    [SkippableFact]
    public async Task VectorStore_pgvector_delete_removes_one_by_id_and_absent_is_a_no_op()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var c = Uid();
        var store = new PostgresVectorStore(pg.Factory);
        await store.UpsertAsync(c, "keep", [1f, 0f, 0f], "KEEP");
        await store.UpsertAsync(c, "drop", [0f, 1f, 0f], "DROP");

        await store.DeleteAsync(c, "drop");   // remove one by id
        await store.DeleteAsync(c, "never");  // absent id → no-op, no throw

        var hits = await store.SearchAsync(c, [0.5f, 0.5f, 0f], k: 5);
        Assert.Single(hits);
        Assert.Equal("KEEP", hits[0].Payload); // only the un-deleted vector remains

        await store.RemoveCollectionAsync(c);  // hygiene: shared container
    }

    [SkippableFact]
    public async Task Semantic_memory_works_over_the_pgvector_store()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var task = Uid();
        var mem = new SemanticMemory([new FakeVectorProvider()], new PostgresVectorStore(pg.Factory));
        await mem.RememberAsync(task, "s", "cancel my subscription anytime");
        await mem.RememberAsync(task, "s", "our pizza menu today");

        var hits = await mem.RecallAsync(task, "s", "how do I cancel", k: 3, minScore: 0.0001);
        Assert.NotEmpty(hits);
        Assert.Contains("cancel", hits[0].Content);
        Assert.DoesNotContain(hits, h => h.Content.Contains("pizza"));
    }
}
