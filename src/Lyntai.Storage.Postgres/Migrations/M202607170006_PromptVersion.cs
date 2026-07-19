using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

[Migration(202607170006)]
[Tags(nameof(StorageFeature.PromptVersion), StorageFeatures.AllTag)]
public sealed class M202607170006_PromptVersion : Migration
{
    public override void Up()
    {
        // versioned prompt overrides: monotonic version per name, exactly one active at a time
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
        // partial index: the single active row per name is the hot read (GetActive)
        Execute.Sql("CREATE INDEX ix_lyntai_prompt_active ON lyntai_prompt_version(name) WHERE is_active");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_prompt_version CASCADE");
    }
}
