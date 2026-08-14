namespace Lyntai.Memory;

/// <summary>Which effects a recall applies to the entries it returned.
///
/// <para><b>Reinforcement is TWO separable effects welded into one store round-trip</b>, and this is the
/// seam that separates them. <see cref="IMemoryGraphStore.TouchAsync"/> resets an entry's age on every
/// scale AND writes back the stability the retrievability policy grew; those pull in opposite directions
/// and the evidence for each is different, so one switch over both cannot express the configuration that
/// measured best.</para>
///
/// <para><b>Cut at the EFFECTS, not at the acts, and that is the whole point of this type.</b> An earlier
/// option gated the two ACTS a recall can reinforce from (recall versus expansion) and was reverted before
/// 3.0 froze: it could not say "reset the age, do not grow the stability", which is precisely the
/// configuration four measurement rounds converged on. Both cuts are legitimate questions, and they
/// COMPOSE — an act-shaped option can still be added later without disturbing this one, because it answers
/// "from which call" while this answers "with which effect". A seam frozen at the wrong joint costs a
/// major to correct, which is why this one landed inside the 3.0 window rather than after it.</para>
///
/// <para><b>What the measurements said</b> (`TASKS.md` Part 64, four studies, 6 shapes × 30 seeds paired
/// per seed). The age reset is doing the useful work — it is what keeps a rarely-queried critical fact
/// alive, and removing it collapsed the combined metric on every shape. The stability growth is what
/// entrenches whatever the ranking policy already favoured, because a recall reinforces whatever it
/// RETURNED and this library has no signal for whether the return was CORRECT — so it is positive feedback
/// on the ranker's own prior, including its mistakes. That is why the damage concentrates in the class
/// where the same entries return repeatedly.</para>
///
/// <para><b>This is not the same knob as <c>DsrOptions.ReinforceGain</c>, and the difference is who owns
/// the decision.</b> That constant is one shipped curve's private property; a consumer who implements
/// their own <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> has no equivalent and,
/// before this type, no way to ask for the reset without the growth at all. Which effects a recall applies
/// is the ENGINE's decision about how it learns, not a property of the forgetting curve. The two compose
/// without conflict: whichever suppresses growth first wins, and neither needs to know about the
/// other.</para>
///
/// <para><b><see cref="StabilityGrowth"/> without <see cref="AgeReset"/> is deliberately NOT offered, and
/// that absence is a design conclusion.</b> It is the one combination this enum cannot express, because
/// the store applies the age reset as an inseparable part of the same write. Offering it would mean a
/// sixth required member on <see cref="IMemoryGraphStore"/> — for the arm every measurement ranked worst
/// (permanent entrenchment with none of the protection), which is speculative surface bought at a real
/// cost to every custom store. If a case for it ever appears, it is additive in behaviour and its absence
/// here is what makes that a considered reversal rather than an oversight.</para>
/// </summary>
[Flags]
public enum MemoryReinforcementEffects
{
    /// <summary>A recall changes nothing about what it returned — neither the age nor the stability, and no
    /// touch is written at all (this is a skipped store call, not a touch with every field suppressed).
    /// <para>Co-activation edges and the review log are NOT governed by this type and still run; they
    /// record that a recall happened rather than changing what an entry is worth. Use
    /// <see cref="GraphMemoryOptions.LogReviews"/> and <see cref="GraphMemoryOptions.CoActivationCap"/> for
    /// those.</para>
    /// <para>Measured as the worst arm for recall quality on every shape — it exists so the position is
    /// reachable and measurable, not because it is recommended.</para></summary>
    None = 0,

    /// <summary>A recall resets the entry's age on every scale, so returning something keeps it alive.
    /// <b>Combined with omitting <see cref="StabilityGrowth"/>, this is the best-measured configuration</b>
    /// and the one 3.0's shipped default already reaches by a different route
    /// (<c>DsrOptions.ReinforceGain = 0</c>).</summary>
    AgeReset = 1,

    /// <summary>A recall writes back the stability (and difficulty) the retrievability policy grew, making
    /// the entry durable for longer.
    /// <para>Has no effect when the installed curve grows nothing — 3.0's shipped
    /// <c>DsrRetrievability</c> defaults to <c>ReinforceGain = 0</c>, so this flag being SET is not the
    /// same as growth actually happening.</para></summary>
    StabilityGrowth = 2,

    /// <summary>Both effects — the default, and what every version before 3.0 did unconditionally.
    /// <para>It stays the default so behaviour is unchanged for every existing consumer: the shipped curve
    /// already neutralizes the growth half at the policy, and defaulting to <see cref="AgeReset"/> here
    /// would additionally override a consumer who deliberately raised <c>ReinforceGain</c>.</para></summary>
    All = AgeReset | StabilityGrowth,
}
