using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against SQLite over a per-test temp db, plus
/// the two SQLite-specific ones the other backends cannot have: CJK substring recall through the trigram
/// index, and FTS staying in sync after a delete.</summary>
public class SqliteMemoryGraphStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private IMemoryGraphStore New() => new SqliteMemoryGraphStore(_db.Factory);

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Cutoff_excludes() => MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(New(), "k4");
    [Fact] public Task Cutoff_keeps_fresh() => MemoryGraphStoreContract.The_candidate_cutoff_keeps_fresh_associative_nodes(New(), "k4b");
    [Fact] public Task Cutoff_spares_exact() => MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(New(), "k5");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k6");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k7");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k8");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k9");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k10");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k11");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k12");
    [Fact] public Task Cancellation() => MemoryGraphStoreContract.Cancellation_propagates(New(), "k13");
    [Fact] public Task Edge_freshness() => MemoryGraphStoreContract.An_edge_records_when_it_was_last_strengthened(New(), "k14");
    [Fact] public Task Strength() => MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(New(), "k15");
    [Fact] public Task No_strength() => MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(New(), "k16");

    /// <summary>SQLite-specific, because the other backends match a contiguous substring: the trigram
    /// tokenizer gives indexed CJK substring recall, which unicode61 would silently return nothing
    /// for.</summary>
    [Fact]
    public async Task Cjk_substring_recall()
    {
        var store = New();
        await store.UpsertAsync(Write("灵台平台负责智能代理的记忆存储"), T0);
        await store.UpsertAsync(Write("另一条无关的记录"), T0);

        var hits = await store.SeedAsync("e", "cjk", "s", "智能代理", null, 10, T0);

        Assert.Single(hits);

        static GraphNodeWrite Write(string text) =>
            new("e", "cjk", "s", text, text, MemoryGrade.Associative, 7, null);
    }

    /// <summary>The FTS index must stop matching text that was deleted. Missing the <c>'delete'</c> command
    /// row is the single most botched thing in this repository's storage layer, and it is silent — stale
    /// rows keep matching forever.</summary>
    [Fact]
    public async Task Fts_stays_in_sync_after_a_delete()
    {
        var store = New();
        await store.UpsertAsync(new GraphNodeWrite("e", "sync", "s", "distinctive phrase here",
            "distinctive phrase here", MemoryGrade.Associative, 7, null), T0);

        await store.ForgetAsync("e", "sync", "s");

        Assert.Empty(await store.SeedAsync("e", "sync", "s", "distinctive", null, 10, T0));
    }
}
