namespace Lyntai.Memory.Modulation;

/// <summary>
/// A retention dimension. <b>This is the extension point</b>: adding one is a class and a registration,
/// never an edit to a record or a conditional.
/// <para><b>A retention policy may only LENGTHEN a half-life.</b> Floors and unconditional admission come from
/// <see cref="MemoryGrade"/> alone, which is what keeps every storage-side decision indexable on the grade
/// column rather than on a value buried in a signal bag. A factor below 1 is clamped away.</para>
/// <para><b><see cref="MaxStabilityFactor"/> is load-bearing, not documentation.</b>
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> is expressed against the STORED stability, which
/// modulation does not change, so the cutoff widens by the product of every registered policy's declared
/// maximum. <b>What an under-wide cutoff costs is DELETION, not a short recall</b>: seeding applies no
/// faintness bound at all, so the cutoff's one consumer is
/// <see cref="IMemoryGraphStore.PruneAsync"/> — an unbounded policy would make no finite cutoff correct,
/// and entries the modulated curve still rates perfectly retrievable would be permanently removed. Same
/// defect, same reason, as <see cref="Lyntai.Memory.Forgetting.DsrOptions.MaxConnectionBoost"/>. The declared bound is CLAMPED
/// rather than trusted: soundness must not depend on an implementation being honest about itself.</para>
/// </summary>
public interface IMemoryRetentionPolicy
{
    /// <summary>Identifies this dimension in diagnostics. Not used for resolution.</summary>
    string Name { get; }

    /// <summary>The largest factor <see cref="StabilityFactor"/> may ever return. Must be finite and at
    /// least 1; the decorator clamps to it either way.</summary>
    double MaxStabilityFactor { get; }

    /// <summary>How much to lengthen this entry's half-life, as a multiplier at or above 1.
    /// <para>Decide the factor from <paramref name="state"/>'s signals and the entry's
    /// <see cref="MemoryGrade"/> — never from its age. <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Retrievability"/>
    /// must never increase with age, and a policy that keyed its factor on age could break that
    /// guarantee once it runs through <see cref="ModulatedRetrievability"/>.</para></summary>
    /// <param name="state">The entry's decay bookkeeping, including its signals.</param>
    double StabilityFactor(in MemoryDecayState state);
}
