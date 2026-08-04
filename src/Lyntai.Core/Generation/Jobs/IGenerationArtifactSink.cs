namespace Lyntai.Generation.Jobs;

/// <summary>Where a finished durable generation's artifacts go. Implemented by the APP, because the platform
/// deliberately doesn't own output locations (<c>docs/DECISIONS.md</c> D26/D30) — Lyntai routes and tracks the
/// render; the app decides whether the bytes become a file, a blob, a database row or a UI notification.</summary>
/// <remarks>Required rather than optional for the render job handler: a durable render whose artifacts were
/// dropped on the floor completed in name only, so there is no sensible default to fall back to.</remarks>
public interface IGenerationArtifactSink
{
    /// <summary>Take delivery of a completed render. Throwing marks the job for retry — so an implementation
    /// that cannot store the artifacts right now should throw rather than swallow, and one that CAN must be
    /// safe to call twice (a retry after a failed store re-delivers).</summary>
    Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default);
}

/// <summary>A completed render, handed to the app.</summary>
/// <param name="JobId">The durable job that produced it — the app's correlation handle.</param>
/// <param name="ProviderId">Which backend produced it (an operation id only means something to its issuer).</param>
/// <param name="OperationId">The backend's operation id, kept so the app can re-fetch or audit.</param>
/// <param name="Artifacts">What was produced. Remember these may carry a URI rather than bytes — the platform
/// never downloads on the caller's behalf.</param>
/// <param name="Usage">What the backend said it cost, where it says.</param>
public sealed record GenerationArtifactDelivery(
    Guid JobId,
    string ProviderId,
    string OperationId,
    IReadOnlyList<GenerationArtifact> Artifacts,
    GenerationUsage? Usage = null);
