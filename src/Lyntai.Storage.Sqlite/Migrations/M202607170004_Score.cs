using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170004)]
public sealed class M202607170004_Score : Migration
{
    public override void Up()
    {
        // score_group instead of the "group" keyword; score is REAL but SELECTs still CAST (affinity trap).
        // UNIQUE(session_id, scorer_id) makes SaveAsync an upsert (re-scoring replaces, not accumulates) and
        // doubles as the session-prefix index, so no separate session index is needed.
        Execute.Sql("""
            CREATE TABLE lyntai_score_result (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                scorer_id TEXT NOT NULL,
                scorer_name TEXT NOT NULL,
                score_group TEXT NOT NULL,
                is_llm INTEGER NOT NULL,
                score REAL NOT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(session_id, scorer_id)
            )
            """);
    }

    public override void Down()
    {
        Delete.Table("lyntai_score_result");
    }
}
