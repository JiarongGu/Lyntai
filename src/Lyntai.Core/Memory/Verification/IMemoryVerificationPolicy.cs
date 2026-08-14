namespace Lyntai.Memory.Verification;

/// <summary>
/// Which of a recall's results actually ANSWERED the query — the correctness signal this engine otherwise
/// does not have.
///
/// <para><b>The problem it exists for, and it is the root of most of this subsystem's open questions.</b>
/// A recall reinforces what it RETURNED, and nothing observes whether the return was right. So reinforcement
/// is positive feedback on the ranker's own prior, including its mistakes: the entries a ranking policy
/// already favours get their age reset and their stability grown, which makes the ranker favour them harder
/// next time. FSRS's "retrieval strengthens memory" comes from a domain where retrieval is <b>verified</b> —
/// the learner knows whether they got it right. This seam is that missing observer.</para>
///
/// <para><b>What it unblocks, stated so the value is checkable rather than asserted.</b> Three separate
/// dead ends reduce to this one missing signal:</para>
/// <list type="number">
/// <item>Reinforcement can be conditioned on being RIGHT rather than on being returned — which is what
/// `TASKS.md` Part 64's act gate could only approximate by switching whole call kinds off.</item>
/// <item>The review log gains real outcomes. Parameter fitting was recorded as structurally impossible
/// (<c>docs/DECISIONS.md</c> <b>D51</b>) for two reasons — the grade was a deterministic function of the
/// model's own prediction, and the log could only ever contain successes. A verifier breaks both: the
/// judgement comes from outside the curve, and it can say NO.</item>
/// <item>Recall quality itself becomes measurable against something other than a corpus this library
/// invented.</item>
/// </list>
///
/// <para><b>It is a JUDGEMENT, not a tokenizer, which is why it is language-neutral by construction.</b>
/// "did this entry answer that question" is the same question in every language, so — like
/// <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/> — it routes around the CJK tokenization
/// problem rather than adding to it. An implementation that reasons in one language only is the one thing
/// that would reintroduce the bias.</para>
///
/// <para><b>Best-effort over a model-free floor, exactly like annotation.</b> Graph memory works with no
/// verifier and no model at all. A failing, slow or absent verifier degrades to "reinforce what was
/// returned" — today's behaviour — never to a failed recall and never to no memory. A memory that stops
/// answering because a judge is down is worse than a memory with no judge.</para>
///
/// <para><b>It never invents results.</b> A verifier only ever narrows what a recall already found; it
/// cannot add an entry, and by default it does not remove one from the caller's answer either — see
/// <see cref="GraphMemoryOptions.VerificationFilters"/> for the opt-in that lets it. Keeping "what the
/// caller sees" separate from "what gets reinforced" is what makes a mistaken judgement cost a little
/// learning rather than a lost answer.</para>
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
