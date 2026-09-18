using Lyntai.Inference;
namespace Lyntai.Generation;

/// <summary>
/// OPTIONAL capability of an <see cref="IModelProvider"/> whose backend generates ASYNCHRONOUSLY: submit a
/// request, get an operation id, poll until terminal, then fetch. This is how essentially every video backend
/// works (renders take minutes) and how batch music works.
///
/// It is a separate interface, and separate CALLS, for a reason: folding "submit and wait" into
/// <see cref="IModelProvider.GenerateAsync"/> would bury an unbounded poll loop inside one method — no
/// progress, no cancellation of the remote job, and nothing left to resume after a process restart. With the
/// operation id exposed, a render survives a restart (persist the id; poll again later) and composes with
/// <c>Lyntai.Jobs</c>.
/// </summary>
/// <remarks>WEBHOOKS: several backends can POST a completion to a URL instead of being polled. Lyntai is a
/// library and hosts nothing — the APP owns that endpoint, and calls <see cref="FetchAsync"/> with the
/// operation id when it fires. That is why fetch-by-id is on this contract rather than hidden inside a poll
/// loop.</remarks>
public interface IGenerationJobProvider
{
    /// <summary>Submit the request and return immediately with an operation to track. Fails safe: a rejected
    /// submission comes back as a <see cref="QueuedOperationStatus.Failed"/> operation with a reason.</summary>
    Task<QueuedOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default);

    /// <summary>Ask the backend where an operation is. Cheap and safe to call repeatedly; carries
    /// <see cref="QueuedOperation.Progress"/> where the backend reports it.</summary>
    Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default);

    /// <summary>Collect the artifacts of a SUCCEEDED operation. Callable from anywhere that has the id —
    /// including an app's webhook handler.</summary>
    Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default);

    /// <summary>Ask the backend to abandon the operation, where it supports that. A backend that cannot
    /// cancel should say so in the returned detail rather than throw.</summary>
    Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default);
}
