namespace Lyntai.Media.Routing;

/// <summary>Picks a capable media backend and falls over when one fails. The media counterpart of
/// <c>ILlmRouter</c>, kept separate because media routing must filter on CAPABILITY first — a chat model
/// always takes text, whereas a video backend simply cannot serve an image request.</summary>
public interface IMediaRouter
{
    /// <summary>Generate inline through the first capable candidate, advancing on a fallible verdict.</summary>
    Task<MediaResult> GenerateAsync(
        IReadOnlyList<MediaCandidate> candidates, MediaRequest request, CancellationToken ct = default);

    /// <summary>Submit an asynchronous generation through the first capable job-capable candidate. The
    /// returned operation is paired with the provider id that owns it, because an operation id only means
    /// something to the backend that issued it.</summary>
    Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<MediaCandidate> candidates, MediaRequest request, CancellationToken ct = default);
}

/// <summary>An accepted asynchronous generation plus the backend that owns it — persist BOTH: an operation id
/// is meaningless without knowing who issued it.</summary>
/// <param name="ProviderId">The backend holding the operation; empty when no candidate accepted the job.</param>
/// <param name="Operation">The operation handle.</param>
public sealed record MediaSubmission(string ProviderId, MediaOperation Operation);
