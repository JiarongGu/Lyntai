using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the InMemory backend.</summary>
public class InMemoryMemoryGraphStoreTests
{
    private static IMemoryGraphStore New() => new InMemoryMemoryGraphStore();

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Quiet_engine() => MemoryGraphStoreContract.A_busy_engine_does_not_age_a_quiet_ones_memories(New(), "k4");
    [Fact] public Task Cutoff_excludes() => MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(New(), "k5");
    [Fact] public Task Cutoff_keeps_fresh() => MemoryGraphStoreContract.The_candidate_cutoff_keeps_fresh_associative_nodes(New(), "k6");
    [Fact] public Task Cutoff_spares_exact() => MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(New(), "k7");
    [Fact] public Task Bigger_crowds_harder() => MemoryGraphStoreContract.A_bigger_write_crowds_harder(New(), "k8");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k9");
    [Fact] public Task Touch_does_not_age() => MemoryGraphStoreContract.A_touch_does_not_advance_the_position(New(), "k10");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k11");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k12");
    [Fact] public Task Edge_ages() => MemoryGraphStoreContract.An_edge_ages_as_the_memory_moves_on(New(), "k13");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k14");
    [Fact] public Task Strength() => MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(New(), "k15");
    [Fact] public Task No_strength() => MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(New(), "k16");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k17");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k18");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k19");
    [Fact] public Task Cancellation() => MemoryGraphStoreContract.Cancellation_propagates(New(), "k20");
}
