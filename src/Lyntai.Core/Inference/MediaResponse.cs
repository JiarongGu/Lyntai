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
    /// <summary>Which registered backend this response came from, as the ROUTER that chose it reports:
    /// <see cref="MediaRouter"/> stamps the answering backend's id on every response a backend gave it, success or
    /// failure, and leaves it null on a failure it synthesized itself (nothing capable, everything benched).
    /// <para>A backend need not set it, and one that does is overwritten by the router. A custom
    /// <see cref="IMediaRouter"/> that does not stamp it leaves it null — and a durable job's delivery then names
    /// no backend. It takes part in record equality, like every other member.</para></summary>
    public string? ProviderId { get; init; }

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
