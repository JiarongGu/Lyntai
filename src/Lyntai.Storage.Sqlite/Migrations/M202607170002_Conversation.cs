using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170002)]
public sealed class M202607170002_Conversation : Migration
{
    public override void Up()
    {
        Create.Table("lyntai_thread")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("title").AsString().Nullable()
            .WithColumn("created_at").AsString().NotNullable()
            // optional opaque thread-level state (a small JSON blob the app owns)
            .WithColumn("metadata").AsString().Nullable();

        // A thread is a typed event stream: `kind` is the event type (a role for a plain chat turn) and
        // `payload` the body (text, or JSON for a richer event). The ON DELETE CASCADE must be inline at
        // CREATE TABLE (SQLite has no ALTER ADD CONSTRAINT).
        Execute.Sql("""
            CREATE TABLE lyntai_message (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id TEXT NOT NULL REFERENCES lyntai_thread(id) ON DELETE CASCADE,
                kind TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_message_thread ON lyntai_message(thread_id)");
    }

    public override void Down()
    {
        Delete.Table("lyntai_message");
        Delete.Table("lyntai_thread");
    }
}
