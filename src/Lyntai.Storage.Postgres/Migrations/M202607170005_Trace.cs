using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Run traces: <c>lyntai_run_trace</c> + <c>lyntai_trace_step</c>. The ambient W3C
/// <c>trace_id</c> (the join key to a distributed trace in an OTel backend) is folded into the header
/// table here (the SQLite backend added it via a later ALTER; a greenfield split has it from the start).</summary>
[Migration(202607170005)]
[Tags(nameof(StorageFeature.Trace), StorageFeatures.AllTag)]
public sealed class M202607170005_Trace : Migration
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
