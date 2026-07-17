namespace Lyntai.Jobs;

/// <summary>
/// What a running job sees: its <see cref="Payload"/>, the <see cref="Checkpoint"/> it last saved (null
/// on the first run, non-null on a <b>resume</b>), the current <see cref="Attempts"/>, and
/// <see cref="SaveCheckpointAsync"/> to persist progress. Saving a checkpoint also renews the job's lease.
/// </summary>
public sealed class JobContext
{
    private readonly Func<string, CancellationToken, Task<bool>> _saveCheckpoint;

    public JobContext(Guid jobId, string payload, string? checkpoint, int attempts,
        Func<string, CancellationToken, Task<bool>> saveCheckpoint)
    {
        JobId = jobId;
        Payload = payload;
        Checkpoint = checkpoint;
        Attempts = attempts;
        _saveCheckpoint = saveCheckpoint;
    }

    public Guid JobId { get; }

    public string Payload { get; }

    /// <summary>The last checkpoint this job persisted. Null on the first run; on a resume it's whatever
    /// the handler saved before the previous process died — resume from here.</summary>
    public string? Checkpoint { get; }

    /// <summary>This attempt's number (1 on the first run).</summary>
    public int Attempts { get; }

    /// <summary>Persist progress and renew the lease. Returns false if this worker lost the lease (the job
    /// was re-claimed by another) — a handler that sees false should stop, since its work is being redone.</summary>
    public Task<bool> SaveCheckpointAsync(string checkpoint, CancellationToken ct = default) => _saveCheckpoint(checkpoint, ct);
}
