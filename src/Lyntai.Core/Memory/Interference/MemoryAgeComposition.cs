namespace Lyntai.Memory.Interference;

/// <summary>
/// Owns how several coexisting <see cref="IMemoryAgePolicy"/>s combine into ONE write-time tick and ONE
/// read-time age — the plural counterpart of <see cref="Lyntai.Memory.Modulation.IMemoryRetentionCompositionPolicy"/>
/// and <see cref="Lyntai.Memory.Salience.IMemorySalienceCompositionPolicy"/> (2026-08-10 memory-policy-seams
/// plan, Task 3). Age became plural because writes, characters and elapsed time are different ASPECTS of "how
/// much has happened" — they coexist rather than compete — and combining them is therefore a decision, not a
/// given; before this seam existed the answer was implicit in there being only ever one clock installed.
/// <para><b>The engine composes nothing itself.</b> <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> calls
/// every registered <see cref="IMemoryAgePolicy"/> and hands this seam the resulting list — never a hardcoded
/// sum or product — so a consumer who disagrees with the default combination rule swaps this one type.</para>
/// <para><b>A single registered policy makes this the identity</b> — every shipped implementation reduces a
/// one-element list to that element's own value, unchanged. That is what keeps the engine's default (one
/// <see cref="BurstDampenedAgePolicy"/>) byte-for-byte identical to the pre-Task-3 behaviour: composing a
/// singleton is composing nothing.</para>
/// <para><b><see cref="MemoryTick"/>'s two halves have DIFFERENT correctness properties under composition —
/// found in fix round 1, C-1, and load-bearing for any implementer of <see cref="Advance"/>.</b> Encoding is
/// applied once, at write time, to a fresh entry's initial stability, and is never re-derived anywhere else —
/// composing it across EVERY registered policy can never double-count. Position is different: it accumulates
/// into the store's SINGLE position counter, and a <see cref="MemoryAgeKind.Derivable"/> policy's own
/// contribution is ALSO recorded exactly, unconditionally, in the primitives that back its
/// <see cref="IMemoryAgePolicy.Age"/> projection — so composing a Derivable policy's tick into the SAME
/// accumulator an <see cref="MemoryAgeKind.Accumulating"/> policy also feeds double-counts that policy's
/// share of "how much happened" the moment <see cref="Age"/> adds its own Derivable projection back in.
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> therefore calls <see cref="Advance"/> TWICE with
/// deliberately different ticks lists when needed — the full set for Encoding, only the Accumulating
/// policy's own tick (if one is registered) for Position — and reads only the relevant half of each
/// result; see <c>GraphMemoryEngine.AdvanceAgePolicies</c>'s own remarks for the exact rule. An implementation
/// of this interface must therefore treat <see cref="Advance"/>'s <c>ticks</c> parameter as whatever SUBSET
/// the caller decided is safe to sum for Position — never assume it is "every registered policy" the way it
/// safely can for Encoding.</para>
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
/// SUMS the resolved ages. Position and age are both crowding-shaped quantities — how much has happened —
/// so an additional, independent aspect crowding an entry ADDS to what already crowded it, the same way two
/// witnesses of unrelated events each make an alibi harder to hold. Encoding is a quality multiplier in
/// (0, 1] already (a burst's <c>1/√n</c> being the shipped example), so composing several judgments the same
/// way <see cref="Lyntai.Memory.Modulation.MultiplicativeRetentionComposition"/> composes retention factors —
/// each can only weaken it further — keeps one convention for "a multiplier several things influence" across
/// the two plural domains that have one.
/// <para>Both reduce a one-element list to that element's own value: <c>Sum</c> of one term and <c>product</c>
/// starting at 1 are both the identity, which is what keeps the shipped default unchanged (see the interface's
/// own remarks).</para>
/// <para><b>Sum assumes composed age, <see cref="MemoryDecayState.Stability"/> and
/// <see cref="Lyntai.Memory.GraphMemoryOptions.EdgeHalfLife"/> already share ONE scale — recorded
/// here rather than solved, because solving it is a bigger design question than this fix round owns (fix
/// round 1, I-3).</b> Two <see cref="MemoryAgeKind.Derivable"/> policies with genuinely different UNITS
/// (writes vs characters vs days) summed together produce a composed age in neither unit — arithmetically
/// well-defined, but only MEANINGFUL if the engine's <c>Stability</c> (chosen against ONE of those units when
/// the entry was created) and edge decay's own half-life are calibrated against the SAME combined scale. This
/// is not new to composition: it is the same "one unit convention per engine" assumption every single-policy
/// engine already makes (§3 of the design doc), sharpened by having several units ACTIVE at once instead of
/// one chosen once. <c>Sum</c> is still the defensible default — the double count fix round 1's C-1 closed was
/// the real defect, not the choice of Sum over Max or any other rule — but a consumer mixing Derivable policies
/// of different units should choose <c>Stability</c> (and <c>EdgeHalfLife</c>, if edges matter to them)
/// against the COMBINED scale those policies compose to, the same way they would when choosing a single
/// policy today.</para>
/// </summary>
public sealed class SummedAgeComposition : IMemoryAgeCompositionPolicy
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
