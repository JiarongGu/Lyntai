using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="CuratedMemoryStoreContract"/> method as a [Fact] — derive with a store
/// factory and the whole contract runs on that backend automatically (T11: no silent skips). Postgres
/// deliberately does NOT derive: it runs the Uid-namespaced subset on the shared container.</summary>
public abstract class CuratedMemoryStoreContractFacts
{
    protected abstract ICuratedMemoryStore NewStore();

    [Fact] public Task Add_get_list() => CuratedMemoryStoreContract.Add_get_list_round_trips(NewStore());
    [Fact] public Task Update_partial() => CuratedMemoryStoreContract.Update_changes_only_the_provided_fields(NewStore());
    [Fact] public Task Recategorise_kind() => CuratedMemoryStoreContract.Update_can_recategorise_kind_in_place(NewStore());
    [Fact] public Task List_filters() => CuratedMemoryStoreContract.List_filters_by_kind_and_enabled(NewStore());
    [Fact] public Task List_by_scope() => CuratedMemoryStoreContract.List_filters_by_scope(NewStore());
    [Fact] public Task Dedup_add() => CuratedMemoryStoreContract.Dedup_add_is_idempotent(NewStore());
    [Fact] public Task Dedup_case() => CuratedMemoryStoreContract.Dedup_identity_is_case_sensitive(NewStore());
    [Fact] public Task Dedup_race() => CuratedMemoryStoreContract.Dedup_add_race_settles_to_a_stable_id(NewStore());
    [Fact] public Task Remove() => CuratedMemoryStoreContract.Remove_deletes(NewStore());
    [Fact] public Task ForComposition() => CuratedMemoryStoreContract.ForComposition_filters_by_task_and_scope(NewStore());
    [Fact] public Task Metadata() => CuratedMemoryStoreContract.Metadata_round_trips_updates_and_clears(NewStore());
    [Fact] public Task Metadata_filter() => CuratedMemoryStoreContract.Metadata_filter_matches_all_pairs(NewStore());
    [Fact] public Task Search() => CuratedMemoryStoreContract.Search_matches_content_with_filters(NewStore());
    [Fact] public Task Search_cjk() => CuratedMemoryStoreContract.Search_recalls_cjk_substrings(NewStore());
    [Fact] public Task Search_multi_word() => CuratedMemoryStoreContract.Search_matches_any_term_of_a_multi_word_query(NewStore());
    [Fact] public Task Search_chinese() => CuratedMemoryStoreContract.Search_matches_a_chinese_query_without_spaces(NewStore());
}

/// <summary>The <see cref="CuratedMemoryStoreContract"/> against the InMemory backend.</summary>
public class InMemoryCuratedMemoryStoreTests : CuratedMemoryStoreContractFacts
{
    protected override ICuratedMemoryStore NewStore() => new InMemoryCuratedMemoryStore();
}

/// <summary>The <see cref="CuratedMemoryStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteCuratedMemoryStoreTests : CuratedMemoryStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override ICuratedMemoryStore NewStore() => new SqliteCuratedMemoryStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
