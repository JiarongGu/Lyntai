namespace Lyntai.Memory.Modulation;

/// <summary>
/// Owns how several coexisting <see cref="IMemoryRetentionPolicy"/>s' already-clamped
/// <see cref="IMemoryRetentionPolicy.StabilityFactor"/> values combine into the ONE multiplier
/// <see cref="ModulatedRetrievability"/> applies — extracted so the combination rule is a swappable decision
/// rather than a hardcoded line inside that class (2026-08-10 memory-policy-seams plan, Task 3). Retention was
/// already plural before this task; what changed is that <c>ModulatedRetrievability</c> used to multiply
/// factors together with no name and no way to replace it — nobody had ever CHOSEN multiplication over max,
/// sum or a weighted blend, it was simply what the class happened to do.
/// <para><b>The clamp stays with <see cref="ModulatedRetrievability"/>, not this seam.</b> "Clamp, never
/// trust" (each policy's report bounded to <c>[1, its declared maximum]</c>) is a CONTRACT enforcement —
/// it has to run before composition sees a value, or an undeclared excess could break
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/>'s superset guarantee
/// regardless of how the clamped values are later combined. This seam only decides how to combine values
/// already known to be safe.</para>
/// <para><b>Zero or one registered retention policy makes this the identity</b> — the default
/// <see cref="MultiplicativeRetentionComposition"/> reduces an empty list to 1 (no widening at all) and a
/// one-element list to that element's own value, both unchanged from <see cref="ModulatedRetrievability"/>'s
/// pre-Task-3 behaviour.</para>
/// </summary>
public interface IMemoryRetentionCompositionPolicy
{
    /// <summary>Combine every registered policy's already-clamped factor into the ONE multiplier applied
    /// to a stored stability. An empty list — no retention policies registered — must return 1, the neutral
    /// multiplier: no widening at all.</summary>
    /// <param name="factors">Each policy's <see cref="IMemoryRetentionPolicy.StabilityFactor"/>, already
    /// clamped into <c>[1, its own declared maximum]</c>, in registration order.</param>
    double Compose(IReadOnlyList<double> factors);
}

/// <summary>
/// The default composition, and today's only behaviour given a name: MULTIPLY every clamped factor together.
/// Each policy lengthens a half-life independently of the others — salience for one reason, a future
/// dimension for another — so stacking them multiplicatively is "each dimension's protection applies on TOP
/// of what the others already bought", the same reading <c>ModulatedRetrievability</c>'s own
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> widening already assumes
/// (the product of every declared maximum, not a sum or a max).
/// </summary>
public sealed class MultiplicativeRetentionComposition : IMemoryRetentionCompositionPolicy
{
    /// <inheritdoc />
    public double Compose(IReadOnlyList<double> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        var product = 1d;
        foreach (var factor in factors) product *= factor;
        return product;
    }
}
