using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — run traces (Postgres leg, parallels the SQLite baseline of the same
/// number): <c>lyntai_run_trace</c> (incl. the ambient W3C <c>trace_id</c> join key) + <c>lyntai_trace_step</c>
/// with the FK cascade from step → trace and the per-session step index.</summary>
[Migration(202607280005)]
[Tags(nameof(StorageFeature.Trace), StorageFeatures.AllTag)]
public sealed class M202607280005_Trace : Migration
{
    public override void Up()
    {
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
                offset_ms BIGINT NOT NULL DEFAULT 0,
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
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_trace_step CASCADE");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_run_trace CASCADE");
    }
}
