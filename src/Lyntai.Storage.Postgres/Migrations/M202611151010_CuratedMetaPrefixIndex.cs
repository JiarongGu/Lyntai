using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Indexes a curated metadata pair on its key and the FIRST 256 characters of its value, in place of the whole
/// value: a btree entry is refused past about 2.7 KB, so the old <c>(key, value)</c> index refused a large value at
/// the write. A match compares the prefix through this index and the whole value after it.
/// <para><b>Postgres-only.</b> SQLite's btree takes an entry of any length, so its <c>(key, value)</c> index stays.
/// The key is still whole in the primary key, so a key past the same limit is still refused.</para></summary>
[Migration(202611151010)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202611151010_CuratedMetaPrefixIndex : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kp ON lyntai_curated_meta(key, left(value, 256))");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kv");
    }

    public override void Down()
    {
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value)");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kp");
    }
}
