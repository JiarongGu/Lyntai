using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>CMEM4 — the keyword-search index for the curated catalog: an external-content FTS5
/// <c>trigram</c> table over <c>content</c> + <c>title</c> (indexed CJK *substring* recall, matching the
/// lexical memory index in <c>M202607170003_Memory</c>), kept in sync by the three AFTER triggers (the
/// <c>'delete'</c> command row on DELETE and UPDATE is what keeps the index from silently corrupting)
/// and backfilled here so pre-existing catalog rows are searchable.</summary>
[Migration(202607270002)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607270002_CuratedMemorySearch : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE VIRTUAL TABLE lyntai_curated_fts USING fts5(content, title, content='lyntai_curated_memory', content_rowid='id', tokenize='trigram')");

        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ai AFTER INSERT ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(rowid, content, title) VALUES (new.id, new.content, new.title);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_ad AFTER DELETE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content, title) VALUES ('delete', old.id, old.content, old.title);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_curated_memory_au AFTER UPDATE ON lyntai_curated_memory BEGIN
                INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content, title) VALUES ('delete', old.id, old.content, old.title);
                INSERT INTO lyntai_curated_fts(rowid, content, title) VALUES (new.id, new.content, new.title);
            END
            """);

        // backfill: the table shipped released, so adopters have rows that must be searchable immediately
        Execute.Sql("INSERT INTO lyntai_curated_fts(rowid, content, title) SELECT id, content, title FROM lyntai_curated_memory");
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ai");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_curated_memory_au");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_fts");
    }
}
