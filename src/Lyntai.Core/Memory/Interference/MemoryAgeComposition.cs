namespace Lyntai.Memory.Interference;

/// <summary>
/// Owns how several coexisting <see cref="IMemoryAgePolicy"/>s combine into ONE write-time tick and ONE
/// read-time age — the plural counterpart of <see cref="Lyntai.Memory.Modulation.IMemoryRetentionCompositionPolicy"/>
/// and <see cref="Lyntai.Memory.Salience.IMemorySalienceCompositionPolicy"/>. Age is plural because writes,
/// characters and elapsed time are different ASPECTS of "how much has happened" — they coexist rather than
/// compete — so combining them is a decision, not a given.
/// <para><b>The engine composes nothing itself.</b> <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> calls
/// every registered <see cref="IMemoryAgePolicy"/> and hands this seam the resulting list — never a hardcoded
/// sum or product — so a consumer who disagrees with the default combination rule swaps this one type.</para>
/// <para><b>A single registered policy makes this the identity</b>: every shipped implementation reduces a
/// one-element list to that element's own value, which is what keeps the engine's default (one
/// <see cref="BurstDampenedAgePolicy"/>) byte-for-byte unchanged.</para>
/// <para><b><see cref="MemoryTick"/>'s two halves have DIFFERENT correctness properties under composition,
/// and this is load-bearing for any implementer of <see cref="Advance"/>.</b> Encoding is applied once at
/// write time and never re-derived, so composing it across EVERY registered policy cannot double-count.
/// Position accumulates into the store's SINGLE counter, and a <see cref="MemoryAgeKind.Derivable"/>
/// policy's contribution is ALSO recorded in the primitives backing its <see cref="IMemoryAgePolicy.Age"/>
/// projection — so summing a Derivable tick into the accumulator an
/// <see cref="MemoryAgeKind.Accumulating"/> policy feeds double-counts it the moment <see cref="Age"/> adds
/// the projection back. <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> therefore calls
/// <see cref="Advance"/> TWICE with different tick lists — the full set for Encoding, the Accumulating
/// policy's own tick for Position. <b>An implementation must treat <c>ticks</c> as whatever SUBSET the
/// caller decided is safe to sum, never as "every registered policy".</b></para>
/// </summary>
public interface IMemoryAgeCompositionPolicy
{
    /// <summary>Combine <paramref name="ticks"/>' own write-time <see cref="MemoryTick"/>s (already computed
    /// by calling <see cref="IMemoryAgePolicy.Advance"/> on each) into the ONE tick the store actually
    /// applies. Never empty in practice — the engine always falls back to a default policy when none is
    /// registered — but an empty list still has to answer something rather than throw.
    /// <para><b><paramref name="ticks"/> is NOT always "every registered policy" — see this interface's own
    /// remarks on why <see cref="MemoryTick.Position"/> and <see cref="MemoryTick.Encoding"/> are composed
    /// from different subsets.</b></para></summary>
    /// <param name="ticks">The ticks to combine — the caller decides which subset, and why, per policy.</param>
    MemoryTick Advance(IReadOnlyList<MemoryTick> ticks);

    /// <summary>Combine every registered policy's own resolved age — already read from the primitives
    /// (<see cref="MemoryAgeKind.Derivable"/>) or the store's accumulator (<see cref="MemoryAgeKind.Accumulating"/>),
    /// per its own <see cref="IMemoryAgePolicy.Kind"/> — into the ONE age <see cref="MemoryDecayState.Age"/>
    /// carries into a retrievability curve.</summary>
    /// <param name="ages">One age per registered policy, in registration order.</param>
    double Age(IReadOnlyList<double> ages);
}

/// <summary>
/// The default composition: <see cref="Advance"/> SUMS positions and MULTIPLIES encodings; <see cref="Age"/>
/// SUMS the resolved ages. Position and age are both crowding-shaped quantities — how much has happened — so
/// an additional, independent aspect crowding an entry ADDS to what already crowded it. Encoding is a quality
/// multiplier in (0, 1] already (a burst's <c>1/√n</c> being the shipped example), so several judgments
/// compose by PRODUCT: each can only weaken it further.
/// <para>Identity on a one-element list — see <see cref="IMemoryAgeCompositionPolicy"/>'s own remarks.</para>
/// <para><b>Sum assumes composed age, <see cref="MemoryDecayState.Stability"/> and
/// <see cref="Lyntai.Memory.GraphMemoryOptions.EdgeHalfLife"/> already share ONE scale.</b>
/// Two <see cref="MemoryAgeKind.Derivable"/> policies with genuinely different UNITS
/// (writes vs characters vs days) summed together produce a composed age in neither unit — arithmetically
/// well-defined, but only MEANINGFUL if the engine's <c>Stability</c> (chosen against ONE of those units when
/// the entry was created) and edge decay's own half-life are calibrated against the SAME combined scale. So a
/// consumer mixing Derivable policies of different units should choose <c>Stability</c> (and
/// <c>EdgeHalfLife</c>, if edges matter to them) against the COMBINED scale those policies compose to, the
/// same way they would when choosing a single policy today.</para>
/// </summary>
public sealed class SummedAgeCompositionPolicy : IMemoryAgeCompositionPolicy
{
    /// <inheritdoc />
    public MemoryTick Advance(IReadOnlyList<MemoryTick> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count == 0) return MemoryTick.One;

        var position = 0d;
        var encoding = 1d;
        foreach (var tick in ticks)
        {
            position += tick.Position;
            encoding *= tick.Encoding;
        }
        return new MemoryTick(position, encoding);
    }

    /// <inheritdoc />
    public double Age(IReadOnlyList<double> ages)
    {
        ArgumentNullException.ThrowIfNull(ages);
        var total = 0d;
        foreach (var age in ages) total += age;
        return total;
    }
}
