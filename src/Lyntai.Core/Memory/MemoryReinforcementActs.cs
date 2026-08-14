namespace Lyntai.Memory;

/// <summary>Which CALLS reinforce the entries they touched.
///
/// <para>The companion to <see cref="MemoryReinforcementEffects"/>, and deliberately a separate type: that
/// one answers <i>with which effect</i>, this one answers <i>from which call</i>. They compose — the acts
/// selected here are reinforced with the effects selected there — and neither can express the other's
/// question. An earlier single option that tried to be both was reverted before 3.0 froze
/// (<c>docs/DECISIONS.md</c> <b>D57</b>).</para>
///
/// <para><b>The question this exists to answer.</b> The README promises that <i>material you keep coming
/// back to</i> becomes durable. The implementation reinforces whatever the RANKER RETURNED — and those were
/// treated as the same thing for the subsystem's whole life. They are not. FSRS's "retrieval strengthens
/// memory" comes from a domain where retrieval is VERIFIED: the learner knows whether they got it right, and
/// reinforcement is conditioned on that. Here, retrieval is asserted by the same ranker being reinforced, so
/// the loop upvotes its own prior — including its mistakes.</para>
///
/// <para><b>This engine already separates a guess from a decision, and then discards the distinction.</b>
/// <c>RecallAsync</c> returns speculative one-line headlines; <c>ExpandAsync</c> is a caller CHOOSING TO PAY
/// for full content, which is literally "coming back to it". Both reinforced with identical weight. The
/// discriminating signal is produced as a byproduct of the core loop and thrown away — which is what this
/// type lets a consumer stop doing.</para>
/// </summary>
[Flags]
public enum MemoryReinforcementActs
{
    /// <summary>Nothing reinforces. Equivalent in effect to
    /// <see cref="MemoryReinforcementEffects.None"/>, and offered here so the two axes stay independently
    /// expressible rather than for any behaviour the other cannot reach.</summary>
    None = 0,

    /// <summary>A recall reinforces every entry it returned — the speculative act, and the one whose
    /// evidence is the ranker's own opinion.</summary>
    Recall = 1,

    /// <summary>Opening an entry for its full content reinforces it — the act a caller PAID for, and the
    /// closest thing this library observes to a verified retrieval.</summary>
    Expansion = 2,

    /// <summary>Both — the default, and what every version before 3.0 did unconditionally.</summary>
    All = Recall | Expansion,
}
