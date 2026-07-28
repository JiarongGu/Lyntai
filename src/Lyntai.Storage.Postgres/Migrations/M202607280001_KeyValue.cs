using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — the key/value store table (Postgres leg, parallels the SQLite baseline
/// of the same number). All Lyntai tables carry the lyntai_ prefix: the package may be pointed at a
/// consumer's existing database and must never collide.</summary>
[Migration(202607280001)]
[Tags(nameof(StorageFeature.KeyValue), StorageFeatures.AllTag)]
public sealed class M202607280001_KeyValue : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_kv (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            )
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_kv CASCADE");
    }
}
