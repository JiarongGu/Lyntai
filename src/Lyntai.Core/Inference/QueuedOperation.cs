namespace Lyntai.Inference;

/// <summary>Where a queued call currently is.</summary>
public enum QueuedOperationStatus
{
    /// <summary>Accepted, not started.</summary>
    Queued,

    /// <summary>Being worked on.</summary>
    Running,

    /// <summary>Finished — the answer is fetchable.</summary>
    Succeeded,

    /// <summary>Ended without an answer; the reason is in the detail.</summary>
    Failed,

    /// <summary>Abandoned at the caller's request.</summary>
    Cancelled,
}

/// <summary>A handle on a queued call. The id is the whole point: persist it and the work survives a
/// restart.
///
/// <para><b>Lives in <c>Lyntai.Inference</c> rather than beside any one domain</b>, because queued delivery
/// is a shape any KIND can serve — a batch API that queues chat or embeddings uses this same handle
/// (<c>docs/DECISIONS.md</c> <b>D153</b>).</para></summary>
/// <param name="Id">The backend's own operation/task id.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Progress">0..1 where the backend reports progress; null when it doesn't.</param>
/// <param name="Detail">The backend's own words — a queue position, a failure reason.</param>
public sealed record QueuedOperation(
    string Id,
    QueuedOperationStatus Status,
    double? Progress = null,
    string? Detail = null)
{
    /// <summary>Whether this operation has stopped changing (succeeded, failed or cancelled).</summary>
    public bool IsTerminal => Status is QueuedOperationStatus.Succeeded or QueuedOperationStatus.Failed
        or QueuedOperationStatus.Cancelled;

    /// <summary>Set on a <see cref="QueuedOperationStatus.Failed"/> submission whose outcome is
    /// <b>not known</b> — no answer arrived, so the backend may or may not have accepted the work.
    ///
    /// <para>It exists because those two cases are worth different money. A backend that ANSWERS "no" can be
    /// retried elsewhere for free; a backend that never answered may already have enqueued a billable render,
    /// and submitting the same request to a second one buys the same generation twice. That is exactly what
    /// <c>GenerationRenderJobHandler</c>'s checkpoint-first ordering exists to prevent, and it is the same
    /// reasoning that makes a timed-out POLL report <see cref="QueuedOperationStatus.Running"/>: no answer
    /// is not evidence of failure.</para>
    ///
    /// <para><b>What acts on it:</b> <c>MediaRouter.SubmitAsync</c> surfaces such a submission instead of
    /// advancing to the next candidate, and does not count it against the backend's dead-host cooldown — no
    /// answer is no evidence of ill health either. The <see cref="Status"/> stays
    /// <see cref="QueuedOperationStatus.Failed"/> on purpose, so every existing status check behaves
    /// exactly as before; only code that opts into this flag changes.</para></summary>
    public bool Inconclusive { get; init; }
}
