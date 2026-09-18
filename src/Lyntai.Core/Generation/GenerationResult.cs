using Lyntai.Inference;

namespace Lyntai.Generation;


/// <summary>The outcome of a media generation.</summary>
/// <param name="Verdict">Why it ended this way.</param>
/// <param name="Artifacts">What was produced — empty unless <paramref name="Verdict"/> is
/// <see cref="ProviderVerdict.Ok"/>.</param>
/// <param name="Usage">What the backend said it cost, when it says.</param>
/// <param name="Detail">The backend's own words, or the failure reason. Surface verbatim rather than parsing:
/// the wording belongs to the backend and changes.</param>
public sealed record GenerationResult(
    ProviderVerdict Verdict,
    IReadOnlyList<GenerationArtifact> Artifacts,
    GenerationUsage? Usage = null,
    string? Detail = null)
    : IProviderOutcome
{
    /// <summary>Whether the call produced media.</summary>
    public bool IsOk => Verdict == ProviderVerdict.Ok;

    /// <summary>A successful result. Throws for an EMPTY artifact list: an "Ok" carrying nothing is the
    /// empty-Ok mistake the LLM side already paid for (<c>.claude/knowledge/pitfalls.md</c>) — it robs routing
    /// of its chance to fall over and hands the caller a successful nothing.</summary>
    public static GenerationResult Success(
        IReadOnlyList<GenerationArtifact> artifacts, GenerationUsage? usage = null, string? detail = null)
    {
        if (artifacts.Count == 0)
            throw new ArgumentException("a successful media result needs at least one artifact", nameof(artifacts));
        return new GenerationResult(ProviderVerdict.Ok, artifacts, usage, detail);
    }

    /// <summary>A failed result — no artifacts, a reason, and a verdict routing can act on.</summary>
    public static GenerationResult Failure(ProviderVerdict verdict, string? detail = null) =>
        new(verdict, [], null, detail);
}
