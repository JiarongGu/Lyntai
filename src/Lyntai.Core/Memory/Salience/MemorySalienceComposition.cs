namespace Lyntai.Memory.Salience;

/// <summary>
/// Owns how several coexisting <see cref="IMemorySaliencePolicy"/>s' judgements combine into the ONE
/// <see cref="MemorySignals"/> bag a write stores — the plural counterpart of
/// <see cref="Lyntai.Memory.Interference.IMemoryAgeCompositionPolicy"/> and
/// <see cref="Lyntai.Memory.Modulation.IMemoryRetentionCompositionPolicy"/> (2026-08-10 memory-policy-seams
/// plan, Task 3). Salience became plural because structural novelty, semantic weight and explicit marking are
/// different ASPECTS of "how strongly was this encoded" — an application may want a model-free structural
/// judgment AND its own semantic one running together, each contributing signals the other has no opinion on.
/// <para><b>The engine composes nothing itself.</b> <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> calls
/// every registered policy (isolating a throwing one to <see cref="MemorySignals.Empty"/>, never failing
/// the write) and hands this seam the resulting bags — never merging them inline.</para>
/// <para><b>A single registered policy makes this the identity</b> — every shipped implementation reduces a
/// one-element list to that element's own bag, unchanged, which is what keeps the engine's default (one
/// <see cref="StructuralSaliencePolicy"/>) byte-for-byte identical to the pre-Task-3 behaviour.</para>
/// </summary>
public interface IMemorySalienceCompositionPolicy
{
    /// <summary>Combine every registered policy's own bag into the ONE bag a write stores. An empty list
    /// — no salience policies, or every one threw — must return <see cref="MemorySignals.Empty"/>.</summary>
    /// <param name="signals">Each policy's own <see cref="MemorySignals"/>, in registration order.</param>
    MemorySignals Signals(IReadOnlyList<MemorySignals> signals);
}

/// <summary>
/// The default composition: for each signal name, take the LARGEST value any policy reported for it.
/// Salience's own contract is a CEILING shared across every policy (<see cref="SalienceOptions.MaxSalience"/>
/// bounds what any one of them may report), so when several policies judge the SAME signal, the strongest
/// evidence any of them found is what "does not fade away" should act on — a policy that saw nothing
/// unusual must not drag down one that did. Different signal NAMES never collide at all: each simply appears
/// in the composed bag once, from whichever policy wrote it.
/// <para>Reduces a one-element list to that element's own bag unchanged (see the interface's own remarks) —
/// the identity the shipped single-policy default relies on.</para>
/// </summary>
public sealed class MaximalSalienceCompositionPolicy : IMemorySalienceCompositionPolicy
{
    /// <inheritdoc />
    public MemorySignals Signals(IReadOnlyList<MemorySignals> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0) return MemorySignals.Empty;
        if (signals.Count == 1) return signals[0];

        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var bag in signals)
            foreach (var (name, value) in bag.Values)
                merged[name] = merged.TryGetValue(name, out var existing) ? Math.Max(existing, value) : value;

        return MemorySignals.From(merged);
    }
}
