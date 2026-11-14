using Lyntai.Inference;
using Lyntai.Jobs;

namespace Lyntai.Generation.Jobs;

/// <summary>Cancels a durable generation job AND the render its backend is running. Cancelling the job alone
/// (<see cref="IJobQueue.CancelAsync"/>) stops the polling and leaves a paid render or a GPU run going: neither job
/// handler cancels a backend on its own, because a job is cancelled between polls when no handler runs, and a
/// handler's token cannot tell a cancel from a shutdown, which must leave a resumable render alone. So the host asks
/// for both here.</summary>
/// <remarks>Also the way to stop a render whose job already ended without it — one that failed past
/// <see cref="GenerationPipelineJobOptions.MaxPollDuration"/> still holds its operation in its checkpoint.</remarks>
public static class GenerationJobCancellation
{
    /// <summary>Cancel job <paramref name="jobId"/>, then ask the backend that holds its submitted operation to cancel
    /// that operation (<see cref="IMediaJobProvider.CancelAsync"/>). The job is cancelled FIRST, so no worker polls
    /// or fetches the render while its backend is being asked.</summary>
    /// <param name="queue">The queue the job is in.</param>
    /// <param name="providers">The registered backends; the one the checkpoint names is asked.</param>
    /// <param name="jobId">A <see cref="GenerationRenderJobHandler"/> or <see cref="GenerationPipelineJobHandler"/>
    /// job.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The backend's answer, which says whether the cancel landed (a backend may report the render still
    /// running). Null when the job holds no submitted operation: no such job, another job type, a stage not yet
    /// submitted or inline, or a render already fetched. When the backend holding it is no longer registered, a
    /// still-running operation whose detail names both, since nothing was asked.</returns>
    public static async Task<QueuedOperation?> CancelAsync(
        IJobQueue queue, IEnumerable<IModelProvider> providers, Guid jobId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(providers);

        await queue.CancelAsync(jobId, ct).ConfigureAwait(false);
        if (await queue.GetAsync(jobId, ct).ConfigureAwait(false) is not { } job ||
            GenerationJobEngine.HeldOperation(job.Type, job.Checkpoint) is not var (providerId, operationId))
            return null;

        return MediaJobBackends.Find(providers, providerId) is { } backend
            ? await backend.CancelAsync(operationId, ct).ConfigureAwait(false)
            : new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: $"cancel not sent: backend '{providerId}' holds operation {operationId} but is not registered");
    }
}
