namespace Lyntai.Memory.Salience;

/// <summary>
/// Judges nothing: every write gets neutral salience. <b>The supported way to turn salience OFF.</b>
///
/// <para><b>Why this type has to exist.</b> Registering an empty collection does NOT disable salience — the
/// engine treats null-or-empty as "take the shipped default" and installs
/// <see cref="StructuralSaliencePolicy"/>, exactly as the age seam does. That is a reasonable convention and
/// a bad trap: "register nothing" is precisely what a consumer who wants nothing would try, and it silently
/// yields the opposite. Registering this policy is unambiguous.</para>
///
/// <para><b>Measured cost of not having it.</b> A study comparing salience ON against OFF built its control
/// by passing an empty collection, so the control was a second copy of the treatment — caught only by an
/// assertion added for a different reason. A control silently identical to its treatment reports agreement,
/// which reads exactly like a null result (<c>TASKS.md</c> Part 69).</para>
///
/// <para><b>What switching salience off actually costs</b>, so the choice is informed: decay resistance and
/// store-admission priority, both default-on (<b>D45</b>). Measured on this library's corpus, salience
/// HELPS — it lowered both the miss rate and the share of junk in recalls on material that can reach one.
/// The one shape where it measurably hurt is <c>many-candidates</c>. So this is a real knob for a
/// dense-candidate deployment, not a general recommendation.</para>
///
/// <para>It declares <see cref="MemorySalienceProvenance.Structural"/> — the bit of the policy it replaces.
/// Safe because provenance uniqueness is checked across what is actually REGISTERED, and honest because a
/// policy that never returns a signal never contributes provenance to any row: an entry written under this
/// policy records <c>None</c>, which is the truth.</para>
/// </summary>
public sealed class NeutralSaliencePolicy : IMemorySaliencePolicy
{
    /// <inheritdoc />
    public MemorySalienceProvenance Provenance => MemorySalienceProvenance.Structural;

    /// <inheritdoc />
    public MemorySignals Signals(MemoryWrite write, in SalienceContext context) => MemorySignals.Empty;
}
