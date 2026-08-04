namespace Lyntai.Generation;

/// <summary>One produced piece of media. Mirrors <see cref="GenerationInput"/>: bytes when the backend returned
/// them, a URI when it returned a location (many video backends hand back a signed URL, and downloading it
/// is the CALLER's decision — the platform never silently fetches megabytes).</summary>
/// <param name="MediaType">MIME type of the artifact (<c>image/png</c>, <c>video/mp4</c>, <c>audio/mpeg</c>).</param>
/// <param name="Data">The bytes, when the backend returned them inline.</param>
/// <param name="Uri">Where the artifact lives, when the backend returned a location instead.</param>
/// <param name="Metadata">Whatever the backend said about it (seed, revised prompt, duration, dimensions),
/// verbatim — useful in a diagnostics pane and for reproducing a generation.</param>
public sealed record GenerationArtifact(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Use this artifact as an INPUT to a further generation — the chaining primitive behind
    /// pipelines like 3d → image → video, or image → video-from-first-frame. Carries bytes or the URI through
    /// unchanged: a backend that can read the URL itself should not be forced through a download the caller
    /// never asked for.</summary>
    /// <param name="role">What the artifact is to the next stage (a <see cref="GenerationInputRoles"/> value).</param>
    public GenerationInput ToInput(string? role = null) => new(MediaType, Data, Uri, role);
}

/// <summary>What a generation cost, as far as the backend reported it. Every field is nullable because
/// backends report wildly different things (a count of images, seconds of video/audio, a price) and the
/// platform never INVENTS a cost from a token price.</summary>
/// <param name="Count">Number of artifacts billed.</param>
/// <param name="Seconds">Duration produced, for time-based media.</param>
/// <param name="CostUsd">What the backend said it cost.</param>
public sealed record GenerationUsage(int? Count = null, double? Seconds = null, double? CostUsd = null);
