using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="KeyValueStoreContract"/> method as a [Fact] — derive with a store factory
/// and the WHOLE contract runs; a new contract method wired here runs on every derived backend
/// automatically, so a backend can no longer silently skip one (T11). Postgres deliberately does NOT
/// derive: it runs the Uid-namespaced subset on the shared container (see <c>PostgresStorageTests</c>).</summary>
public abstract class KeyValueStoreContractFacts
{
    protected abstract IKeyValueStore NewStore();

    [Fact] public Task Round_trip() => KeyValueStoreContract.Set_get_delete_round_trip(NewStore(), "k");
    [Fact] public Task Missing() => KeyValueStoreContract.Missing_key_returns_null(NewStore(), "k");
    [Fact] public Task Overwrite() => KeyValueStoreContract.Overwrite_updates_the_value(NewStore(), "k");
    [Fact] public Task Cjk() => KeyValueStoreContract.Cjk_value_round_trips(NewStore(), "k");
    [Fact] public Task List_prefix() => KeyValueStoreContract.List_keys_filters_by_prefix_in_ordinal_order(NewStore(), "k");
    [Fact] public Task List_literals() => KeyValueStoreContract.List_keys_treats_like_wildcards_as_literals(NewStore(), "k");
    [Fact] public Task List_all() => KeyValueStoreContract.List_keys_without_prefix_lists_all_keys(NewStore(), "k");
}

/// <summary>The <see cref="KeyValueStoreContract"/> against the InMemory backend.</summary>
public class InMemoryKeyValueStoreContractTests : KeyValueStoreContractFacts
{
    protected override IKeyValueStore NewStore() => new InMemoryKeyValueStore();
}

/// <summary>The <see cref="KeyValueStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteKeyValueStoreContractTests : KeyValueStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IKeyValueStore NewStore() => new SqliteKeyValueStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
