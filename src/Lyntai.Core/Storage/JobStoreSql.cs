using Lyntai.Jobs;

namespace Lyntai.Storage;

/// <summary>
/// The dialect-NEUTRAL SQL + row materialization both relational <see cref="IJobStore"/> backends share —
/// the job state machine (transitions, the <c>claimed_by</c> fence) lives HERE once, so the SQLite and
/// Postgres stores can't drift on it (drift in fencing = a correctness bug, not a style nit). Booleans are
/// bound as parameters (<c>@t</c>/<c>@f</c>), not dialect literals, which is what makes the statements
/// identical across backends. Only the CLAIM statement stays per-dialect (single-writer UPDATE…RETURNING
/// on SQLite vs FOR UPDATE SKIP LOCKED on Postgres) along with each dialect's null-probe list filter.
/// A BYO relational backend can reuse these the same way.
/// </summary>
public static class JobStoreSql
{
    /// <summary>The full column list, in the canonical order <see cref="JobRow"/> materializes.</summary>
    public const string Cols =
        "id, lane, type, payload, status, checkpoint, attempts, max_attempts, last_error, " +
        "available_at, claimed_at, claimed_by, created_at, updated_at, priority, cancel_requested, " +
        "progress, total, stage, step_log, partition_key";

    /// <summary>Enqueue (binds <c>@f</c> = false for <c>cancel_requested</c>).</summary>
    public const string Insert =
        $"INSERT INTO lyntai_job ({Cols}) VALUES " +
        "(@id, @lane, @type, @payload, 'Pending', NULL, 0, @maxAttempts, NULL, @availableAt, NULL, NULL, @now, @now, @priority, @f, 0, 0, NULL, NULL, @partitionKey)";

    /// <summary>The write fence: a mutating statement lands only while THIS worker holds the Running claim.</summary>
    public const string FenceWhere = "WHERE id=@id AND claimed_by=@workerId AND status='Running'";

    /// <summary>The dialect-NEUTRAL free-slot predicate for the cross-process concurrency semaphore: an
    /// index below the cap that nobody holds, or whose holder's lease has expired. Written against the
    /// candidate alias <c>s</c>; parameters: <c>@cap</c>, <c>@staleBefore</c>.
    /// <para>Shared for the same reason <see cref="ClaimCandidateWhere"/> is — the two backends' acquire
    /// statements must differ only in their LOCKING frame, never in what counts as free. <c>slot_index &lt;
    /// @cap</c> is what keeps the cap pure configuration: lowering it strands the high rows rather than
    /// needing them deleted, and raising it lets the lazy insert create more.</para></summary>
    public const string FreeSlotWhere = """
        s.slot_index < @cap
          AND (s.worker_id IS NULL OR s.acquired_at <= @staleBefore)
        """;

    /// <summary>Renew every slot this worker still holds — one statement, so the cost does not grow with
    /// the number of jobs in flight. Fenced by holder, so a slot already reclaimed stays with its new owner.</summary>
    public const string HeartbeatSlots =
        "UPDATE lyntai_job_slot SET acquired_at=@now WHERE worker_id=@workerId";

    /// <summary>Give the slot back, fenced by the holder so an expired worker cannot free its successor's.</summary>
    public const string ReleaseSlot = """
        UPDATE lyntai_job_slot SET worker_id=NULL, acquired_at=NULL
        WHERE slot_index=@slotIndex AND worker_id=@workerId
        """;

    /// <summary>The dialect-NEUTRAL claim-candidate predicate — pending/stale-lease-reclaim selection plus
    /// the actor-mailbox partition guard and its FIFO tiebreak; the most correctness-critical text in the
    /// subsystem, shared so the two backends' claim statements can only differ in their LOCKING frame
    /// (single-writer <c>UPDATE…RETURNING</c> vs <c>FOR UPDATE SKIP LOCKED</c>). Written against the
    /// candidate alias <c>c</c>; parameters: <c>@lane</c>, <c>@now</c>, <c>@staleBefore</c>.</summary>
    public const string ClaimCandidateWhere = """
        c.lane=@lane
          AND ((c.status='Pending' AND c.available_at<=@now)
            OR (c.status='Running' AND c.claimed_at<@staleBefore))
          -- actor-mailbox partition guard: NULL partition_key ⇒ unguarded (current semantics)
          AND (c.partition_key IS NULL OR (
            -- one-at-a-time: no OTHER live-leased Running of this (lane, partition)
            NOT EXISTS (SELECT 1 FROM lyntai_job p WHERE p.lane=c.lane AND p.partition_key=c.partition_key
                          AND p.id<>c.id AND p.status='Running' AND p.claimed_at>=@staleBefore)
            -- a Pending candidate: no Running of the partition AT ALL (a stale Running is RECLAIMED
            -- first, not skipped), and it's the EARLIEST available Pending of the partition (FIFO)
            AND (c.status<>'Pending' OR (
              NOT EXISTS (SELECT 1 FROM lyntai_job p WHERE p.lane=c.lane AND p.partition_key=c.partition_key
                            AND p.status='Running')
              AND NOT EXISTS (SELECT 1 FROM lyntai_job p WHERE p.lane=c.lane AND p.partition_key=c.partition_key
                            AND p.status='Pending' AND p.available_at<=@now
                            AND (p.available_at<c.available_at
                              OR (p.available_at=c.available_at AND p.id<c.id)))))))
        """;

    // ── fenced SET clauses (compose as $"UPDATE lyntai_job {SetX} {FenceWhere}") ─────────────────────
    public const string SetCheckpoint = "SET checkpoint=@checkpoint, claimed_at=@now, updated_at=@now";
    public const string SetProgress = "SET progress=@done, total=@total, stage=@stage, updated_at=@now";
    public const string SetStepLog = "SET step_log=@stepLog, updated_at=@now";
    public const string SetSucceeded = "SET status='Succeeded', updated_at=@now";
    public const string SetFailedRetry = "SET status='Pending', available_at=@retryAt, last_error=@error, claimed_by=NULL, claimed_at=NULL, updated_at=@now";
    /// <summary>A poll: back to Pending at @retryAt, and <c>attempts</c> DECREMENTED to undo the increment the
    /// claim applied — a look at a healthy operation is not an attempt. <c>last_error</c> is cleared because
    /// nothing failed; leaving a stale one makes a later dead-letter report the wrong reason.</summary>
    public const string SetPollAgain = "SET status='Pending', available_at=@retryAt, attempts=attempts-1, " +
        "last_error=NULL, claimed_by=NULL, claimed_at=NULL, updated_at=@now";
    public const string SetFailedTerminal = "SET status='Failed', last_error=@error, updated_at=@now";
    public const string SetDead = "SET status='Dead', last_error=@error, updated_at=@now";
    public const string SetCancelled = "SET status='Cancelled', updated_at=@now";

    // ── unfenced transitions (admin/API paths gated by status predicates) ────────────────────────────
    public const string Replay =
        "UPDATE lyntai_job SET status='Pending', attempts=0, last_error=NULL, available_at=@now, " +
        "claimed_by=NULL, claimed_at=NULL, cancel_requested=@f, updated_at=@now WHERE id=@id AND status IN ('Dead','Failed')";
    public const string Pause = "UPDATE lyntai_job SET status='Paused', updated_at=@now WHERE id=@id AND status='Pending'";
    public const string Resume = "UPDATE lyntai_job SET status='Pending', updated_at=@now WHERE id=@id AND status='Paused'";
    public const string RequestCancel = "UPDATE lyntai_job SET cancel_requested=@t, updated_at=@now WHERE id=@id AND status='Running'";

    /// <summary>Cancel a job that has NOT STARTED — <see cref="Lyntai.Jobs.JobStatus.Pending"/> OR
    /// <see cref="Lyntai.Jobs.JobStatus.Paused"/>. The name predates the Paused arm and is kept because it is
    /// released surface; read it as "cancel the not-yet-running one". A held job belongs here rather than in
    /// <see cref="RequestCancel"/> because <c>cancel_requested</c> is a message to the worker holding the
    /// claim and a held job has none — so it is cancelled outright, never flagged and left held. Widening it
    /// HERE (not in each backend) is what stops the two relational stores from drifting.</summary>
    public const string CancelPending = "UPDATE lyntai_job SET status='Cancelled', updated_at=@now WHERE id=@id AND status IN ('Pending','Paused')";

    // ── reads ─────────────────────────────────────────────────────────────────────────────────────────
    public const string CountRunning = "SELECT COUNT(*) FROM lyntai_job WHERE lane=@lane AND status='Running'";
    public const string ActiveLanes = "SELECT DISTINCT lane FROM lyntai_job WHERE status IN ('Pending','Running') ORDER BY lane";
    public const string GetById = $"SELECT {Cols} FROM lyntai_job WHERE id=@id";
}

/// <summary>The materialization row for <c>lyntai_job</c> — settable properties (so any micro-ORM binds
/// it regardless of column type affinities) projected to <see cref="JobRecord"/> via <see cref="ToRecord"/>.
/// Shared by the relational backends so the column↔record mapping can't drift.</summary>
public sealed class JobRow
{
    public string Id { get; set; } = "";
    public string Lane { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Checkpoint { get; set; }
    public long Attempts { get; set; }
    public long MaxAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Priority { get; set; }
    public bool CancelRequested { get; set; }
    public long Progress { get; set; }
    public long Total { get; set; }
    public string? Stage { get; set; }
    public string? StepLog { get; set; }
    public string? PartitionKey { get; set; }

    public JobRecord ToRecord() => new(Guid.Parse(Id), Lane, Type, Payload, Enum.Parse<JobStatus>(Status),
        Checkpoint, (int)Attempts, (int)MaxAttempts, LastError, AvailableAt, ClaimedAt, ClaimedBy, CreatedAt,
        UpdatedAt, (int)Priority, CancelRequested, (int)Progress, (int)Total, Stage, StepLog, PartitionKey);
}
