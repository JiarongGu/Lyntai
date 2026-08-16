using Lyntai.Memory.Forgetting;

namespace Lyntai.Memory.Modulation;

/// <summary>
/// Layers <see cref="IMemoryRetentionPolicy"/>s over any <see cref="IMemoryRetrievabilityPolicy"/>, so the curve
/// itself never has to know which retention dimensions exist.
/// <para><b>Modulation is a READ-TIME view.</b> <see cref="Reinforce"/> deliberately passes the state
/// through untouched: it returns a value the store writes back, and compounding the modulated figure would
/// bake a signal's effect into the stored stability permanently, where no later change to the signal could
/// undo it.</para>
/// <para><b>A stored stability of 0 makes every retention policy inert.</b> Modulation multiplies the stored
/// stability (<c>0 × factor = 0</c>), and the inner policy substitutes its own
/// <see cref="IMemoryRetrievabilityPolicy.InitialStability"/> for a non-positive stability — unmodulated. This is
/// inherited from the inner policy rather than introduced here, and it is safe in direction (it never
/// widens what the cutoff must cover), but the author of a retention policy would reasonably expect a
/// brand-new entry to be modulated too, so it is called out here rather than left to be rediscovered.</para>
/// </summary>
public sealed class ModulatedRetrievability : IMemoryRetrievabilityPolicy
{
    private readonly IMemoryRetrievabilityPolicy _inner;
    private readonly IMemoryRetentionPolicy[] _retentionPolicies;
    private readonly IMemoryRetentionCompositionPolicy _composition;

    /// <summary>The composed product of every declared maximum — how far <see cref="CandidateCutoff"/> must
    /// widen. Computed once from <see cref="_retentionPolicies"/> (never re-enumerating the constructor's parameter,
    /// which could be a lazy or non-idempotent sequence): it cannot vary per entry, because one cutoff is
    /// applied to a whole sweep, and computing it from a different enumeration than the one actually
    /// clamped against could produce a product smaller than the clamp allows — narrowing the cutoff below
    /// what modulation can produce. <b>That narrowing DELETES</b>: the cutoff bounds no seed (seeding applies
    /// no faintness bound at all), so its one consumer is
    /// <see cref="IMemoryGraphStore.PruneAsync"/>, and an entry it fails to cover is reaped rather than
    /// merely missed.</summary>
    private readonly double _maxFactor;

    /// <param name="inner">The base curve.</param>
    /// <param name="retentionPolicies">The registered dimensions; an empty set makes this exactly
    /// <paramref name="inner"/>.</param>
    /// <param name="composition">How the retention policies' clamped factors combine into one; null takes
    /// <see cref="MultiplicativeRetentionCompositionPolicy"/> — today's behaviour, given a name and a swap
    /// point.</param>
    public ModulatedRetrievability(IMemoryRetrievabilityPolicy inner,
        IEnumerable<IMemoryRetentionPolicy> retentionPolicies,
        IMemoryRetentionCompositionPolicy? composition = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _retentionPolicies = [.. retentionPolicies ?? throw new ArgumentNullException(nameof(retentionPolicies))];
        _composition = composition ?? new MultiplicativeRetentionCompositionPolicy();
        _maxFactor = _composition.StabilityFactor([.. _retentionPolicies.Select(Declared)]);
    }

    /// <inheritdoc />
    public double InitialStability => _inner.InitialStability;

    /// <summary>Forwarded to the wrapped policy unchanged (see the <c>inner</c> constructor parameter):
    /// modulation is a READ-TIME view over the curve's own stability, exactly like <see cref="Reinforce"/>
    /// below, so whichever policy actually computed the stored state is the one provenance must credit, not
    /// this decorator.</summary>
    /// <inheritdoc cref="IMemoryRetrievabilityPolicy.Provenance" />
    public MemoryRetrievabilityProvenance Provenance => _inner.Provenance;

    /// <inheritdoc />
    public double Retrievability(in MemoryDecayState state) => _inner.Retrievability(Modulated(state));

    /// <inheritdoc />
    public MemoryDecayState Reinforce(in MemoryDecayState state) => _inner.Reinforce(state);

    /// <summary>Forwarded to the wrapped policy unchanged, on the SAME raw <paramref name="state"/>
    /// <see cref="Reinforce"/> itself uses — never <see cref="Modulated"/> — for the identical reason
    /// <see cref="Reinforce"/> already gives (2026-08-11, fsrs-properly plan Task 3): whichever policy
    /// actually computed the grade is the one a review log must credit, on the unmodulated state that
    /// produced it.</summary>
    /// <inheritdoc cref="IMemoryRetrievabilityPolicy.DerivedGrade" />
    public double? DerivedGrade(in MemoryDecayState state) => _inner.DerivedGrade(state);

    /// <inheritdoc />
    public double CandidateCutoff(double minRetrievability) =>
        _inner.CandidateCutoff(minRetrievability) * _maxFactor;

    /// <summary>The state as the curve should see it: the stored stability, lengthened by every retention
    /// policy, each clamped into <c>[1, its declared maximum]</c>.</summary>
    private MemoryDecayState Modulated(in MemoryDecayState state)
    {
        if (_retentionPolicies.Length == 0) return state;

        var clamped = new double[_retentionPolicies.Length];
        for (var i = 0; i < _retentionPolicies.Length; i++)
        {
            var declared = Declared(_retentionPolicies[i]);
            var reported = _retentionPolicies[i].StabilityFactor(state);
            // clamp, never trust: an undeclared excess would break CandidateCutoff's superset guarantee,
            // and the symptom of that is a memory that quietly stops coming back
            clamped[i] = double.IsFinite(reported) ? Math.Clamp(reported, 1, declared) : 1;
        }
        var factor = _composition.StabilityFactor(clamped);

        return factor == 1 ? state : state with { Stability = state.Stability * factor };
    }

    /// <summary>A retention policy's declared maximum, coerced the same way everywhere it is used. Non-finite
    /// (including <see cref="double.NaN"/>, which <see cref="Math.Max(double,double)"/> would otherwise
    /// propagate — <c>Math.Max(1, NaN)</c> is <c>NaN</c> by contract) is treated as no widening at all
    /// rather than as an unbounded one, because a NaN cutoff compares false against everything and would
    /// silently empty every candidate set. This must be the ONLY place either use computes the bound: the
    /// product in <see cref="_maxFactor"/> and the per-entry clamp in <see cref="Modulated"/> have to agree,
    /// or the clamp could allow a factor the cutoff never widened for.</summary>
    private static double Declared(IMemoryRetentionPolicy policy) =>
        double.IsFinite(policy.MaxStabilityFactor) ? Math.Max(1, policy.MaxStabilityFactor) : 1;
}
