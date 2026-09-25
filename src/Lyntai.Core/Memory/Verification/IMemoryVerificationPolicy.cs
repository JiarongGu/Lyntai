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

/// <summary>How a verdict reaches the page — see <see cref="GraphMemoryOptions.VerdictCombination"/>.
/// <para>It sits here rather than beside the option because it describes what a VERDICT means, which is this
/// domain's concern; placement is by ownership (<c>docs/DECISIONS.md</c> D46).</para></summary>
public enum MemoryVerdictCombination
{
    /// <summary>Every endorsed candidate is promoted ahead of every unendorsed one, each group keeping the
    /// ranking policy's own order. The shipped default, and unchanged since verification was introduced.
    /// <para><b>Its failure mode is bounded by the size of the endorsement set.</b> A verdict endorsing more
    /// candidates than the caller's limit REPLACES the page rather than refining it, because everything
    /// unendorsed is pushed off however well it was ranked.</para></summary>
    Partition = 0,

    /// <summary>The verdict COMPETES with the ranking on rank, the way every other signal in this engine is
    /// combined (<c>docs/DECISIONS.md</c> D82, D103) — so an endorsement moves a candidate up without
    /// entitling it to the page, and a well-ranked unendorsed candidate can still lead.
    /// <para><b>An unendorsed candidate is ranked LAST, never unranked</b>, which is the whole arithmetic:
    /// treating it as absent makes the worst endorsement outscore the best non-endorsement at every rank and
    /// silently reproduces <see cref="Partition"/>.</para>
    /// <para><b>Expect SAFETY, not a higher score.</b> Measured on LoCoMo against a real 4B judge it removes
    /// most of the partition's cost and never beats the base — all of a 10.5-point loss on one vector backend, 9.5
    /// of 12.0 on a second (<c>docs/memory-measurements.md</c> §5). What it buys is that a weak judge can no longer
    /// destroy a good ranking; the rescue a verdict exists for is kept.</para></summary>
    Fuse = 1,
}

/// <summary>What a verifier is shown.</summary>
/// <param name="Query">The query text, verbatim and in its own language.</param>
/// <param name="Candidates">What the recall is about to return, in rank order — each with its id, its
/// headline and its full <see cref="MemoryVerificationCandidate.Content"/>, which the engine supplies at no
/// extra read. Which of the two to judge is the policy's choice: a judge paying by the token reads the
/// headline.</param>
public sealed record MemoryVerificationRequest(
    string Query,
    IReadOnlyList<MemoryVerificationCandidate> Candidates);

/// <summary>One thing being judged.</summary>
/// <param name="Id">The engine-local id, echoed back in <see cref="MemoryVerification.RelevantIds"/>.</param>
/// <param name="Headline">Its one-line summary — what a caller would see without paying to expand.</param>
/// <param name="Relevance">How well it matched, exactly as <see cref="Lyntai.Memory.MemoryItem.Relevance"/>
/// will report it to the caller — so a policy can look at the SCORE DISTRIBUTION without reading the text.
/// <para><b>Its scale and shape are SOURCE- and backend-specific, and a policy may not assume otherwise.</b>
/// One request mixes values from different generators: a graph store's normalized rank POSITION for a lexical
/// hit, a real cosine for a semantic seed, and <c>0</c> for everything no relevance question was asked of —
/// a graph-walk neighbour, a subject seed, and the grade-admitted row below. They are ordered, not measured —
/// comparable within one request and no further, and not even commensurable across the sources inside
/// it.</para>
/// <para><b>A top score of <c>1</c> is not evidence of a good match.</b> Rank position puts the best row at
/// exactly <c>1</c> whatever the query, and a single-row result at <c>1</c> because there is no gradient to
/// place it on — the same output for a query answered perfectly and one answered not at all. <b>An absolute
/// floor cannot be derived from this number alone.</b> A RELATIVE test (top against the rest) is what it
/// supports, and even that reads an ordering rather than a fit.</para>
/// <para><b>0 for an authoritative fact the query did not match</b>, admitted by grade rather than by
/// relevance. Those rows are indistinguishable HERE from a recall that matched nothing — on the SQLite LIKE
/// fallback a no-match page IS the grade-admitted rows — so a policy must not read a page of zeros as a
/// judgement that the recall failed.</para></param>
public sealed record MemoryVerificationCandidate(string Id, string Headline, double Relevance = 0)
{
    /// <summary>The entry's full stored text, or <see langword="null"/> when whoever built this candidate
    /// supplied none — which a policy must be able to tell from a genuinely empty entry, the same
    /// distinction <see cref="MemoryVerification"/> draws between no opinion and an empty endorsement. The
    /// engine always supplies it: the store already reads the column, so it costs no extra query.
    ///
    /// <para><b>Read it when your policy needs the TEXT, and prefer <see cref="Headline"/> when it does
    /// not.</b> The headline is a truncation — <see cref="Lyntai.Memory.GraphMemoryOptions.HeadlineChars"/>
    /// characters of the content — so a policy that scores WORDING is scoring a fragment. Measured: a
    /// cross-encoder reranker reading headlines LOST 7.5 points against the arm it was meant to improve,
    /// and reading whole entries GAINED 5.0. A judge paying by the token has the opposite trade and should
    /// keep reading the headline — unless the headline is an authored LABEL rather than a truncation, which
    /// is what <see cref="LlmVerificationOptions.ContentChars"/> is for — and that is why this carries the
    /// text rather than replacing what is already there (<c>docs/DECISIONS.md</c> <b>D108</b>).</para>
    ///
    /// <para><b>An init property rather than a positional parameter</b>, so the record's
    /// <c>Deconstruct</c> keeps its arity and a consumer already destructuring one still compiles.</para>
    /// </summary>
    public string? Content { get; init; }
}

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

    /// <summary>How strongly each candidate scored, keyed by <see cref="MemoryVerificationCandidate.Id"/>,
    /// or <see langword="null"/> when the policy reported none. Covers EVERY candidate it scored, not only
    /// the endorsed ones — the rejected scores are the half a margin needs, since an endorsement says
    /// nothing about how far ahead it was.
    ///
    /// <para><b>For a caller of the POLICY</b> — one using a scoring backend as a standalone reranker. The
    /// graph engine acts on <see cref="RelevantIds"/> alone and does not carry these onto
    /// <see cref="MemoryRecall"/>.</para>
    ///
    /// <para><b>Null is not an empty map.</b> Null means the policy said nothing; a populated map of zeros
    /// is a real judgement that nothing resembled the query. That is <see cref="Judged"/>'s distinction one
    /// level down, and collapsing it would let a policy that cannot score look like one that scored
    /// everything at the floor.</para>
    ///
    /// <para><b>The scale is the POLICY's and is not comparable across them</b>, the same caveat
    /// <see cref="MemoryVerificationCandidate.Relevance"/> carries. A cross-encoder's number and a judge's
    /// are not the same quantity, and neither supports an absolute floor derived from one value — a
    /// RELATIVE test (the top against the rest) is what this supports.</para>
    ///
    /// <para><b>An init property rather than a positional parameter</b>, so the record's
    /// <c>Deconstruct</c> keeps its arity: widening the primary constructor would be a BINARY break for
    /// every one of the implementations that construct this.</para></summary>
    public IReadOnlyDictionary<string, double>? Scores { get; init; }
}
