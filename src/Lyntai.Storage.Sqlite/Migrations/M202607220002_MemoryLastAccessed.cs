using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Adds <c>last_accessed_at</c> to memory for LRU eviction (<c>MemoryRetentionPolicy</c>): the store
/// writes it on every remember and refreshes it on recall, so the LRU eviction mode can keep the
/// most-recently-USED facts. Backfilled to <c>created_at</c> for existing rows. The FTS external-content
/// index only mirrors <c>content</c>, so a new column needs no trigger changes. A SEPARATE numbered
/// migration (not a fold) because the memory table shipped released.</summary>
[Migration(202607220002)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202607220002_MemoryLastAccessed : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_memory_entry ADD COLUMN last_accessed_at TEXT NULL");
        Execute.Sql("UPDATE lyntai_memory_entry SET last_accessed_at = created_at WHERE last_accessed_at IS NULL");

        // Scope the FTS re-sync trigger to content-only updates: the store now UPDATEs last_accessed_at on
        // recall (LRU) and created_at/expires_at on dedup-refresh — none change content, so the old
        // fire-on-any-update trigger churned the FTS index for nothing. AFTER UPDATE OF content fixes both.
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_entry_au");
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_entry_au AFTER UPDATE OF content ON lyntai_memory_entry BEGIN
                INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
            END
            """);
    }

    public override void Down()
    {
        // SQLite pre-3.35 can't DROP COLUMN; the nullable column is harmless if left (mirrors M…0007_MemoryTtl).
    }
}
