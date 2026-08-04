namespace Lyntai.Media;

/// <summary>
/// OPTIONAL capability of an <see cref="IMediaProvider"/> whose backend generates ASYNCHRONOUSLY: submit a
/// request, get an operation id, poll until terminal, then fetch. This is how essentially every video backend
/// works (renders take minutes) and how batch music works.
///
/// It is a separate interface, and separate CALLS, for a reason: folding "submit and wait" into
/// <see cref="IMediaProvider.GenerateAsync"/> would bury an unbounded poll loop inside one method — no
/// progress, no cancellation of the remote job, and nothing left to resume after a process restart. With the
/// operation id exposed, a render survives a restart (persist the id; poll again later) and composes with
/// <c>Lyntai.Jobs</c>.
/// </summary>
/// <remarks>WEBHOOKS: several backends can POST a completion to a URL instead of being polled. Lyntai is a
/// library and hosts nothing — the APP owns that endpoint, and calls <see cref="FetchAsync"/> with the
/// operation id when it fires. That is why fetch-by-id is on this contract rather than hidden inside a poll
/// loop.</remarks>
public interface IMediaJobProvider
{
    /// <summary>Submit the request and return immediately with an operation to track. Fails safe: a rejected
    /// submission comes back as a <see cref="MediaOperationStatus.Failed"/> operation with a reason.</summary>
    Task<MediaOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default);

    /// <summary>Ask the backend where an operation is. Cheap and safe to call repeatedly; carries
    /// <see cref="MediaOperation.Progress"/> where the backend reports it.</summary>
    Task<MediaOperation> PollAsync(string operationId, CancellationToken ct = default);

    /// <summary>Collect the artifacts of a SUCCEEDED operation. Callable from anywhere that has the id —
    /// including an app's webhook handler.</summary>
    Task<MediaResult> FetchAsync(string operationId, CancellationToken ct = default);

    /// <summary>Ask the backend to abandon the operation, where it supports that. A backend that cannot
    /// cancel should say so in the returned detail rather than throw.</summary>
    Task<MediaOperation> CancelAsync(string operationId, CancellationToken ct = default);
}

/// <summary>Where an asynchronous generation currently is.</summary>
public enum MediaOperationStatus
{
    /// <summary>Accepted, not started.</summary>
    Queued,

    /// <summary>Being generated.</summary>
    Running,

    /// <summary>Finished — artifacts are fetchable.</summary>
    Succeeded,

    /// <summary>Ended without artifacts; the reason is in the detail.</summary>
    Failed,

    /// <summary>Abandoned at the caller's request.</summary>
    Cancelled,
}

/// <summary>A handle on an asynchronous generation. The id is the whole point: persist it and a render
/// survives a restart.</summary>
/// <param name="Id">The backend's own operation/task id.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Progress">0..1 where the backend reports progress; null when it doesn't.</param>
/// <param name="Detail">The backend's own words — a queue position, a failure reason.</param>
public sealed record MediaOperation(
    string Id,
    MediaOperationStatus Status,
    double? Progress = null,
    string? Detail = null)
{
    /// <summary>Whether this operation has stopped changing (succeeded, failed or cancelled).</summary>
    public bool IsTerminal => Status is MediaOperationStatus.Succeeded or MediaOperationStatus.Failed
        or MediaOperationStatus.Cancelled;
}
