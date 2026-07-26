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
    [Fact] public Task List_filters() => CuratedMemoryStoreContract.List_filters_by_kind_and_enabled(NewStore());
    [Fact] public Task List_by_scope() => CuratedMemoryStoreContract.List_filters_by_scope(NewStore());
    [Fact] public Task Dedup_add() => CuratedMemoryStoreContract.Dedup_add_is_idempotent(NewStore());
    [Fact] public Task Dedup_case() => CuratedMemoryStoreContract.Dedup_identity_is_case_sensitive(NewStore());
    [Fact] public Task Dedup_race() => CuratedMemoryStoreContract.Dedup_add_race_settles_to_a_stable_id(NewStore());
    [Fact] public Task Clear_source() => CuratedMemoryStoreContract.Update_with_empty_source_clears_it(NewStore());
    [Fact] public Task Remove() => CuratedMemoryStoreContract.Remove_deletes(NewStore());
    [Fact] public Task ForComposition() => CuratedMemoryStoreContract.ForComposition_filters_by_task_and_scope(NewStore());
    [Fact] public Task Title() => CuratedMemoryStoreContract.Title_round_trips_updates_and_clears(NewStore());
    [Fact] public Task Search() => CuratedMemoryStoreContract.Search_matches_content_and_title_with_filters(NewStore());
    [Fact] public Task Search_cjk() => CuratedMemoryStoreContract.Search_recalls_cjk_substrings(NewStore());
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
