using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170002)]
public sealed class M202607170002_Conversation : Migration
{
    public override void Up()
    {
        Create.Table("thread")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("title").AsString().Nullable()
            .WithColumn("created_at").AsString().NotNullable();

        // raw SQL: the ON DELETE CASCADE must be inline at CREATE TABLE (SQLite has no ALTER ADD CONSTRAINT)
        Execute.Sql("""
            CREATE TABLE message (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id TEXT NOT NULL REFERENCES thread(id) ON DELETE CASCADE,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_message_thread ON message(thread_id)");
    }

    public override void Down()
    {
        Delete.Table("message");
        Delete.Table("thread");
    }
}
