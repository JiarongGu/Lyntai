using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>The cross-process job concurrency semaphore (<c>lyntai_job_slot</c>) — 3.0. The Postgres twin of
/// the SQLite migration of the same number; see that one for why this is a table rather than a count.
///
/// <para>Same shape deliberately, because the two backends run the SAME acquire SQL apart from their
/// locking frame — <c>FOR UPDATE SKIP LOCKED</c> here, single-writer <c>UPDATE…RETURNING</c> there. A
/// divergence in the SCHEMA would let that shared predicate mean two different things, which
/// <c>.claude/knowledge/sql-storage.md</c> records as where wrong-data bugs live.</para>
///
/// <para><c>timestamptz</c> rather than the text SQLite stores, matching how every other timestamp differs
/// between these two backends.</para></summary>
[Migration(202608161159)]
[Tags(nameof(StorageFeature.Jobs), StorageFeatures.AllTag)]
public sealed class M202608161159_JobSlots : Migration
{
    public override void Up() =>
        Execute.Sql("""
            CREATE TABLE lyntai_job_slot (
                slot_index INTEGER PRIMARY KEY,
                worker_id TEXT NULL,
                acquired_at TIMESTAMPTZ NULL
            )
            """);

    public override void Down() => Execute.Sql("DROP TABLE lyntai_job_slot");
}
