using Lyntai.Storage;

namespace Lyntai.Jobs;

/// <summary>The front door for durable jobs — a thin, injectable wrapper over <see cref="IJobStore"/> that
/// fills in defaults (e.g. max attempts) and exposes dead-letter inspection/replay.</summary>
public interface IJobQueue
{
    Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default);

    /// <summary>Convenience overload — enqueue with default attempts, immediately available, at
    /// <paramref name="priority"/> (higher runs first within the lane). <paramref name="partitionKey"/>
    /// (null = unpartitioned) serializes jobs sharing a <c>(lane, key)</c> into a FIFO actor mailbox.</summary>
    Task<Guid> EnqueueAsync(string lane, string type, string payload, int priority = 0, string? partitionKey = null, CancellationToken ct = default);

    /// <summary>One job's current record — status, attempts, checkpoint, and the LIVE progress fields
    /// (<see cref="JobRecord.Progress"/>/<see cref="JobRecord.Total"/>/<see cref="JobRecord.Stage"/>/
    /// <see cref="JobRecord.StepLog"/>) while it runs. Null when unknown. The front door's read side —
    /// no need to inject the storage-layer <see cref="IJobStore"/> just to watch a job.</summary>
    Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>List jobs, optionally filtered by <paramref name="status"/> and/or <paramref name="lane"/>,
    /// newest first.</summary>
    Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default);

    /// <summary>The dead-letter queue: jobs that exhausted their retries (<see cref="JobStatus.Dead"/>),
    /// newest first, for inspection.</summary>
    Task<IReadOnlyList<JobRecord>> ListDeadAsync(string? lane = null, int limit = 100, CancellationToken ct = default);

    /// <summary>Requeue a dead-lettered (or Failed) job for another run. Returns whether one was requeued.</summary>
    Task<bool> ReplayAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cancel a job whether it hasn't started (Pending or <see cref="JobStatus.Paused"/> — cancelled
    /// immediately) or is Running (cancellation is requested — the worker stops it cooperatively). Returns
    /// whether anything was cancelled/requested.
    /// <para>A held job needs no <see cref="ResumeAsync"/> first: the immediate half reaches
    /// <see cref="JobStatus.Paused"/> too, so the job is never briefly claimable on its way to
    /// <see cref="JobStatus.Cancelled"/>.</para></summary>
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Administratively hold a Pending job (Pending → Paused) so it isn't claimed until resumed.
    /// Returns whether it was paused. A held job stays cancellable — see <see cref="CancelAsync"/>.</summary>
    Task<bool> PauseAsync(Guid id, CancellationToken ct = default);

    /// <summary>Release a held job (Paused → Pending). Returns whether it was resumed.</summary>
    Task<bool> ResumeAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc/>
public sealed class JobQueue(IJobStore? store, LyntaiOptions options) : IJobQueue
{
    /// <summary>The one no-store failure message the queue AND the runner throw (kept identical so an
    /// operator sees the same fix whichever entry point hit it first).</summary>
    internal const string RequiresStorageMessage =
        "Durable jobs require a storage backend — call UseSqliteStorage / UsePostgresStorage / UseInMemoryStorage.";

    // durable jobs REQUIRE persistence — fail loudly rather than silently dropping work (unlike the
    // fail-open cortex helpers)
    private readonly IJobStore _store = store ?? throw new InvalidOperationException(RequiresStorageMessage);

    public Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default) =>
        _store.EnqueueAsync(spec with { MaxAttempts = spec.MaxAttempts ?? options.Jobs.DefaultMaxAttempts }, ct);

    public Task<Guid> EnqueueAsync(string lane, string type, string payload, int priority = 0, string? partitionKey = null, CancellationToken ct = default) =>
        EnqueueAsync(new JobSpec(lane, type, payload, Priority: priority, PartitionKey: partitionKey), ct);

    public Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default) => _store.GetAsync(id, ct);

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default) =>
        _store.ListAsync(status, lane, limit, ct);

    public Task<IReadOnlyList<JobRecord>> ListDeadAsync(string? lane = null, int limit = 100, CancellationToken ct = default) =>
        _store.ListAsync(JobStatus.Dead, lane, limit, ct);

    public Task<bool> ReplayAsync(Guid id, CancellationToken ct = default) => _store.ReplayAsync(id, ct);

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default) =>
        await _store.CancelAsync(id, ct).ConfigureAwait(false)          // Pending/Paused → Cancelled outright
        || await _store.RequestCancelAsync(id, ct).ConfigureAwait(false); // else Running → request cancellation

    public Task<bool> PauseAsync(Guid id, CancellationToken ct = default) => _store.PauseAsync(id, ct);

    public Task<bool> ResumeAsync(Guid id, CancellationToken ct = default) => _store.ResumeAsync(id, ct);
}
