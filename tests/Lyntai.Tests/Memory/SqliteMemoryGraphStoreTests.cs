using Dapper;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against SQLite over a per-test temp db, plus
/// the two SQLite-specific ones the other backends cannot have: CJK substring recall through the trigram
/// index, and FTS staying in sync after a delete.</summary>
public class SqliteMemoryGraphStoreTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private IMemoryGraphStore New() => new SqliteMemoryGraphStore(_db.Factory);

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Seed_multi_word() => MemoryGraphStoreContract.Seeding_matches_any_term_of_a_multi_word_query(New(), "k1a");
    [Fact] public Task Seed_chinese() => MemoryGraphStoreContract.Seeding_matches_a_chinese_query_without_spaces(New(), "k1b");
    [Fact] public Task Seed_chinese_bigram() => MemoryGraphStoreContract.Seeding_matches_a_two_character_chinese_word(New(), "k1c");
    [Fact] public Task Subjects_round_trip() => MemoryGraphStoreContract.Subjects_round_trip_and_are_scoped_to_their_task(New(), "k1d");
    [Fact] public Task Subjects_replace() => MemoryGraphStoreContract.Recording_subjects_replaces_the_previous_set(New(), "k1e");
    [Fact] public Task Subjects_case() => MemoryGraphStoreContract.Subjects_match_case_insensitively(New(), "k1f");
    [Fact] public Task Subjects_deleted_with_node() => MemoryGraphStoreContract.Deleting_a_node_takes_its_subjects_with_it(New(), "k1g");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Quiet_engine() => MemoryGraphStoreContract.A_busy_engine_does_not_age_a_quiet_ones_memories(New(), "k4");
    [Fact] public Task Never_excludes_faint() => MemoryGraphStoreContract.Seeding_never_excludes_a_faint_entry(New(), "k7");
    [Fact] public Task Admits_exact_on_query_path() => MemoryGraphStoreContract.Seeding_admits_authoritative_material_the_query_does_not_match(New(), "k21");
    [Fact] public Task Exact_survives_the_limit() => MemoryGraphStoreContract.Seeding_admits_a_long_quiet_exact_fact_over_fresher_material(New(), "k22");
    [Fact] public Task Exact_facts_have_a_bound_too() => MemoryGraphStoreContract.Seeding_cannot_admit_more_exact_facts_than_the_limit(New(), "k23");
    [Fact] public Task Bigger_ages_more() => MemoryGraphStoreContract.A_bigger_write_ages_more(New(), "k8");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k9");
    [Fact] public Task Touch_does_not_age() => MemoryGraphStoreContract.A_touch_does_not_advance_the_position(New(), "k10");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k11");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k12");
    [Fact] public Task Edge_ages() => MemoryGraphStoreContract.An_edge_ages_as_the_memory_moves_on(New(), "k13");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k14");
    [Fact] public Task Strength() => MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(New(), "k15");
    [Fact] public Task No_strength() => MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(New(), "k16");
    [Fact] public Task Exact_zero_relevance() => MemoryGraphStoreContract.An_admitted_but_non_matching_exact_fact_reports_zero_relevance(New(), "k15c");
    [Fact] public Task Exact_zero_relevance_short() => MemoryGraphStoreContract.An_admitted_but_non_matching_exact_fact_reports_zero_on_the_short_query_path(New(), "k15d");
    [Fact] public Task Strength_scales() => MemoryGraphStoreContract.A_node_reports_its_connection_freshness_on_every_age_scale(New(), "k15b");
    [Fact] public Task No_strength_scales() => MemoryGraphStoreContract.An_unconnected_node_reports_no_connection_freshness(New(), "k16b");
    [Fact] public Task Edge_age_scales() => MemoryGraphStoreContract.A_neighbour_reports_its_edge_age_on_every_scale(New(), "k13b");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k17");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k18");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k19");
    [Fact] public Task Cancellation() => MemoryGraphStoreContract.Cancellation_propagates(New(), "k20");
    [Fact] public Task Signals_round_trip() => MemoryGraphStoreContract.Signals_round_trip_through_the_store(New(), "k24");
    [Fact] public Task No_signals_reads_back_empty() => MemoryGraphStoreContract.A_node_with_no_signals_reads_back_empty(New(), "k25");
    [Fact] public Task Salient_entry_survives_the_limit() => MemoryGraphStoreContract.Seeding_admits_a_long_quiet_salient_entry_over_fresher_material(New(), "k26");
    [Fact] public Task Re_remember_with_no_signals_keeps_existing() => MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_bag(New(), "k27");
    [Fact] public Task Re_remember_with_fresh_signals_replaces() => MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_existing_bag(New(), "k28");
    [Fact] public Task Non_finite_salience_is_neutral() => MemoryGraphStoreContract.Seeding_treats_a_non_finite_salience_as_the_neutral_value(New(), "k29");
    [Fact] public Task Below_neutral_salience_is_neutral() => MemoryGraphStoreContract.Seeding_treats_a_below_neutral_salience_as_the_neutral_value(New(), "k30");
    [Fact] public Task Ordinal_age() => MemoryGraphStoreContract.Ordinal_age_counts_writes_since_last_use(New(), "k31");
    [Fact] public Task Volume_age() => MemoryGraphStoreContract.Volume_age_counts_characters_written_since_last_use(New(), "k32");
    [Fact] public Task Quiet_engine_primitives() => MemoryGraphStoreContract.A_quiet_engine_does_not_age_on_any_of_the_three_primitives(New(), "k33");
    [Fact] public Task Touch_resets_primitives() => MemoryGraphStoreContract.Touch_resets_all_three_primitives_together_with_the_legacy_position(New(), "k34");
    [Fact] public Task Touch_does_not_age_primitives() => MemoryGraphStoreContract.A_touch_does_not_advance_any_of_the_three_primitives(New(), "k35");
    [Fact] public Task Ordinals_monotone_not_dense() => MemoryGraphStoreContract.Ordinals_are_monotone_but_not_dense_after_a_prune(New(), "k36");
    [Fact] public Task Delete_removes_exactly_the_given_ids() => MemoryGraphStoreContract.DeleteAsync_removes_exactly_the_given_ids(New(), "k38");
    [Fact] public Task Delete_takes_edges_with_the_nodes() => MemoryGraphStoreContract.DeleteAsync_takes_edges_with_the_nodes(New(), "k39");
    [Fact] public Task Delete_is_scoped_to_the_named_engine() => MemoryGraphStoreContract.DeleteAsync_is_scoped_to_the_named_engine(New(), "k40");
    [Fact] public Task Delete_with_no_ids_removes_nothing() => MemoryGraphStoreContract.DeleteAsync_with_no_ids_removes_nothing(New(), "k41");
    [Fact] public Task Provenance_round_trips() => MemoryGraphStoreContract.Provenance_round_trips_through_the_store(New(), "k42");
    [Fact] public Task No_provenance_reads_back_as_none() => MemoryGraphStoreContract.A_node_with_no_provenance_reads_back_as_none(New(), "k43");
    [Fact] public Task Re_remember_does_not_touch_retrievability_provenance() => MemoryGraphStoreContract.A_plain_re_remember_does_not_touch_retrievability_provenance(New(), "k44");
    [Fact] public Task Re_remember_with_no_signals_keeps_existing_salience_provenance() => MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_salience_provenance(New(), "k45");
    [Fact] public Task Re_remember_with_fresh_signals_replaces_salience_provenance() => MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_salience_provenance(New(), "k46");
    [Fact] public Task Touch_updates_retrievability_provenance() => MemoryGraphStoreContract.Touch_updates_the_retrievability_provenance(New(), "k47");
    [Fact] public Task Difficulty_seeded_from_signal() => MemoryGraphStoreContract.Difficulty_is_seeded_from_the_signal_at_first_write(New(), "k48");
    [Fact] public Task No_difficulty_signal_reads_back_neutral() => MemoryGraphStoreContract.A_node_with_no_difficulty_signal_reads_back_neutral(New(), "k49");
    [Fact] public Task Re_remember_with_no_signals_keeps_existing_difficulty() => MemoryGraphStoreContract.Re_remembering_with_no_signals_keeps_the_existing_difficulty(New(), "k50");
    [Fact] public Task Re_remember_with_fresh_signals_replaces_difficulty() => MemoryGraphStoreContract.Re_remembering_with_fresh_signals_replaces_the_difficulty(New(), "k51");
    [Fact] public Task Touch_updates_difficulty() => MemoryGraphStoreContract.Touch_updates_difficulty(New(), "k52");
    [Fact] public Task Non_finite_or_out_of_range_difficulty_is_coerced() => MemoryGraphStoreContract.Seeding_treats_a_non_finite_or_out_of_range_difficulty_signal_as_coerced(New(), "k53");
    [Fact] public Task Re_remember_with_unrelated_signal_does_not_touch_difficulty() => MemoryGraphStoreContract.Re_remembering_with_an_unrelated_signal_does_not_touch_the_tracked_difficulty(New(), "k54");
    [Fact] public Task Reviews_round_trip() => MemoryGraphStoreContract.Reviews_round_trip_through_the_store(New(), "k55");
    // SQLite has no BOOLEAN: this one exercises the hand-written 1/0/NULL mapping at the call site, which
    // is where a tri-state silently collapses to two.
    [Fact] public Task Reviews_verified_tri_state() => MemoryGraphStoreContract.Reviews_round_trip_the_verified_tri_state(New(), "k55v");
    [Fact] public Task Record_reviews_with_none_is_a_no_op() => MemoryGraphStoreContract.RecordReviewsAsync_with_no_reviews_is_a_no_op(New(), "k56");
    [Fact] public Task Review_log_evicts_down_to_the_cap() => MemoryGraphStoreContract.RecordReviewsAsync_evicts_down_to_the_cap(New(), "k57");

    [Fact]
    public Task Elapsed_age()
    {
        var clock = new MutableClock();
        return MemoryGraphStoreContract.Elapsed_age_advances_by_real_time_between_writes(
            new SqliteMemoryGraphStore(_db.Factory, clock: clock.Get), "k37", clock.Advance);
    }

    /// <summary>SQLite-specific because it pins the INDEX choice: the trigram tokenizer gives indexed CJK
    /// substring recall, which unicode61 would silently return nothing for. (The portable half — that a CJK
    /// query matches on every backend at all — is
    /// <c>MemoryGraphStoreContract.Seeding_matches_a_chinese_query_without_spaces</c>, added in 3.0 with
    /// <see cref="Lyntai.Storage.SearchTerms"/>.)</summary>
    [Fact]
    public async Task Cjk_substring_recall()
    {
        var store = New();
        await store.UpsertAsync(Write("灵台平台负责智能代理的记忆存储"));
        await store.UpsertAsync(Write("另一条无关的记录"));

        var hits = await store.SeedAsync("e", "cjk", "s", "智能代理", 10);

        Assert.Single(hits);

        static GraphNodeWrite Write(string text) =>
            new("e", "cjk", "s", text, text, MemoryGrade.Associative, 7, 1, null);
    }

    /// <summary>The FTS index must stop matching text that was deleted. Missing the <c>'delete'</c> command
    /// row is the single most botched thing in this repository's storage layer, and it is silent — stale
    /// rows keep matching forever.</summary>
    [Fact]
    public async Task Fts_stays_in_sync_after_a_delete()
    {
        var store = New();
        await store.UpsertAsync(new GraphNodeWrite("e", "sync", "s", "distinctive phrase here",
            "distinctive phrase here", MemoryGrade.Associative, 7, 1, null));

        await store.ForgetAsync("e", "sync", "s");

        Assert.Empty(await store.SeedAsync("e", "sync", "s", "distinctive", 10));
    }

    /// <summary>SQLite-specific, because only this backend merges two independent queries. The FTS branch is
    /// itself LIMIT-bound, so exact facts need RESERVED capacity rather than an append: appending them and
    /// truncating the tail returns exactly the matches, silently re-dropping every exact fact — the defect
    /// the merge exists to prevent.
    /// <para>Pins the relevance half too. <c>QueryAsync</c> normalizes relevance per QUERY, so the two
    /// batches each arrive with their own 1.0-topped gradient and a spliced result would report a fact that
    /// matched NOTHING as the best hit for the query — and rank is Relevance × Retrievability, with an exact
    /// fact's retrievability already 1.</para></summary>
    [Fact]
    public async Task Exact_facts_survive_a_full_page_of_matches()
    {
        var store = New();
        await store.UpsertAsync(Write("the user is vegetarian", MemoryGrade.Authoritative));
        for (var i = 0; i < 12; i++)
            await store.UpsertAsync(Write($"restaurant booking {i} needs confirming"));

        var hits = await store.SeedAsync("e", "merge", "s", "restaurant", 10);

        Assert.Equal(10, hits.Count);
        var exact = hits.Single(h => h.Content == "the user is vegetarian");
        Assert.True(exact.Relevance < 1,
            $"an exact fact matching nothing reported Relevance {exact.Relevance}");

        static GraphNodeWrite Write(string text, MemoryGrade grade = MemoryGrade.Associative) =>
            new("e", "merge", "s", text, text, grade, 7, 1, null);
    }

    /// <summary>The other half of the merge, and the case the test above structurally cannot see: its exact
    /// fact matches NOTHING, so sending that one to the tail is correct. An exact fact that IS the query's
    /// best match must keep the position bm25 earned it — only exact facts the query did not match are
    /// tail-ordered.
    /// <para>Why this is not cosmetic. <c>GraphMemoryEngine.RecallAsync</c> ranks by
    /// <c>Relevance × Retrievability × HopAttenuation^Hop</c> and then takes the top <c>limit</c>, and an
    /// authoritative node's retrievability is already 1 — so handing the fact that DIRECTLY ANSWERS the query
    /// the bottom of the renormalized gradient can drop it out of recall entirely, which is strictly worse
    /// than not merging at all.</para></summary>
    [Fact]
    public async Task An_exact_fact_that_matches_the_query_keeps_its_earned_position()
    {
        var store = New();
        await store.UpsertAsync(Write("the user is vegetarian", MemoryGrade.Authoritative));
        // longer notes that also match, so the exact fact is the strongest bm25 hit rather than the only one
        for (var i = 0; i < 12; i++)
            await store.UpsertAsync(Write(
                $"booking {i} should mention the vegetarian option once the party size is confirmed"));

        var hits = await store.SeedAsync("e", "earned", "s", "vegetarian", 10);

        Assert.Equal(10, hits.Count);
        var exact = hits.Single(h => h.Content == "the user is vegetarian");
        Assert.True(exact.Relevance > hits.Min(h => h.Relevance),
            $"an exact fact that IS the query's match was demoted to the tail (Relevance {exact.Relevance})");

        static GraphNodeWrite Write(string text, MemoryGrade grade = MemoryGrade.Associative) =>
            new("e", "earned", "s", text, text, grade, 7, 1, null);
    }

    /// <summary>Grade priority on the LIKE branch, which every other fact here misses: they all take the FTS
    /// or the no-query branch. <see cref="Lyntai.Storage.FtsQuery.Build"/> returns null only when every token
    /// is ≤2 characters, so a SHORT query is what reaches the fallback — and the fallback carries its own
    /// <c>ORDER BY</c>, which the grade-first fix had to change separately.
    /// <para>The exact fact here is written FIRST and never touched again, so it holds the lowest
    /// <c>last_recalled_position</c> in the scope: under a recency-only ordering the limit cuts it before the
    /// engine ranks anything.</para></summary>
    [Fact]
    public async Task The_LIKE_fallback_seeds_exact_facts_ahead_of_fresher_matches()
    {
        var store = New();
        await store.UpsertAsync(Write("the user's spouse is Alice", MemoryGrade.Authoritative));
        for (var i = 0; i < 12; i++)
            await store.UpsertAsync(Write($"booking {i} is confirmed")); // each contains "ok", none is exact

        // every token is ≤2 chars, so FtsQuery.Build returns null and this lands on the LIKE fallback
        var hits = await store.SeedAsync("e", "like", "s", "ok", 5);

        Assert.Equal(5, hits.Count);
        Assert.Equal("the user's spouse is Alice", hits[0].Content);

        static GraphNodeWrite Write(string text, MemoryGrade grade = MemoryGrade.Associative) =>
            new("e", "like", "s", text, text, grade, 7, 1, null);
    }

    /// <summary>SQLite-specific because only this backend runs the exact-facts sub-query the FTS-merge path
    /// needs. That sub-query is itself <c>LIMIT</c>-bound BEFORE <c>Merge</c> ever runs, so a salient-but-quiet
    /// exact fact can be squeezed out by the sub-query's own limit before capacity is ever reserved for it —
    /// the same shape as <see cref="MemoryGraphStoreContract.Seeding_admits_a_long_quiet_salient_entry_over_fresher_material"/>,
    /// but pinning the FTS-merge path's sub-query specifically, which the shared contract fact cannot reach
    /// (it never drives a query that matches something).</summary>
    [Fact]
    public async Task The_exact_facts_subquery_orders_by_salience()
    {
        var store = New();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 10);
        await store.UpsertAsync(new GraphNodeWrite("e", "ftssal", "s", "the salient exact fact",
            "the salient exact fact", MemoryGrade.Authoritative, 7, 1, null, signals));
        // 7 MORE authoritative facts, written AFTER the salient one — without salience ordering, the
        // sub-query's own recency-first LIMIT 5 keeps the 5 newest of these 8 and drops the salient-but-oldest one
        for (var i = 0; i < 7; i++)
            await store.UpsertAsync(Write($"an exact fact numbered {i}", MemoryGrade.Authoritative));
        await store.UpsertAsync(Write("the meeting notes need filing"));
        await store.UpsertAsync(Write("the meeting ran long"));

        // a query only the associative notes match, so the exact facts arrive through the sub-query
        var hits = await store.SeedAsync("e", "ftssal", "s", "meeting", 5);

        Assert.Contains(hits, h => h.Content == "the salient exact fact");

        static GraphNodeWrite Write(string text, MemoryGrade grade = MemoryGrade.Associative) =>
            new("e", "ftssal", "s", text, text, grade, 7, 1, null);
    }

    /// <summary>The <c>NOT NULL DEFAULT 1</c> decision, pinned as ACTUAL INSERT BEHAVIOUR rather than only
    /// as DDL text (<c>MigrationSchemaSnapshotTests</c> already guards the text). Bypasses the store to
    /// write a row that OMITS <c>salience</c> entirely — exactly what every row that predates this migration
    /// becomes the moment <c>ALTER TABLE ADD COLUMN salience REAL NOT NULL DEFAULT 1</c> runs — and confirms
    /// the column default gives it the neutral value rather than something that would let it silently
    /// outrank a judged row (a NULLable column sorts FIRST on Postgres' <c>ORDER BY salience DESC</c>).</summary>
    [Fact]
    public async Task A_row_that_omits_salience_gets_the_neutral_column_default()
    {
        using var db = new TempDb();

        using (var conn = db.Factory.Open())
        {
            await conn.ExecuteAsync("""
                INSERT INTO lyntai_memory_node
                    (engine, task_key, scope, headline, content, content_hash, grade,
                     created_at, last_recalled_position, recall_count, stability)
                VALUES ('e', 'legacy', 's', 'a pre-signals row', 'a pre-signals row', 'hash-legacy', 1,
                        '2020-01-01T00:00:00Z', 0, 0, 7)
                """);

            var salience = await conn.ExecuteScalarAsync<double>(
                "SELECT salience FROM lyntai_memory_node WHERE task_key = 'legacy'");
            Assert.Equal(1, salience); // the DEFAULT, not NULL
        }

        // and it behaves neutrally in ordering: a judged, higher-salience row still outranks it
        var store = new SqliteMemoryGraphStore(db.Factory);
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", "legacy", "s", "h", "a judged row",
            MemoryGrade.Associative, 7, 1, null, signals));

        var hits = await store.SeedAsync("e", "legacy", "s", null, 10);

        Assert.Equal(2, hits.Count);
        Assert.Equal("a judged row", hits[0].Content);
    }

    /// <summary>The write path's own finiteness guard, pinned as ACTUAL COLUMN CONTENT rather than only as
    /// the <c>NOT NULL DEFAULT 1</c> DDL. <see cref="MemorySignalsJson.Serialize"/> already drops a
    /// non-finite signal before it reaches the <c>signals</c> JSON column — <see cref="SalienceTests"/>
    /// proves a bag can legitimately hold one — but <c>UpsertAsync</c> reads the RAW incoming bag a second
    /// time, straight into the promoted <c>salience</c> column, bypassing that filter entirely. Bound
    /// unguarded, <c>NaN</c> binds as literal <c>NaN</c> into a column every seed query orders on
    /// (<c>ORDER BY … n.salience DESC …</c>) — undermining the "no row silently mis-sorts" invariant the
    /// migration's own comment argues for. <c>Math.Max(1, NaN)</c> does NOT fix this on its own: .NET's
    /// <c>Math.Max</c> propagates NaN per IEEE 754:2019 (confirmed on this runtime before writing this fix),
    /// so the guard has to test <see cref="double.IsFinite(double)"/> explicitly, the same shape
    /// <see cref="StructuralSaliencePolicy"/> already uses for its own novelty input.</summary>
    [Fact]
    public async Task A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN()
    {
        var store = New();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", "nanwrite", "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = _db.Factory.Open();
        var salience = await conn.ExecuteScalarAsync<double>(
            "SELECT salience FROM lyntai_memory_node WHERE task_key = 'nanwrite'");
        Assert.False(double.IsNaN(salience), $"the salience column holds {salience}, not the neutral value");
        Assert.Equal(1, salience);
    }

    /// <summary>The <c>NOT NULL DEFAULT 5</c> decision for <c>difficulty</c> (2026-08-10, fsrs-properly plan
    /// Task 2; corrected 2026-08-11 from an initial <c>DEFAULT 1</c> — see the migration's own doc for why
    /// the floor was a genuine defect, not a placeholder choice), pinned as ACTUAL INSERT BEHAVIOUR — the
    /// same shape <see cref="A_row_that_omits_salience_gets_the_neutral_column_default"/> pins for
    /// <c>salience</c>. Bypasses the store to write a row that OMITS <c>difficulty</c> entirely — exactly
    /// what every row from before this migration ran becomes — and confirms the column default gives it the
    /// neutral MID-POINT, which <see cref="Lyntai.Memory.Forgetting.DsrRetrievability.Reinforce"/> then reads
    /// and evolves from normally rather than needing any special case for "never computed". (The permanent
    /// demonstration that the OLD default, `1`, did NOT evolve normally — it stayed pinned at the floor under
    /// a realistic recall — lives in
    /// <c>DsrRetrievabilityTests.A_row_migrated_under_the_old_default_stays_pinned_while_the_corrected_default_moves</c>,
    /// a pure policy-level fact that does not need a store round-trip.)</summary>
    [Fact]
    public async Task A_row_that_omits_difficulty_gets_the_neutral_column_default()
    {
        using var db = new TempDb();

        using (var conn = db.Factory.Open())
        {
            await conn.ExecuteAsync("""
                INSERT INTO lyntai_memory_node
                    (engine, task_key, scope, headline, content, content_hash, grade,
                     created_at, last_recalled_position, recall_count, stability)
                VALUES ('e', 'predifficulty', 's', 'a pre-difficulty row', 'a pre-difficulty row',
                        'hash-predifficulty', 1, '2020-01-01T00:00:00Z', 0, 0, 7)
                """);

            var difficulty = await conn.ExecuteScalarAsync<double>(
                "SELECT difficulty FROM lyntai_memory_node WHERE task_key = 'predifficulty'");
            Assert.Equal(5, difficulty); // the DEFAULT (mid-point, corrected 2026-08-11), not NULL
        }

        // and Reinforce evolves it normally from there — no special case needed for a row this migration
        // never touched at write time (ProvenanceRetrievability is None for it, distinguishing "never
        // computed" from "computed as neutral" without guessing from the value alone)
        var store = new SqliteMemoryGraphStore(db.Factory);
        var node = Assert.Single(await store.SeedAsync("e", "predifficulty", "s", null, 10));
        Assert.Equal(0, node.ProvenanceRetrievability);

        var policy = new Lyntai.Memory.Forgetting.DsrRetrievability();
        var reinforced = policy.Reinforce(node.DecayState);
        Assert.True(double.IsFinite(reinforced.Difficulty));
        Assert.InRange(reinforced.Difficulty, 1, 10);
    }

    /// <summary>The write path's own finiteness guard for <c>difficulty</c>, pinned as ACTUAL COLUMN
    /// CONTENT — the same shape <see cref="A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN"/>
    /// pins for <c>salience</c>. <c>UpsertAsync</c> reads the RAW incoming bag through
    /// <see cref="MemorySignals.Difficulty"/>, which already coerces non-finite to the neutral value — this
    /// proves the coercion actually reaches the column, not merely the in-memory bag.</summary>
    [Fact]
    public async Task A_non_finite_judged_difficulty_writes_the_neutral_column_value_not_NaN()
    {
        var store = New();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", "nandifficulty", "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = _db.Factory.Open();
        var difficulty = await conn.ExecuteScalarAsync<double>(
            "SELECT difficulty FROM lyntai_memory_node WHERE task_key = 'nandifficulty'");
        Assert.False(double.IsNaN(difficulty), $"the difficulty column holds {difficulty}, not the neutral value");
        Assert.Equal(5, difficulty); // the neutral mid-point, corrected 2026-08-11 from the floor 1
    }
}
