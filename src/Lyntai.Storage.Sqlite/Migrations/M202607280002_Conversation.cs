using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Baseline (1.0 squash) — the conversation event store: threads + their typed event stream and
/// the (thread_id, seq) uniqueness/read index. Raw SQL reproduces the exact stored DDL of the accreted
/// pre-1.0 migrations (byte-identical net schema). FK-referenced <c>lyntai_thread</c> is created before
/// <c>lyntai_message</c>.</summary>
[Migration(202607280002)]
[Tags(nameof(StorageFeature.Conversation), StorageFeatures.AllTag)]
public sealed class M202607280002_Conversation : Migration
{
    public override void Up()
    {
        Execute.Sql("""CREATE TABLE "lyntai_thread" ("id" TEXT NOT NULL, "title" TEXT, "created_at" TEXT NOT NULL, "metadata" TEXT, CONSTRAINT "PK_lyntai_thread" PRIMARY KEY ("id"))""");

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
        Execute.Sql("CREATE UNIQUE INDEX ix_lyntai_message_thread_seq ON lyntai_message(thread_id, seq)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_message_thread_seq");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_message");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_thread");
    }
}
