namespace Lyntai.Memory;

/// <summary>What a decay curve needs to know about one entry. Carries no content and no clock, so a policy
/// is pure arithmetic and trivially testable.</summary>
/// <param name="Age">How much has happened in this memory since the entry was last used, on the engine's
/// own scale — see <see cref="IMemoryClock"/>. Not a duration: an engine may count writes, characters
/// written, or elapsed days, and <paramref name="Stability"/> is in the same units.</param>
/// <param name="RecallCount">How many times it has been recalled.</param>
/// <param name="Stability">Its half-life, in the engine's units — the quantity reinforcement grows.</param>
/// <param name="Strength">The summed RAW weight of the entry's connections. A memory woven into a dense,
/// repeatedly-reinforced network resists forgetting; an isolated one fades. Zero for a store with no
/// graph.</param>
/// <param name="StrengthAge">How much has happened since any of those connections was last strengthened, so
/// <paramref name="Strength"/> can be decayed as an aggregate.
/// <para>This treats a neighbourhood as being as fresh as its freshest link, which OVER-estimates
/// durability. That is deliberate: decaying every edge individually would need a per-edge exponent inside
/// the aggregate, which no backend can do portably, and over-estimating raises retrievability — the only
/// direction that keeps <see cref="IRetrievabilityPolicy.CandidateCutoff"/> a conservative
/// superset.</para></param>
public readonly record struct MemoryDecayState(
    double Age,
    int RecallCount,
    double Stability,
    double Strength = 0,
    double StrengthAge = 0);

/// <summary>
/// The model of forgetting. Swappable, and the default is registered for you — nothing has to be
/// implemented to use graph memory.
/// <para>Exposing the constants as loose numbers would settle the VALUES while freezing the FORMULA, so an
/// application can tune <see cref="HalfLifeOptions"/> or replace the curve entirely, and neither choice
/// forecloses the other.</para>
/// <para>A policy never sees a clock. What "age" counts is <see cref="IMemoryClock"/>'s business; a policy
/// only turns an age into a probability.</para>
/// </summary>
public interface IRetrievabilityPolicy
{
    /// <summary>Retrievability in [0,1]. Must be 1 at zero age and must never increase with age.</summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    double Retrievability(in MemoryDecayState state);

    /// <summary>The entry's new <see cref="MemoryDecayState.Stability"/> after a successful recall. Must
    /// never be smaller than the current one.</summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    double Reinforce(in MemoryDecayState state);

    /// <summary>Stability for a brand-new entry, in the engine's units.</summary>
    double InitialStability { get; }

    /// <summary>
    /// A CONSERVATIVE bound on <c>age / stability</c> for a given minimum retrievability: no entry whose
    /// true retrievability is at least <paramref name="minRetrievability"/> may exceed it.
    /// <para>This is what lets a store bound its candidate set with plain arithmetic and never evaluate the
    /// curve — which matters because no fixed SQL expression could encode a policy the application
    /// supplies. A policy that cannot bound its curve returns <see cref="double.PositiveInfinity"/> —
    /// correct, at the cost of an in-scope scan.</para>
    /// </summary>
    /// <param name="minRetrievability">The floor a caller intends to apply.</param>
    double CandidateCutoff(double minRetrievability);
}

/// <summary>Constants of the default exponential curve.
/// <para><b>None of them carries a unit in its type</b>, and that is deliberate: a <c>TimeSpan</c> would
/// assert wall-clock time, which is one of several things an engine's <see cref="IMemoryClock"/> might be
/// counting. They are in whatever units that clock advances by.</para></summary>
public sealed record HalfLifeOptions
{
    /// <summary>Half-life of a brand-new entry. At the default <see cref="PerWriteClock"/> this means an
    /// unused entry is half-forgotten after twenty further things are remembered. <b>Unmeasured</b> — see
    /// the MEM-TUNE task.</summary>
    public double InitialStability { get; init; } = 20;

    /// <summary>How much a successful recall multiplies the half-life by, as <c>1 + factor</c>.
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double ReinforceFactor { get; init; } = 0.5;

    /// <summary>The ceiling reinforcement cannot grow past.
    /// <para><b>Not a rounding-out knob — it closes a real defect.</b> Unbounded compounding turns a
    /// twenty-event half-life into a hundred-thousand-event one in about twenty recalls, so a
    /// frequently-recalled ASSOCIATIVE entry would become permanently retrievable while still labelled
    /// associative — silently acquiring the durability of authoritative material with none of its
    /// guarantees.</para>
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double MaxStability { get; init; } = 2000;

    /// <summary>How steeply connectedness lengthens a half-life, as
    /// <c>1 + factor · ln(1 + strength)</c>. The logarithm gives diminishing returns so a hub does not
    /// dominate. Set to 0 to ignore connections entirely. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double ConnectionBoost { get; init; } = 0.5;

    /// <summary>The largest multiple connectedness may apply to a half-life.
    /// <para><b>Load-bearing, not decoration.</b> A store filters candidates against the STORED stability,
    /// so <see cref="IRetrievabilityPolicy.CandidateCutoff"/> widens by exactly this factor. Were the boost
    /// unbounded, no finite cutoff could cover it and a well-connected entry would be excluded while still
    /// perfectly retrievable — silently losing exactly the memories connectedness was meant to
    /// protect.</para>
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double MaxConnectionBoost { get; init; } = 4;

    /// <summary>Half-life of an edge's weight. Without it edges only ever grow: every pair that has ever
    /// co-occurred stays linked at a rising weight, the graph saturates, and spreading stops discriminating
    /// because everything reaches everything. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double EdgeHalfLife { get; init; } = 100;
}

/// <summary>The default curve: <c>r = 2 ^ (-age / stability)</c>, with the half-life growing on each
/// successful recall up to <see cref="HalfLifeOptions.MaxStability"/> and lengthened by how connected the
/// entry is.</summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class HalfLifeRetrievability(HalfLifeOptions? options = null) : IRetrievabilityPolicy
{
    private readonly HalfLifeOptions _options = options ?? new HalfLifeOptions();

    /// <inheritdoc />
    public double InitialStability => _options.InitialStability;

    /// <inheritdoc />
    public double Retrievability(in MemoryDecayState state) =>
        state.Age <= 0 ? 1 : Math.Clamp(Math.Pow(2, -state.Age / EffectiveStability(state)), 0, 1);

    /// <summary>The half-life actually in force: the stored one, lengthened by how connected the entry is.
    /// <para>The result is NEVER below the stored stability. Clamping with a bare
    /// <c>min(stability × boost, MaxStability)</c> would shorten the half-life of an entry whose stored
    /// stability already exceeds the ceiling — lowering retrievability, which would break
    /// <see cref="CandidateCutoff"/>'s superset guarantee and start losing memories.</para></summary>
    private double EffectiveStability(in MemoryDecayState state)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        var boost = Math.Min(1 + _options.ConnectionBoost * Math.Log(1 + EffectiveStrength(state)),
            Math.Max(1, _options.MaxConnectionBoost));
        return Math.Max(stability, Math.Min(stability * boost, _options.MaxStability));
    }

    /// <summary>The entry's connection strength, decayed as one aggregate from however much has happened
    /// since any of its links was last strengthened — so a neighbourhood that went quiet stops propping the
    /// memory up.</summary>
    private double EffectiveStrength(in MemoryDecayState state)
    {
        if (state.Strength <= 0) return 0;
        return state.StrengthAge <= 0 || _options.EdgeHalfLife <= 0
            ? state.Strength
            : state.Strength * Math.Pow(2, -state.StrengthAge / _options.EdgeHalfLife);
    }

    /// <inheritdoc />
    public double Reinforce(in MemoryDecayState state)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        return Math.Min(stability * (1 + _options.ReinforceFactor), _options.MaxStability);
    }

    /// <inheritdoc />
    public double CandidateCutoff(double minRetrievability) =>
        minRetrievability is <= 0 or > 1
            ? double.PositiveInfinity
            // widened by the boost ceiling: a store filters against the STORED stability, and a connected
            // entry's effective half-life is up to MaxConnectionBoost times that
            : Math.Log2(1 / minRetrievability) * Math.Max(1, _options.MaxConnectionBoost);
}
