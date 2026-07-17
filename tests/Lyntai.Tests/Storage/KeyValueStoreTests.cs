using Dapper;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class KeyValueStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqliteKeyValueStore _store;

    public KeyValueStoreTests() => _store = new SqliteKeyValueStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Set_get_delete_round_trip()
    {
        await _store.SetAsync("k1", "v1");
        Assert.Equal("v1", await _store.GetAsync("k1"));

        await _store.DeleteAsync("k1");
        Assert.Null(await _store.GetAsync("k1"));
    }

    [Fact]
    public async Task Missing_key_returns_null()
    {
        Assert.Null(await _store.GetAsync("never-set"));
    }

    [Fact]
    public async Task Overwrite_updates_value_and_updated_at()
    {
        await _store.SetAsync("k", "old");
        string ReadUpdatedAt()
        {
            using var conn = _db.Factory.Open();
            return conn.ExecuteScalar<string>("SELECT updated_at FROM app_config WHERE key = 'k'")!;
        }
        var first = ReadUpdatedAt();

        await Task.Delay(30); // ensure a distinct timestamp
        await _store.SetAsync("k", "new");

        Assert.Equal("new", await _store.GetAsync("k"));
        var second = ReadUpdatedAt();
        Assert.True(string.CompareOrdinal(second, first) > 0, $"updated_at not bumped: {first} → {second}");
    }
}
