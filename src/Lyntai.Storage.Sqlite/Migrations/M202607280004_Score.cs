using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the scoring/eval result table. Raw SQL reproduces the exact stored DDL
/// of the accreted pre-1.0 migrations (byte-identical net schema). <c>score_group</c> avoids the "group"
/// keyword; <c>UNIQUE(session_id, scorer_id)</c> makes SaveAsync an upsert and doubles as the session-prefix
/// index.</summary>
[Migration(202607280004)]
[Tags(nameof(StorageFeature.Score), StorageFeatures.AllTag)]
public sealed class M202607280004_Score : Migration
{
    public override void Up()
    {
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
        Execute.Sql("DROP TABLE IF EXISTS lyntai_score_result");
    }
}
