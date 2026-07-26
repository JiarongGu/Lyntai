using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>CMEM4 — the keyword-search index for the curated catalog: <c>pg_trgm</c> GIN trigram indexes
/// over <c>content</c> and <c>title</c> so <c>SearchAsync</c>'s ILIKE substring match (including CJK
/// substrings) is index-served, the Postgres analogue of the SQLite FTS5-trigram migration of the same
/// number. The extension create is repeated here (idempotent) because until now only the Memory domain
/// installed it — a CuratedMemory-only build must not depend on Memory being selected.</summary>
[Migration(202607270002)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607270002_CuratedMemorySearch : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_content_trgm ON lyntai_curated_memory USING gin (content gin_trgm_ops)");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_title_trgm ON lyntai_curated_memory USING gin (title gin_trgm_ops)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_title_trgm");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_content_trgm");
        // leave pg_trgm installed — other objects in a consumer db may rely on it (see the Memory migration).
    }
}
