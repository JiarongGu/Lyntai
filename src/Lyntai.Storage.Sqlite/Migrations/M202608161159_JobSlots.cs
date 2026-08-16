using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>The cross-process job concurrency semaphore (<c>lyntai_job_slot</c>) — 3.0.
///
/// <para>A row per execution slot, taken by the same atomic-claim pattern the job table already uses. Rows
/// are created LAZILY, up to whatever <c>JobOptions.GlobalMaxConcurrency</c> currently says, and the acquire
/// predicate only looks below that cap — so the cap stays pure configuration: raising it needs no migration
/// and lowering it needs no cleanup, because the high rows simply stop being selected.</para>
///
/// <para><b>Why a table at all.</b> Counting Running jobs cannot gate a claim — a count-then-claim races,
/// which <c>IJobStore.CountRunningAsync</c> has always warned about — and folding the count into the claim
/// statement fixes that only on a single-writer store. Postgres claims with <c>FOR UPDATE SKIP LOCKED</c>
/// precisely so workers do not block each other, so a count there reads an MVCC snapshot and two claimers
/// see the same headroom. A slot is a ROW, so exclusion comes from the mechanism that already works on both
/// backends — and <c>SKIP LOCKED</c> then helps, because two workers taking two DIFFERENT slots is right.</para></summary>
[Migration(202608161159)]
[Tags(nameof(StorageFeature.Jobs), StorageFeatures.AllTag)]
public sealed class M202608161159_JobSlots : Migration
{
    public override void Up() =>
        // worker_id NULL = free. acquired_at is what makes a crashed worker's slot recoverable, on the SAME
        // lease the job claim uses — deliberately not a second expiry concept to keep in step.
        Execute.Sql("""
            CREATE TABLE lyntai_job_slot (
                slot_index INTEGER PRIMARY KEY,
                worker_id TEXT NULL,
                acquired_at TEXT NULL
            )
            """);

    public override void Down() => Execute.Sql("DROP TABLE lyntai_job_slot");
}
