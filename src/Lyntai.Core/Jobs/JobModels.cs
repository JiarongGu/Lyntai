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

    /// <summary>Gave up after exhausting attempts or a hard failure. Terminal (the app may re-enqueue).</summary>
    Failed,

    /// <summary>Cancelled before it ran. Terminal.</summary>
    Cancelled,
}

/// <summary>What to enqueue: the <paramref name="Lane"/> (execution lane, for concurrency), the
/// <paramref name="Type"/> (dispatches to the matching <see cref="IJobHandler"/>), and the
/// <paramref name="Payload"/> (JSON the handler reads). <paramref name="MaxAttempts"/> bounds retries;
/// <paramref name="AvailableAt"/> delays first execution (null = immediately).</summary>
public sealed record JobSpec(
    string Lane,
    string Type,
    string Payload,
    int? MaxAttempts = null,
    DateTimeOffset? AvailableAt = null);

/// <summary>A persisted job row, as returned by a claim. <paramref name="Checkpoint"/> is the last
/// progress the handler saved (null on the first run, non-null on a resume).</summary>
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
    DateTimeOffset UpdatedAt);
