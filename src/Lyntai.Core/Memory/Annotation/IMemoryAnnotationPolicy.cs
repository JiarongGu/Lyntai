namespace Lyntai.Memory.Annotation;

/// <summary>
/// What a remembered fact is ABOUT — the seam that lets an entity cluster become connected.
///
/// <para><b>The measured problem this exists for.</b> A graph engine's edges come from vector similarity at
/// write time or from CO-ACTIVATION during recall, and co-activation links whatever a recall happened to
/// RETURN together. Nothing in that is about two facts concerning the same entity. Measured over a full
/// corpus replay (<c>MemoryClusterEdgeFormationTests</c>): 442 edges written in English of which <b>2</b>
/// joined two of three cluster members, and 366 in Chinese of which <b>0</b> did. Cluster recall therefore
/// sits at the no-graph floor (<c>miss = 1 - 1/AttributeCount</c>) — identical at recall limit 10 and 50,
/// which is what proves those entries are never GATHERED and that no ranking policy can reach them.</para>
///
/// <para><b>Why this cannot be solved lexically.</b> "my spouse is Alice", "she works as an anaesthetist",
/// "we met in Kyoto" share no distinguishing word in any language — only pronouns. A tokenizer improvement
/// cannot connect them; that is a semantic judgement, and it is the same judgement in every language, which
/// is why it routes around the CJK tokenization problem entirely rather than adding to it.</para>
///
/// <para><b>The annotator SEES CONTEXT, not just the write, and the contract would be misleading without
/// it.</b> Given <c>"我的配偶是爱丽丝"</c> alone a model can say the fact concerns a spouse named Alice. Given
/// <c>"她在一家医院做麻醉师"</c> alone it cannot know who "she" is — so the two facts would receive different
/// subjects and never link, and the feature would appear to work while connecting nothing. Passing recent
/// entries is what makes a pronoun resolvable.</para>
///
/// <para><b>Best-effort, over a model-free floor.</b> Graph memory works today with no annotator and no
/// model at all. A failing or absent annotator degrades to "no new links" — never to a failed write and
/// never to no memory — exactly the contract similarity enrichment already carries.</para>
/// </summary>
public interface IMemoryAnnotationPolicy
{
    /// <summary>Judge what <paramref name="request"/>'s write is about.</summary>
    /// <param name="request">The write, plus the recent entries that make a pronoun resolvable.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The subjects it concerns, and optionally a grade. Never null; an implementation with no
    /// opinion returns <see cref="MemoryAnnotation.None"/>.</returns>
    Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default);
}

/// <summary>What an <see cref="IMemoryAnnotationPolicy"/> is shown.</summary>
/// <param name="Write">The fact being remembered.</param>
/// <param name="Recent">Recent entries in the same task and scope, newest first — the context that makes a
/// pronoun resolvable. Empty on the first write, and empty when the engine cannot read them, which an
/// implementation must tolerate rather than treat as "no context exists".</param>
/// <param name="Known">Subjects already in use in this task and scope, most-used first — the candidates to
/// REUSE rather than invent past.
/// <para><b>Measured necessity, not a nicety.</b> Asked to label three Chinese facts about one person, real
/// models returned <c>Alice / 爱丽丝 / 我</c> (llama3.2:3b) and <c>我的配偶 / 爱丽丝 / 京都</c>
/// (qwen2.5-vl:7b). Every answer is defensible — the relation, the entity, the location — and that is
/// exactly why nothing linked: "what is this about" has several valid answers, so a model alternates between
/// them. Showing the handles already in use anchors it, the same way showing recent facts resolves a
/// pronoun.</para></param>
public sealed record MemoryAnnotationRequest(
    MemoryWrite Write,
    IReadOnlyList<string> Recent,
    IReadOnlyList<string> Known)
{
    /// <summary>Without reuse candidates — for a caller that has none, and for the tests that predate
    /// them.</summary>
    public MemoryAnnotationRequest(MemoryWrite write, IReadOnlyList<string> recent)
        : this(write, recent, []) { }
}

/// <summary>What a fact is about.</summary>
/// <param name="Subjects">The entities or topics it concerns, as stable handles a later fact about the same
/// entity would produce again ("spouse", "配偶", "deploy-key"). <b>Stability across writes is the whole
/// mechanism</b>: two facts link because their subjects MATCH, so an annotator that phrases the same entity
/// differently each time connects nothing. Empty means "no opinion" and links nothing.</param>
/// <param name="Grade">A suggested grade, applied ONLY when the write did not state one
/// (<see cref="MemoryGrade.Inherit"/>). An explicit grade from the caller always wins — a model may advise
/// what matters, never overrule what the application already decided.</param>
public sealed record MemoryAnnotation(IReadOnlyList<string> Subjects, MemoryGrade? Grade = null)
{
    /// <summary>No opinion: no subjects, no grade. What an implementation returns when it cannot judge, and
    /// what the engine behaves identically to having no annotator at all for.</summary>
    public static MemoryAnnotation None { get; } = new([]);
}
