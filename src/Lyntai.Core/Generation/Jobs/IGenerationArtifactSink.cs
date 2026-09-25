using Lyntai.Inference;

namespace Lyntai.Generation.Jobs;

/// <summary>Where a finished durable generation's artifacts go. Implemented by the APP, because the platform
/// deliberately doesn't own output locations (<c>docs/DECISIONS.md</c> D20/D24) — Lyntai routes and tracks the
/// render; the app decides whether the bytes become a file, a blob, a database row or a UI notification.</summary>
/// <remarks>Required rather than optional for the render and pipeline job handlers: a durable render whose
/// artifacts were dropped on the floor completed in name only, so there is no sensible default to fall back to.</remarks>
public interface IGenerationArtifactSink
{
    /// <summary>Take delivery of a completed render. Throwing marks the job for retry — so an implementation
    /// that cannot store the artifacts right now should throw rather than swallow, and one that CAN must be
    /// safe to call twice (a retry after a failed store re-delivers).
    /// <para><b>Implementing it:</b> a pipeline job delivers once PER STAGE under one
    /// <see cref="GenerationArtifactDelivery.JobId"/>, so a sink that makes a redelivery a no-op keys it on
    /// the job id AND <see cref="GenerationArtifactDelivery.StageIndex"/> — keyed on the job id alone, every
    /// stage after the first is discarded as a duplicate.</para></summary>
    Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default);
}

/// <summary>A completed render, handed to the app.</summary>
/// <param name="JobId">The durable job that produced it — the app's correlation handle.</param>
/// <param name="ProviderId">Which backend produced it (an operation id only means something to its issuer).
/// Empty for a pipeline stage that ran inline, because the inline door does not report which candidate served
/// it.</param>
/// <param name="OperationId">The backend's operation id, kept so the app can re-fetch or audit. Empty for a
/// pipeline stage that ran inline, which has none.</param>
/// <param name="Artifacts">What was produced. Remember these may carry a URI rather than bytes — the platform
/// never downloads on the caller's behalf.</param>
/// <param name="Usage">What the backend said it cost, where it says.</param>
public sealed record GenerationArtifactDelivery(
    Guid JobId,
    string ProviderId,
    string OperationId,
    IReadOnlyList<MediaArtifact> Artifacts,
    MediaUsage? Usage = null)
{
    /// <summary>Which pipeline stage produced these artifacts, 0-based as
    /// <see cref="GenerationPipelineJob.Stages"/> indexes them — the job's own messages number stages from 1,
    /// so index 1 is "stage 2". Null for a delivery from outside a pipeline, such as a render job's.</summary>
    public int? StageIndex { get; init; }

    /// <summary>Whether these artifacts are the job's OUTPUT: true, the default, for a render job's delivery and
    /// a pipeline's last stage; false for an earlier pipeline stage, delivered because it was paid for and a
    /// later stage may fail.
    /// <para><b>Using it:</b> a sink that keeps only what a job produced skips a delivery whose value is
    /// false; one that keeps every paid-for artifact reads it only as a label.</para></summary>
    public bool IsFinal { get; init; } = true;
}
