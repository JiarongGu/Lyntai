namespace Lyntai.Inference;


/// <summary>The outcome of a media generation.</summary>
/// <param name="Verdict">Why it ended this way.</param>
/// <param name="Artifacts">What was produced — empty unless <paramref name="Verdict"/> is
/// <see cref="ProviderVerdict.Ok"/>.</param>
/// <param name="Usage">What the backend said it cost, when it says.</param>
/// <param name="Detail">The backend's own words, or the failure reason. Surface verbatim rather than parsing:
/// the wording belongs to the backend and changes.</param>
public sealed record MediaResponse(
    ProviderVerdict Verdict,
    IReadOnlyList<MediaArtifact> Artifacts,
    MediaUsage? Usage = null,
    string? Detail = null)
    : IProviderOutcome
{
    /// <summary>Whether the call produced media.</summary>
    public bool IsOk => Verdict == ProviderVerdict.Ok;

    /// <summary>A successful result. Throws for an EMPTY artifact list: an "Ok" carrying nothing is the
    /// empty-Ok mistake the LLM side already paid for (<c>.claude/knowledge/pitfalls.md</c>) — it robs routing
    /// of its chance to fall over and hands the caller a successful nothing.</summary>
    public static MediaResponse Success(
        IReadOnlyList<MediaArtifact> artifacts, MediaUsage? usage = null, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts); // parity with the vector/score twins — a null must
                                                      // name the argument, not surface as an NRE on .Count
        if (artifacts.Count == 0)
            throw new ArgumentException("a successful media result needs at least one artifact", nameof(artifacts));
        return new MediaResponse(ProviderVerdict.Ok, artifacts, usage, detail);
    }

    /// <summary>A failed result — no artifacts, a reason, and a verdict routing can act on.</summary>
    public static MediaResponse Failure(ProviderVerdict verdict, string? detail = null) =>
        new(verdict, [], null, detail);
}
