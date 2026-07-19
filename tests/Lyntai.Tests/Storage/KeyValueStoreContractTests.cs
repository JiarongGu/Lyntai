using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="KeyValueStoreContract"/> against the InMemory backend.</summary>
public class InMemoryKeyValueStoreContractTests
{
    private static InMemoryKeyValueStore New() => new();

    [Fact] public Task Round_trip() => KeyValueStoreContract.Set_get_delete_round_trip(New(), "k");
    [Fact] public Task Missing() => KeyValueStoreContract.Missing_key_returns_null(New(), "k");
    [Fact] public Task Overwrite() => KeyValueStoreContract.Overwrite_updates_the_value(New(), "k");
    [Fact] public Task Cjk() => KeyValueStoreContract.Cjk_value_round_trips(New(), "k");
}

/// <summary>Runs the <see cref="KeyValueStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteKeyValueStoreContractTests : IDisposable
{
    private readonly TempDb _db = new();
    private SqliteKeyValueStore Store => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Round_trip() => KeyValueStoreContract.Set_get_delete_round_trip(Store, "k");
    [Fact] public Task Missing() => KeyValueStoreContract.Missing_key_returns_null(Store, "k");
    [Fact] public Task Overwrite() => KeyValueStoreContract.Overwrite_updates_the_value(Store, "k");
    [Fact] public Task Cjk() => KeyValueStoreContract.Cjk_value_round_trips(Store, "k");
}
