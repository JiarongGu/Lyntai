using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Baseline (1.0 squash) — the typed conversation event store (Postgres leg, parallels the SQLite
/// baseline of the same number): <c>lyntai_thread</c> + <c>lyntai_message</c> with the per-thread seq
/// unique index and the FK cascade from message → thread.</summary>
[Migration(202607280002)]
[Tags(nameof(StorageFeature.Conversation), StorageFeatures.AllTag)]
public sealed class M202607280002_Conversation : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_thread (
                id TEXT PRIMARY KEY,
                title TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                metadata TEXT NULL
            )
            """);
        // A thread is a typed event stream: `id` a globally-unique GUID handle; `seq` the 1-based per-thread
        // order (external event-stream schemas key on (thread_id, seq)); `kind` the event type (a role for a
        // plain chat turn); `payload` the body (text or JSON); `metadata` optional per-event JSON.
        Execute.Sql("""
            CREATE TABLE lyntai_message (
                id TEXT PRIMARY KEY,
                thread_id TEXT NOT NULL REFERENCES lyntai_thread(id) ON DELETE CASCADE,
                seq BIGINT NOT NULL,
                kind TEXT NOT NULL,
                payload TEXT NOT NULL,
                metadata TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL
            )
            """);
        // one index serves both the thread-scoped read (thread_id prefix) and per-thread seq uniqueness
        Execute.Sql("CREATE UNIQUE INDEX ix_lyntai_message_thread_seq ON lyntai_message(thread_id, seq)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_message CASCADE");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_thread CASCADE");
    }
}
