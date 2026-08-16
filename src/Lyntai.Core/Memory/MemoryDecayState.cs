namespace Lyntai.Memory;

/// <summary>What a decay curve needs to know about one entry. Carries no content and no clock, so a policy
/// is pure arithmetic and trivially testable.</summary>
/// <param name="Age">How much has happened in this memory since the entry was last used, on the engine's
/// own scale — see <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>. Not a duration: an engine may
/// count writes, characters written, or elapsed days, and <paramref name="Stability"/> is in the same
/// units.
/// <para>A store tracks the three underlying primitives (design doc §5.7) unconditionally, so any shipped
/// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/> can project its own view of them without ever
/// reinterpreting a stored number. This member is whatever the engine passes in.</para></param>
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
/// direction that keeps <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.CandidateCutoff"/> a
/// conservative superset.</para></param>
/// <param name="Signals">Open retention signals carried from the write, for an
/// <see cref="Lyntai.Memory.Modulation.IMemoryRetentionPolicy"/> to read. Empty by default, so every
/// construction that predates signals still compiles and still decays identically.</param>
/// <param name="Difficulty">How hard this entry is to retain, on FSRS's 1-10 scale where <c>1</c> is
/// EASIEST and <c>10</c> is hardest — the LIVE counterpart to
/// <see cref="MemorySignals.WellKnown.Difficulty"/>.
/// <para><b>The neutral/unjudged value is the mid-point <c>5</c>, NOT the floor <c>1</c></b> — <c>1</c> is
/// the scale's EASIEST, not "no information", and starting there makes the axis structurally unable to
/// vary. <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own remarks carry the
/// mechanism and why <c>5</c> is a stated choice rather than an FSRS-published one.</para>
/// <para><b>Precedence, when a signal-bag value and this live value disagree:</b> an explicit
/// <see cref="MemorySignals.WellKnown.Difficulty"/> on ANY write — a fresh node, or a re-remember carrying
/// a NON-EMPTY bag — OVERWRITES this field, so a consumer's explicit judgement is never shadowed by an
/// inferred one. Between writes only <c>Reinforce</c> evolves it, and it never re-reads the bag: a
/// policy's own tracking survives an unrelated re-remember whose bag happens to be empty.</para>
/// <para><b>A row written before the neutral was corrected stores <c>1</c> and keeps it</b> — not migrated,
/// and indistinguishable BY VALUE from a genuine "easiest" judgement except through
/// <see cref="Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance"/>. It does NOT drift toward the new
/// dynamics by being touched: from the floor, an Easy-leaning grade (the common case for a successful
/// recall) damps to a value BELOW <c>1</c> that the clamp returns to <c>1</c>, so such a row stays there
/// permanently. Only a Hard-leaning recall can move it.</para></param>
public readonly record struct MemoryDecayState(
    double Age,
    int RecallCount,
    double Stability,
    double Strength = 0,
    double StrengthAge = 0,
    MemorySignals Signals = default,
    double Difficulty = 5);
