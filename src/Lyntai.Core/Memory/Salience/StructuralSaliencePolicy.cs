namespace Lyntai.Memory.Salience;

/// <summary>
/// The registered default: model-free, using <see cref="SalienceContext.Novelty"/> as a prediction-error
/// proxy — material unlike anything already stored is held more strongly, which is the one salience signal
/// obtainable without judging meaning.
/// <para>It deliberately reports the neutral 1 until an engine holds
/// <see cref="SalienceOptions.MinimumComparables"/> entries: in a nearly-empty memory everything is novel, so
/// scoring novelty there would mark a whole first session as maximally important.</para>
/// <para>What this policy reports feeds decay resistance and store admission priority — "does not fade
/// away" — always, and rank priority only if a consumer has opted into
/// <see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight"/> (off by default; per
/// <c>docs/DECISIONS.md</c> D45).
/// This type has no opinion of its own on any of that; it only judges how strongly the write should be
/// encoded.</para>
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
