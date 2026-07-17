using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170006)]
public sealed class M202607170006_PromptVersion : Migration
{
    public override void Up()
    {
        // versioned prompt overrides: monotonic version per name, exactly one active at a time
        Execute.Sql("""
            CREATE TABLE lyntai_prompt_version (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                version INTEGER NOT NULL,
                template TEXT NOT NULL,
                author TEXT NULL,
                created_at TEXT NOT NULL,
                is_active INTEGER NOT NULL
            )
            """);
        Execute.Sql("CREATE UNIQUE INDEX ux_lyntai_prompt_name_version ON lyntai_prompt_version(name, version)");
        // partial index: the single active row per name is the hot read (GetActive)
        Execute.Sql("CREATE INDEX ix_lyntai_prompt_active ON lyntai_prompt_version(name) WHERE is_active = 1");
    }

    public override void Down()
    {
        Delete.Table("lyntai_prompt_version");
    }
}
