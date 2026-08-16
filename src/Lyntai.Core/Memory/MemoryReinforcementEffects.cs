namespace Lyntai.Memory;

/// <summary>Which effects a recall applies to the entries it returned.
///
/// <para><b>Reinforcement is TWO separable effects welded into one store round-trip</b>, and this is the
/// seam that separates them: <see cref="IMemoryGraphStore.TouchAsync"/> resets an entry's age on every
/// scale AND writes back the stability the retrievability policy grew.</para>
///
/// <para>The companion axis — which CALLS reinforce at all — is <see cref="MemoryReinforcementActs"/>. The
/// two compose: the acts selected there are applied with the effects selected here.</para>
///
/// <para><b>Not the same knob as <c>DsrOptions.ReinforceGain</c>:</b> that is one curve's private property,
/// and a consumer with their own <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> has no
/// equivalent. Which effects a recall applies is the ENGINE's decision about how it learns, and the two
/// stack: whichever suppresses growth first wins.</para>
///
/// <para><b><see cref="StabilityGrowth"/> without <see cref="AgeReset"/> is deliberately NOT offered</b>:
/// the store applies the age reset as an inseparable part of the same write, so that combination would
/// apply NEITHER effect, and offering it properly would mean another required member on
/// <see cref="IMemoryGraphStore"/>. Adding it later is additive.</para>
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
