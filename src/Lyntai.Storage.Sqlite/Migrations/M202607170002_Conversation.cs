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

        // A thread is a typed event stream: `id` is a globally-unique GUID handle; `seq` is the 1-based
        // per-thread order (external event-stream schemas key on (thread_id, seq)); `kind` is the event type
        // (a role for a plain chat turn); `payload` the body (text or JSON); `metadata` optional per-event
        // JSON. The ON DELETE CASCADE must be inline at CREATE TABLE (SQLite has no ALTER ADD CONSTRAINT).
        Execute.Sql("""
            CREATE TABLE lyntai_message (
                id TEXT PRIMARY KEY,
                thread_id TEXT NOT NULL REFERENCES lyntai_thread(id) ON DELETE CASCADE,
                seq INTEGER NOT NULL,
                kind TEXT NOT NULL,
                payload TEXT NOT NULL,
                metadata TEXT NULL,
                created_at TEXT NOT NULL
            )
            """);
        // one index serves both the thread-scoped read (thread_id prefix) and per-thread seq uniqueness
        Execute.Sql("CREATE UNIQUE INDEX ix_lyntai_message_thread_seq ON lyntai_message(thread_id, seq)");
    }

    public override void Down()
    {
        Delete.Table("lyntai_message");
        Delete.Table("lyntai_thread");
    }
}
