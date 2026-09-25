using Lyntai;
using Lyntai.Cortex;
using Lyntai.Jobs;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Integration tests for the PostgreSQL backend against a real container (Testcontainers).
/// Every test scopes to a unique key/task/session so they can share the one migrated database.
/// Every test SKIPS (visibly, never a pass) when Docker is unavailable — see <see cref="PostgresFixture"/>.</summary>
[Collection("postgres")]
public sealed class PostgresStorageTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    [SkippableFact]
    public async Task Live_postgres_connection_works()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        using var conn = pg.Factory.Open();
        var version = await Dapper.SqlMapper.QuerySingleAsync<string>(conn, "SELECT version()");
        Assert.Contains("PostgreSQL", version); // proves a real server, not a trivially-skipped test
    }

    /// <summary>Every <see cref="Lyntai.Tests.Memory.MemoryGraphStoreContract"/> fact against a real
    /// Postgres, driven from the shared theory source so coverage is STRUCTURAL rather than counted.</summary>
    /// <remarks>
    /// <para>This replaced one long method that awaited all sixty-nine facts in sequence and then asserted
    /// a hand-bumped <c>covered</c> literal against the reflected <c>declared</c> count. That assertion
    /// could not see the case it most needed to: a fact wired to Postgres ALONE passes once the author
    /// bumps the literal, while InMemory and SQLite silently never run it.</para>
    /// <para>Still one container: the fixture is shared across the whole <c>postgres</c> collection, and
    /// xUnit runs a class's cases sequentially, so startup is paid once exactly as before. Each case takes a
    /// fresh <see cref="Uid"/> because the database is shared.</para>
    /// </remarks>
    [SkippableTheory]
    [MemberData(nameof(Lyntai.Tests.Memory.MemoryGraphStoreFacts.Names),
        MemberType = typeof(Lyntai.Tests.Memory.MemoryGraphStoreFacts))]
    public Task Graph_store_satisfies_the_contract(string fact)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        return Lyntai.Tests.Memory.MemoryGraphStoreFacts.RunAsync(
            fact, clock => new PostgresMemoryGraphStore(pg.Factory, clock: clock), Uid());
    }

    /// <summary>The write path's own finiteness guard, pinned as ACTUAL COLUMN CONTENT — the same fact
    /// <c>SqliteMemoryGraphStoreTests</c> pins for SQLite. <c>PostgresMemoryGraphStore.UpsertAsync</c> reads
    /// the RAW incoming <see cref="MemorySignals"/> bag a second time, straight into the promoted
    /// <c>salience</c> column, bypassing <see cref="MemorySignalsJson.Serialize"/>'s own finiteness filter —
    /// and unlike SQLite, Npgsql accepts a NaN <c>double</c> parameter without complaint, so the corruption
    /// here is silent rather than a thrown exception: the row would order wherever Postgres's NaN comparison
    /// semantics happen to put it in <c>ORDER BY … n.salience DESC …</c>, undermining the "no row silently
    /// mis-sorts" invariant the migration's own comment argues for.</summary>
    [SkippableFact]
    public async Task A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var key = Uid();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = pg.Factory.Open();
        var salience = await Dapper.SqlMapper.ExecuteScalarAsync<double>(conn,
            "SELECT salience FROM lyntai_memory_node WHERE task_key = @key", new { key });
        Assert.False(double.IsNaN(salience), $"the salience column holds {salience}, not the neutral value");
        Assert.Equal(1, salience);
    }

    /// <summary>The write path's own finiteness guard for <c>difficulty</c> (2026-08-10, fsrs-properly plan
    /// Task 2), pinned as ACTUAL COLUMN CONTENT — the same shape
    /// <see cref="A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN"/> pins for
    /// <c>salience</c>, and for the identical reason: Npgsql binds a NaN <c>double</c> without complaint.</summary>
    [SkippableFact]
    public async Task A_non_finite_judged_difficulty_writes_the_neutral_column_value_not_NaN()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var key = Uid();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = pg.Factory.Open();
        var difficulty = await Dapper.SqlMapper.ExecuteScalarAsync<double>(conn,
            "SELECT difficulty FROM lyntai_memory_node WHERE task_key = @key", new { key });
        Assert.False(double.IsNaN(difficulty), $"the difficulty column holds {difficulty}, not the neutral value");
        Assert.Equal(5, difficulty); // the neutral mid-point, corrected 2026-08-11 from the floor 1 (SAME fix
                                     // as the SQLite twin — same code path, MemorySignals.Difficulty)
    }

    [SkippableFact]
    public async Task Every_object_carries_the_lyntai_prefix()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        using var conn = pg.Factory.Open();
        // the package may live inside a consumer's existing db — nothing unprefixed allowed
        var stray = (await Dapper.SqlMapper.QueryAsync<string>(conn, """
            SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT LIKE 'lyntai\_%'
            UNION ALL
            SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' AND sequencename NOT LIKE 'lyntai\_%'
            UNION ALL
            SELECT indexname FROM pg_indexes WHERE schemaname = 'public'
              AND indexname NOT LIKE 'lyntai\_%' AND indexname NOT LIKE 'ix\_lyntai\_%' AND indexname NOT LIKE 'ux\_lyntai\_%'
            """)).ToList();
        Assert.Empty(stray);
    }

    /// <summary>F1 (feature toggles): a DISABLED storage feature lands no table on Postgres. Selective
    /// migration is driven by per-migration <c>[Tags(nameof(StorageFeature.X), StorageFeatures.AllTag)]</c>
    /// + the runner's active tag set, exactly as SQLite. Uses a THROWAWAY container (not the shared,
    /// already-all-migrated fixture db) so the subset migration is observed in isolation.</summary>
    [SkippableFact]
    public async Task Selective_migration_lands_only_the_selected_features_tables()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");

        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await container.StartAsync();
        var cs = container.GetConnectionString();

        MigrationRunnerService.MigrateUp(cs, StorageFeature.Score | StorageFeature.Conversation);
        var factory = new PostgresConnectionFactory(cs);

        Assert.True(await TableExists(factory, "lyntai_score_result")); // Score selected
        Assert.True(await TableExists(factory, "lyntai_thread"));        // Conversation selected
        Assert.True(await TableExists(factory, "lyntai_message"));
        Assert.False(await TableExists(factory, "lyntai_kv"));           // KeyValue NOT selected → no table
        Assert.False(await TableExists(factory, "lyntai_memory_entry")); // Memory NOT selected
        Assert.False(await TableExists(factory, "lyntai_job"));          // Jobs NOT selected
        Assert.True(await TableExists(factory, "lyntai_version_info"));  // version table always
    }

    /// <summary>The awaitable twin lands the same schema on Postgres — run against the already-migrated
    /// shared fixture db, so it also pins that a second pass is a no-op. Cancellation semantics (the token
    /// honoured before any work) are covered without Docker in <see cref="AsyncMigrationTests"/>.</summary>
    [SkippableFact]
    public async Task MigrateUpAsync_is_idempotent_against_a_migrated_database()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");

        await MigrationRunnerService.MigrateUpAsync(pg.ConnectionString);

        using var conn = pg.Factory.Open();
        var applied = (await Dapper.SqlMapper.QueryAsync<long>(conn,
            "SELECT version FROM lyntai_version_info ORDER BY version")).ToList();
        Assert.Equal(SchemaFacts.PostgresVersions, applied);
    }

    private static Task<bool> TableExists(IDbConnectionFactory factory, string table) =>
        SchemaFacts.PostgresTableExists(factory, table);

    // ---- cross-backend contracts, run against Postgres over the shared container ----------------------
    // Each is namespaced by a unique key (Uid()) so it coexists with the other tests on the one shared,
    // migrated database. The few table-wide facts that cannot be isolated that way are listed, with their
    // reasons, in PostgresContractCoverageTests.NotOnPostgres.

    [SkippableFact] public Task KeyValue_round_trip() => Pg(() => KeyValueStoreContract.Set_get_delete_round_trip(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_missing() => Pg(() => KeyValueStoreContract.Missing_key_returns_null(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_overwrite() => Pg(() => KeyValueStoreContract.Overwrite_updates_the_value(new PostgresKeyValueStore(pg.Factory), Uid())); // ON CONFLICT upsert
    [SkippableFact] public Task KeyValue_cjk() => Pg(() => KeyValueStoreContract.Cjk_value_round_trips(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_list_prefix() => Pg(() => KeyValueStoreContract.List_keys_filters_by_prefix_in_ordinal_order(new PostgresKeyValueStore(pg.Factory), Uid())); // COLLATE "C" ordering
    [SkippableFact] public Task KeyValue_list_literals() => Pg(() => KeyValueStoreContract.List_keys_treats_like_wildcards_as_literals(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_list_all() => Pg(() => KeyValueStoreContract.List_keys_without_prefix_lists_all_keys(new PostgresKeyValueStore(pg.Factory), Uid()));

    [SkippableFact] public Task Conversation_create_get() => Pg(() => ConversationStoreContract.Create_and_get_thread(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_duplicate_id() => Pg(() => ConversationStoreContract.Duplicate_thread_id_throws_and_preserves_the_original(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_metadata() => Pg(() => ConversationStoreContract.Thread_metadata_round_trips_and_updates(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_mixed_events() => Pg(() => ConversationStoreContract.Appends_mixed_kind_events_with_json_payloads_in_seq_order(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_cjk() => Pg(() => ConversationStoreContract.Cjk_payload_round_trips(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_seq_metadata() => Pg(() => ConversationStoreContract.Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_unknown_thread_append() => Pg(() => ConversationStoreContract.Appending_to_an_unknown_thread_throws(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_aliases() => Pg(() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_cascade() => Pg(() => ConversationStoreContract.Delete_thread_cascades_to_messages(new PostgresConversationStore(pg.Factory), Uid())); // FK cascade
    [SkippableFact] public Task Conversation_list_newest_first() => Pg(() => ConversationStoreContract.List_threads_returns_newest_first(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_non_positive_limit() => Pg(() => ConversationStoreContract.A_non_positive_limit_lists_no_threads(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_count() => Pg(() => ConversationStoreContract.Count_reflects_inserted_and_deleted_threads(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_paged() => Pg(() => ConversationStoreContract.Paged_cursor_walks_every_thread_exactly_once(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_paged_tiebreak() => Pg(() => ConversationStoreContract.A_cursor_at_the_same_instant_falls_back_to_the_id(new PostgresConversationStore(pg.Factory), Uid()));

    [SkippableFact] public Task Trace_save_load() => Pg(() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_resave_replaces() => Pg(() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_unknown() => Pg(() => TraceStoreContract.Unknown_session_returns_null(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_seq_offset() => Pg(() => TraceStoreContract.Step_sequence_and_offset_round_trip(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_unset_seq() => Pg(() => TraceStoreContract.Unset_sequences_store_the_list_position(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_seq_order() => Pg(() => TraceStoreContract.Steps_read_back_in_sequence_order(new PostgresTraceStore(pg.Factory), Uid()));

    [SkippableFact] public Task PromptVersion_none() => Pg(() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_monotonic() => Pg(() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_history() => Pg(() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback() => Pg(() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback_missing() => Pg(() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_isolation() => Pg(() => PromptVersionStoreContract.Names_are_isolated(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_raced() => Pg(() => PromptVersionStoreContract.Concurrent_saves_of_one_name_get_distinct_consecutive_versions(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_raced_rollbacks() => Pg(() => PromptVersionStoreContract.Racing_saves_and_rollbacks_leave_exactly_one_active_revision(new PostgresPromptVersionStore(pg.Factory), Uid()));

    // ScoreStoreContract: only the session-scoped Rescore is table-safe on the shared container (Aggregate
    // and Export are table-wide → InMemory + SQLite only, as noted above).
    [SkippableFact] public Task Score_rescore() => Pg(() => ScoreStoreContract.Rescore_replaces_not_accumulates(new PostgresScoreStore(pg.Factory)));

    // MemoryStoreContract: task-scoped, so every method is safe on the shared container. A mutable clock
    // drives the TTL contract deterministically; PostgresMemoryStore is built with cap = 3 for the cap test.
    [SkippableFact] public Task Memory_token_recall() => Pg(() => MemoryStoreContract.Remember_then_recall_by_single_token_substring(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_cjk() => Pg(() => MemoryStoreContract.Cjk_substring_recall(PgMemory(), Uid())); // pg_trgm CJK substring recall
    [SkippableFact] public Task Memory_scope() => Pg(() => MemoryStoreContract.Scope_filter_applies(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_task_isolation() => Pg(() => MemoryStoreContract.Task_isolation_applies(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_dedup() => Pg(() => MemoryStoreContract.Remembering_an_identical_fact_dedups(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_scope_dedup() => Pg(() => MemoryStoreContract.Different_scopes_are_not_deduped_together(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_ttl() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Ttl_entries_expire_from_recall_and_are_pruned(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_ttl_refresh() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Refreshing_a_fact_extends_its_ttl(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_ttl_unstated_replaces() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Re_remembering_without_a_ttl_replaces_an_explicit_one(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_recency_refresh() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Re_remembering_refreshes_recall_recency(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_prune_by_age() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Prune_older_than_removes_by_age_within_a_task(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_prune_scoped() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Prune_scoped_to_one_task_leaves_the_sibling(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_cap() => Pg(() => MemoryStoreContract.Cap_trims_to_the_newest_entries(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_separated_words() => Pg(() => MemoryStoreContract.Separated_words_recall_on_every_backend(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_match_count_ranking() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.More_matched_terms_outrank_a_newer_weaker_match(PgMemory(mc), Uid(), mc.Advance)); } // pg_trgm has no bm25
    [SkippableFact] public Task Memory_limit_scope() => Pg(() => MemoryStoreContract.Limit_caps_results_and_composes_with_scope(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_non_positive_limit() => Pg(() => MemoryStoreContract.A_non_positive_limit_recalls_nothing(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_forget() => Pg(() => MemoryStoreContract.Forget_clears_a_task(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_forget_scoped() => Pg(() => MemoryStoreContract.Forget_scoped_clears_only_that_scope(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_fail_open() => Pg(() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_lru() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Lru_evicts_least_recently_recalled(PgMemoryWith(MemoryEvictionPolicy.CountCap(3, MemoryEvictionMode.Lru), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_lru_bare() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency(PgMemoryWith(MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_default_ttl() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl(PgMemoryWith(MemoryEvictionPolicy.TimeToLive(TimeSpan.FromMinutes(5)), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_size_budget() => Pg(() => MemoryStoreContract.Size_budget_evicts_to_fit(PgMemoryWith(MemoryEvictionPolicy.SizeBudget(25), new MutableClock()), Uid()));

    [SkippableFact] public Task Memory_size_budget_runes() => Pg(() => MemoryStoreContract.Size_budget_counts_code_points_not_utf16_units(PgMemoryWith(MemoryEvictionPolicy.SizeBudget(2), new MutableClock()), Uid()));
    [SkippableFact] public Task Memory_both_bounds() => Pg(() => MemoryStoreContract.Both_count_cap_and_size_budget_apply(PgMemoryWith(new MemoryEvictionPolicy { MaxEntriesPerScope = 3, MaxCharsPerScope = 25 }, new MutableClock()), Uid()));
    [SkippableFact] public Task Memory_lru_tie() => Pg(() => MemoryStoreContract.Lru_recency_tie_broken_by_id(PgMemoryWith(MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru), new MutableClock()), Uid()));
    [SkippableFact] public Task Memory_manual() => Pg(() => MemoryStoreContract.Manual_policy_never_evicts(PgMemoryWith(MemoryEvictionPolicy.Manual, new MutableClock()), Uid()));

    /// <summary>Skip-guard wrapper so each contract delegator is a one-liner.</summary>
    private async Task Pg(Func<Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        await body();
    }

    private PostgresMemoryStore PgMemory(MutableClock? clock = null) =>
        new(pg.Factory, new LyntaiOptions { MemoryEviction = MemoryEvictionPolicy.CountCap(3), MemoryRecallLimit = 100 }, clock: (clock ?? new MutableClock()).Get);

    private PostgresMemoryStore PgMemoryWith(MemoryEvictionPolicy p, MutableClock clock) =>
        new(pg.Factory, new LyntaiOptions { MemoryEviction = p, MemoryRecallLimit = 100 }, clock: clock.Get);

    [SkippableFact]
    public async Task Score_round_trips_double_and_bool_exactly()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresScoreStore(pg.Factory);
        var s = Uid();

        await store.SaveAsync(s,
        [
            new ScoredResult("outcome", "Outcome", "deterministic", false, 0.123456789, "close"),
            new ScoredResult("judge", "Judge", "llm", true, 1.0, null),
        ]);

        var results = await store.GetAsync(s);
        Assert.Equal(2, results.Count);
        Assert.Equal(0.123456789, results[0].Score); // double precision, exact
        Assert.False(results[0].IsLlm);               // native boolean
        Assert.True(results[1].IsLlm);
        Assert.Equal(1.0, results[1].Score);
    }

    [SkippableFact]
    public async Task Curated_memory_crud_and_filters()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // the CRUD contract methods proper (no longer a hand-copied approximation that could drift) —
        // unique kinds isolate the shared container; the id-scoped methods need no namespacing
        await CuratedMemoryStoreContract.Add_get_list_round_trips(store, Uid() + "-k");
        await CuratedMemoryStoreContract.Update_changes_only_the_provided_fields(store);
        await CuratedMemoryStoreContract.Update_can_recategorise_kind_in_place(store, Uid() + "-from", Uid() + "-to");
        await CuratedMemoryStoreContract.List_filters_by_kind_and_enabled(store, Uid() + "-a", Uid() + "-b");
        await CuratedMemoryStoreContract.Remove_deletes(store);
        // These two ran on InMemory and SQLite only until PostgresContractCoverageTests said so. Both drive
        // the UPDATE path that moves a row's identity, which is exactly where a dialect can differ.
        await CuratedMemoryStoreContract.Update_can_rescope_task_and_scope_in_place(
            store, Uid() + "-rs-from", Uid() + "-rs-to");
        await CuratedMemoryStoreContract.Update_refuses_an_identity_collision(
            store, Uid() + "-collide", Uid() + "-k1", Uid() + "-k2");
    }

    [SkippableFact] public Task Curated_memory_non_positive_limit() => Pg(() =>
        CuratedMemoryStoreContract.A_non_positive_limit_returns_nothing(new PostgresCuratedMemoryStore(pg.Factory), Uid() + "-lim"));

    [SkippableFact]
    public async Task Curated_memory_task_scope_composition()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // Unique tasks so the shared container doesn't cross-contaminate the absolute membership asserts.
        await CuratedMemoryStoreContract.ForComposition_filters_by_task_and_scope(store, Uid() + "-tr", Uid() + "-meta");
    }

    [SkippableFact]
    public async Task Curated_memory_dedup_and_scope_filter()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // Unique task/scope so the shared container doesn't cross-contaminate the absolute-count asserts.
        await CuratedMemoryStoreContract.Dedup_add_is_idempotent(store, Uid() + "-dd", "site:" + Uid());
        await CuratedMemoryStoreContract.List_filters_by_scope(store, Uid() + "-sc");
        await CuratedMemoryStoreContract.Dedup_identity_is_case_sensitive(store, Uid() + "-dc");
        await CuratedMemoryStoreContract.Dedup_add_race_settles_to_a_stable_id(store, Uid() + "-dr");
    }

    [SkippableFact]
    public async Task Curated_memory_metadata()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // Unique kind/task so the shared container doesn't cross-contaminate.
        await CuratedMemoryStoreContract.Metadata_round_trips_updates_and_clears(store, Uid() + "-md");
        await CuratedMemoryStoreContract.Metadata_filter_matches_all_pairs(store, Uid() + "-mf");
    }

    [SkippableFact]
    public async Task Curated_memory_search()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // Unique tasks so the shared container doesn't cross-contaminate the absolute-membership asserts.
        await CuratedMemoryStoreContract.Search_matches_content_with_filters(store, Uid() + "-se");
        await CuratedMemoryStoreContract.Search_recalls_cjk_substrings(store, Uid() + "-cjk");
        await CuratedMemoryStoreContract.Search_matches_any_term_of_a_multi_word_query(store, Uid() + "-mw");
        await CuratedMemoryStoreContract.Search_matches_a_chinese_query_without_spaces(store, Uid() + "-zh");
    }

    // ---- durable jobs: every JobStoreContract fact, each under a UNIQUE lane on the shared container ------
    // PostgresContractCoverageTests fails if a fact is missing here.

    [SkippableFact] public Task Job_claim_flips_running() => JobPg(JobStoreContract.Claim_flips_to_running_and_increments_attempts);
    [SkippableFact] public Task Job_empty_lane_null() => JobPg(JobStoreContract.Empty_lane_claims_null);
    [SkippableFact] public Task Job_two_claims_distinct() => JobPg(JobStoreContract.Two_claims_never_return_the_same_job);
    [SkippableFact] public Task Job_complete_terminal() => JobPg(JobStoreContract.Complete_is_terminal);
    [SkippableFact] public Task Job_fail_retry_requeue() => JobPg(JobStoreContract.Fail_with_retry_requeues_available_later); // retry-requeue timestamp math on timestamptz
    [SkippableFact] public Task Job_fail_terminal() => JobPg(JobStoreContract.Fail_without_retry_is_terminal);
    [SkippableFact] public Task Job_checkpoint_renews_lease() => JobPg(JobStoreContract.Checkpoint_round_trips_and_renews_the_lease);
    [SkippableFact] public Task Job_stale_reclaim() => JobPg(JobStoreContract.Stale_lease_is_reclaimed_with_the_checkpoint);
    [SkippableFact] public Task Job_fenced_by_worker() => JobPg(JobStoreContract.Writes_are_fenced_by_worker_id);
    // the `attempts=attempts-1` decrement is SQL only this backend's dialect can prove correct here
    [SkippableFact] public Task Job_poll_does_not_spend_an_attempt() => JobPg(JobStoreContract.Poll_requeues_without_spending_an_attempt_and_is_fenced);
    [SkippableFact] public Task Job_cancel_pending_not_running() => JobPg(JobStoreContract.Cancel_takes_a_pending_job_but_not_a_running_one);
    [SkippableFact] public Task Job_enqueue_default_max_attempts() => JobPg(JobStoreContract.Enqueue_without_max_attempts_uses_the_shared_default);
    [SkippableFact] public Task Job_active_lanes_and_count() => JobPg(JobStoreContract.Active_lanes_and_running_count);
    [SkippableFact] public Task Job_priority_first() => JobPg(JobStoreContract.Higher_priority_is_claimed_first);
    [SkippableFact] public Task Job_dead_letter() => JobPg(JobStoreContract.Dead_letter_is_terminal_inspectable_and_fenced);
    [SkippableFact] public Task Job_replay_dead() => JobPg(JobStoreContract.Replay_requeues_a_dead_job);
    [SkippableFact] public Task Job_request_cancel() => JobPg(JobStoreContract.Request_cancel_flags_a_running_job_then_cancel_running_finalizes);
    [SkippableFact] public Task Job_tiebreak_by_id() => JobPg(JobStoreContract.Same_tick_same_priority_claims_in_id_order); // a TEXT id under the database collation
    [SkippableFact] public Task Job_pause_resume() => JobPg(JobStoreContract.Pause_holds_a_pending_job_out_of_claims_then_resume_restores_it);
    [SkippableFact] public Task Job_pause_pending_only() => JobPg(JobStoreContract.Pause_only_affects_a_pending_job);
    [SkippableFact] public Task Job_cancel_reaches_paused() => JobPg(JobStoreContract.Cancel_reaches_a_paused_job_without_resuming_it);
    [SkippableFact] public Task Job_progress_and_steps() => JobPg(JobStoreContract.Progress_and_steps_are_readable_while_running_and_fenced);
    [SkippableFact] public Task Job_concurrent_steps() => JobPg(JobStoreContract.Concurrent_step_reports_all_land);
    [SkippableFact] public Task Job_partition_serial_fifo() => JobPg(JobStoreContract.Same_partition_serializes_and_is_fifo);
    [SkippableFact] public Task Job_partitions_parallel() => JobPg(JobStoreContract.Different_partitions_run_in_parallel);
    [SkippableFact] public Task Job_partition_priority_ignored_within() => JobPg(JobStoreContract.Priority_is_ignored_within_a_partition_but_honored_across);
    [SkippableFact] public Task Job_partition_stale_reclaim_keeps_position() => JobPg(JobStoreContract.Stale_partition_running_is_reclaimed_before_later_pending);

    /// <summary>The one real concurrency test on this backend: 40 claimers over real async Npgsql
    /// connections, so <c>FOR UPDATE SKIP LOCKED</c> is what keeps two of them off one row.</summary>
    [SkippableFact]
    public async Task Job_skip_locked_never_double_claims_under_concurrency()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        const int n = 20;
        for (var i = 0; i < n; i++) await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));

        var claims = await Task.WhenAll(Enumerable.Range(0, n * 2)
            .Select(i => store.ClaimNextAsync(lane, $"w{i}", TimeSpan.FromMinutes(5))));

        var ids = claims.Where(j => j is not null).Select(j => j!.Id).ToList();
        Assert.Equal(n, ids.Count);              // FOR UPDATE SKIP LOCKED gave each job to exactly one
        Assert.Equal(n, ids.Distinct().Count());
    }

    /// <summary>Skip-guarded runner for a lane-scoped contract fact: the store over a fresh MutableClock,
    /// and a UNIQUE lane (Uid()) so it coexists with the other tests on the shared container.</summary>
    private async Task JobPg(Func<IJobStore, MutableClock, string, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        await body(new PostgresJobStore(pg.Factory, clock.Get), clock, Uid());
    }

    [SkippableFact] public Task Job_slots_cap_and_reuse() =>
        SlotPg(JobStoreContract.Slots_are_handed_out_up_to_the_cap_and_reused_after_release);
    [SkippableFact] public Task Job_slot_lease_reclaim() =>
        SlotPg(JobStoreContract.A_slot_past_its_lease_is_reclaimed_and_a_heartbeat_prevents_it);
    [SkippableFact] public Task Job_slot_release_fenced() =>
        SlotPg(JobStoreContract.Releasing_a_slot_is_fenced_by_worker_id);
    [SkippableFact] public Task Job_slot_heartbeat_spans_leases() =>
        SlotPg(JobStoreContract.A_heartbeating_holder_keeps_its_slot_across_many_leases);
    [SkippableFact] public Task Job_slot_heartbeat_no_revive() =>
        SlotPg(JobStoreContract.A_heartbeat_does_not_revive_a_slot_already_reclaimed);
    [SkippableFact] public Task Job_slot_cap_is_configuration() =>
        SlotPg(JobStoreContract.Lowering_the_cap_needs_no_cleanup);
    [SkippableFact] public Task Job_slot_cap_non_positive() =>
        SlotPg(JobStoreContract.A_non_positive_cap_hands_out_no_slot);

    /// <summary>The list-limit guard on the real Postgres, where an unguarded negative LIMIT THROWS —
    /// a different wrong answer from SQLite's (the whole table) and the in-process store's (empty).
    /// Self-isolating by lane, so it needs no table clear.</summary>
    [SkippableFact] public Task Job_list_limit_non_positive() =>
        JobPg((store, clock, _) => JobStoreContract.A_non_positive_list_limit_returns_nothing(store, clock));

    /// <summary>Runner for a SLOT contract fact, which cannot use <see cref="JobPg"/>'s unique-lane trick:
    /// slot state is TABLE-WIDE by design — a cross-process cap that a lane could partition would not be
    /// one — so the table is cleared first instead. Safe because every Postgres suite shares the
    /// <c>postgres</c> collection and therefore runs serially.</summary>
    private async Task SlotPg(Func<IJobStore, MutableClock, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        using (var conn = pg.Factory.Open())
            await Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM lyntai_job_slot");
        var clock = new MutableClock();
        await body(new PostgresJobStore(pg.Factory, clock.Get), clock);
    }
}
