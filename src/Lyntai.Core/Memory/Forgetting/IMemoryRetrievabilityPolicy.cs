namespace Lyntai.Memory.Forgetting;

/// <summary>
/// Which retrievability policy computed this entry's stored <see cref="MemoryDecayState.Stability"/> AND
/// <see cref="MemoryDecayState.Difficulty"/> — read through <see cref="Lyntai.Memory.MemoryProvenance"/>,
/// never compared or combined directly (design doc §5.7). Retrievability needs provenance because it WRITES
/// persisted state a later policy might need and not find: <see cref="DsrRetrievability"/> maintains
/// difficulty on every review now too, which is what makes this
/// distinction load-bearing rather than hypothetical — an entry whose difficulty reads <c>5</c> (the neutral
/// value — see <see cref="DsrOptions.NeutralDifficulty"/>) might be a row this policy
/// judged genuinely average, or a row that has simply never been touched since it was written with no
/// explicit signal, and a bare number cannot tell the two apart. (A row whose difficulty reads <c>1</c>,
/// the OLD neutral, is most likely a row written or last reinforced before this correction — see
/// <see cref="MemoryDecayState.Difficulty"/>'s own remarks — though a genuinely judged-easiest row also
/// reads that way, the same ambiguity one level down.) Provenance is what makes the FIRST distinction
/// possible: <c>None</c> means no retrievability policy ever touched this row, full stop.
/// <see cref="DsrRetrievability"/> is still a PARTIAL, UNFITTED FSRS in other ways it discloses on its own
/// class doc — no per-grade rating (a derived grade stands in, see its own
/// <see cref="DsrRetrievability.Reinforce"/> remarks), no mean-reversion term, and every constant is FSRS's
/// own published default rather than fitted against this library's own review history.
/// <para><b>Bits 0-31 are reserved for this library; never allocate above bit 31 here.</b> Bits 32-62 are a
/// consumer's own range — <see cref="IMemoryRetrievabilityPolicy"/> is public and a third-party
/// implementation must be able to carry provenance too, so this enum stays open to an unnamed member: cast
/// any single bit in 32-62 to this type, exactly as a named library member below is. Bit 63 is NEVER set —
/// see <see cref="Lyntai.Memory.MemoryProvenance"/> for why.</para></summary>
[Flags]
public enum MemoryRetrievabilityProvenance : long
{
    /// <summary>No policy computed this entry's stored stability — every row from before this domain
    /// existed, and nothing else.</summary>
    None = 0x0000_0000,

    /// <summary><b>RETIRED — the policy that declared this bit, <c>HalfLifeRetrievability</c>, was deleted in
    /// 3.0 (<c>docs/DECISIONS.md</c>).</b> The member and its value stay exactly as they were: every row a
    /// 2.5.x deployment wrote under that curve still carries this bit in <c>GraphNode.ProvenanceRetrievability</c>,
    /// and freeing the value for reuse would let a future policy silently claim credit for computing a
    /// 2.5.x row's stability that it never touched — precisely the misattribution provenance exists to
    /// prevent. <b>No policy this library ships, or that a consumer registers, may declare this bit again.</b>
    /// <c>MemoryProvenanceTests</c> (<c>tests/Lyntai.Tests/Memory/MemoryProvenanceTests.cs</c>) pins that no
    /// shipped policy does; a consumer's own policy declaring bit <c>32</c> or above is unaffected, since this
    /// bit is in the library's own 0-31 range.</summary>
    HalfLife = 0x0000_0001,

    /// <summary><see cref="DsrRetrievability"/>.</summary>
    Dsr = 0x0000_0002,
}

/// <summary>
/// The model of forgetting. Swappable, and the default is registered for you — nothing has to be
/// implemented to use graph memory.
/// <para>Exposing the constants as loose numbers would settle the VALUES while freezing the FORMULA, so an
/// application can tune <see cref="DsrOptions"/> or replace the curve entirely, and neither choice
/// forecloses the other.</para>
/// <para>A policy never sees a clock. What "age" counts is
/// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>'s business; a policy only turns an age into a
/// probability.</para>
/// </summary>
public interface IMemoryRetrievabilityPolicy
{
    /// <summary>Retrievability in [0,1]. Must be 1 at zero age and must never increase with age.</summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    double Retrievability(in MemoryDecayState state);

    /// <summary>The entry's new decay bookkeeping after a successful recall — the FULL state, not a
    /// delta. A scalar return can only ever persist <see cref="MemoryDecayState.Stability"/>; <see cref="DsrRetrievability"/>
    /// is the first policy to use the wider seam: it now maintains
    /// <see cref="MemoryDecayState.Difficulty"/> too, per FSRS's own difficulty-update law adapted to a
    /// GRADE this library derives rather than receives (see <see cref="DsrRetrievability.Reinforce"/>'s own
    /// remarks for exactly what was adapted). Returning the state gives a policy room to own more tomorrow —
    /// the store persists whatever comes back, and <see cref="Provenance"/> already records who computed it.
    /// <para><b>A policy MUST return every field it does not own exactly as <paramref name="state"/> carries
    /// it.</b> Today that is every field except <see cref="MemoryDecayState.Stability"/> and
    /// <see cref="MemoryDecayState.Difficulty"/>, since the one shipped curve writes nothing else back — so a
    /// caller may persist the WHOLE returned state without special-casing which fields a particular policy
    /// claimed. The shared retrievability-policy test contract pins exactly this for every
    /// implementation.</para>
    /// <para><b><see cref="MemoryDecayState.Stability"/> itself must never be smaller than the current one —
    /// unconditionally, including above any ceiling the policy imposes.</b> A policy that bounds growth caps
    /// it with a FLOOR under the clamp (<c>Math.Max(current, Math.Min(grown, ceiling))</c>), so an entry
    /// already stored past the ceiling is FROZEN — it can no longer grow — rather than truncated down to it.
    /// The distinction is not academic: <see cref="DsrRetrievability"/> shipped the bare
    /// <c>Math.Min(grown, MaxStability)</c> through 2.5.x and a stored 100000 came back as 2000, a 50×
    /// SHORTENING, reachable by lowering the ceiling under an existing corpus or by any stability written
    /// outside the policy (fixed 2026-08-11, <c>docs/task-archive.md</c> Part 54 DSR2; pinned by
    /// <c>RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory</c>, which exercises an
    /// over-ceiling stability precisely because the ordinary fixture cannot).</para></summary>
    /// <param name="state">The entry's decay bookkeeping.</param>
    MemoryDecayState Reinforce(in MemoryDecayState state);

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

    /// <summary>This policy's own bit in <see cref="MemoryRetrievabilityProvenance"/> — what a store OR's
    /// into <c>GraphNode.ProvenanceRetrievability</c> whenever this policy computes
    /// <see cref="InitialStability"/> or <see cref="Reinforce"/>. Exactly one bit; never
    /// <see cref="MemoryRetrievabilityProvenance.None"/> and never more than one bit, or a fitness check
    /// reading it would report this policy as having contributed when it never ran (or never distinguish it
    /// from a different one).</summary>
    MemoryRetrievabilityProvenance Provenance { get; }

    /// <summary>The grade this policy would derive from <paramref name="state"/> — for a REVIEW LOG (design
    /// spec §3) to record what a reinforcement actually did, never to
    /// re-derive it later from whatever state happens to be at hand. <b>MUST be exactly the value
    /// <see cref="Reinforce"/> itself would use internally were it called on this SAME <paramref name="state"/>
    /// right now</b> — <see cref="DsrRetrievability"/> guarantees this by construction: both this member and
    /// <see cref="Reinforce"/>'s own difficulty-update step call the identical private formula, gated by the
    /// identical branch, so there is exactly one code path computing the value rather than two that merely
    /// agree today.
    /// <para><b>Null means "no grade was actually used this reinforcement," not merely "this policy has no
    /// grade concept."</b> Both collapse to the same answer for a caller that only wants to log what
    /// happened: nothing to record. The two ARE different internally — <see cref="DsrRetrievability"/>
    /// returns null on a same-position/session-burst review (<see cref="Reinforce"/>'s own Δt=0 branch;
    /// design spec §1's drift guard applies only when the difficulty update actually ran) even though it
    /// always has a grade concept, while a future policy that never grades anything returns null
    /// unconditionally.</para>
    /// <para><b>Defaults to null.</b> Exactly one shipped policy owns a grade concept today
    /// (<see cref="DsrRetrievability"/>); every other implementation — including every test double already
    /// in this tree — needs no override to satisfy this member correctly.</para>
    /// <para><paramref name="state"/> MUST be the PRE-reinforcement state, the same one a caller is about to
    /// pass to <see cref="Reinforce"/> — calling this on a state <see cref="Reinforce"/> already
    /// produced answers a different, meaningless question (design spec §1's own caveat: the grade must come
    /// from the state that made THIS recall succeed, never from a value the same update is about to
    /// write).</para></summary>
    /// <param name="state">The entry's decay bookkeeping, before this reinforcement.</param>
    double? DerivedGrade(in MemoryDecayState state) => null;
}
