using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>
/// The full Lyntai schema for PostgreSQL in one initial migration (a greenfield backend starts from
/// a single baseline — later schema changes get their own numbered migrations). Every object is
/// <c>lyntai_</c>-prefixed so the package can point at a consumer's existing database. Memory recall
/// uses the <c>pg_trgm</c> extension — a GIN trigram index over <c>content</c> makes ILIKE substring
/// search (including CJK substrings) fast, the Postgres analogue of the SQLite FTS5-trigram approach.
/// </summary>
[Migration(202607170001)]
public sealed class M202607170001_InitialSchema : Migration
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

        Execute.Sql("""
            CREATE TABLE lyntai_thread (
                id TEXT PRIMARY KEY,
                title TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                metadata TEXT NULL
            )
            """);
        // A thread is a typed event stream: `id` a globally-unique GUID handle; `seq` the 1-based per-thread
        // order (external event-stream schemas key on (thread_id, seq)); `kind` the event type (a role for a
        // plain chat turn); `payload` the body (text or JSON); `metadata` optional per-event JSON.
        Execute.Sql("""
            CREATE TABLE lyntai_message (
                id TEXT PRIMARY KEY,
                thread_id TEXT NOT NULL REFERENCES lyntai_thread(id) ON DELETE CASCADE,
                seq BIGINT NOT NULL,
                kind TEXT NOT NULL,
                payload TEXT NOT NULL,
                metadata TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL
            )
            """);
        // one index serves both the thread-scoped read (thread_id prefix) and per-thread seq uniqueness
        Execute.Sql("CREATE UNIQUE INDEX ix_lyntai_message_thread_seq ON lyntai_message(thread_id, seq)");

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

        // UNIQUE(session_id, scorer_id) makes SaveAsync an upsert (re-scoring replaces) and serves the
        // session-prefix lookups, so no separate session index is needed.
        Execute.Sql("""
            CREATE TABLE lyntai_score_result (
                id BIGSERIAL PRIMARY KEY,
                session_id TEXT NOT NULL,
                scorer_id TEXT NOT NULL,
                scorer_name TEXT NOT NULL,
                score_group TEXT NOT NULL,
                is_llm BOOLEAN NOT NULL,
                score DOUBLE PRECISION NOT NULL,
                reason TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                UNIQUE(session_id, scorer_id)
            )
            """);

        Execute.Sql("""
            CREATE TABLE lyntai_run_trace (
                session_id TEXT PRIMARY KEY,
                mode TEXT NOT NULL,
                started_at TIMESTAMPTZ NOT NULL,
                ended_at TIMESTAMPTZ NULL,
                trace_id TEXT NULL
            )
            """);
        Execute.Sql("""
            CREATE TABLE lyntai_trace_step (
                id BIGSERIAL PRIMARY KEY,
                session_id TEXT NOT NULL REFERENCES lyntai_run_trace(session_id) ON DELETE CASCADE,
                seq INTEGER NOT NULL,
                kind TEXT NOT NULL,
                label TEXT NOT NULL,
                input_tokens BIGINT NOT NULL,
                output_tokens BIGINT NOT NULL,
                cost_usd DOUBLE PRECISION NOT NULL,
                duration_ms BIGINT NOT NULL,
                detail TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_trace_step_session ON lyntai_trace_step(session_id, seq)");

        Execute.Sql("""
            CREATE TABLE lyntai_prompt_version (
                id BIGSERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                version INTEGER NOT NULL,
                template TEXT NOT NULL,
                author TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                is_active BOOLEAN NOT NULL
            )
            """);
        Execute.Sql("CREATE UNIQUE INDEX ux_lyntai_prompt_name_version ON lyntai_prompt_version(name, version)");
        Execute.Sql("CREATE INDEX ix_lyntai_prompt_active ON lyntai_prompt_version(name) WHERE is_active");
    }

    public override void Down()
    {
        foreach (var table in new[]
        {
            "lyntai_prompt_version", "lyntai_trace_step", "lyntai_run_trace", "lyntai_score_result",
            "lyntai_memory_entry", "lyntai_message", "lyntai_thread", "lyntai_kv",
        })
            Execute.Sql($"DROP TABLE IF EXISTS {table} CASCADE");
    }
}
