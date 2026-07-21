using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="CuratedMemoryStoreContract"/> against the InMemory backend.</summary>
public class InMemoryCuratedMemoryStoreTests
{
    private static InMemoryCuratedMemoryStore New() => new();

    [Fact] public Task Add_get_list() => CuratedMemoryStoreContract.Add_get_list_round_trips(New());
    [Fact] public Task Update_partial() => CuratedMemoryStoreContract.Update_changes_only_the_provided_fields(New());
    [Fact] public Task List_filters() => CuratedMemoryStoreContract.List_filters_by_kind_and_enabled(New());
    [Fact] public Task Clear_source() => CuratedMemoryStoreContract.Update_with_empty_source_clears_it(New());
    [Fact] public Task Remove() => CuratedMemoryStoreContract.Remove_deletes(New());
    [Fact] public Task ForComposition() => CuratedMemoryStoreContract.ForComposition_filters_by_task_and_scope(New());
}

/// <summary>Runs the <see cref="CuratedMemoryStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteCuratedMemoryStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private SqliteCuratedMemoryStore Store => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Add_get_list() => CuratedMemoryStoreContract.Add_get_list_round_trips(Store);
    [Fact] public Task Update_partial() => CuratedMemoryStoreContract.Update_changes_only_the_provided_fields(Store);
    [Fact] public Task List_filters() => CuratedMemoryStoreContract.List_filters_by_kind_and_enabled(Store);
    [Fact] public Task Clear_source() => CuratedMemoryStoreContract.Update_with_empty_source_clears_it(Store);
    [Fact] public Task Remove() => CuratedMemoryStoreContract.Remove_deletes(Store);
    [Fact] public Task ForComposition() => CuratedMemoryStoreContract.ForComposition_filters_by_task_and_scope(Store);
}
