using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A keyed call's receipt (<c>docs/DECISIONS.md</c> D242): what a caller who lost the reply reads back, keyed by
/// (engine, task, key) and written in the effect's own transaction. The fingerprint is a digest of what the call stated, so the same
/// key with other content is refused; <c>node_id</c> carries <c>ON DELETE CASCADE</c>, so removing an entry removes the receipts that
/// name it — the cascade fires only because every connection comes from <see cref="SqliteConnectionFactory"/>, which applies
/// <c>foreign_keys=ON</c>. The expiry index serves the purge a later keyed write runs; the library runs no timer.</summary>
[Migration(202610311319)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202610311319_MemoryReceipt : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_memory_receipt (
                engine TEXT NOT NULL,
                task_key TEXT NOT NULL,
                op_key TEXT NOT NULL,
                kind INTEGER NOT NULL,
                fingerprint TEXT NOT NULL,
                node_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                revision INTEGER NOT NULL,
                outcome INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                PRIMARY KEY (engine, task_key, op_key)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_receipt_expiry ON lyntai_memory_receipt(engine, expires_at)");
        Execute.Sql("CREATE INDEX ix_lyntai_memory_receipt_node ON lyntai_memory_receipt(node_id)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE lyntai_memory_receipt");
    }
}
