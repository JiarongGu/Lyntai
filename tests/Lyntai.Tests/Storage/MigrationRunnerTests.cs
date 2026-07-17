using Dapper;

namespace Lyntai.Tests.Storage;

public class MigrationRunnerTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Fresh_db_has_all_migrations_applied()
    {
        using var conn = _db.Factory.Open();

        var versions = conn.Query<long>("SELECT Version FROM VersionInfo ORDER BY Version").ToList();

        Assert.Equal([202607170001, 202607170002, 202607170003, 202607170004, 202607170005], versions);
    }

    [Fact]
    public void Migrating_again_is_idempotent()
    {
        Lyntai.Storage.Sqlite.Migrations.MigrationRunnerService.MigrateUp(_db.Path);

        using var conn = _db.Factory.Open();
        Assert.Equal(5L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM VersionInfo"));
    }
}
