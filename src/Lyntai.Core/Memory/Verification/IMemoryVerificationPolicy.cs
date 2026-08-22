namespace Lyntai.Memory.Verification;

/// <summary>
/// Which of a recall's results actually ANSWERED the query — the correctness signal this engine otherwise
/// does not have. Without one, a recall reinforces whatever it RETURNED, so reinforcement is positive
/// feedback on the ranker's own prior including its mistakes.
///
/// <para><b>It is a JUDGEMENT, not a tokenizer, so it is language-neutral by construction</b> — like
/// <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/>, it routes around CJK tokenization rather
/// than adding to it. An implementation that reasons in one language only would reintroduce the bias.</para>
///
/// <para><b>Best-effort over a model-free floor: any failure must yield
/// <see cref="MemoryVerification.NoOpinion"/></b> — never "nothing was relevant", never a failed recall and
/// never no memory. A memory that stops answering because a judge is down is worse than one with no
/// judge.</para>
///
/// <para><b>It never invents results</b> — a verifier ranks and narrows what a recall already found.
/// <b>With <see cref="GraphMemoryOptions.VerificationFilters"/> OFF (the default) it still PROMOTES</b>
/// every endorsed candidate to the front of the ordinary results, keeping the policy's relative order within
/// the promoted group and within the rest, BEFORE the caller's limit is applied and over a candidate set
/// <see cref="GraphMemoryOptions.VerificationDepth"/> deep — so a buried answer can be pulled onto the page.
/// Nothing is removed; the option adds removal, it is not what makes a verdict visible. A judge that endorses
/// what already leads therefore returns an identical page, which is a result about the corpus rather than a
/// wiring problem. Facts: <c>MemoryVerificationOrderingTests</c>; <c>docs/memory.md</c> §7.</para>
/// </summary>
public interface IMemoryVerificationPolicy
{
    /// <summary>Judge which candidates answered the query.</summary>
    /// <param name="request">The query and what the recall is about to return.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The verdict. Never null; an implementation with no opinion returns
    /// <see cref="MemoryVerification.NoOpinion"/>, which the engine treats exactly as having no
    /// verifier.</returns>
    Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request, CancellationToken ct = default);
}

/// <summary>What a verifier is shown.</summary>
/// <param name="Query">The query text, verbatim and in its own language.</param>
/// <param name="Candidates">What the recall is about to return, in rank order — id and headline only.
/// <para>Headlines rather than full content on purpose: a recall is the cheap index (design §5.7), and a
/// verifier that needed every entry's body would cost more than the recall it is judging. An
/// implementation wanting more can open an entry itself.</para></param>
public sealed record MemoryVerificationRequest(
    string Query,
    IReadOnlyList<MemoryVerificationCandidate> Candidates);

/// <summary>One thing being judged.</summary>
/// <param name="Id">The engine-local id, echoed back in <see cref="MemoryVerification.RelevantIds"/>.</param>
/// <param name="Headline">Its one-line summary — what a caller would see without paying to expand.</param>
public sealed record MemoryVerificationCandidate(string Id, string Headline);

/// <summary>Which candidates answered the query.</summary>
/// <param name="RelevantIds">The ids that did. An id not listed is judged NOT to have answered — which is
/// the half that carries new information, since "was returned" was already known.</param>
/// <param name="Judged">Whether a judgement actually happened. <b>False is not the same as an empty
/// <paramref name="RelevantIds"/></b>: "the verifier could not decide" must not be read as "nothing was
/// relevant", or a model outage would silently teach the engine that every recall failed. The engine
/// reinforces normally when this is false, and reinforces the listed subset when it is true.</param>
public sealed record MemoryVerification(IReadOnlyList<string> RelevantIds, bool Judged = true)
{
    /// <summary>No judgement — what an implementation returns when it cannot decide, and what a missing or
    /// failing verifier is indistinguishable from.</summary>
    public static MemoryVerification NoOpinion { get; } = new([], Judged: false);

    /// <summary>A judgement that nothing returned was relevant. Distinct from
    /// <see cref="NoOpinion"/> and meaningful: it is a recall that found nothing useful, which is exactly the
    /// observation the review log could never previously contain.</summary>
    public static MemoryVerification NothingRelevant { get; } = new([]);
}
