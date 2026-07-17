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

        var versions = conn.Query<long>("SELECT Version FROM lyntai_version_info ORDER BY Version").ToList();

        Assert.Equal([202607170001, 202607170002, 202607170003, 202607170004, 202607170005], versions);
    }

    [Fact]
    public void Every_lyntai_object_carries_the_prefix()
    {
        using var conn = _db.Factory.Open();

        // the storage package may live inside a consumer's existing db — nothing unprefixed allowed
        var unprefixed = conn.Query<string>("""
            SELECT name FROM sqlite_master
            WHERE name NOT LIKE 'lyntai\_%' ESCAPE '\' AND name NOT LIKE 'sqlite_%'
              AND name NOT LIKE 'ix_lyntai\_%' ESCAPE '\' AND name NOT LIKE 'ux_lyntai\_%' ESCAPE '\'
            """).ToList();

        Assert.Empty(unprefixed);
    }

    [Fact]
    public void Migrating_again_is_idempotent()
    {
        Lyntai.Storage.Sqlite.Migrations.MigrationRunnerService.MigrateUp(_db.Path);

        using var conn = _db.Factory.Open();
        Assert.Equal(5L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_version_info"));
    }
}
