using Lyntai.Storage;

namespace Lyntai.Jobs;

/// <summary>The enqueue front door for durable jobs — a thin, injectable wrapper over
/// <see cref="IJobStore"/> that fills in defaults (e.g. max attempts).</summary>
public interface IJobQueue
{
    Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default);

    /// <summary>Convenience overload — enqueue with default attempts, immediately available.</summary>
    Task<Guid> EnqueueAsync(string lane, string type, string payload, CancellationToken ct = default);
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

    public Task<Guid> EnqueueAsync(string lane, string type, string payload, CancellationToken ct = default) =>
        EnqueueAsync(new JobSpec(lane, type, payload), ct);
}
