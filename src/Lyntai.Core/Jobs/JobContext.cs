namespace Lyntai.Jobs;

/// <summary>
/// What a running job sees: its <see cref="Payload"/>, the <see cref="Checkpoint"/> it last saved (null
/// on the first run, non-null on a <b>resume</b>), the current <see cref="Attempts"/>, the live progress
/// snapshot it last reported (<see cref="Progress"/>/<see cref="Total"/>/<see cref="Stage"/>/<see cref="Steps"/>
/// — carried across a resume), and the calls to persist progress: <see cref="SaveCheckpointAsync"/> (the
/// resume point — also renews the lease), plus <see cref="ReportProgressAsync"/>, <see cref="ReportStageAsync"/>
/// and the <c>ReportStepAsync</c> overloads (live status for a UI — observability, not a lease renewal). A stage
/// or step reported as a <see cref="JobMessage"/> keeps a code and arguments a reader can localize it by.
/// </summary>
public sealed class JobContext
{
    private readonly Func<string, CancellationToken, Task<bool>> _saveCheckpoint;
    private readonly Func<int, int, JobMessage?, CancellationToken, Task<bool>>? _reportStage;
    private readonly Func<JobMessage, CancellationToken, Task<bool>>? _reportStep;

    /// <summary>A context over plain-string reporters — what a test of a handler builds. A <see cref="JobMessage"/>
    /// the handler reports reaches them as its <see cref="JobMessage.Text"/>.</summary>
    public JobContext(Guid jobId, string payload, string? checkpoint, int attempts,
        Func<string, CancellationToken, Task<bool>> saveCheckpoint,
        Func<int, int, string?, CancellationToken, Task<bool>>? reportProgress = null,
        Func<string, CancellationToken, Task<bool>>? reportStep = null,
        int progress = 0, int total = 0, string? stage = null, string? stepLog = null)
        : this(jobId, payload, checkpoint, attempts, saveCheckpoint,
            reportProgress is null ? null : (done, count, message, ct) => reportProgress(done, count, message?.Text, ct),
            reportStep is null ? null : (message, ct) => reportStep(message.Text, ct),
            progress, total, stage is null ? null : new JobMessage(stage), stepLog)
    {
    }

    private JobContext(Guid jobId, string payload, string? checkpoint, int attempts,
        Func<string, CancellationToken, Task<bool>> saveCheckpoint,
        Func<int, int, JobMessage?, CancellationToken, Task<bool>>? reportStage,
        Func<JobMessage, CancellationToken, Task<bool>>? reportStep,
        int progress, int total, JobMessage? stage, string? stepLog)
    {
        JobId = jobId;
        Payload = payload;
        Checkpoint = checkpoint;
        Attempts = attempts;
        _saveCheckpoint = saveCheckpoint;
        _reportStage = reportStage;
        _reportStep = reportStep;
        Progress = progress;
        Total = total;
        Stage = stage?.Text;
        StageMessage = stage;
        Steps = JobStepLog.Parse(stepLog);
    }

    /// <summary>The runner's context: reporters that keep a message's code and arguments.</summary>
    internal static JobContext ForRunner(Guid jobId, string payload, string? checkpoint, int attempts,
        Func<string, CancellationToken, Task<bool>> saveCheckpoint,
        Func<int, int, JobMessage?, CancellationToken, Task<bool>> reportStage,
        Func<JobMessage, CancellationToken, Task<bool>> reportStep,
        int progress, int total, JobMessage? stage, string? stepLog) =>
        new(jobId, payload, checkpoint, attempts, saveCheckpoint, reportStage, reportStep, progress, total, stage, stepLog);

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

    /// <summary>The last reported stage as a message — with its code and arguments when it was reported as one
    /// (null until reported; carried across a resume).</summary>
    public JobMessage? StageMessage { get; }

    /// <summary>The steps reported so far (parsed from the persisted log; carried across a resume).</summary>
    public IReadOnlyList<JobStep> Steps { get; }

    /// <summary>Persist progress and renew the lease. Returns false if this worker lost the lease (the job
    /// was re-claimed by another) — a handler that sees false should stop, since its work is being redone.</summary>
    public Task<bool> SaveCheckpointAsync(string checkpoint, CancellationToken ct = default) => _saveCheckpoint(checkpoint, ct);

    /// <summary>Report live progress (items <paramref name="done"/> of <paramref name="total"/>, optionally
    /// at <paramref name="stage"/>) — readable by an observer while the job runs. Does NOT renew the lease.
    /// Returns false if the lease was lost (or no reporter is wired).</summary>
    public Task<bool> ReportProgressAsync(int done, int total, string? stage = null, CancellationToken ct = default) =>
        _reportStage?.Invoke(done, total, stage is null ? null : new JobMessage(stage), ct) ?? Task.FromResult(false);

    /// <summary>Report live progress at a <paramref name="stage"/> a reader can localize by its code and arguments —
    /// otherwise as <see cref="ReportProgressAsync"/>, under its own name so a <c>null</c> stage there stays
    /// unambiguous.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="stage"/> is null.</exception>
    public Task<bool> ReportStageAsync(int done, int total, JobMessage stage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return _reportStage?.Invoke(done, total, stage, ct) ?? Task.FromResult(false);
    }

    /// <summary>Append a human-readable step to the job's step log — readable by an observer while the job
    /// runs. Does NOT renew the lease. Returns false if the lease was lost (or no reporter is wired).</summary>
    public Task<bool> ReportStepAsync(string message, CancellationToken ct = default) =>
        ReportStepAsync(new JobMessage(message), ct);

    /// <summary>Append a step a reader can localize by its code and arguments — otherwise as the string overload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public Task<bool> ReportStepAsync(JobMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _reportStep?.Invoke(message, ct) ?? Task.FromResult(false);
    }
}
