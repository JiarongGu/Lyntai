using Lyntai.Memory;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the InMemory backend.</summary>
public class InMemoryMemoryGraphStoreTests
{
    private static IMemoryGraphStore New() => new InMemoryMemoryGraphStore();

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
    [Fact] public Task Reviews_verified_tri_state() => MemoryGraphStoreContract.Reviews_round_trip_the_verified_tri_state(New(), "k55v");
    [Fact] public Task Record_reviews_with_none_is_a_no_op() => MemoryGraphStoreContract.RecordReviewsAsync_with_no_reviews_is_a_no_op(New(), "k56");
    [Fact] public Task Review_log_evicts_down_to_the_cap() => MemoryGraphStoreContract.RecordReviewsAsync_evicts_down_to_the_cap(New(), "k57");

    [Fact]
    public Task Elapsed_age()
    {
        var clock = new MutableClock();
        return MemoryGraphStoreContract.Elapsed_age_advances_by_real_time_between_writes(
            new InMemoryMemoryGraphStore(clock: clock.Get), "k37", clock.Advance);
    }
}
