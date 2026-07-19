using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>lyntai_memory_entry + the <c>pg_trgm</c> extension and its GIN trigram index over
/// <c>content</c> — makes ILIKE substring search (including CJK substrings) fast, the Postgres analogue
/// of the SQLite FTS5-trigram approach. The extension is created with Memory (the only domain that needs
/// it), so a build that disables Memory never requires pg_trgm.</summary>
[Migration(202607170003)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202607170003_Memory : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        Execute.Sql("""
            CREATE TABLE lyntai_memory_entry (
                id BIGSERIAL PRIMARY KEY,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                expires_at TIMESTAMPTZ NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_task_scope ON lyntai_memory_entry(task_key, scope)");
        Execute.Sql("CREATE INDEX ix_lyntai_memory_content_trgm ON lyntai_memory_entry USING gin (content gin_trgm_ops)");
        // dedup key: an atomic INSERT ... ON CONFLICT upsert needs a unique index. md5(content) keeps the
        // index small and within btree size limits (a unique index on raw TEXT content could exceed them).
        Execute.Sql("CREATE UNIQUE INDEX ux_lyntai_memory_dedup ON lyntai_memory_entry (task_key, scope, md5(content))");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_entry CASCADE");
        // leave pg_trgm installed — other objects in a consumer db may rely on it; dropping an extension
        // could cascade beyond Lyntai's own schema.
    }
}
