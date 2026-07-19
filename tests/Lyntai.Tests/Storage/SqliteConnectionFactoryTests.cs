using System.Data;
using System.Data.Common;
using Dapper;
using Lyntai.Storage;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // R12 — the factory opens async (over the driver's OpenAsync), with the pragmas applied.
    [Fact]
    public async Task OpenAsync_returns_a_working_connection_with_pragmas()
    {
        await using var conn = await _db.Factory.OpenAsync();

        Assert.Equal(ConnectionState.Open, conn.State);
        Assert.Equal("wal", await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode"));
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

    [Fact]
    public async Task Round_trips_a_scalar()
    {
        using var conn = _db.Factory.Open();
        Assert.Equal(42L, await conn.ExecuteScalarAsync<long>("SELECT 42"));
    }
}
