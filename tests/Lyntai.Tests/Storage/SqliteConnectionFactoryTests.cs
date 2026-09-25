using System.Data;
using System.Data.Common;
using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // The factory opens async (over the driver's OpenAsync) with the pragmas applied. Against a database
    // nothing else has opened: WAL is a DATABASE property a migration would already have set, and a pooled
    // physical connection can carry a per-connection pragma over from an earlier open — either would let
    // an OpenAsync that applies nothing pass. foreign_keys is the one whose loss is silent (cascades stop).
    [Fact]
    public async Task OpenAsync_returns_a_working_connection_with_pragmas()
    {
        using var fresh = new TempDbPath("open-async");
        await using var conn = await new SqliteConnectionFactory(fresh.Path).OpenAsync();

        Assert.Equal(ConnectionState.Open, conn.State);
        Assert.Equal("wal", await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode"));
        Assert.Equal(1L, await conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys"));
        Assert.Equal(5000L, await conn.ExecuteScalarAsync<long>("PRAGMA busy_timeout"));
        Assert.Equal(42L, await conn.ExecuteScalarAsync<long>("SELECT 42"));
    }

    // R12 — a factory that implements only the sync Open() still gets a working OpenAsync via the interface
    // default method (so adding OpenAsync is non-breaking for existing implementers).
    [Fact]
    public async Task Default_OpenAsync_delegates_to_the_sync_Open()
    {
        IDbConnectionFactory syncOnly = new SyncOnlyFactory();

        await using var conn = await syncOnly.OpenAsync();
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    private sealed class SyncOnlyFactory : IDbConnectionFactory
    {
        public DbConnection Open()
        {
            var c = new SqliteConnection("Data Source=:memory:");
            c.Open();
            return c;
        }
    }

    [Fact]
    public void Open_applies_the_family_pragmas()
    {
        using var conn = _db.Factory.Open();

        Assert.Equal("wal", conn.ExecuteScalar<string>("PRAGMA journal_mode"));
        Assert.Equal(1L, conn.ExecuteScalar<long>("PRAGMA foreign_keys"));
        Assert.Equal(5000L, conn.ExecuteScalar<long>("PRAGMA busy_timeout"));
    }

}
