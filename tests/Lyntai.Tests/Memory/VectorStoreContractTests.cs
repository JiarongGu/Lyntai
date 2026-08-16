using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="VectorStoreContract"/> fact against the in-process store. The SQLite and
/// Postgres wirings live beside their own fixtures (<c>SqliteGovernanceStoreTests</c>,
/// <c>PostgresGovernanceStoreTests</c>) because those own the database lifetime — the same split
/// <c>MemoryGraphStoreContract</c> already uses.</summary>
public class InMemoryVectorStoreContractTests
{
    private static IVectorStore New() => new InMemoryVectorStore();

    [Fact] public Task Cosine_not_dot() => VectorStoreContract.Ranking_is_by_cosine_so_magnitude_does_not_win(New(), "c1");
    [Fact] public Task Score_in_range() => VectorStoreContract.A_score_is_a_cosine_in_the_documented_range(New(), "c2");
    [Fact] public Task Upsert_replaces() => VectorStoreContract.Upserting_the_same_id_replaces_rather_than_duplicating(New(), "c3");
    [Fact] public Task Bounded_by_k() => VectorStoreContract.Search_returns_at_most_k(New(), "c4");
    [Fact] public Task Delete_one() => VectorStoreContract.Delete_removes_one_entry_and_leaves_the_others(New(), "c5");
    [Fact] public Task Remove_collection() => VectorStoreContract.Removing_a_collection_clears_it_and_absent_deletes_are_no_ops(New(), "c6");
    [Fact] public Task Isolated() => VectorStoreContract.Collections_are_isolated(New(), "c7");
}
