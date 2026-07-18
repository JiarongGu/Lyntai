namespace Lyntai.Jobs;

/// <summary>
/// What a running job sees: its <see cref="Payload"/>, the <see cref="Checkpoint"/> it last saved (null
/// on the first run, non-null on a <b>resume</b>), the current <see cref="Attempts"/>, the live progress
/// snapshot it last reported (<see cref="Progress"/>/<see cref="Total"/>/<see cref="Stage"/>/<see cref="Steps"/>
/// — carried across a resume), and the calls to persist progress: <see cref="SaveCheckpointAsync"/> (the
/// resume point — also renews the lease), plus <see cref="ReportProgressAsync"/> / <see cref="ReportStepAsync"/>
/// (live status for a UI — observability, not a lease renewal).
/// </summary>
public sealed class JobContext
{
    private readonly Func<string, CancellationToken, Task<bool>> _saveCheckpoint;
    private readonly Func<int, int, string?, CancellationToken, Task<bool>>? _reportProgress;
    private readonly Func<string, CancellationToken, Task<bool>>? _reportStep;

    public JobContext(Guid jobId, string payload, string? checkpoint, int attempts,
        Func<string, CancellationToken, Task<bool>> saveCheckpoint,
        Func<int, int, string?, CancellationToken, Task<bool>>? reportProgress = null,
        Func<string, CancellationToken, Task<bool>>? reportStep = null,
        int progress = 0, int total = 0, string? stage = null, string? stepLog = null)
    {
        JobId = jobId;
        Payload = payload;
        Checkpoint = checkpoint;
        Attempts = attempts;
        _saveCheckpoint = saveCheckpoint;
        _reportProgress = reportProgress;
        _reportStep = reportStep;
        Progress = progress;
        Total = total;
        Stage = stage;
        Steps = JobStepLog.Parse(stepLog);
    }

    public Guid JobId { get; }

    public string Payload { get; }

    /// <summary>The last checkpoint this job persisted. Null on the first run; on a resume it's whatever
    /// the handler saved before the previous process died — resume from here.</summary>
    public string? Checkpoint { get; }

    /// <summary>This attempt's number (1 on the first run).</summary>
    public int Attempts { get; }

    /// <summary>The last reported progress count (0 until reported; carried across a resume).</summary>
    public int Progress { get; }

    /// <summary>The last reported total (0 until reported).</summary>
    public int Total { get; }

    /// <summary>The last reported stage label (null until reported).</summary>
    public string? Stage { get; }

    /// <summary>The steps reported so far (parsed from the persisted log; carried across a resume).</summary>
    public IReadOnlyList<JobStep> Steps { get; }

    /// <summary>Persist progress and renew the lease. Returns false if this worker lost the lease (the job
    /// was re-claimed by another) — a handler that sees false should stop, since its work is being redone.</summary>
    public Task<bool> SaveCheckpointAsync(string checkpoint, CancellationToken ct = default) => _saveCheckpoint(checkpoint, ct);

    /// <summary>Report live progress (items <paramref name="done"/> of <paramref name="total"/>, optionally
    /// at <paramref name="stage"/>) — readable by an observer while the job runs. Does NOT renew the lease.
    /// Returns false if the lease was lost (or no reporter is wired).</summary>
    public Task<bool> ReportProgressAsync(int done, int total, string? stage = null, CancellationToken ct = default) =>
        _reportProgress?.Invoke(done, total, stage, ct) ?? Task.FromResult(false);

    /// <summary>Append a human-readable step to the job's step log — readable by an observer while the job
    /// runs. Does NOT renew the lease. Returns false if the lease was lost (or no reporter is wired).</summary>
    public Task<bool> ReportStepAsync(string message, CancellationToken ct = default) =>
        _reportStep?.Invoke(message, ct) ?? Task.FromResult(false);
}
