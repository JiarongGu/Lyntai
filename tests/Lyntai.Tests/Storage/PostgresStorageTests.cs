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
/// The whole class skips (early-return) when Docker is unavailable — see <see cref="PostgresFixture"/>.</summary>
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
    /// Postgres. One test running all of them, like the other Postgres suites, so container startup is
    /// paid once; each fact is namespaced by a fresh Uid because the container is shared.</summary>
    [SkippableFact]
    public async Task Graph_store_satisfies_the_contract()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        // a controllable clock, ONLY for Elapsed_age_advances_by_real_time_between_writes below — every
        // other fact here uses created_at solely for audit purposes and does not depend on wall-clock time
        var clock = new MutableClock();
        var store = new PostgresMemoryGraphStore(pg.Factory, clock: clock.Get);
        var key = Uid();
        var contract = typeof(Lyntai.Tests.Memory.MemoryGraphStoreContract);

        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(store, key + "a");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_matches_any_term_of_a_multi_word_query(store, key + "a1");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_matches_a_chinese_query_without_spaces(store, key + "a2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_matches_a_two_character_chinese_word(store, key + "a3");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Subjects_round_trip_and_are_scoped_to_their_task(store, key + "a4");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Recording_subjects_replaces_the_previous_set(store, key + "a5");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Subjects_match_case_insensitively(store, key + "a6");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Deleting_a_node_takes_its_subjects_with_it(store, key + "a7");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(store, key + "b");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Engines_are_isolated_from_one_another(store, key + "c");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_busy_engine_does_not_age_a_quiet_ones_memories(store, key + "c2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_never_excludes_a_faint_entry(store, key + "e");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_bigger_write_ages_more(store, key + "e2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Touch_records_reinforcement(store, key + "f");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_touch_does_not_advance_the_position(store, key + "f2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(store, key + "g");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(store, key + "h");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Degree_counts_connections(store, key + "i");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(store, key + "j");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Forget_clears_a_scope(store, key + "k");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(store, key + "l");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Cancellation_propagates(store, key + "m");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.An_edge_ages_as_the_memory_moves_on(store, key + "n");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(store, key + "o");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(store, key + "p");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_node_reports_its_connection_freshness_on_every_age_scale(store, key + "o2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.An_admitted_but_non_matching_exact_fact_reports_zero_relevance(store, key + "o3");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.An_admitted_but_non_matching_exact_fact_reports_zero_on_the_short_query_path(store, key + "o4");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_neighbour_reports_its_edge_age_on_every_scale(store, key + "n2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.An_unconnected_node_reports_no_connection_freshness(store, key + "p2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_admits_authoritative_material_the_query_does_not_match(store, key + "q");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_admits_a_long_quiet_exact_fact_over_fresher_material(store, key + "r");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_cannot_admit_more_exact_facts_than_the_limit(store, key + "s");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Signals_round_trip_through_the_store(store, key + "t");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_node_with_no_signals_reads_back_empty(store, key + "u");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_admits_a_long_quiet_salient_entry_over_fresher_material(store, key + "v");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_bag(store, key + "w");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_existing_bag(store, key + "x");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_treats_a_non_finite_salience_as_the_neutral_value(store, key + "y");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_treats_a_below_neutral_salience_as_the_neutral_value(store, key + "z");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Ordinal_age_counts_writes_since_last_use(store, key + "aa");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Volume_age_counts_characters_written_since_last_use(store, key + "ab");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_quiet_engine_does_not_age_on_any_of_the_three_primitives(store, key + "ac");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Touch_resets_all_three_primitives_together_with_the_legacy_position(store, key + "ad");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_touch_does_not_advance_any_of_the_three_primitives(store, key + "ae");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Ordinals_are_monotone_but_not_dense_after_a_prune(store, key + "af");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Elapsed_age_advances_by_real_time_between_writes(store, key + "ag", clock.Advance);
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.DeleteAsync_removes_exactly_the_given_ids(store, key + "ah");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.DeleteAsync_takes_edges_with_the_nodes(store, key + "ai");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.DeleteAsync_is_scoped_to_the_named_engine(store, key + "aj");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.DeleteAsync_with_no_ids_removes_nothing(store, key + "ak");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Provenance_round_trips_through_the_store(store, key + "al");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_node_with_no_provenance_reads_back_as_none(store, key + "am");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_plain_re_remember_does_not_touch_retrievability_provenance(store, key + "an");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_salience_provenance(store, key + "ao");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_salience_provenance(store, key + "ap");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Touch_updates_the_retrievability_provenance(store, key + "aq");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Difficulty_is_seeded_from_the_signal_at_first_write(store, key + "ar");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.A_node_with_no_difficulty_signal_reads_back_neutral(store, key + "as");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_difficulty(store, key + "at");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_difficulty(store, key + "au");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Touch_updates_difficulty(store, key + "av");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Seeding_treats_a_non_finite_or_out_of_range_difficulty_signal_as_coerced(store, key + "aw");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Re_remembering_with_an_unrelated_signal_does_not_touch_the_tracked_difficulty(store, key + "ax");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Reviews_round_trip_through_the_store(store, key + "ay");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.Reviews_round_trip_the_verified_tri_state(store, key + "ay2");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.RecordReviewsAsync_with_no_reviews_is_a_no_op(store, key + "az");
        await Lyntai.Tests.Memory.MemoryGraphStoreContract.RecordReviewsAsync_evicts_down_to_the_cap(store, key + "ba");

        // no silent skips: if a fact is added to the contract and not called here, this fails
        var covered = 68;
        var declared = contract.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length;
        Assert.Equal(declared, covered);
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
        var applied = await Dapper.SqlMapper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM lyntai_version_info");
        Assert.Equal(11L, applied); // 9 baseline (1.0 squash) + MemoryGraph (2.5.0) + MemoryRetentionModel (3.0 squash)
    }

    private static async Task<bool> TableExists(IDbConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = @table)",
            new { table });
    }

    // ---- cross-backend contracts, run against Postgres over the shared container ----------------------
    // Each is namespaced by a unique key (Uid()) so it coexists with the other tests on the one shared,
    // migrated database. Table-wide contract methods (ScoreStoreContract Aggregate/Export;
    // CuratedMemoryStoreContract / JobStoreContract full suites) are NOT routed here — they read across the
    // whole table and would see other tests' rows on the shared container, so they stay InMemory+SQLite
    // (see those backends' *ContractTests). The session/task/id-scoped contract methods are safe here.

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
    [SkippableFact] public Task Conversation_aliases() => Pg(() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_cascade() => Pg(() => ConversationStoreContract.Delete_thread_cascades_to_messages(new PostgresConversationStore(pg.Factory), Uid())); // FK cascade
    [SkippableFact] public Task Conversation_list_newest_first() => Pg(() => ConversationStoreContract.List_threads_returns_newest_first(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_count() => Pg(() => ConversationStoreContract.Count_reflects_inserted_and_deleted_threads(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_paged() => Pg(() => ConversationStoreContract.Paged_cursor_walks_every_thread_exactly_once(new PostgresConversationStore(pg.Factory), Uid()));

    [SkippableFact] public Task Trace_save_load() => Pg(() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_resave_replaces() => Pg(() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_unknown() => Pg(() => TraceStoreContract.Unknown_session_returns_null(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_seq_offset() => Pg(() => TraceStoreContract.Step_sequence_and_offset_round_trip(new PostgresTraceStore(pg.Factory), Uid()));

    [SkippableFact] public Task PromptVersion_none() => Pg(() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_monotonic() => Pg(() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_history() => Pg(() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback() => Pg(() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback_missing() => Pg(() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_isolation() => Pg(() => PromptVersionStoreContract.Names_are_isolated(new PostgresPromptVersionStore(pg.Factory), Uid()));

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
    [SkippableFact] public Task Memory_recency_refresh() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Re_remembering_refreshes_recall_recency(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_prune_by_age() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Prune_older_than_removes_by_age_within_a_task(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_prune_scoped() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Prune_scoped_to_one_task_leaves_the_sibling(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_cap() => Pg(() => MemoryStoreContract.Cap_trims_to_the_newest_entries(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_limit_scope() => Pg(() => MemoryStoreContract.Limit_caps_results_and_composes_with_scope(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_forget() => Pg(() => MemoryStoreContract.Forget_clears_a_task(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_forget_scoped() => Pg(() => MemoryStoreContract.Forget_scoped_clears_only_that_scope(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_fail_open() => Pg(() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_lru() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Lru_evicts_least_recently_recalled(PgMemoryWith(MemoryEvictionPolicy.CountCap(3, MemoryEvictionMode.Lru), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_lru_bare() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency(PgMemoryWith(MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_default_ttl() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl(PgMemoryWith(MemoryEvictionPolicy.TimeToLive(TimeSpan.FromMinutes(5)), mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_size_budget() => Pg(() => MemoryStoreContract.Size_budget_evicts_to_fit(PgMemoryWith(MemoryEvictionPolicy.SizeBudget(25), new MutableClock()), Uid()));

    /// <summary><b>A row matching MORE of the query outranks one matching less, even when the weaker match is
    /// newer.</b> Postgres-specific because this backend is where the claim lives: it has no <c>bm25</c>, so
    /// 3.0 gave its substring path an <c>ORDER BY</c> led by the COUNT of matched terms
    /// (<c>docs/DECISIONS.md</c> D55). That ordering is the entire bound on the pollution the change bought —
    /// term-wise matching finds strictly more than a contiguous substring did, and without a rank among the
    /// extra hits a one-term brush-past displaces a near-exact hit purely by being newer.
    /// <para>Written so RECENCY POINTS THE WRONG WAY: the two-term entry is written FIRST, so a recency-only
    /// order returns the one-term entry first and this test fails. Deleting the count expression from the
    /// ORDER BY is exactly that mutation.</para></summary>
    [SkippableFact]
    public async Task Memory_recall_ranks_by_how_many_query_terms_matched()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = PgMemory();
        var key = Uid();

        await store.RememberAsync(key, "s", "the deploy pipeline requires manual approval");  // both terms
        await store.RememberAsync(key, "s", "the pipeline is unrelated to this");             // one term, NEWER

        var hits = await store.RecallAsync(key, "s", "deploy pipeline");

        Assert.Equal(2, hits.Count);   // term-wise matching finds both — that is the 3.0 behaviour
        Assert.Contains("manual approval", hits[0].Content, StringComparison.Ordinal);
    }
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
        new(pg.Factory, new LyntaiOptions { MemoryCapPerScope = 3, MemoryRecallLimit = 100 }, clock: (clock ?? new MutableClock()).Get);

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

    // ---- durable jobs (each test uses a UNIQUE lane so they don't collide on the shared db) -----------
    // JobStoreContract is table-wide (fixed "default" lane + ActiveLanesAsync / ListAsync(status)) so it is
    // NOT routed against the shared container — the InMemory + SQLite JobStore*Tests run the full contract.
    // These ad-hoc tests use a UNIQUE Uid() lane, covering the Postgres-specific SKIP-LOCKED claim path.

    [SkippableFact]
    public async Task Job_claim_checkpoint_complete_lifecycle()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", """{"x":1}"""));

        var job = await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(5));
        Assert.Equal(id, job!.Id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);

        Assert.True(await store.SaveCheckpointAsync(id, "w1", """{"step":2}"""));
        Assert.Equal("""{"step":2}""", (await store.GetAsync(id))!.Checkpoint);
        Assert.False(await store.CompleteAsync(id, "intruder")); // fenced
        Assert.True(await store.CompleteAsync(id, "w1"));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
    }

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

    [SkippableFact]
    public async Task Job_stale_lease_is_reclaimed()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        var store = new PostgresJobStore(pg.Factory, clock.Get);
        var lane = Uid();
        var lease = TimeSpan.FromMinutes(1);
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", lease);

        clock.Advance(lease + TimeSpan.FromSeconds(1)); // w1 presumed dead
        var reclaimed = await store.ClaimNextAsync(lane, "w2", lease);

        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal("w2", reclaimed.ClaimedBy);
        Assert.Equal(2, reclaimed.Attempts);
    }

    [SkippableFact]
    public async Task Job_higher_priority_is_claimed_first()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 1));
        var hi = await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 5));

        var claimed = await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));
        Assert.Equal(hi, claimed!.Id);
        Assert.Equal(5, claimed.Priority);
    }

    [SkippableFact]
    public async Task Job_dead_letters_and_replays()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.DeadLetterAsync(id, "w1", "exhausted"));
        Assert.Equal(JobStatus.Dead, (await store.GetAsync(id))!.Status);
        Assert.Contains(await store.ListAsync(JobStatus.Dead, lane), j => j.Id == id);

        Assert.True(await store.ReplayAsync(id));
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Pending, job!.Status);
        Assert.Equal(0, job.Attempts);
    }

    [SkippableFact]
    public async Task Job_request_cancel_flags_running_then_cancel_running_finalizes()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.RequestCancelAsync(id));
        Assert.True((await store.GetAsync(id))!.CancelRequested);
        Assert.False(await store.CancelRunningAsync(id, "intruder")); // fenced
        Assert.True(await store.CancelRunningAsync(id, "w1"));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    [SkippableFact]
    public async Task Job_pause_holds_out_of_claims_then_resume_restores()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));

        Assert.True(await store.PauseAsync(id));                              // Pending → Paused
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1))); // not claimable

        Assert.True(await store.ResumeAsync(id));                            // Paused → Pending
        Assert.Equal(id, (await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1)))!.Id);
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
    }

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

    [SkippableFact]
    public async Task Job_progress_and_steps_are_readable_while_running()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.ReportProgressAsync(id, "w1", 3, 10, "phase-1"));
        Assert.True(await store.ReportStepAsync(id, "w1", "started"));
        Assert.True(await store.ReportStepAsync(id, "w1", "halfway"));

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Running, job!.Status);
        Assert.Equal(3, job.Progress);
        Assert.Equal(10, job.Total);
        Assert.Equal("phase-1", job.Stage);
        Assert.Equal(["started", "halfway"], JobStepLog.Parse(job.StepLog).Select(s => s.Message));

        Assert.False(await store.ReportProgressAsync(id, "intruder", 9, 10, "x")); // fenced
    }

    [SkippableFact]
    public async Task Job_concurrent_step_reports_all_land()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        const int n = 25; // concurrent reports must not clobber each other (the read-modify-write race)
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => store.ReportStepAsync(id, "w1", $"step-{i}")));

        var messages = JobStepLog.Parse((await store.GetAsync(id))!.StepLog).Select(s => s.Message).ToList();
        Assert.Equal(n, messages.Count);
    }

    // ---- partition keys (actor-mailbox) — run the shared JobStoreContract methods against Postgres over
    // the shared container, each namespaced to a UNIQUE lane (Uid()) so the FIFO/one-at-a-time guard is
    // exercised on the SKIP-LOCKED claim path in isolation from the other tests' rows.

    [SkippableFact] public Task Job_fail_retry_requeue() => JobPg(JobStoreContract.Fail_with_retry_requeues_available_later); // retry-requeue timestamp math on timestamptz
    [SkippableFact] public Task Job_partition_serial_fifo() => JobPg(JobStoreContract.Same_partition_serializes_and_is_fifo);
    [SkippableFact] public Task Job_partitions_parallel() => JobPg(JobStoreContract.Different_partitions_run_in_parallel);
    [SkippableFact] public Task Job_partition_priority_ignored_within() => JobPg(JobStoreContract.Priority_is_ignored_within_a_partition_but_honored_across);
    [SkippableFact] public Task Job_partition_stale_reclaim_keeps_position() => JobPg(JobStoreContract.Stale_partition_running_is_reclaimed_before_later_pending);

    /// <summary>Skip-guarded runner for a partition contract method — builds the store over a shared
    /// MutableClock (the FIFO scenarios advance it between enqueues, the reclaim scenario advances past the
    /// lease) and passes a UNIQUE lane (Uid()) so it coexists with the other tests on the shared container.</summary>
    private async Task JobPg(Func<IJobStore, MutableClock, string, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        await body(new PostgresJobStore(pg.Factory, clock.Get), clock, Uid());
    }
}
