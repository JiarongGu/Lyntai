using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — the curated memory CATALOG (Postgres leg, parallels the SQLite baseline
/// of the same number): <c>lyntai_curated_memory</c> with its kind/task list indexes and content pg_trgm
/// GIN search index, plus the relational <c>lyntai_curated_meta</c> query index (FK cascade from the
/// catalog).
/// <para>A CLEAN single-CREATE baseline: the retired <c>source</c>/<c>title</c> columns and the transient
/// title trigram index that the pre-1.0 accreted migrations added-then-dropped are simply absent — the net
/// column SET [id, kind, content, enabled, created_at, updated_at, task, scope, metadata] is declared once,
/// in a natural order. Physical <c>ordinal_position</c> differs from the pre-squash history (which left
/// gaps where the drops happened), but nothing in Lyntai depends on column order (all access is by name via
/// Dapper), and the position-agnostic <c>PgMigrationSchemaSnapshotTests</c> gate compares the column SET,
/// indexes, and FKs — so this baseline is schema-identical to the pre-squash net catalog.</para></summary>
[Migration(202607280009)]
[Tags(nameof(StorageFeature.CuratedMemory), StorageFeatures.AllTag)]
public sealed class M202607280009_CuratedMemory : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");

        Execute.Sql("""
            CREATE TABLE lyntai_curated_memory (
                id         BIGSERIAL PRIMARY KEY,
                kind       TEXT NOT NULL,
                content    TEXT NOT NULL,
                enabled    BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                task       TEXT NULL,
                scope      TEXT NULL,
                metadata   TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_kind ON lyntai_curated_memory(kind, enabled)");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_memory_task ON lyntai_curated_memory(task, enabled)");
        Execute.Sql("CREATE INDEX ix_lyntai_curated_content_trgm ON lyntai_curated_memory USING gin (content gin_trgm_ops)");

        Execute.Sql("""
            CREATE TABLE lyntai_curated_meta (
                memory_id BIGINT NOT NULL REFERENCES lyntai_curated_memory(id) ON DELETE CASCADE,
                key       TEXT NOT NULL,
                value     TEXT NOT NULL,
                PRIMARY KEY (memory_id, key)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_meta_kv");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_meta CASCADE");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_content_trgm");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_task");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_curated_memory_kind");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_curated_memory CASCADE");
        // leave pg_trgm installed — other objects in a consumer db may rely on it.
    }
}
