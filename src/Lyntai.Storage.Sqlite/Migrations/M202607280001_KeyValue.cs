using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the key/value store table. Emitted as raw SQL to reproduce the exact
/// stored DDL of the accreted pre-1.0 migrations (byte-identical net schema; the fluent Create.Table API
/// would render different text). All Lyntai tables carry the lyntai_ prefix so the package can be pointed
/// at a consumer's existing database and never collide.</summary>
[Migration(202607280001)]
[Tags(nameof(StorageFeature.KeyValue), StorageFeatures.AllTag)]
public sealed class M202607280001_KeyValue : Migration
{
    public override void Up()
    {
        Execute.Sql("""CREATE TABLE "lyntai_kv" ("key" TEXT NOT NULL, "value" TEXT NOT NULL, "updated_at" TEXT NOT NULL, CONSTRAINT "PK_lyntai_kv" PRIMARY KEY ("key"))""");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_kv");
    }
}
