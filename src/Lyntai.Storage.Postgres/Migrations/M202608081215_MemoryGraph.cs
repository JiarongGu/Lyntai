using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Graph memory (MEM2b, Postgres leg — parallels the SQLite migration of the same number):
/// <c>lyntai_memory_node</c> + <c>lyntai_memory_edge</c>, with a <c>pg_trgm</c> GIN index over the node's
/// content for substring recall.
/// <para>No FTS mirror and no triggers, unlike the SQLite leg: a GIN trigram index is maintained by the
/// database itself, so the whole class of sync bugs that makes the SQLite side need three triggers and a
/// backfill simply does not exist here. The edge's composite primary key and both cascading foreign keys
/// are declared inline, matching its twin.</para></summary>
[Migration(202608081215)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608081215_MemoryGraph : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");

        Execute.Sql("""
            CREATE TABLE lyntai_memory_node (
                id               BIGSERIAL PRIMARY KEY,
                engine           TEXT NOT NULL,
                task_key         TEXT NOT NULL,
                scope            TEXT NOT NULL,
                headline         TEXT NOT NULL,
                content          TEXT NOT NULL,
                content_hash     TEXT NOT NULL,
                grade            INTEGER NOT NULL,
                metadata         TEXT NULL,
                created_at       TIMESTAMPTZ NOT NULL,
                last_recalled_at TIMESTAMPTZ NOT NULL,
                recall_count     INTEGER NOT NULL DEFAULT 0,
                stability        DOUBLE PRECISION NOT NULL
            )
            """);
        Execute.Sql("""
            CREATE UNIQUE INDEX ux_lyntai_memory_node_dedup
            ON lyntai_memory_node(engine, task_key, scope, content_hash)
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_node_scope ON lyntai_memory_node(engine, task_key, scope)");
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_node_trgm ON lyntai_memory_node USING gin (content gin_trgm_ops)
            """);

        Execute.Sql("""
            CREATE TABLE lyntai_memory_edge (
                from_id         BIGINT NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                to_id           BIGINT NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                kind            TEXT NOT NULL DEFAULT '',
                weight          DOUBLE PRECISION NOT NULL,
                strengthened_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (from_id, to_id, kind)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_edge_to ON lyntai_memory_edge(to_id)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_edge");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node");
    }
}
