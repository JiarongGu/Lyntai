using Dapper;

namespace Lyntai.Tests.Storage;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

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
