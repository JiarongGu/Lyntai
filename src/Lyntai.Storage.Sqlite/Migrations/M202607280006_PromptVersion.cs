using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the versioned prompt-override table (monotonic version per name, exactly
/// one active at a time) with its uniqueness index and the partial active-row index. Raw SQL reproduces the
/// exact stored DDL of the accreted pre-1.0 migrations (byte-identical net schema).</summary>
[Migration(202607280006)]
[Tags(nameof(StorageFeature.PromptVersion), StorageFeatures.AllTag)]
public sealed class M202607280006_PromptVersion : Migration
{
    public override void Up()
    {
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
        Execute.Sql("CREATE INDEX ix_lyntai_prompt_active ON lyntai_prompt_version(name) WHERE is_active = 1");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_prompt_active");
        Execute.Sql("DROP INDEX IF EXISTS ux_lyntai_prompt_name_version");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_prompt_version");
    }
}
