namespace Lyntai.Memory;

/// <summary>What a decay curve needs to know about one entry. Carries no content and no clock, so a policy
/// is pure arithmetic and trivially testable.</summary>
/// <param name="Age">How much has happened in this memory since the entry was last used, on the engine's
/// own scale — see <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>. Not a duration: an engine may
/// count writes, characters written, or elapsed days, and <paramref name="Stability"/> is in the same
/// units.
/// <para>A store now tracks the three underlying primitives (design doc §5.7) unconditionally, so any of the
/// four shipped <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/> implementations CAN project its own
/// view of them via <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy.Age"/> — coexisting, without ever
/// reinterpreting a stored number. This member is unaffected: it stays exactly what the engine passes in,
/// the same shape and the same source as before.</para></param>
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
/// <para><b>The neutral/unjudged value is <c>5</c> (the mid-point), NOT <c>1</c> — corrected 2026-08-11.</b>
/// An earlier draft of this field defaulted to <c>1</c>, which is the scale's EASIEST value, not "no
/// information" — starting every unjudged entry at the floor made the axis structurally unable to move (a
/// diagnostic on a live replay, fsrs-properly plan Task 4 follow-up, found the corpus's own most-reinforced
/// entries pinned at exactly <c>1</c> across well over a hundred touches each, because the update law's own
/// grade-driven delta is overwhelmingly Easy-leaning on a fresh, successful recall, and computes a value
/// BELOW the floor that <see cref="Lyntai.Memory.Forgetting.DsrRetrievability.Reinforce"/>'s
/// <c>Math.Clamp(_, 1, 10)</c> floors right back). <c>5</c> is equidistant from both bounds and free to move
/// in EITHER direction on the very next review — see
/// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own remarks for the full reasoning
/// and why <c>5</c> is a STATED CHOICE, not a value derived from anything FSRS publishes.</para>
/// <para>Additive-but-binary-breaking, the same pattern <see cref="Strength"/>/<see cref="StrengthAge"/>/
/// <see cref="Signals"/> and <see cref="GraphNode"/>'s own age primitives used (2026-08-10 fsrs-properly
/// plan, Task 2). Before this field existed, <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/> read
/// difficulty straight from the signals bag on every call and never wrote anything back, so it was frozen
/// at whatever a salience policy judged at write time — a PARTIAL FSRS, in the taxonomy
/// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Reinforce"/> itself used to document.
/// This field is what a policy that DOES maintain difficulty writes back into, via the same
/// full-state-return seam <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy.Reinforce"/> has
/// always exposed.</para>
/// <para><b>Precedence when a signal-bag value and this live value disagree</b> — the question a second
/// writable owner always raises: an explicit <see cref="MemorySignals.WellKnown.Difficulty"/> value
/// supplied on ANY write (a fresh node, or a re-remember carrying a NON-EMPTY signals bag) OVERWRITES this
/// field, exactly like <c>salience</c>'s own promoted column already does — a consumer's explicit judgement
/// must never be silently shadowed by the model's own inferred value. Between writes, only
/// <c>Reinforce</c> evolves it further, and it never re-reads the bag: the live value is what every
/// reinforcement both reads and writes, so a policy's own tracking is not reset by an unrelated re-remember
/// whose incoming bag happens to be empty (a salience policy declining to judge, exactly as an empty bag already
/// preserves <see cref="Signals"/> itself).</para>
/// <para><c>1</c> (the OLD neutral) for every row written before this correction — including every row
/// written under the LIVE difficulty axis between Task 2 (2026-08-10) and this fix (2026-08-11) — and
/// indistinguishable BY VALUE from a row this field's owner judged genuinely easiest on purpose:
/// <see cref="Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance"/> is what tells the two apart
/// (<c>None</c> means no retrievability policy ever touched this row at all). A pre-fix row is NOT migrated
/// or reinterpreted — it keeps its stored <c>1</c> as a historical fact. <b>It does NOT drift toward the new
/// dynamics merely by being touched again — a whole-plan review caught an earlier draft of this paragraph
/// claiming exactly that, and it is false for precisely the population that matters.</b> Starting at the
/// FLOOR, a touch that derives an Easy-leaning grade (the common case for a fresh, successful recall — this
/// library's own corpus measured 89.6% of derived grades Easy-leaning) computes a damped value BELOW the
/// floor that <see cref="Lyntai.Memory.Forgetting.DsrRetrievability.Reinforce"/>'s own clamp floors right
/// back to the IDENTICAL <c>1</c> — a pre-fix row that keeps being successfully recalled stays at the floor
/// PERMANENTLY, never drifting anywhere, which is the exact defect
/// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own correction exists to prevent for
/// every row written from 2026-08-11 on. Only a Hard-leaning recall (roughly the ~10% of this corpus's own
/// derived grades below neutral) can move a pre-fix row away from the floor at all.</para></param>
public readonly record struct MemoryDecayState(
    double Age,
    int RecallCount,
    double Stability,
    double Strength = 0,
    double StrengthAge = 0,
    MemorySignals Signals = default,
    double Difficulty = 5);
