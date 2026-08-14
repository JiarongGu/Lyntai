using Lyntai.Memory.Salience;

namespace Lyntai.Memory.Modulation;

/// <summary>
/// The first retention dimension: a salient entry decays more slowly. <b>This POLICY itself only scales
/// stability</b> — it never touches seeding and never touches rank directly; <see cref="StabilityFactor"/>
/// is the whole of what it does.
/// <para>Salience as a WHOLE is no longer decay-only (2026-08-09 — <c>docs/DECISIONS.md</c> D45, corrected
/// same day by D45): the same
/// signal this policy reads also orders admission in the store when a candidate set overflows its budget
/// (on by default, together with this policy's own lengthening — that pair is the whole of "does not
/// fade away"), and CAN lift rank in <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> by a bounded
/// logarithm (<see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight"/>, off by
/// default — a consumer opts in) —
/// both live OUTSIDE this type, at the store and the engine respectively. Read this class's own behaviour as
/// decay resistance only; read <see cref="MemorySignals.WellKnown.Salience"/> for what the signal means end
/// to end.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults. Shares
/// <see cref="SalienceOptions.MaxSalience"/> with the salience policy so the reported ceiling and the
/// declared bound cannot drift apart.</param>
public sealed class SalienceRetentionPolicy(SalienceOptions? options = null) : IMemoryRetentionPolicy
{
    private readonly SalienceOptions _options = options ?? new SalienceOptions();

    /// <inheritdoc />
    public string Name => MemorySignals.WellKnown.Salience;

    /// <inheritdoc />
    public double MaxStabilityFactor => _options.MaxSalience;

    /// <inheritdoc />
    public double StabilityFactor(in MemoryDecayState state) =>
        Math.Clamp(state.Signals.Get(MemorySignals.WellKnown.Salience, fallback: 1),
            1, _options.MaxSalience);
}
