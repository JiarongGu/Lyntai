namespace Lyntai.Memory;

/// <summary>What a decay curve needs to know about one entry. Carries no content, so a policy can be pure
/// arithmetic and trivially testable.</summary>
/// <param name="CreatedAt">When the entry was first stored.</param>
/// <param name="LastRecalledAt">When it was last successfully recalled; equals <paramref name="CreatedAt"/>
/// until it has been.</param>
/// <param name="RecallCount">How many times it has been recalled.</param>
/// <param name="Stability">Its half-life in DAYS — the quantity reinforcement grows.</param>
/// <param name="Strength">The summed RAW weight of the entry's connections. A memory woven into a dense,
/// repeatedly-reinforced network resists forgetting; an isolated one fades. Zero for a store with no
/// graph.</param>
/// <param name="StrengthAsOf">When any of those connections was last strengthened, so
/// <paramref name="Strength"/> can be decayed as an aggregate.
/// <para>This treats a neighbourhood as being as fresh as its freshest link, which OVER-estimates
/// durability. That is deliberate: decaying every edge individually would need a per-edge exponent inside
/// the aggregate, which no backend can do portably, and over-estimating raises retrievability — the only
/// direction that keeps <see cref="IRetrievabilityPolicy.CandidateCutoff"/> a conservative
/// superset.</para></param>
public readonly record struct MemoryDecayState(
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRecalledAt,
    int RecallCount,
    double Stability,
    double Strength = 0,
    DateTimeOffset? StrengthAsOf = null);

/// <summary>
/// The model of forgetting. Swappable, and the default is registered for you — nothing has to be
/// implemented to use graph memory.
/// <para>Exposing the constants as loose numbers would settle the VALUES while freezing the FORMULA, so an
/// application can tune <see cref="HalfLifeOptions"/> or replace the curve entirely, and neither choice
/// forecloses the other.</para>
/// </summary>
public interface IRetrievabilityPolicy
{
    /// <summary>Retrievability in [0,1] for a node's state at <paramref name="now"/>. Must be 1 at zero
    /// elapsed time and must never increase with age.</summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    /// <param name="now">The moment to evaluate at.</param>
    double Retrievability(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>The node's new <see cref="MemoryDecayState.Stability"/> after a successful recall. Must
    /// never be smaller than the current one.</summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    /// <param name="now">The moment of recall.</param>
    double Reinforce(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>Stability, in days, for a brand-new node.</summary>
    double InitialStability { get; }

    /// <summary>
    /// A CONSERVATIVE bound on <c>age_days / stability</c> for a given minimum retrievability: no node whose
    /// true retrievability is at least <paramref name="minRetrievability"/> may exceed it.
    /// <para>This is what lets a store bound its candidate set with plain division and never evaluate the
    /// curve — which matters twice over: SQLite has <c>pow</c> only when built with
    /// <c>SQLITE_ENABLE_MATH_FUNCTIONS</c>, and no fixed SQL expression could encode a policy the
    /// application supplies. A policy that cannot bound its curve returns
    /// <see cref="double.PositiveInfinity"/> — correct, at the cost of an in-scope scan.</para>
    /// </summary>
    /// <param name="minRetrievability">The floor a caller intends to apply.</param>
    double CandidateCutoff(double minRetrievability);
}

/// <summary>Constants of the default exponential curve.</summary>
public sealed record HalfLifeOptions
{
    /// <summary>Half-life of a brand-new entry. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public TimeSpan InitialStability { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How much a successful recall multiplies the half-life by, as <c>1 + factor</c>.
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double ReinforceFactor { get; init; } = 0.5;

    /// <summary>The ceiling reinforcement cannot grow past.
    /// <para><b>Not a rounding-out knob — it closes a real defect.</b> Unbounded compounding turns a
    /// seven-day half-life into sixty-four years in about twenty recalls at the default factor, so a
    /// frequently-recalled ASSOCIATIVE entry would become permanently retrievable while still labelled
    /// associative — silently acquiring the durability of authoritative material with none of its
    /// guarantees.</para>
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public TimeSpan MaxStability { get; init; } = TimeSpan.FromDays(365);

    /// <summary>How steeply connectedness lengthens a half-life, as
    /// <c>1 + factor · ln(1 + strength)</c>. The logarithm gives diminishing returns so a hub does not
    /// dominate. Set to 0 to ignore connections entirely. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double ConnectionBoost { get; init; } = 0.5;

    /// <summary>The largest multiple connectedness may apply to a half-life.
    /// <para><b>Load-bearing, not decoration.</b> A store filters candidates against the STORED stability,
    /// so <see cref="IRetrievabilityPolicy.CandidateCutoff"/> widens by exactly this factor. Were the boost
    /// unbounded, no finite cutoff could cover it and a well-connected node would be excluded while still
    /// perfectly retrievable — silently losing exactly the memories connectedness was meant to
    /// protect.</para>
    /// <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double MaxConnectionBoost { get; init; } = 4;

    /// <summary>Half-life of an edge's weight. Without it edges only ever grow: every pair that has ever
    /// co-occurred stays linked at a rising weight, the graph saturates, and spreading stops discriminating
    /// because everything reaches everything. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public TimeSpan EdgeHalfLife { get; init; } = TimeSpan.FromDays(30);
}

/// <summary>The default curve: <c>r = 2 ^ (-age_since_recall / stability)</c>, with the half-life growing on
/// each successful recall up to <see cref="HalfLifeOptions.MaxStability"/>.</summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class HalfLifeRetrievability(HalfLifeOptions? options = null) : IRetrievabilityPolicy
{
    private readonly HalfLifeOptions _options = options ?? new HalfLifeOptions();

    /// <inheritdoc />
    public double InitialStability => _options.InitialStability.TotalDays;

    /// <inheritdoc />
    public double Retrievability(in MemoryDecayState state, DateTimeOffset now)
    {
        var ageDays = (now - state.LastRecalledAt).TotalDays;
        if (ageDays <= 0) return 1;
        return Math.Clamp(Math.Pow(2, -ageDays / EffectiveStability(state, now)), 0, 1);
    }

    /// <summary>The half-life actually in force: the stored one, lengthened by how connected the entry is.
    /// <para>The result is NEVER below the stored stability. Clamping with a bare
    /// <c>min(stability × boost, MaxStability)</c> would shorten the half-life of an entry whose stored
    /// stability already exceeds the ceiling — lowering retrievability, which would break
    /// <see cref="CandidateCutoff"/>'s superset guarantee and start losing memories.</para></summary>
    private double EffectiveStability(in MemoryDecayState state, DateTimeOffset now)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        var boost = Math.Min(1 + _options.ConnectionBoost * Math.Log(1 + EffectiveStrength(state, now)),
            Math.Max(1, _options.MaxConnectionBoost));
        return Math.Max(stability, Math.Min(stability * boost, _options.MaxStability.TotalDays));
    }

    /// <summary>The entry's connection strength, decayed as one aggregate from the moment any of its links
    /// was last strengthened — so a neighbourhood that went quiet stops propping the memory up.</summary>
    private double EffectiveStrength(in MemoryDecayState state, DateTimeOffset now)
    {
        if (state.Strength <= 0) return 0;
        if (state.StrengthAsOf is not DateTimeOffset asOf) return state.Strength;

        var ageDays = (now - asOf).TotalDays;
        var halfLife = _options.EdgeHalfLife.TotalDays;
        return ageDays <= 0 || halfLife <= 0
            ? state.Strength
            : state.Strength * Math.Pow(2, -ageDays / halfLife);
    }

    /// <inheritdoc />
    public double Reinforce(in MemoryDecayState state, DateTimeOffset now)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        return Math.Min(stability * (1 + _options.ReinforceFactor), _options.MaxStability.TotalDays);
    }

    /// <inheritdoc />
    public double CandidateCutoff(double minRetrievability) =>
        minRetrievability is <= 0 or > 1
            ? double.PositiveInfinity
            // widened by the boost ceiling: a store filters against the STORED stability, and a connected
            // entry's effective half-life is up to MaxConnectionBoost times that
            : Math.Log2(1 / minRetrievability) * Math.Max(1, _options.MaxConnectionBoost);
}
