namespace Lyntai.Generation.Routing;

/// <summary>Picks a capable media backend and falls over when one fails. The media counterpart of
/// <c>ILlmRouter</c>, kept separate because media routing must filter on CAPABILITY first — a chat model
/// always takes text, whereas a video backend simply cannot serve an image request.</summary>
public interface IGenerationRouter
{
    /// <summary>Generate inline through the first capable candidate, advancing on a fallible verdict.</summary>
    /// <remarks>When no candidate succeeds, the reported result is the first SUBSTANTIVE failure — a blameless
    /// verdict (<see cref="GenerationVerdict.NotConfigured"/>, <see cref="GenerationVerdict.Unsupported"/>)
    /// never masks a real one, or a caller would be sent off to set up a key while the backend they HAD
    /// configured is the one that is down. Failing that, it is the first blameless result that gave a REASON:
    /// "this backend cannot take an input image" and "your prompt is too long for me" are answers a caller can
    /// act on, and they used to be replaced by a synthetic "every capable backend reported it is not
    /// configured" that was not even accurate. Only a run in which nothing said anything reports that
    /// sentence.</remarks>
    Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default);

    /// <summary>Submit an asynchronous generation through the first capable job-capable candidate. The
    /// returned operation is paired with the provider id that owns it, because an operation id only means
    /// something to the backend that issued it.</summary>
    /// <remarks>The per-verdict fallback policy governs this path too, at one remove: a submission comes back
    /// carrying a status, not a verdict, so a rejected one is classified from the backend's own
    /// <see cref="GenerationOperation.Detail"/> and then answered by the same table — a rate limit benches the
    /// backend, an unconfigured one is advanced past blamelessly, and a rejection nothing recognises still
    /// counts toward the dead-host threshold.
    /// <para>Two rules hold whatever the text says. A submission the backend never answered
    /// (<see cref="GenerationOperation.Inconclusive"/>) surfaces rather than advancing, and is not held
    /// against the backend, because trying the next candidate could buy the same render twice — that is
    /// decided BEFORE the verdict is. And when no candidate accepted the job, the returned
    /// <see cref="GenerationSubmission.ProviderId"/> is EMPTY; the first rejecting backend and its reason are
    /// folded into the operation's detail instead, so the id field keeps meaning exactly one thing — the first
    /// SUBSTANTIVE rejection where there was one, otherwise a blameless rejection that still explained itself,
    /// on the same rule <see cref="GenerateAsync"/> follows.</para></remarks>
    Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default);
}

/// <summary>An accepted asynchronous generation plus the backend that owns it — persist BOTH: an operation id
/// is meaningless without knowing who issued it.</summary>
/// <param name="ProviderId">The backend holding the operation; empty when no candidate accepted the job.</param>
/// <param name="Operation">The operation handle.</param>
public sealed record GenerationSubmission(string ProviderId, GenerationOperation Operation);
