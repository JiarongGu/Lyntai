using Lyntai.Jobs;
using Lyntai.Storage;

namespace Lyntai.Tests.Fakes;

/// <summary>An <see cref="IJobStore"/> that forwards everything to an inner store and can be told to THROW
/// from <see cref="ClaimNextAsync"/> — the window between a slot acquire and its claim, where a transient
/// store fault (or a shutdown cancellation) lands in production.</summary>
public sealed class ClaimThrowingJobStore(IJobStore inner) : IJobStore
{
    /// <summary>When true, <see cref="ClaimNextAsync"/> throws instead of claiming.</summary>
    public bool ThrowOnClaim { get; set; }

    public Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default) => inner.EnqueueAsync(spec, ct);

    public Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default) =>
        ThrowOnClaim
            ? throw new InvalidOperationException("the store blipped between the slot acquire and the claim")
            : inner.ClaimNextAsync(lane, workerId, lease, ct);

    public Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default) =>
        inner.SaveCheckpointAsync(id, workerId, checkpoint, ct);

    public Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default) =>
        inner.ReportProgressAsync(id, workerId, done, total, stage, ct);

    public Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default) =>
        inner.ReportStepAsync(id, workerId, message, ct);

    public Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default) => inner.CompleteAsync(id, workerId, ct);

    public Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default) =>
        inner.FailAsync(id, workerId, error, retryAt, ct);

    public Task<bool> PollAgainAsync(Guid id, string workerId, DateTimeOffset runAt, CancellationToken ct = default) =>
        inner.PollAgainAsync(id, workerId, runAt, ct);

    public Task<bool> DeadLetterAsync(Guid id, string workerId, string error, CancellationToken ct = default) =>
        inner.DeadLetterAsync(id, workerId, error, ct);

    public Task<bool> ReplayAsync(Guid id, CancellationToken ct = default) => inner.ReplayAsync(id, ct);

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) => inner.CancelAsync(id, ct);

    public Task<bool> PauseAsync(Guid id, CancellationToken ct = default) => inner.PauseAsync(id, ct);

    public Task<bool> ResumeAsync(Guid id, CancellationToken ct = default) => inner.ResumeAsync(id, ct);

    public Task<bool> RequestCancelAsync(Guid id, CancellationToken ct = default) => inner.RequestCancelAsync(id, ct);

    public Task<bool> CancelRunningAsync(Guid id, string workerId, CancellationToken ct = default) =>
        inner.CancelRunningAsync(id, workerId, ct);

    public Task<int> CountRunningAsync(string lane, CancellationToken ct = default) => inner.CountRunningAsync(lane, ct);

    public Task<int?> TryAcquireSlotAsync(int cap, string workerId, TimeSpan lease, CancellationToken ct = default) =>
        inner.TryAcquireSlotAsync(cap, workerId, lease, ct);

    public Task ReleaseSlotAsync(int slotIndex, string workerId, CancellationToken ct = default) =>
        inner.ReleaseSlotAsync(slotIndex, workerId, ct);

    public Task HeartbeatSlotsAsync(string workerId, CancellationToken ct = default) => inner.HeartbeatSlotsAsync(workerId, ct);

    public Task<IReadOnlyList<string>> ActiveLanesAsync(CancellationToken ct = default) => inner.ActiveLanesAsync(ct);

    public Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default) =>
        inner.ListAsync(status, lane, limit, ct);
}
