using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the InMemory backend. MEM2b adds the
/// SQLite and Postgres classes deriving from the same facts.</summary>
public class InMemoryMemoryGraphStoreTests
{
    private static IMemoryGraphStore New() => new InMemoryMemoryGraphStore();

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Cutoff_excludes() => MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(New(), "k4");
    [Fact] public Task Cutoff_spares_exact() => MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(New(), "k5");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k6");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k7");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k8");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k9");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k10");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k11");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k12");
    [Fact] public Task Cancellation() => MemoryGraphStoreContract.Cancellation_propagates(New(), "k13");
}
