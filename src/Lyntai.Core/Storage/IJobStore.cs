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
    /// <paramref name="lease"/>), flipping it to Running with a fresh lease and incrementing attempts.
    /// Returns null when the lane has nothing runnable.</summary>
    Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default);

    /// <summary>Persist the handler's progress AND renew the lease (so a job longer than the lease isn't
    /// stolen). Fenced by <paramref name="workerId"/>; false = lost the lease.</summary>
    Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default);

    /// <summary>Mark the job Succeeded (terminal). Fenced; false = lost the lease.</summary>
    Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default);

    /// <summary>Fail the job. When <paramref name="retryAt"/> is set (and attempts remain) it goes back to
    /// Pending available at that time; otherwise Failed (terminal). Fenced; false = lost the lease.</summary>
    Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default);

    /// <summary>Cancel a still-Pending job (no effect on a Running one). Returns whether it was cancelled.</summary>
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);

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
