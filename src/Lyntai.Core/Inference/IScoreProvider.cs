namespace Lyntai.Inference;

/// <summary>A query and a set of documents to rank against it.</summary>
/// <param name="Query">What the documents are being scored for relevance to.</param>
/// <param name="Documents">The candidates. <b>The caller owns the SIZE of this list and nothing here bounds
/// it</b> — a backend may serve the whole set in one request or one forward pass, so a payload, a context
/// window or an activation buffer scales with what you send. Bound it before calling; the memory seam does,
/// at <c>GraphMemoryOptions.VerificationDepth</c>.</param>
/// <param name="Consumer">Who is asking — the same attribution tag <see cref="TextRequest.Consumer"/>
/// carries, so a rerank call is not structurally invisible to budgeting and telemetry (D162). The slot is
/// the frozen part; governance wiring reads it as it lands.</param>
/// <param name="TimeoutSeconds">Per-call deadline override, clamped to
/// <see cref="LyntaiOptions.MaxProviderTimeout"/> exactly as on the text shape; null takes the configured
/// default.</param>
public sealed record ScoreRequest(
    string Query,
    IReadOnlyList<string> Documents,
    string? Consumer = null,
    int? TimeoutSeconds = null) : IConsumerTagged;

/// <summary>The outcome of a rerank call.
///
/// <para><b>Input order is the contract, not the ranking.</b> A rerank endpoint answers sorted and carries
/// its own indices; putting the scores back in input order is the backend's job, because the caller holds
/// the documents and an index it did not send is unusable. A caller ranks by sorting what it gets back.</para>
///
/// <para><b>The verdict sits BESIDE the scores</b> for the reason <see cref="VectorResponse"/> states about
/// vectors: there is no score meaning "I could not" — a zero ranks as confidently as any other number — so
/// failure is reported on its own axis, leaving <see cref="Scores"/> empty.</para></summary>
/// <param name="Verdict">How the call ended.</param>
/// <param name="Scores">One per input document, IN INPUT ORDER. Empty unless <paramref name="Verdict"/> is
/// <see cref="ProviderVerdict.Ok"/>.</param>
/// <param name="Detail">The backend's own words, or the failure reason.</param>
/// <param name="Usage">What the call spent, where the wire reported it; null from an in-process
/// cross-encoder, which spends no tokens anywhere.</param>
public sealed record ScoreResponse(
    ProviderVerdict Verdict,
    IReadOnlyList<double> Scores,
    string? Detail = null,
    ProviderUsage? Usage = null) : IProviderOutcome
{
    /// <summary>Whether the call produced scores.</summary>
    public bool IsOk => Verdict == ProviderVerdict.Ok;

    /// <summary>A successful response. <b>Throws for an EMPTY score list</b>: an "Ok" carrying nothing robs
    /// routing of its chance to fall over and hands the caller a successful nothing.</summary>
    public static ScoreResponse Success(IReadOnlyList<double> scores, string? detail = null,
        ProviderUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
            throw new ArgumentException("a successful rerank needs at least one score", nameof(scores));
        return new ScoreResponse(ProviderVerdict.Ok, scores, detail, usage);
    }

    /// <summary>A failed response, carrying no scores.</summary>
    public static ScoreResponse Failure(ProviderVerdict verdict, string? detail = null) =>
        new(verdict, [], detail);
}

/// <summary>A backend that ranks documents against a query — a cross-encoder in process, or a rerank
/// endpoint over HTTP.
///
/// <para>Declared by implementing this, not by a name. <c>AddMemoryScoringVerification</c> selects any
/// backend that serves it (<c>docs/DECISIONS.md</c> <b>D139</b>), so reaching the memory verification seam
/// takes a registration and nothing else.</para></summary>
public interface IScoreProvider : IProviderCall<ScoreRequest, ScoreResponse>;
