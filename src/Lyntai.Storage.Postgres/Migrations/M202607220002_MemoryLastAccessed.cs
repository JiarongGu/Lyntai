using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Adds <c>last_accessed_at</c> to memory for LRU eviction (<c>MemoryRetentionPolicy</c>): the store
/// writes it on every remember and refreshes it on recall, so the LRU eviction mode can keep the
/// most-recently-USED facts. Backfilled to <c>created_at</c> for existing rows. Parallels the SQLite
/// migration of the same number; a SEPARATE migration (not a fold) because the memory table shipped released.</summary>
[Migration(202607220002)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202607220002_MemoryLastAccessed : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_entry ADD COLUMN last_accessed_at TIMESTAMPTZ NULL");
        Execute.Sql("UPDATE lyntai_memory_entry SET last_accessed_at = created_at WHERE last_accessed_at IS NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_entry DROP COLUMN IF EXISTS last_accessed_at");
    }
}
