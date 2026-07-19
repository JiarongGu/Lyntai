using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

// All Lyntai tables carry the lyntai_ prefix: the storage package may be pointed at a consumer's
// existing database and must never collide with its tables.
[Migration(202607170001)]
[Tags(nameof(StorageFeature.KeyValue), StorageFeatures.AllTag)]
public sealed class M202607170001_KeyValue : Migration
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
