using Lyntai.Jobs;

namespace Lyntai.Storage;

/// <summary>
/// Durable persistence for jobs (design §9). The load-bearing operation is <see cref="ClaimNextAsync"/>,
/// which MUST atomically hand one job to exactly one worker — implementations do it as a single
/// <c>UPDATE … RETURNING</c> (SQLite) / <c>… FOR UPDATE SKIP LOCKED …</c> (Postgres), never a
/// select-then-update. Reclaim of crashed workers is folded into the claim: a <see cref="JobStatus.Running"/>
/// job whose lease has gone stale (claimed longer ago than <c>lease</c>) is re-claimable and resumes from
/// its checkpoint.
///
/// The mutating writes (<see cref="SaveCheckpointAsync"/>/<see cref="CompleteAsync"/>/<see cref="FailAsync"/>)
/// are FENCED by <c>workerId</c> and return whether they took effect: a <c>false</c> means this worker
/// lost the lease (another re-claimed the job), so the caller must abandon it — that's what makes a
/// zombie worker harmless.
/// </summary>
public interface IJobStore
{
    /// <summary>Enqueue a job. Returns its id.</summary>
    Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default);

    /// <summary>Atomically claim one runnable job in <paramref name="lane"/> for <paramref name="workerId"/>
    /// (a Pending job past its available_at, or a Running job whose lease is older than
    /// <paramref name="lease"/>), flipping it to Running with a fresh lease and incrementing attempts. Picks
    /// by <c>priority DESC, available_at, id</c> — higher priority first. Returns null when the lane has
    /// nothing runnable.</summary>
    Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default);

    /// <summary>Persist the handler's progress AND renew the lease (so a job longer than the lease isn't
    /// stolen). Fenced by <paramref name="workerId"/>; false = lost the lease.</summary>
    Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default);

    /// <summary>Record a LIVE progress snapshot (readable while the job runs, e.g. for a UI): items
    /// <paramref name="done"/> of <paramref name="total"/>, at <paramref name="stage"/>. Does NOT renew the
    /// lease (it's observability, not the resume checkpoint). Fenced by <paramref name="workerId"/>; false =
    /// lost the lease.</summary>
    Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default);

    /// <summary>Append a human-readable step to the job's step log (capped, JSON — parse with
    /// <see cref="Lyntai.Jobs.JobStepLog.Parse"/>). Observability only; does not renew the lease. Fenced by
    /// <paramref name="workerId"/>; false = lost the lease.</summary>
    Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default);

    /// <summary>Mark the job Succeeded (terminal). Fenced; false = lost the lease.</summary>
    Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default);

    /// <summary>Fail the job. When <paramref name="retryAt"/> is set (and attempts remain) it goes back to
    /// Pending available at that time; otherwise Failed (terminal). Fenced; false = lost the lease.</summary>
    Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default);

    /// <summary>Put the job back to Pending at <paramref name="runAt"/> WITHOUT counting the claim against
    /// its attempts — the store side of <see cref="Lyntai.Jobs.JobOutcome.Poll"/>. Fenced; false = lost the lease.
    /// <para>Implementations must UNDO the increment <see cref="ClaimNextAsync"/> applied, because a poll is
    /// not an attempt: the handler looked at an operation that is progressing normally and found it unfinished.
    /// Without that, a long-running render is dead-lettered after <c>MaxAttempts</c> looks — measured
    /// 2026-08-14 at roughly thirty seconds for a hosted render. It also clears <c>last_error</c>: nothing
    /// failed, and leaving a stale error makes the next dead-letter report the wrong reason.</para>
    /// <para>The fence is load-bearing here in a way it is not for the terminal transitions: this is the one
    /// outcome that moves a job BACKWARDS, so an unfenced version would let a worker whose lease was already
    /// reclaimed keep resetting another worker's job indefinitely.</para></summary>
    Task<bool> PollAgainAsync(Guid id, string workerId, DateTimeOffset runAt, CancellationToken ct = default);

    /// <summary>Move the job to the DEAD-LETTER queue (<see cref="JobStatus.Dead"/>, terminal) — used when
    /// transient retries are exhausted, so it's inspectable (<c>ListAsync(JobStatus.Dead)</c>) and
    /// replayable rather than a silent Failed. Fenced by <paramref name="workerId"/>; false = lost the lease.</summary>
    Task<bool> DeadLetterAsync(Guid id, string workerId, string error, CancellationToken ct = default);

    /// <summary>Requeue a terminal-failed job (<see cref="JobStatus.Dead"/> or <see cref="JobStatus.Failed"/>)
    /// — back to Pending, attempts reset to 0, error cleared, available now. NOT fenced (an admin op on a
    /// job no worker holds). Returns whether a matching job was requeued.</summary>
    Task<bool> ReplayAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cancel a job that has NOT STARTED — <see cref="JobStatus.Pending"/> or
    /// <see cref="JobStatus.Paused"/> (a held job cancels directly; no need to <see cref="ResumeAsync"/> it
    /// first). No effect on a Running one — ask for that with <see cref="RequestCancelAsync"/>. Returns
    /// whether it was cancelled.</summary>
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Administratively hold a Pending job: Pending → <see cref="JobStatus.Paused"/>, so it's not
    /// claimed until resumed. No effect on a non-Pending job. Returns whether it was paused. (To hold a whole
    /// lane transiently without persisting a state change, use <see cref="Lyntai.Jobs.IJobAdmissionController"/>.)
    /// <para>A held job is still cancellable: <see cref="CancelAsync"/> matches Pending AND Paused, so it goes
    /// straight to <see cref="JobStatus.Cancelled"/>. <see cref="RequestCancelAsync"/> still answers false for
    /// it — that flag is a message to the worker holding the claim, and a held job has none.</para></summary>
    Task<bool> PauseAsync(Guid id, CancellationToken ct = default);

    /// <summary>Release a held job: <see cref="JobStatus.Paused"/> → Pending, claimable again. No effect on
    /// a non-Paused job. Returns whether it was resumed.</summary>
    Task<bool> ResumeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Request cancellation of a RUNNING job — sets its <c>cancel_requested</c> flag. The worker
    /// running it observes the flag (its handler's <c>CancellationToken</c> is cancelled) and, honoring it,
    /// stops; the job then becomes Cancelled. No effect on a non-Running job (use <see cref="CancelAsync"/>
    /// for one that hasn't started, Pending or <see cref="JobStatus.Paused"/>). Returns whether the flag was
    /// set.</summary>
    Task<bool> RequestCancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Mark a Running job Cancelled — used by the runner once it has stopped the handler in
    /// response to a cancel request. Fenced by <paramref name="workerId"/>; false = lost the lease.</summary>
    Task<bool> CancelRunningAsync(Guid id, string workerId, CancellationToken ct = default);

    /// <summary>Count of Running jobs in a lane — for observability/tests only, NEVER a claim gate (a
    /// count-then-claim would race). The atomic claim is the real mutual exclusion.</summary>
    Task<int> CountRunningAsync(string lane, CancellationToken ct = default);

    /// <summary>Distinct lanes that currently have a non-terminal (Pending or Running) job — so the runner
    /// can poll every lane with work without the app having to pre-declare it.</summary>
    Task<IReadOnlyList<string>> ActiveLanesAsync(CancellationToken ct = default);

    Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>List jobs, optionally filtered by status and/or lane, newest first.</summary>
    Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default);
}
