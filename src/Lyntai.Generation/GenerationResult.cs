namespace Lyntai.Generation;

/// <summary>Why a media call ended the way it did. The platform's OWN vocabulary — deliberately not
/// <c>LlmVerdict</c>: media is a separate domain, and its results are consumed by media routing.
/// (The transport-to-verdict PATTERNS are still shared — see <see cref="GenerationVerdictClassifier"/> — so there
/// is one corpus of "what does a 429 mean", not two.)</summary>
public enum GenerationVerdict
{
    /// <summary>Artifacts were produced.</summary>
    Ok,

    /// <summary>The backend isn't set up (no endpoint/key, engine not provisioned). NOT a transient failure:
    /// routing should skip this candidate rather than penalise it, and a host should offer setup.</summary>
    NotConfigured,

    /// <summary>The backend cannot do THIS request (wrong medium, unsupported role, duration past its limit).
    /// A capability gap, not a fault — advance to another candidate without penalising this one.</summary>
    Unsupported,

    /// <summary>Rate-limited / quota exhausted — cool this backend and advance.</summary>
    RateLimited,

    /// <summary>Credentials rejected — cool and advance; retrying immediately can't help.</summary>
    AuthFailed,

    /// <summary>The backend refused on content policy. SURFACE it — another backend is likely to refuse too,
    /// and silently shopping a refused prompt around is not the platform's call to make.</summary>
    Refused,

    /// <summary>The call or render exceeded its budget.</summary>
    Timeout,

    /// <summary>Anything else.</summary>
    Failed,
}

/// <summary>The outcome of a media generation.</summary>
/// <param name="Verdict">Why it ended this way.</param>
/// <param name="Artifacts">What was produced — empty unless <paramref name="Verdict"/> is
/// <see cref="GenerationVerdict.Ok"/>.</param>
/// <param name="Usage">What the backend said it cost, when it says.</param>
/// <param name="Detail">The backend's own words, or the failure reason. Surface verbatim rather than parsing:
/// the wording belongs to the backend and changes.</param>
public sealed record GenerationResult(
    GenerationVerdict Verdict,
    IReadOnlyList<GenerationArtifact> Artifacts,
    GenerationUsage? Usage = null,
    string? Detail = null)
{
    /// <summary>Whether the call produced media.</summary>
    public bool IsOk => Verdict == GenerationVerdict.Ok;

    /// <summary>A successful result. Throws for an EMPTY artifact list: an "Ok" carrying nothing is the
    /// empty-Ok mistake the LLM side already paid for (<c>.claude/knowledge/pitfalls.md</c>) — it robs routing
    /// of its chance to fall over and hands the caller a successful nothing.</summary>
    public static GenerationResult Success(
        IReadOnlyList<GenerationArtifact> artifacts, GenerationUsage? usage = null, string? detail = null)
    {
        if (artifacts.Count == 0)
            throw new ArgumentException("a successful media result needs at least one artifact", nameof(artifacts));
        return new GenerationResult(GenerationVerdict.Ok, artifacts, usage, detail);
    }

    /// <summary>A failed result — no artifacts, a reason, and a verdict routing can act on.</summary>
    public static GenerationResult Failure(GenerationVerdict verdict, string? detail = null) =>
        new(verdict, [], null, detail);
}
