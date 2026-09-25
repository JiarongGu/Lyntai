using Lyntai.Memory.Salience;

namespace Lyntai.Memory.Modulation;

/// <summary>
/// The first retention dimension: a salient entry decays more slowly. <b>This POLICY itself only scales
/// stability</b> — it never touches seeding and never touches rank directly; <see cref="StabilityFactor"/>
/// is the whole of what it does.
/// <para>The signal's other consumers — store admission, and the opt-in rank vote — live outside this type;
/// <see cref="MemorySignals.WellKnown.Salience"/> states what it means end to end.</para>
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
    /// <remarks>Reads the signal through <see cref="MemorySignals.Salience"/> — the ONE coercion every read
    /// site shares, which turns a non-finite stored value into the neutral 1 — and then applies only this
    /// policy's own ceiling, so the factor is always in [1, <see cref="MaxStabilityFactor"/>].</remarks>
    public double StabilityFactor(in MemoryDecayState state) =>
        Math.Min(MemorySignals.Salience(state.Signals), _options.MaxSalience);
}
