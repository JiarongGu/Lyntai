namespace Lyntai.Memory.Salience;

/// <summary>
/// The registered default: model-free, using <see cref="SalienceContext.Novelty"/> as a prediction-error
/// proxy — material unlike anything already stored is held more strongly, which is the one salience signal
/// obtainable without judging meaning.
/// <para>It deliberately reports the neutral 1 until an engine holds
/// <see cref="SalienceOptions.MinimumComparables"/> entries: in a nearly-empty memory everything is novel, so
/// scoring novelty there would mark a whole first session as maximally important.</para>
/// <para>It only judges how strongly the write is encoded; what that does downstream is stated on
/// <see cref="MemorySignals.WellKnown.Salience"/>.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class StructuralSaliencePolicy(SalienceOptions? options = null) : IMemorySaliencePolicy
{
    private readonly SalienceOptions _options = options ?? new SalienceOptions();

    /// <inheritdoc />
    public MemorySalienceProvenance Provenance => MemorySalienceProvenance.Structural;

    /// <inheritdoc />
    public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (context.ComparableCount < _options.MinimumComparables) return MemorySignals.Empty;

        // a non-finite novelty is a caller defect, not a reason to fail a write — take the neutral value
        var novelty = double.IsFinite(context.Novelty) ? Math.Clamp(context.Novelty, 0, 1) : 0;
        var salience = Math.Clamp(1 + _options.NoveltyWeight * novelty, 1, _options.MaxSalience);

        return salience == 1
            ? MemorySignals.Empty
            : MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
    }
}
