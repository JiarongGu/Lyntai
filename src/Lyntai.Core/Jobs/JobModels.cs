namespace Lyntai.Jobs;

/// <summary>Lifecycle state of a durable job.</summary>
public enum JobStatus
{
    /// <summary>Enqueued, waiting to be claimed (once <c>available_at</c> passes).</summary>
    Pending,

    /// <summary>Claimed by a worker and executing (or its lease has gone stale after a crash — a stale
    /// Running job is re-claimable and resumes from its checkpoint).</summary>
    Running,

    /// <summary>Finished successfully. Terminal.</summary>
    Succeeded,

    /// <summary>A hard failure the handler declared permanent (<see cref="JobOutcome.Fail"/>). Terminal
    /// (the app may re-enqueue). Distinct from <see cref="Dead"/> — a Fail is "don't retry this".</summary>
    Failed,

    /// <summary>Cancelled — before it ran, or while running once the handler honoured a cancel request.
    /// Terminal.</summary>
    Cancelled,

    /// <summary>Exhausted its retries (transient failures ran out of attempts) → the dead-letter queue.
    /// Terminal but INSPECTABLE + REPLAYABLE (<see cref="Storage.IJobStore.ReplayAsync"/>) — the point of a
    /// DLQ over a silent Failed.</summary>
    Dead,

    /// <summary>Administratively held: a Pending job taken out of the claimable set until resumed (via
    /// <see cref="Storage.IJobStore.ResumeAsync"/>). NON-terminal — resume returns it to Pending. Distinct
    /// from <see cref="IJobAdmissionController"/>, which holds a whole LANE transiently without touching the
    /// jobs' state.</summary>
    Paused,
}

/// <summary>What to enqueue: the <paramref name="Lane"/> (execution lane, for concurrency), the
/// <paramref name="Type"/> (dispatches to the matching <see cref="IJobHandler"/>), and the
/// <paramref name="Payload"/> (JSON the handler reads). <paramref name="MaxAttempts"/> bounds retries —
/// null means <see cref="JobOptions.DefaultMaxAttempts"/> through <see cref="IJobQueue"/>, or
/// <see cref="JobSpec.DefaultMaxAttempts"/> for a spec handed straight to a store;
/// <paramref name="AvailableAt"/> delays first execution (null = immediately). <paramref name="Priority"/>
/// orders the claim within a lane — HIGHER runs first (default 0), then oldest-available, then FIFO.
/// <paramref name="PartitionKey"/> (null = unpartitioned, the default) turns a set of jobs sharing a
/// <c>(lane, key)</c> into an actor mailbox: at most one such job runs at a time and they run in strict
/// FIFO order (priority is IGNORED WITHIN a partition); jobs with different keys (or no key) still run in
/// parallel up to the lane's concurrency.</summary>
public sealed record JobSpec(
    string Lane,
    string Type,
    string Payload,
    int? MaxAttempts = null,
    DateTimeOffset? AvailableAt = null,
    int Priority = 0,
    string? PartitionKey = null)
{
    /// <summary>The attempt budget applied to a spec whose <see cref="JobSpec.MaxAttempts"/> is null — the ONE home
    /// for the number. Every <see cref="Storage.IJobStore"/> backend reads it, and it seeds
    /// <see cref="JobOptions.DefaultMaxAttempts"/>, which is what <see cref="IJobQueue"/> fills in and an app
    /// can configure. A BYO backend should read it too.</summary>
    public const int DefaultMaxAttempts = 3;
}

/// <summary>A persisted job row, as returned by a claim. <paramref name="Checkpoint"/> is the last
/// progress the handler saved (null on the first run, non-null on a resume). <paramref name="Progress"/>/
/// <paramref name="Total"/>/<paramref name="Stage"/> are the live progress snapshot reported by the
/// handler (readable while it runs, e.g. for a UI); <paramref name="StepLog"/> is the JSON step log (parse
/// with <see cref="JobStepLog.Parse"/>).</summary>
public sealed record JobRecord(
    Guid Id,
    string Lane,
    string Type,
    string Payload,
    JobStatus Status,
    string? Checkpoint,
    int Attempts,
    int MaxAttempts,
    string? LastError,
    DateTimeOffset AvailableAt,
    DateTimeOffset? ClaimedAt,
    string? ClaimedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Priority = 0,
    bool CancelRequested = false,
    int Progress = 0,
    int Total = 0,
    string? Stage = null,
    string? StepLog = null,
    string? PartitionKey = null)
{
    private readonly JobMessage? _stageMessage;

    /// <summary>The live stage as a <see cref="JobMessage"/> — with its code and arguments when it was reported as
    /// one, else <see cref="Stage"/>'s text alone. Null exactly when <see cref="Stage"/> is.
    /// <para>Derived on read, not stored beside <see cref="Stage"/>: a <c>with</c> that changes the stage copies
    /// the message too, so a carried message counts only while its text IS the stage.</para></summary>
    public JobMessage? StageMessage
    {
        get => Stage is null ? null
            : _stageMessage is { } carried && string.Equals(carried.Text, Stage, StringComparison.Ordinal) ? carried
            : new JobMessage(Stage);
        init => _stageMessage = value;
    }
}
