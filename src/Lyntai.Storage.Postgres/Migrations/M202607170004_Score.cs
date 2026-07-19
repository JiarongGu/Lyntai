using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

[Migration(202607170004)]
[Tags(nameof(StorageFeature.Score), StorageFeatures.AllTag)]
public sealed class M202607170004_Score : Migration
{
    public override void Up()
    {
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
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_score_result CASCADE");
    }
}
