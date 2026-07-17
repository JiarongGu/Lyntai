using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170005)]
public sealed class M202607170005_Trace : Migration
{
    public override void Up()
    {
        Create.Table("run_trace")
            .WithColumn("session_id").AsString().PrimaryKey()
            .WithColumn("mode").AsString().NotNullable()
            .WithColumn("started_at").AsString().NotNullable()
            .WithColumn("ended_at").AsString().Nullable();

        Execute.Sql("""
            CREATE TABLE trace_step (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL REFERENCES run_trace(session_id) ON DELETE CASCADE,
                seq INTEGER NOT NULL,
                kind TEXT NOT NULL,
                label TEXT NOT NULL,
                input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                cost_usd REAL NOT NULL,
                duration_ms INTEGER NOT NULL,
                detail TEXT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_trace_step_session ON trace_step(session_id, seq)");
    }

    public override void Down()
    {
        Delete.Table("trace_step");
        Delete.Table("run_trace");
    }
}
