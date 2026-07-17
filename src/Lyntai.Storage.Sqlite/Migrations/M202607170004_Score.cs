using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170004)]
public sealed class M202607170004_Score : Migration
{
    public override void Up()
    {
        // score_group instead of the "group" keyword; score is REAL but SELECTs still CAST (affinity trap)
        Execute.Sql("""
            CREATE TABLE score_result (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                scorer_id TEXT NOT NULL,
                scorer_name TEXT NOT NULL,
                score_group TEXT NOT NULL,
                is_llm INTEGER NOT NULL,
                score REAL NOT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_score_session ON score_result(session_id)");
    }

    public override void Down()
    {
        Delete.Table("score_result");
    }
}
