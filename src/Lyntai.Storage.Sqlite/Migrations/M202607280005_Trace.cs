using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the run-trace store: <c>lyntai_run_trace</c> (+ its ALTER-appended
/// <c>trace_id</c>, the OTel join key) and its <c>lyntai_trace_step</c> children with the session index.
/// Raw SQL reproduces the EXACT stored DDL of the accreted pre-1.0 migrations (byte-identical net schema):
/// the trace table keeps the double-quoted fluent form with <c>trace_id</c> spliced in before the PK
/// constraint. FK-referenced <c>lyntai_run_trace</c> is created before <c>lyntai_trace_step</c>.</summary>
[Migration(202607280005)]
[Tags(nameof(StorageFeature.Trace), StorageFeatures.AllTag)]
public sealed class M202607280005_Trace : Migration
{
    public override void Up()
    {
        Execute.Sql("""CREATE TABLE "lyntai_run_trace" ("session_id" TEXT NOT NULL, "mode" TEXT NOT NULL, "started_at" TEXT NOT NULL, "ended_at" TEXT, trace_id TEXT NULL, CONSTRAINT "PK_lyntai_run_trace" PRIMARY KEY ("session_id"))""");

        Execute.Sql("""
            CREATE TABLE lyntai_trace_step (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL REFERENCES lyntai_run_trace(session_id) ON DELETE CASCADE,
                seq INTEGER NOT NULL,
                offset_ms INTEGER NOT NULL DEFAULT 0,
                kind TEXT NOT NULL,
                label TEXT NOT NULL,
                input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                cost_usd REAL NOT NULL,
                duration_ms INTEGER NOT NULL,
                detail TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_trace_step_session ON lyntai_trace_step(session_id, seq)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_trace_step_session");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_trace_step");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_run_trace");
    }
}
