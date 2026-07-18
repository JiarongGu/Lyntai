using Lyntai.Storage;

namespace Lyntai.Jobs;

/// <summary>The front door for durable jobs — a thin, injectable wrapper over <see cref="IJobStore"/> that
/// fills in defaults (e.g. max attempts) and exposes dead-letter inspection/replay.</summary>
public interface IJobQueue
{
    Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default);

    /// <summary>Convenience overload — enqueue with default attempts, immediately available, at
    /// <paramref name="priority"/> (higher runs first within the lane).</summary>
    Task<Guid> EnqueueAsync(string lane, string type, string payload, int priority = 0, CancellationToken ct = default);

    /// <summary>The dead-letter queue: jobs that exhausted their retries (<see cref="JobStatus.Dead"/>),
    /// newest first, for inspection.</summary>
    Task<IReadOnlyList<JobRecord>> ListDeadAsync(string? lane = null, int limit = 100, CancellationToken ct = default);

    /// <summary>Requeue a dead-lettered (or Failed) job for another run. Returns whether one was requeued.</summary>
    Task<bool> ReplayAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc/>
public sealed class JobQueue(IJobStore? store, LyntaiOptions options) : IJobQueue
{
    // durable jobs REQUIRE persistence — fail loudly rather than silently dropping work (unlike the
    // fail-open cortex helpers)
    private readonly IJobStore _store = store ?? throw new InvalidOperationException(
        "Durable jobs require a storage backend — call UseSqliteStorage / UsePostgresStorage / UseInMemoryStorage.");

    public Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default) =>
        _store.EnqueueAsync(spec with { MaxAttempts = spec.MaxAttempts ?? options.Jobs.DefaultMaxAttempts }, ct);

    public Task<Guid> EnqueueAsync(string lane, string type, string payload, int priority = 0, CancellationToken ct = default) =>
        EnqueueAsync(new JobSpec(lane, type, payload, Priority: priority), ct);

    public Task<IReadOnlyList<JobRecord>> ListDeadAsync(string? lane = null, int limit = 100, CancellationToken ct = default) =>
        _store.ListAsync(JobStatus.Dead, lane, limit, ct);

    public Task<bool> ReplayAsync(Guid id, CancellationToken ct = default) => _store.ReplayAsync(id, ct);
}
