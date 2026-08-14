using System.Globalization;

namespace Lyntai.Memory.Forgetting;

/// <summary>Constants of the power-law curve — the only shipped forgetting curve as of 3.0. The exponential
/// curve this once shared the domain with, <c>HalfLifeRetrievability</c>, was deleted the same release
/// (<c>docs/DECISIONS.md</c> D49 made this curve the registered default on FSRS's own external validation;
/// see <c>CHANGELOG.md</c>'s <c>## Unreleased</c> for the deletion itself).</summary>
public sealed record DsrOptions
{
    private readonly double _decay = -0.5;
    private readonly double _initialStability = 20;
    private readonly double _maxStability = 2000;
    private readonly double _connectionBoost = 0.5;
    private readonly double _maxConnectionBoost = 4;
    private readonly double _edgeHalfLife = 100;
    private readonly double _reinforceGain = 0;   // MEASURED default — see ReinforceGain's own remarks
    private readonly double _stabilizationDecay = 0.4;
    private readonly double _spacingWeight = 1.5;
    private readonly double _difficultyWeight = 0.08;
    private readonly double _difficultyChangeWeight = 3.0194;
    private readonly double _difficultyReversionWeight = 0.001;
    private readonly double _difficultyReversionTarget = -4.77;
    private readonly double _neutralDifficulty = 5;

    /// <summary>Half-life of a brand-new entry, in the engine's units. <b>Unmeasured.</b>
    /// <para><b>Must be a FINITE positive number.</b> <see cref="DsrRetrievability.Reinforce"/> substitutes
    /// this value whenever a stored <see cref="MemoryDecayState.Stability"/> is non-positive, so a bad value
    /// here reaches <c>Math.Pow(stability, -StabilizationDecay)</c> as the base. At zero that is
    /// <c>+Infinity</c> (a negative exponent on zero), and the substituted stability is itself zero, so the
    /// final <c>stability × (1 + increase)</c> is <c>0 × Infinity</c> — <c>NaN</c> by IEEE-754's own rule,
    /// the same failure shape <see cref="Decay"/>'s guard exists to prevent, and a NaN stability written
    /// back to a store is PERMANENT. A negative value reaches the same power with a negative base and a
    /// non-integer exponent, which is <c>NaN</c> directly.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double InitialStability
    {
        get => _initialStability;
        init
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.InitialStability must be a finite positive number — see the property's XML " +
                    "doc for why zero, a negative value, and a non-finite value each corrupt Reinforce's " +
                    "output rather than merely degrading it.");
            _initialStability = value;
        }
    }

    /// <summary>The power-law exponent. FSRS fits this against real review logs and lands near −0.5; that is
    /// the default here.
    /// <para>A less negative value is a heavier tail — older memories resisting forgetting more.</para>
    /// <para><b>Must be a FINITE negative number — this is the first policy whose option domain is
    /// load-bearing.</b> At <c>Decay = 0</c>, <c>Math.Pow(x, 0)</c> is 1 for every <c>x</c>, so
    /// <see cref="DsrRetrievability.Retrievability"/> would return 1 forever — nothing would ever be
    /// forgotten — while <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> still reports a FINITE bound
    /// from the same formula, so <c>PruneAsync</c> would delete every row past that bound that the curve
    /// itself still rates fully retrievable: the superset guarantee failing at full scale, not by a rounding
    /// error. At any positive <c>Decay</c> the derived <c>F</c> falls below −1, so the curve's base goes
    /// negative once age is large enough and <c>Math.Pow</c> of a negative base to a non-integer exponent is
    /// <c>NaN</c> — which <c>Math.Clamp</c> propagates straight through into a stored retrievability and
    /// poisons ranking wherever it is read.</para>
    /// <para><c>double.NegativeInfinity</c> is REJECTED TOO, and not for symmetry: it reproduces the
    /// <c>Decay = 0</c> failure exactly, by a different route. <c>1 / NegativeInfinity</c> is <c>-0.0</c>, so
    /// <c>F = 0.5^(-0.0) − 1 = 0</c>, the curve's base collapses to <c>1</c>, and <c>Math.Pow(1.0,
    /// NegativeInfinity)</c> is <c>1.0</c> by IEEE-754's own special-case rules — <c>r ≡ 1</c> forever, the
    /// same permanent-retrievability failure, while <c>CandidateCutoff</c> computes <c>Math.Pow(floor, -0.0)
    /// = 1</c>, giving <c>(1 − 1) / 0 = NaN</c>, and a <c>NaN</c> cutoff compares false against every
    /// candidate so <c>PruneAsync</c> silently stops pruning entirely — the opposite failure, from the same
    /// option. A guard reading only "less than zero" misses this because <c>NegativeInfinity &lt; 0</c> is
    /// true; <see cref="double.IsFinite(double)"/> is what actually excludes it.</para>
    /// <para>Guarding here turns every one of these into an <see cref="ArgumentOutOfRangeException"/> at the
    /// line that configured the policy, rather than a wrong answer surfacing deep in the recall path with no
    /// error at all. <c>NaN</c> is rejected by the same <see cref="double.IsFinite(double)"/> check: every
    /// comparison against <c>NaN</c> is false, so a bare <c>&gt;= 0</c> guard alone would let it through
    /// silently.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a positive value, a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double Decay
    {
        get => _decay;
        init
        {
            if (!double.IsFinite(value) || value >= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.Decay must be a finite negative number — see the property's XML doc for why " +
                    "zero, a positive value, NaN, and negative infinity each corrupt the curve rather than " +
                    "merely degrading it.");
            _decay = value;
        }
    }

    /// <summary>The ceiling reinforcement cannot grow stability past. Unbounded compounding would let an
    /// ASSOCIATIVE entry become permanently retrievable while still labelled associative. <b>Unmeasured.</b>
    /// <para><b>Must be a FINITE positive number — the one option on this record whose bad values reach
    /// PERSISTED state.</b> <see cref="DsrRetrievability.Reinforce"/> ends in
    /// <c>Math.Max(stability, Math.Min(stability × (1 + increase), MaxStability))</c> and
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> feeds that return straight into the store's
    /// <c>TouchAsync</c>, so whatever this produces is WRITTEN BACK. <c>NaN</c> propagates through
    /// <c>Math.Min</c> AND the <c>Math.Max</c> floor outside it by IEEE-754's own rule — a clamp is not a
    /// finiteness guard, and neither is a floor, the same fact
    /// <see cref="Lyntai.Memory.Modulation.ModulatedRetrievability"/> and
    /// <see cref="DsrRetrievability.Reinforce"/>'s own
    /// finiteness floor already write down — and a <c>NaN</c> stability compares false against every
    /// threshold, so the entry then neither ranks, nor prunes, nor reports as broken: silent, PERMANENT
    /// corruption reachable from a public option rather than only from a BYO policy.</para>
    /// <para>Zero or a negative ceiling is not merely degenerate either: it would clamp EVERY reinforcement
    /// down to itself. That route to
    /// <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>'s own written
    /// guarantee being broken — the result "must never be smaller than the current one", an EXISTING contract
    /// fact (<c>RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory</c>), not a rule invented
    /// here — is why this guard is not something a later change may relax. <b>The OTHER route, a stability
    /// already stored above a legitimately-configured ceiling, was closed separately on 2026-08-11</b>
    /// (<c>docs/task-archive.md</c> Part 54, DSR2): <see cref="DsrRetrievability.Reinforce"/> now floors its clamp at the
    /// entry's own stability, so this ceiling caps GROWTH and never CUTS — an over-ceiling entry is FROZEN
    /// rather than truncated, which is exactly what this property's first sentence asks for. The guard here
    /// stays regardless, because a non-positive ceiling is meaningless on its own terms.</para>
    /// <para><c>+Infinity</c> is REJECTED TOO, and not for symmetry: <c>Math.Min(x, +Infinity)</c> is
    /// <c>x</c>, so it removes the ceiling entirely — precisely the unbounded compounding this property's
    /// own first sentence exists to prevent. A deployment that genuinely wants an effectively unreachable
    /// ceiling writes <see cref="double.MaxValue"/>, which says so at the configuring line.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double MaxStability
    {
        get => _maxStability;
        init
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.MaxStability must be a finite positive number — see the property's XML doc " +
                    "for why a non-finite value is written back into the store as a permanently poisoned " +
                    "stability, and why a non-positive ceiling shortens every memory it clamps.");
            _maxStability = value;
        }
    }

    /// <summary>How steeply connectedness lengthens a half-life, as <c>1 + factor · ln(1 + strength)</c>, and
    /// the ceiling that keeps <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> finite.
    /// <para><b>Must be FINITE and at or above zero.</b> A negative factor does NOT make connectedness
    /// SHORTEN a half-life: <see cref="DsrRetrievability.EffectiveStability"/>'s outer
    /// <c>Math.Max(stability, …)</c> floors the boosted value at the stored one precisely so that cannot
    /// happen, and the contract fact that says so
    /// (<c>RetrievabilityPolicyContract.Connectedness_never_lowers_retrievability</c>) keeps passing. The
    /// failure is quieter than an inversion and therefore worse: connectedness becomes a silent NO-OP for
    /// every entry that has any strength at all, while <see cref="MaxConnectionBoost"/> still widens
    /// <see cref="DsrRetrievability.CandidateCutoff"/> for a protection that is no longer happening — the
    /// mechanism reads as configured at every call site and is structurally off, the same shape
    /// <see cref="SpacingWeight"/>'s own negative branch describes one law further in. Zero is ALLOWED and
    /// means exactly that, deliberately (<c>1 + 0 · ln(…) = 1</c>), and it reads that way at the
    /// configuring line where a sign error does not.</para>
    /// <para>Non-finite is the end that corrupts, and <c>+Infinity</c> is the sharp case rather than the
    /// symmetric one: at <see cref="MemoryDecayState.Strength"/> zero — the ordinary state of most entries —
    /// the term is <c>Infinity × ln(1) = Infinity × 0</c>, which is <c>NaN</c> by IEEE-754's own rule.
    /// <c>Math.Min</c> and <c>Math.Max</c> then propagate that through
    /// <see cref="DsrRetrievability.EffectiveStability"/> into a <c>NaN</c> retrievability, which compares
    /// false against every threshold (so the entry silently stops ranking) and, through the derived grade
    /// <see cref="DsrRetrievability.Reinforce"/> computes from it, reaches the
    /// <see cref="MemoryDecayState.Difficulty"/> that same call WRITES BACK.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double ConnectionBoost
    {
        get => _connectionBoost;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.ConnectionBoost must be a finite non-negative number — see the property's " +
                    "XML doc for why a negative value turns connectedness into a silent no-op the cutoff " +
                    "still pays for, and why an infinite one is NaN at the commonest state of all.");
            _connectionBoost = value;
        }
    }

    /// <summary>The largest multiple connectedness may apply to a half-life, and the factor
    /// <see cref="DsrRetrievability.CandidateCutoff"/> widens by. <b>Load-bearing, not decoration</b>: a store
    /// filters candidates against the STORED stability while a connected entry's effective half-life is up to
    /// this multiple of it, so without a finite bound no cutoff could cover a well-connected entry and
    /// <c>PruneAsync</c> would reap memories the curve still rates perfectly retrievable — the ones
    /// connectedness exists to protect. <b>Unmeasured.</b>
    /// <para><b>Must be FINITE and at or above 1.</b> Both readers already floor it —
    /// <see cref="DsrRetrievability.EffectiveStability"/> and <see cref="DsrRetrievability.CandidateCutoff"/>
    /// each take <c>Math.Max(1, MaxConnectionBoost)</c> — so a value below 1 never actually reduces a
    /// half-life; it is a configured number that silently means a DIFFERENT number, which is harder to
    /// diagnose than either a reduction or a throw. The floors stay, because the paragraph above depends on
    /// them, but they exist there for the reason
    /// <see cref="Lyntai.Memory.Modulation.IMemoryRetentionPolicy.MaxStabilityFactor"/> is clamped rather
    /// than trusted: that one is an INTERFACE member on somebody else's implementation and cannot be
    /// validated at construction. This is an option on a shipped record, which can be — so it rejects at the
    /// line that configured it instead of substituting a different number and carrying on.</para>
    /// <para>Non-finite is the end that corrupts, and it corrupts BOTH readers at once, because
    /// <c>Math.Max(1, NaN)</c> is <c>NaN</c> (IEEE-754 — a clamp is not a finiteness guard):
    /// <see cref="DsrRetrievability.Retrievability"/> goes <c>NaN</c>, and so does
    /// <see cref="DsrRetrievability.CandidateCutoff"/>, where a <c>NaN</c> bound compares false against
    /// every candidate and <c>PruneAsync</c> silently stops pruning entirely — the same ending
    /// <see cref="Decay"/>'s own doc traces from <see cref="double.NegativeInfinity"/>. <c>+Infinity</c> is
    /// rejected as well: it is not wrong so much as an unrequested escape hatch, turning the cutoff into
    /// <see cref="double.PositiveInfinity"/> for every floor, which
    /// <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> documents as correct at the cost of a full
    /// in-scope scan — a cost that should be asked for, not inherited from a boost ceiling.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a value below 1 or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double MaxConnectionBoost
    {
        get => _maxConnectionBoost;
        init
        {
            if (!double.IsFinite(value) || value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.MaxConnectionBoost must be a finite number at or above 1 — see the " +
                    "property's XML doc for why a value below 1 is silently floored to 1 by both readers " +
                    "rather than taking effect, and why a non-finite one makes CandidateCutoff stop " +
                    "bounding PruneAsync at all.");
            _maxConnectionBoost = value;
        }
    }

    /// <summary>Half-life of an entry's aggregate CONNECTION STRENGTH inside this curve — how fast a
    /// neighbourhood that went quiet stops propping the memory up, via
    /// <c>strength × 2^(−strengthAge / EdgeHalfLife)</c> feeding
    /// <see cref="DsrRetrievability.EffectiveStability"/> and therefore retrievability.
    /// <para><b>NOT <see cref="Lyntai.Memory.GraphMemoryOptions.EdgeHalfLife"/>, which has the same name and
    /// the same default of <c>100</c> and governs a DIFFERENT thing</b> — that one decays an edge's WEIGHT
    /// during traversal, which is what stops the graph saturating, and the engine reads it for every arm
    /// regardless of which retrievability policy is installed. They agree by coincidence, not by design, so
    /// name which one you mean whenever you tune either (`.claude/knowledge/pitfalls.md`; an earlier draft of
    /// this summary described that property's job rather than this one's, corrected 2026-08-11).</para>
    /// <para><b>Must be a FINITE positive number.</b> A half-life is positive by definition, and
    /// <see cref="DsrRetrievability.EffectiveStrength"/> reads a non-positive one as "no decay at all" —
    /// its <c>state.StrengthAge &lt;= 0 || EdgeHalfLife &lt;= 0</c> branch returns the raw stored strength —
    /// so zero or a negative value does not tune this mechanism, it silently switches it OFF, and a
    /// neighbourhood last touched a corpus ago keeps propping the memory up at full strength forever. That
    /// branch stays as belt and braces (its <see cref="MemoryDecayState.StrengthAge"/> half is still live),
    /// but the option can no longer reach it. <c>+Infinity</c> arrives at the same "off" by another route
    /// (<c>Math.Pow(2, -age/Infinity)</c> is <c>Math.Pow(2, -0)</c>, exactly <c>1</c>), and <c>NaN</c> is not
    /// caught by that branch at all — <c>NaN &lt;= 0</c> is false — so it reaches
    /// <c>Math.Pow(2, -age/NaN)</c>, the strength goes <c>NaN</c>, and
    /// <see cref="DsrRetrievability.EffectiveStability"/>'s <c>Math.Min</c>/<c>Math.Max</c> propagate it into
    /// a <c>NaN</c> retrievability. <see cref="double.IsFinite(double)"/> is what excludes both.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double EdgeHalfLife
    {
        get => _edgeHalfLife;
        init
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.EdgeHalfLife must be a finite positive number — see the property's XML doc " +
                    "for why a non-positive or infinite value switches connection-strength decay off " +
                    "instead of tuning it, and why NaN slips past the same branch into a NaN " +
                    "retrievability.");
            _edgeHalfLife = value;
        }
    }

    /// <summary>The overall scale of a reinforcement's stability gain — FSRS's <c>w[3]</c>-shaped knob.
    /// Multiplies the whole increase term in <see cref="DsrRetrievability.Reinforce"/>, so it does not
    /// express any one law on its own; it sets how strong the combined effect of all three is.
    ///
    /// <para><b>Defaults to ZERO as of 3.0, and that is a MEASURED default rather than a cautious one</b>
    /// (<c>docs/DECISIONS.md</c> D54, five studies). Stability growth driven by retrieval made recall
    /// measurably WORSE on every corpus shape and both quality metrics — and not because of how it was
    /// tuned: a capped variant and a non-compounding one computed purely from the entry's recall count were
    /// both tried and both lost to simply not growing. What survives the measurement is that a recall's
    /// value comes from its age RESET (which expires, and which still happens at every gain) rather than
    /// from a permanent half-life increase that banks the ranking policy's own errors.</para>
    ///
    /// <para><b>Durability did not go away; it moved to the two sources that measure well.</b> An entry is
    /// long-lived here because it was NOVEL when written
    /// (<see cref="Lyntai.Memory.Modulation.SalienceRetentionPolicy"/>, measured to help) or because it is
    /// well CONNECTED (<see cref="ConnectionBoost"/>) — properties of the material and of the graph, not of
    /// how often this engine's own ranker chose to return it.</para>
    ///
    /// <para><b>The three FSRS laws are kept, not deleted, and raising this turns them back on in one
    /// line.</b> They are a faithful implementation and the measurement behind the default is one synthetic
    /// corpus; a deployment with real review data may well find its own value here, which is exactly what
    /// <see cref="Lyntai.Memory.MemoryReviewLogPacing"/>'s log exists to make possible. Anything above zero
    /// restores the pre-3.0 behaviour proportionally; <c>2.0</c> is what 2.5.x shipped.</para>
    /// <para><b>Must be FINITE and at or above zero.</b> A negative gain does not SHRINK a memory:
    /// <see cref="DsrRetrievability.Reinforce"/>'s own finiteness floor,
    /// <c>double.IsFinite(increase) ? Math.Max(0, increase) : 0</c>, turns the whole negative increase into
    /// zero, so <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>'s "must never be smaller than the
    /// current one" guarantee still holds. What it produces is the silent NO-OP
    /// <see cref="SpacingWeight"/>'s own negative branch describes, one level further out: every entry
    /// returns its stability unchanged, no recall ever strengthens anything, and the whole corpus decays to
    /// prunable with no error anywhere. Because this multiplies the COMBINED term rather than one law's, it
    /// zeroes all three laws at once where <see cref="SpacingWeight"/> zeroes only law 3. Zero is ALLOWED
    /// and means exactly that as a deliberate setting — reinforcement off — which reads that way at the
    /// configuring line where a sign error does not.</para>
    /// <para>Non-finite lands in the same no-op by a different route and is rejected for the same reason it
    /// is everywhere else on this record: <c>NaN</c> reaches that floor, <c>Math.Max(0, NaN)</c> is
    /// <c>NaN</c> (IEEE-754), and the finiteness branch then reads it as no growth — a configured gain that
    /// silently means "never reinforce" is the one outcome no value of a knob called <i>gain</i> should be
    /// able to produce quietly.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double ReinforceGain
    {
        get => _reinforceGain;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.ReinforceGain must be a finite non-negative number — see the property's " +
                    "XML doc for why a negative or non-finite gain makes reinforcement a silent no-op " +
                    "across all three stability laws rather than merely weakening it.");
            _reinforceGain = value;
        }
    }

    /// <summary>Law 2 — <b>stabilization decay</b>: the exponent <c>k</c> in the <c>S^(−k)</c> term of
    /// <see cref="DsrRetrievability.Reinforce"/>. The higher the stored stability, the smaller the term, so
    /// a memory that is already durable gains proportionally less from one more recall than a fragile one
    /// does — what stops a frequently-recalled entry compounding into permanence. <b>Unmeasured.</b>
    /// <para><b>Must be FINITE and at or above zero.</b> A negative value does not weaken law 2, it
    /// <b>INVERTS</b> it: <c>S^(+k)</c> GROWS with stability, so the entry that is already most durable gains
    /// the most, which is exactly the runaway the law exists to stop. Measured at <c>−0.4</c> with every
    /// other default, an entry recalled at each half-life (age = stability, so <c>r = 0.5</c>) runs
    /// 20 → 168 → the 2000 ceiling in TWO reinforcements, where the correct sign runs 20 → 33.5 → 51.8 — and
    /// a permanently-ceilinged ASSOCIATIVE entry is the defect <see cref="MaxStability"/> exists to prevent,
    /// arriving through a different door. Zero is ALLOWED and means the law is off (a gain independent of
    /// stability): degenerate, but not reversed.</para>
    /// <para>No upper bound, because there is no inversion at that end: a large <c>k</c> drives
    /// <c>S^(−k)</c> toward zero — or, for a stability below 1, toward overflow, which
    /// <see cref="DsrRetrievability.Reinforce"/>'s own finiteness floor reads as "no growth". Both weaken the
    /// gain monotonically rather than reversing its direction.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double StabilizationDecay
    {
        get => _stabilizationDecay;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.StabilizationDecay must be a finite non-negative number — see the " +
                    "property's XML doc for why a negative value inverts law 2 rather than merely " +
                    "weakening it, compounding a stability to the ceiling instead of damping it.");
            _stabilizationDecay = value;
        }
    }

    /// <summary>Law 3 — <b>the spacing effect</b>: how strongly a low retrievability at review time amplifies
    /// the gain, via <c>e^(spacing·(1−r)) − 1</c> in <see cref="DsrRetrievability.Reinforce"/>. At
    /// <c>r = 1</c> the term is zero regardless of this weight — an immediate re-recall gains nothing — and
    /// a larger weight makes recalling a nearly-forgotten entry (low <c>r</c>) reward far more than recalling
    /// a fresh one. <b>Unmeasured.</b>
    /// <para><b>Must be FINITE and within <c>[0, 700]</c>, and BOTH ends are load-bearing.</b> A negative
    /// weight makes <c>e^(spacing·(1−r)) − 1</c> negative for every <c>r &lt; 1</c>, so
    /// <see cref="DsrRetrievability.Reinforce"/>'s <c>Math.Max(0, …)</c> floor turns the whole increase into
    /// zero and reinforcement becomes a silent NO-OP: every entry returns its stability unchanged, recall
    /// never strengthens anything, and the whole corpus decays to prunable with no error anywhere. Zero is
    /// allowed and means the law is off (the term is <c>e^0 − 1 = 0</c>), which is the same no-op — but as a
    /// deliberate setting rather than as a sign error, and it reads that way at the configuring line.</para>
    /// <para>The upper bound is <c>Math.Exp</c>'s overflow point, not a taste: the argument is
    /// <c>weight × (1 − r)</c> with <c>(1 − r)</c> at most 1, so a weight at or below 700 can never overflow
    /// (<c>Math.Exp</c> goes infinite past ≈709.78) while anything above it overflows for LOW <c>r</c>
    /// first — the finiteness floor then reads that as no growth, so a nearly-forgotten entry gains NOTHING
    /// while a fresh one still gains: law 3 inverted at the extreme, in the one regime the law is
    /// for.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value, a value above 700, or a
    /// non-finite value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double SpacingWeight
    {
        get => _spacingWeight;
        init
        {
            if (!double.IsFinite(value) || value is < 0 or > 700)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.SpacingWeight must be a finite number in [0, 700] — see the property's XML " +
                    "doc for why a negative value makes Reinforce a silent no-op and why a value past " +
                    "Math.Exp's overflow point inverts law 3 for exactly the faint entries it is for.");
            _spacingWeight = value;
        }
    }

    /// <summary>Law 1 — <b>difficulty</b>: how strongly <see cref="MemorySignals.WellKnown.Difficulty"/>
    /// dampens the gain in <see cref="DsrRetrievability.Reinforce"/>, via
    /// <c>e^(−weight·(difficulty−1))</c>. Difficulty EXACTLY <c>1</c> (the scale's easiest value) leaves the
    /// term at <c>1</c> — no dampening; harder material shrinks it.
    /// <para><b>An ABSENT difficulty signal is NOT the same as difficulty <c>1</c> — corrected 2026-08-11.</b>
    /// An earlier draft of this doc (and of <see cref="MemoryDecayState.Difficulty"/>'s own default) treated
    /// "absent" as "easiest", so an unjudged entry got NO dampening at all under this law. Absent now resolves
    /// to <see cref="NeutralDifficulty"/> (the mid-point <c>5</c> by default — see that property's own
    /// remarks for why), so an unjudged entry DOES get a real, if modest, dampening at this weight's own
    /// default (<c>e^(-0.08·4) ≈ 0.73</c>) — "no information about difficulty" is no longer free, which is
    /// the FSRS-faithful reading: real FSRS's own initial difficulty is mid-range, not its easiest
    /// value.</para>
    /// <b>Unmeasured.</b>
    /// <para><b>Must be FINITE and at or above zero.</b> A negative weight makes the exponent positive, so
    /// the term rises above 1 with difficulty and HARDER material gains MORE per recall — law 1 exactly
    /// backwards, and silently, because the result is still finite, still in range, and still monotone in
    /// difficulty. Zero is allowed and means the law is off: the term is <c>1</c> for EVERY entry regardless
    /// of its own difficulty value, a structural no-op rather than a coincidence with any one difficulty
    /// value.</para>
    /// <para>No upper bound: the exponent is never positive once the weight is non-negative (difficulty is
    /// coerced into <c>[1, 10]</c> by <see cref="MemorySignals.Difficulty"/>), so a large weight drives the
    /// term toward zero — hard material gaining nothing is a monotone weakening, not a
    /// reversal.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double DifficultyWeight
    {
        get => _difficultyWeight;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.DifficultyWeight must be a finite non-negative number — see the property's " +
                    "XML doc for why a negative value inverts law 1 (harder material gaining more) while " +
                    "still producing a finite, in-range, monotone result.");
            _difficultyWeight = value;
        }
    }

    /// <summary>The difficulty UPDATE law's own weight — FSRS's <c>w6</c>, adapted for a derived rather than
    /// received grade. <b>Not <see cref="DifficultyWeight"/></b>: that one dampens STABILITY growth by the
    /// CURRENT difficulty (law 1); this one moves the DIFFICULTY value itself on every review, in
    /// <see cref="DsrRetrievability.Reinforce"/>'s own difficulty-update step — see its remarks for the exact
    /// formula and which published FSRS form it adapts. Difficulty rises on a derived-hard recall and falls
    /// on a derived-easy one; the ceiling/floor clamp bounds it regardless of this weight's magnitude, which
    /// is why (unlike <see cref="SpacingWeight"/>) there is no upper bound to guard against an overflow this
    /// formula cannot produce.
    /// <para><b>FSRS-6's own published default, not an invented placeholder</b> (fix round 1: an earlier
    /// draft shipped <c>0.5</c> here with no real provenance — <c>w6</c> has moved release to release: FSRS
    /// v4 <c>0.86</c>, v4.5 <c>0.8975</c>, v5 <c>1.4604</c>, v6 <c>3.0194</c>. This adopts v6's number because
    /// <see cref="DifficultyReversionWeight"/>/<see cref="DifficultyReversionTarget"/> below adopt v6's
    /// numbers too, and mixing versions inside one triple would itself be an invented combination FSRS never
    /// shipped. <b>Still not "measured for this library"</b> — it is FSRS-6's own fit against ITS review
    /// corpus, not this one's; real fitting is deferred to design spec §4.</para>
    /// <para><b>Must be FINITE and at or above zero.</b> A negative weight flips the sign of the derived
    /// delta, so a HARD recall would LOWER difficulty and an EASY one would RAISE it — the update inverted,
    /// silently, because the result stays finite and in range throughout. Zero is allowed and means THIS LAW
    /// alone is off; it does <b>not</b> make difficulty fully inert on its own any more — see
    /// <see cref="DifficultyReversionWeight"/> for why a second force now moves difficulty even at
    /// <c>DifficultyChangeWeight = 0</c> (fix round 1, C2), and for the actual difficulty-inert
    /// control.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value or a non-finite value
    /// (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double DifficultyChangeWeight
    {
        get => _difficultyChangeWeight;
        init
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.DifficultyChangeWeight must be a finite non-negative number — see the " +
                    "property's XML doc for why a negative value inverts the difficulty update (a hard " +
                    "recall lowering difficulty, an easy one raising it) while still producing a finite, " +
                    "in-range result.");
            _difficultyChangeWeight = value;
        }
    }

    /// <summary>The RESTORING force <see cref="DifficultyChangeWeight"/>'s linear damping needs to avoid
    /// making <c>Difficulty = 10</c> an ABSORBING state (fix round 1, C2 — an earlier draft shipped damping
    /// with no reversion at all, which is the bound with no escape: damping's own factor,
    /// <c>(10-D)/9</c>, is IDENTICALLY ZERO at <c>D = 10</c>, so once every review's grade-driven delta is
    /// damped away to nothing there, reversion is the ONLY term left that can still move <c>D</c> at all).
    /// FSRS's own <c>w7</c> — the blend weight between the damped value and
    /// <see cref="DifficultyReversionTarget"/> in <see cref="DsrRetrievability.Reinforce"/>'s final step:
    /// <c>D'' = w7 · target + (1 - w7) · D'</c>.
    /// <para><b>FSRS-6's own published default, <c>0.001</c> — tiny, and NOT the same as zero.</b> FSRS-6's
    /// own reversion target computes to roughly <c>-4.77</c> (see <see cref="DifficultyReversionTarget"/>),
    /// so even at <c>w7 = 0.001</c> there is a persistent <c>≈0.001 × (D - (-4.77)) ≈ 0.015</c>-per-review
    /// downward pull at <c>D = 10</c> — slow, but never exactly zero, which is a QUALITATIVE difference from
    /// no reversion at all, not merely a quantitative one.</para>
    /// <para><b>The actual difficulty-inert control (design spec §2/§5) is BOTH weights at zero together</b>
    /// — <c>DifficultyChangeWeight = 0</c> alone is no longer sufficient once this field exists, because
    /// reversion is a SEPARATE force this weight does not gate.</para>
    /// <para><b>Must be FINITE and within <c>[0, 1]</c> — the domain a weighted average is meaningful
    /// over.</b> Outside it, <c>w7 · target + (1 - w7) · D'</c> stops being a value BETWEEN
    /// <see cref="DifficultyReversionTarget"/> and the damped value, which is the whole
    /// point of "reversion" — a large enough weight in either direction collapses every review's difficulty
    /// toward the SAME extreme regardless of the actual grade, drowning the grade-driven signal entirely
    /// rather than merely pulling on it. The final <c>Math.Clamp(_, 1, 10)</c> still bounds the STORED value
    /// even outside <c>[0, 1]</c>, so this guard is about the blend staying meaningful, not about prevented
    /// corruption the way <see cref="Decay"/>'s guard is.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a value outside <c>[0, 1]</c> or a non-finite
    /// value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double DifficultyReversionWeight
    {
        get => _difficultyReversionWeight;
        init
        {
            if (!double.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.DifficultyReversionWeight must be a finite number in [0, 1] — see the " +
                    "property's XML doc for why outside that range the blend stops being a weighted average " +
                    "between the damped value and the reversion target.");
            _difficultyReversionWeight = value;
        }
    }

    /// <summary>The point <see cref="DifficultyReversionWeight"/> pulls difficulty toward on every review —
    /// FSRS's own reversion target, which real FSRS computes internally from a per-grade initial-difficulty
    /// sub-formula (<c>D0(Easy)</c>, itself derived from <c>w4</c>/<c>w5</c>). This library has no <c>w4</c>/
    /// <c>w5</c> pair and no per-grade write-time rating to seed one from, so this exposes the RESULT of that
    /// computation directly as one settable number rather than reproducing the sub-formula that produces it
    /// — the adaptation fix round 1's C2 asked for, in the shape it asked for.
    /// <para><b>FSRS-6's own default, approximately <c>-4.77</c> — a genuinely negative number, not a typo.</b>
    /// FSRS-6's <c>D0</c> sub-formula went exponential (from v5's linear form) and, evaluated at its own
    /// published <c>w4</c>/<c>w5</c> for the Easy grade, lands FAR below the <c>[1, 10]</c> difficulty range
    /// — computed UNCLAMPED, which is exactly why <see cref="DifficultyReversionWeight"/>'s own tiny default
    /// still produces a real, non-zero downward pull at every difficulty value, including the ceiling.</para>
    /// <para><b>Must be FINITE; no other bound.</b> Any finite value is safe here: the final
    /// <c>Math.Clamp(_, 1, 10)</c> in <see cref="DsrRetrievability.Reinforce"/> bounds the stored result
    /// regardless of how extreme this target is, and unlike <see cref="Decay"/> or <see cref="InitialStability"/>
    /// there is no <c>Math.Pow</c> of this value anywhere for a bad magnitude to blow up — it only ever
    /// enters a bounded linear blend.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a non-finite value (<c>NaN</c>,
    /// <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double DifficultyReversionTarget
    {
        get => _difficultyReversionTarget;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.DifficultyReversionTarget must be a finite number — a non-finite target " +
                    "would poison the difficulty blend even though the formula has no other way to produce " +
                    "NaN or infinity on its own.");
            _difficultyReversionTarget = value;
        }
    }

    /// <summary>The DIFFICULTY every entry starts at when nothing has ever judged it — substituted by
    /// <see cref="DsrRetrievability.Reinforce"/> whenever an incoming <see cref="MemoryDecayState.Difficulty"/>
    /// is non-finite, the identical substitution pattern <see cref="InitialStability"/> already uses for a
    /// non-positive <see cref="MemoryDecayState.Stability"/>.
    /// <para><b>Corrected 2026-08-11 from the FLOOR (<c>1</c>) to the MID-POINT (<c>5</c>) — a diagnosed
    /// defect, not a taste change.</b> FSRS's own <c>[1,10]</c> scale has <c>1</c> mean EASIEST, not "no
    /// information" — starting every unjudged entry there made the axis structurally unable to vary: the
    /// derived grade (see <see cref="DsrRetrievability.Reinforce"/>'s own remarks) is overwhelmingly
    /// Easy-leaning on a fresh, successful recall (the common case for anything actually retrieved), so the
    /// update law's own linear damping computes a value BELOW the floor almost immediately, and
    /// <c>Math.Clamp(_, 1, 10)</c> floors it right back — an unjudged entry could reach the floor on its
    /// first touch and then never leave it, no matter how many reviews followed. A diagnostic reading the
    /// review log's own per-touch trail (2026-08-11, fsrs-properly plan Task 4 follow-up) confirmed exactly
    /// this on a live replay: the corpus's own most-reinforced entries sat at <c>Difficulty ≡ 1</c> across
    /// well over a hundred touches each, moving only in two brief, immediately-undone excursions. <c>5</c> is
    /// equidistant from both bounds — free to move in EITHER direction on the very next review — and it
    /// makes "no information about this entry's difficulty" mean AVERAGE, which is what neutral is supposed
    /// to mean, rather than "easiest possible."</para>
    /// <para><b>This is a STATED CHOICE, not a derivation — said so rather than implied otherwise.</b> Real
    /// FSRS derives its own <c>D0</c> (initial difficulty) from the FIRST rating a human gives; this library
    /// has no first rating to derive from (design spec §1 — nobody grades a graph-memory touch), so unlike
    /// almost every other constant on this options record, there is no published FSRS number to adopt here.
    /// <c>5</c> is reasoned (equidistant, unbiased toward either direction), not measured or borrowed — the
    /// same honesty this curve's own <c>r = 0.5</c> half-life anchor already discloses for its own divergence
    /// from FSRS's 90% convention.</para>
    /// <para><b>Read alongside <see cref="MemorySignals.Difficulty"/>'s own fallback, which MUST agree with
    /// this default.</b> The signal layer computes what a FRESH node's difficulty column is seeded with when
    /// no explicit signal is supplied; this property is what a retrievability policy substitutes for a
    /// non-finite incoming value on a REVIEW. The two are different call sites for the identical concept —
    /// "no information about this entry's difficulty" — and were allowed to drift to different numbers once
    /// already (both were <c>1</c>, which is exactly how this defect went unnoticed); they are kept in sync by
    /// convention (Core cannot depend on this Forgetting-domain type), documented here so the next change to
    /// either one is made deliberately, not independently.</para>
    /// <para><b>Must be FINITE and within <c>[1, 10]</c> — the domain <see cref="MemoryDecayState.Difficulty"/>
    /// itself is coerced into.</b> This is the substitute <see cref="DsrRetrievability.Reinforce"/> uses when
    /// the incoming state's <see cref="MemoryDecayState.Difficulty"/> is non-finite, so an out-of-range value
    /// here would hand the difficulty-update law a starting point outside the domain its own clamp exists to
    /// enforce.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a value outside <c>[1, 10]</c> or a non-finite
    /// value (<c>NaN</c>, <c>+Infinity</c>, or <c>-Infinity</c>).</exception>
    public double NeutralDifficulty
    {
        get => _neutralDifficulty;
        init
        {
            if (!double.IsFinite(value) || value is < 1 or > 10)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "DsrOptions.NeutralDifficulty must be a finite number in [1, 10] — see the property's " +
                    "XML doc for why this substitutes directly for a non-finite incoming Difficulty, so an " +
                    "out-of-domain value here would feed the difficulty-update law a starting point its own " +
                    "clamp is supposed to prevent.");
            _neutralDifficulty = value;
        }
    }
}

/// <summary>
/// The DSR forgetting curve: <c>r = (1 + F · age / stability)^decay</c>, a POWER law rather than an
/// exponential.
/// <para><b>Why a power law.</b> A superposition of exponentials with different stabilities is better
/// approximated by a power function, so a HETEROGENEOUS corpus decays as a power law even when each
/// individual memory decays exponentially. This library's corpus is heterogeneous by definition — identity
/// facts beside booking details beside conversational noise. FSRS's own curve moved from exponential (v1–v3)
/// to power (v4) for exactly that reason, fitted against real review logs.</para>
/// <para><b>Stability still means HALF-LIFE here, not FSRS's 90% point.</b> That is a deliberate divergence:
/// the stored stability column is already populated, so adopting FSRS's convention would silently reinterpret
/// every existing value the moment an application swapped policies. <c>F</c> is therefore DERIVED from
/// <see cref="DsrOptions.Decay"/> — <c>F = 0.5^(1/decay) − 1</c> — so the half-life anchor holds whatever
/// exponent is chosen. Exposing F as a second knob would let the two drift into a curve that is neither.</para>
/// <para><b>Pruning is generous.</b> The heavy tail makes <see cref="CandidateCutoff"/> wide — roughly thirty
/// times wider, at the same floor, than the exponential curve 2.5.x shipped (deleted in 3.0,
/// <c>docs/DECISIONS.md</c>), so a deployment upgrading from that release will see pruning become
/// noticeably less aggressive. The direction is safe regardless: the cutoff bounds <c>PruneAsync</c>, which
/// deletes rows ABOVE it, so wider deletes less.</para>
/// <para><b>This is the only shipped forgetting curve, and the DI-registered default, as of 3.0</b>
/// (<c>MemoryEngineRegistration.AddMemoryEngine</c> <c>TryAdd</c>s it, and a bare-constructed
/// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> now defaults to it too — the two-defaults split a
/// second, deleted curve used to create is gone with that curve). FSRS's own external validation is the
/// primary evidence for making it the default, with one measured, known, prioritized gap (a partial, unfitted
/// FSRS) — see <c>docs/DECISIONS.md</c> D49, which also supersedes D46's "not yet decided" framing.</para>
/// <para><b>Difficulty is LIVE state as of 2026-08-10 (fsrs-properly plan Task 2)</b>: <see cref="Reinforce"/>
/// now maintains <see cref="MemoryDecayState.Difficulty"/> on every review, derived from a GRADE this
/// library computes rather than receives (design spec §1) — see <see cref="Reinforce"/>'s own remarks for
/// the exact adaptation, including a fix-round-1 correction against primary sources
/// (`py-fsrs/scheduler.py`, `fsrs-rs/model.rs`, fsrs4anki v4.7.2, the Anki manual): the update law now
/// includes mean reversion (FSRS-5/6's own restoring force, not merely damping) and the derived grade never
/// emits FSRS's lapse rating. Before any of this, difficulty was read from the signals bag and never written
/// back, so it stayed frozen at whatever a salience policy judged at write time (or absent, therefore neutral).
/// This is still a PARTIAL, UNFITTED FSRS: no per-review RATING (a derived grade stands in), and every
/// constant is a published FSRS default rather than one fitted against this library's own review
/// history.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults.</param>
/// <exception cref="ArgumentOutOfRangeException"><see cref="DsrOptions.Decay"/> is in domain but so extreme
/// in MAGNITUDE that the derived factor collapses — see <see cref="DeriveFactor"/>.</exception>
public sealed class DsrRetrievability(DsrOptions? options = null) : IMemoryRetrievabilityPolicy
{
    private readonly DsrOptions _options = options ?? new DsrOptions();

    /// <summary>The factor that anchors <c>r = 0.5</c> at <c>age = stability</c>, derived from the exponent
    /// rather than configured. Computed once: it depends only on the options.</summary>
    private readonly double _factor = DeriveFactor((options ?? new DsrOptions()).Decay, nameof(options));

    /// <summary><c>F = 0.5^(1/decay) − 1</c>, validated as the DERIVED value rather than as its input.
    /// <para><b>Why the check is here and not on <see cref="DsrOptions.Decay"/>.</b> That guard fixes the
    /// SIGN and rejects non-finite values, which is the whole of what it can see; MAGNITUDE only becomes
    /// visible after the derivation, and both extremes reproduce the exact catastrophes the sign guard
    /// exists to prevent. <c>Decay = -0.0005</c> gives <c>1/decay = -2000</c>, so <c>0.5^-2000</c> overflows
    /// to <c>+Infinity</c> and every entry older than zero reads <c>r = 0</c> — recall returns nothing and
    /// <c>PruneAsync</c> reaps the corpus. <c>Decay = -1e16</c> gives an exponent so near zero that
    /// <c>0.5^(-1e-16)</c> rounds to exactly <c>1.0</c> in a <see cref="double"/>, so <c>F = 0</c>, the
    /// curve's base collapses to 1 and <c>r ≡ 1</c> forever — nothing is ever forgotten, the same
    /// permanent-retrievability failure <see cref="double.NegativeInfinity"/> produces, from a value the
    /// sign guard happily accepts.</para>
    /// <para>Both are silently the OPPOSITE of what <see cref="DsrOptions.Decay"/>'s own doc promises ("a
    /// less negative value is a heavier tail"), so a consumer reading that doc and reaching for
    /// <c>-0.0005</c> gets total forgetting instead of a heavy tail with no error at all. One check on the
    /// derived value closes both ends; two checks on the input could not, because the boundary is where the
    /// derivation loses precision, not where the input crosses a round number.</para></summary>
    /// <param name="decay">The configured exponent.</param>
    /// <param name="paramName">The constructor parameter to blame, so the exception points at the line that
    /// configured the policy.</param>
    /// <exception cref="ArgumentOutOfRangeException">The derived factor is not a finite positive
    /// number.</exception>
    private static double DeriveFactor(double decay, string paramName)
    {
        var factor = Math.Pow(0.5, 1 / decay) - 1;
        if (!double.IsFinite(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(paramName, decay,
                "DsrOptions.Decay is in domain but its MAGNITUDE collapses the derived curve factor " +
                $"F = 0.5^(1/decay) - 1, which came out as {factor.ToString(CultureInfo.InvariantCulture)} " +
                "rather than a finite positive number — see DsrRetrievability.DeriveFactor's XML doc for " +
                "why either extreme silently inverts the curve rather than merely degrading it.");
        return factor;
    }

    /// <inheritdoc />
    public double InitialStability => _options.InitialStability;

    /// <inheritdoc />
    public MemoryRetrievabilityProvenance Provenance => MemoryRetrievabilityProvenance.Dsr;

    /// <inheritdoc />
    public double Retrievability(in MemoryDecayState state) =>
        state.Age <= 0
            ? 1
            : Math.Clamp(Math.Pow(1 + _factor * state.Age / EffectiveStability(state), _options.Decay), 0, 1);

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
    /// <remarks>Follows FSRS's three stability-increase laws:
    /// <para><c>S' = S · (1 + gain · difficultyTerm · S^(−stabilizationDecay) · (e^(spacing·(1−r)) − 1))</c>,
    /// capped at <see cref="DsrOptions.MaxStability"/> — <b>a cap on GROWTH, never a CUT</b>: the clamp is
    /// floored at <c>S</c> itself, so an entry whose stored stability already exceeds the ceiling is FROZEN
    /// rather than truncated down to it (<c>docs/task-archive.md</c> Part 54 DSR2, fixed 2026-08-11; before that a stored
    /// 100000 came back as 2000, breaking
    /// <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>'s own "never smaller than the current one"
    /// guarantee, which now holds unconditionally).</para>
    /// <para><b>Law 2 — stabilization decay</b> is the <c>S^(−k)</c> term: the potential to strengthen shrinks
    /// as stability grows, which is what stops a frequently-recalled entry compounding into permanence.
    /// <b>Law 3 — the spacing effect</b> is the <c>(1 − r)</c> term: recalling something nearly forgotten
    /// strengthens it far more than recalling something just seen. At <c>r = 1</c> the term is zero, so an
    /// immediate re-recall gains NOTHING — that is the model working, not a bug, and it is the sharpest
    /// behavioural difference from the flat multiplier this policy sits beside. <b>Law 1 — difficulty</b>
    /// reads the entry's LIVE <see cref="MemoryDecayState.Difficulty"/> — the PRE-reinforcement value, the
    /// same one the difficulty-update step below reads, never the value this same call is about to
    /// write.</para>
    /// <para><b>Under <see cref="Lyntai.Memory.Modulation.ModulatedRetrievability"/>, the spacing term sees
    /// the UNMODULATED retrievability.</b> That wrapper deliberately calls this method with the raw stored
    /// state rather than the state a retention policy lengthened (see its own remarks on why — the short version is
    /// that <see cref="Reinforce"/>'s return is what gets STORED, and compounding the modulated figure would
    /// bake the modulation in permanently). This choice is NOT invisible here, the way it was on the deleted
    /// exponential curve — that one's own <c>Reinforce</c> never read retrievability at all, so the
    /// raw-vs-modulated distinction had nothing to bite on there: a modulated (for instance salient) entry has
    /// a raised effective retrievability that this method never sees, so <c>r</c> reads lower than the entry's
    /// true state, the spacing term
    /// <c>e^(spacing·(1−r)) − 1</c> reads higher, and the entry gains MORE stability per recall than an
    /// equally-aged, unmodulated one would. The same signal therefore both slows decay and speeds growth.</para>
    /// <para><b>This is ON BY DEFAULT and the effect is not small — measured, at the defaults, on the
    /// increase term with a stored stability of 100:</b>
    /// <code>
    ///   age/S │ salience 1.5 │ salience 2.5 │ salience 4.0
    ///     0.5 │        1.33× │        1.99× │        2.98×
    ///       1 │        1.26× │        1.77× │        2.53×
    ///       5 │        1.12× │        1.35× │        1.66×
    /// </code>
    /// <c>SalienceRetentionPolicy</c> is added to the retention collection for every graph engine, so a consumer
    /// who never mentions salience still gets this. The inflation is LARGEST for the FRESHEST recalls — the
    /// opposite of the intuition that a rarely-touched entry is where a retention signal matters — and it
    /// COMPOUNDS, because each inflated gain is written back and raises the base of the next one, toward
    /// <see cref="DsrOptions.MaxStability"/>. 4.0 is not a corner case either: it is
    /// <c>SalienceOptions.MaxSalience</c>'s own default, so it is the most a shipped salience policy can
    /// report.</para>
    /// <para><b>Deferred deliberately, not overlooked.</b> Removing it would mean either compounding the
    /// modulated figure into the stored stability — which is exactly what
    /// <see cref="Lyntai.Memory.Modulation.ModulatedRetrievability"/> refuses to do, because it would bake a
    /// signal's effect in permanently where no later change to the signal could undo it — or giving the
    /// wrapper a second, modulation-aware seam that only one of the two shipped curves would use. Either is a
    /// design change to the modulation contract rather than a fix here, and the direction is safe (more
    /// stability, so a wider <c>CandidateCutoff</c>, so fewer deletions). <b>It does confound a
    /// curve-vs-curve measurement</b>, which is why the comparison in <c>docs/task-archive.md</c> Part 54 must register
    /// no retention policies or control for salience explicitly.</para>
    /// <para><b>The difficulty update (design spec §1/§2, 2026-08-10 fsrs-properly plan Task 2, corrected
    /// against primary sources in fix round 1) — a SEPARATE mechanism from the three stability laws above,
    /// run in the same call, EXCEPT on a same-day/zero-elapsed review (see the Δt=0 branch below).</b> Real
    /// FSRS updates <c>Difficulty</c> from a human-supplied rating (Again/Hard/Good/Easy — <c>Again</c> is a
    /// LAPSE, the other three are successes). Nobody grades a graph-memory recall, and every graded event
    /// here is a SUCCESS by construction (an entry that is not returned never reaches this method), so the
    /// RATING is DERIVED from <c>retrievability</c> at the moment of this recall, restricted to the
    /// SUCCESS sub-range — never <c>Again</c>. Recalled at low <c>r</c> (nearly forgotten) derives toward
    /// Hard; at high <c>r</c> (fresh) toward Easy.</para>
    /// <para><b>Adapted from FSRS-5's <c>next_difficulty</c> law, with FSRS-6's own recalibrated constants</b>
    /// (checked against `py-fsrs/scheduler.py`, `fsrs-rs/model.rs`, fsrs4anki v4.7.2 and the Anki manual —
    /// FSRS-4/4.5's own <c>next_difficulty</c> has NO damping term at all; FSRS-5 introduced the linear
    /// damping below and moved the reversion target from <c>D0(3)</c> to <c>D0(4)</c>; FSRS-6 kept that
    /// SHAPE and recalibrated <c>w6</c>/<c>w7</c>/its own <c>D0</c> sub-formula, which went exponential where
    /// v5's was linear):
    /// <c>ΔD = -w6 · (G - 3)</c>, linear damping toward the ceiling, <c>D' = D + ΔD · (10 - D) / 9</c>, mean
    /// reversion toward the target, <c>D'' = w7 · target + (1 - w7) · D'</c>, then a final clamp to
    /// <c>[1, 10]</c>. <b>What this adapts, precisely, so it can be checked against that form:</b>
    /// (1) the discrete rating <c>G ∈ {1,2,3,4}</c> is replaced by a CONTINUOUS derived grade restricted to
    /// the success range, <c>g = 2 + 2 · retrievability ∈ [2, 4]</c> — exact at both ends (<c>r=0 → g=2</c>
    /// "Hard", <c>r=1 → g=4</c> "Easy"), and <c>g=3</c> ("Good", FSRS's own no-change reference) now lands at
    /// <c>r = 0.5</c> — this library's OWN half-life anchor, not an arbitrary point (fix round 1, C3
    /// corrected an earlier draft that used <c>g = 1 + 3r</c>, which reached <c>g=1</c> — <c>Again</c>, a
    /// LAPSE — at <c>r=0</c>, exactly the rating a purely-successful recall must never emit, while ALSO
    /// growing stability maximally in the same call). (2) <c>w6</c> is
    /// <see cref="DsrOptions.DifficultyChangeWeight"/>, <c>w7</c> is
    /// <see cref="DsrOptions.DifficultyReversionWeight"/>, and the reversion target is
    /// <see cref="DsrOptions.DifficultyReversionTarget"/> — all three adopt FSRS-6's OWN published defaults
    /// (see each property's own doc), not independently invented numbers. (3) The linear damping term is
    /// kept verbatim. (4) The reversion target is exposed as a directly-settable NUMBER rather than
    /// reproducing FSRS's own per-grade <c>D0</c> sub-formula that computes it — this library has no
    /// <c>w4</c>/<c>w5</c> pair to compute one from, and the target itself is a plain constant once <c>w4</c>/
    /// <c>w5</c> and the grade (always <c>4</c>, Easy) are fixed, so exposing the result changes nothing
    /// about the SHAPE of the law, only where one sub-computation's output comes from (fix round 1, C2
    /// corrected an earlier draft that dropped reversion entirely, reasoning it needed a "per-grade" quantity
    /// this library lacks — WRONG: the target is evaluated at a FIXED grade, so it is a plain constant like
    /// every other option here, and dropping it left <c>D = 10</c> ABSORBING, since linear damping's own
    /// factor is identically zero there and reversion was the only other term that could still move
    /// it).</para>
    /// <para><b>THE drift guard (design spec §1's own caveat): the grade above is computed from
    /// <paramref name="state"/> — the state BEFORE this reinforcement — never from the state this call is
    /// about to return.</b> This is not FSRS's grade: FSRS's is a human judging their own recall, and this is
    /// the CURVE'S OWN prediction of how retrievable the entry was. A curve graded by its own prediction can
    /// drift — one that systematically overestimates retrievability would systematically derive "Easy" and
    /// keep lowering its own difficulty, reinforcing the overestimate forever. Reading the PRE-reinforcement
    /// state is the cheap half of the mitigation; the other (fitting against held-out recall outcomes, never
    /// against grade agreement) is future work the spec assigns to §4, deliberately not this task.</para>
    /// <para><b>The Δt=0 branch (fix round 1, I1) — a SECOND, across-call drift this method alone cannot see
    /// by construction.</b> A recall does not advance the engine's position (correctly: a recall is not new
    /// material), so a SESSION BURST — several recalls of the same entry with no intervening write — hands
    /// this method <c>state.Age = 0</c> every time after the first touch, which makes <c>r = 1</c> every
    /// time (see <see cref="Retrievability"/>'s own zero-age branch), which derives <c>Easy</c> every time,
    /// which LOWERS difficulty every time — difficulty becomes a function of RECALL CADENCE, not of how hard
    /// the material is, and it never stops as long as the burst continues. FSRS has its own answer: reviews
    /// with zero elapsed time bypass the ordinary update. <b>Adopted, not invented</b>: at
    /// <c>state.Age &lt;= 0</c>, difficulty is returned UNCHANGED — the same branch this method already takes
    /// for stability, where an immediate re-recall's spacing term is exactly zero (see
    /// <c>DsrRetrievabilityTests.An_immediate_re_recall_gains_nothing</c> in the test suite for the stability half of this;
    /// unlike stability, difficulty's own formula does NOT naturally zero out at <c>r=1</c> under the
    /// corrected mapping above — <c>g=4</c> there is the Easy end, not the neutral one — so this needs an
    /// explicit branch where stability's did not).</para>
    /// <para><c>Difficulty</c> is coerced the same way <see cref="MemorySignals.Difficulty"/> already coerces
    /// the signal it used to read directly: non-finite becomes <see cref="DsrOptions.NeutralDifficulty"/>
    /// (the mid-point <c>5</c> by default — corrected 2026-08-11 from the floor <c>1</c>, see that property's
    /// own remarks for why), out-of-range clamps into <c>[1, 10]</c> — the live field arrives through a
    /// public seam (a consumer may construct <see cref="MemoryDecayState"/> directly) exactly like every
    /// other per-call field this method does not fully trust.</para></remarks>
    public MemoryDecayState Reinforce(in MemoryDecayState state)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        // PRE-reinforcement, by construction: `state` is never mutated, and both the difficulty term below
        // and the derived grade further down read this SAME local — never a value this call produces.
        var retrievability = Retrievability(state);

        // Difficulty is now LIVE STATE (design spec §2), not a value re-derived from the signals bag on
        // every call — coerced the same way MemorySignals.Difficulty always coerced it, since a directly
        // constructed MemoryDecayState is just as untrusted a seam as the signals bag ever was. The
        // non-finite branch substitutes NeutralDifficulty (the mid-point 5 by default), not the floor 1 —
        // corrected 2026-08-11 after a diagnostic showed the floor made this axis structurally unable to
        // vary; see that property's own remarks.
        var difficulty = double.IsFinite(state.Difficulty)
            ? Math.Clamp(state.Difficulty, 1, 10)
            : _options.NeutralDifficulty;

        var increase = _options.ReinforceGain
            * Math.Exp(-_options.DifficultyWeight * (difficulty - 1))
            * Math.Pow(stability, -_options.StabilizationDecay)
            * (Math.Exp(_options.SpacingWeight * (1 - retrievability)) - 1);

        // Belt and braces alongside DsrOptions.InitialStability's own guard: `increase` also depends on
        // Retrievability, which depends on state.Age/Strength/StrengthAge - none of which DsrOptions can
        // validate, because they arrive per-call in the caller's own MemoryDecayState rather than at
        // construction. A non-finite Age is a second route to a non-finite `increase`, and Math.Max(0, NaN)
        // is NaN by .NET's own documented contract - the same fact ModulatedRetrievability.Declared already
        // writes down for Math.Max(1, NaN) - so the floor must check finiteness BEFORE flooring, not after.
        var safeIncrease = double.IsFinite(increase) ? Math.Max(0, increase) : 0;

        // The outer Math.Max is the SAME shape EffectiveStability uses one method away, and for the same
        // reason (docs/task-archive.md Part 54 DSR2, fixed 2026-08-11): a bare Math.Min(grown, MaxStability)
        // SHORTENS an entry whose stored stability already exceeds the ceiling - measured, a stored 100000
        // came back as 2000, a 50x cut, violating this method's own written guarantee that the result is
        // never smaller than the current one. Flooring at `stability` makes the ceiling cap GROWTH without ever acting as a
        // CUT: an over-ceiling entry is FROZEN (it can no longer grow), which is what MaxStability's own doc
        // asks for - "unbounded compounding would let an ASSOCIATIVE entry become permanently retrievable
        // while still labelled associative" is an argument about growth, and freezing stops growth as
        // completely as truncation did.
        //
        // The floor is the SUBSTITUTED local, not state.Stability, deliberately: a non-positive stored
        // stability is treated as InitialStability by every other line of this method (and by
        // EffectiveStability), so flooring at the raw stored value would let a MaxStability configured BELOW
        // InitialStability return less for a zero-stability entry than for one already sitting at
        // InitialStability - breaking A_zero_stability_entry_reinforces_from_InitialStability_not_from_zero's
        // own claim that the two paths agree. Flooring at `stability` keeps one substitution rule for the
        // whole method.
        var grown = Math.Max(stability, Math.Min(stability * (1 + safeIncrease), _options.MaxStability));

        // The Δt=0 branch (fix round 1, I1): a same-position recall (no write moved the engine on since the
        // last touch) bypasses the difficulty update entirely, exactly as FSRS bypasses its ordinary review
        // formulas on a zero-elapsed review - otherwise a session burst with no intervening write would
        // derive Easy every single time (Age<=0 forces Retrievability to exactly 1) and pump difficulty
        // toward the floor purely as a function of recall CADENCE. See this method's own <remarks>.
        //
        // Routed through the PUBLIC DerivedGrade(state) overload (2026-08-11, fsrs-properly plan Task 3),
        // not the private formula directly: a review log needs the EXACT value this call is about to use,
        // and the only way to guarantee the two never drift apart is for both to run the same code, not two
        // copies that happen to agree today. `null` here means the branch below already decided nothing
        // should move, in which case there is nothing for a log to disagree about either.
        var grade = DerivedGrade(state);
        var nextDifficulty = grade is null ? difficulty : NextDifficulty(difficulty, grade.Value);

        return state with { Stability = grown, Difficulty = nextDifficulty };
    }

    /// <inheritdoc />
    /// <remarks>Delegates to the same private <c>DerivedGrade(double)</c> formula <see cref="Reinforce"/>
    /// itself calls, gated by the identical <c>state.Age &lt;= 0</c> branch — see that method's own remarks
    /// on the Δt=0 bypass (fix round 1, I1) and the interface member's own remarks on why this must be one
    /// code path rather than two.
    /// <para><b>A NON-FINITE retrievability reports NO JUDGEMENT rather than a poisoned one (2026-08-14
    /// review), reusing the meaning <c>null</c> already carries above: nothing computable happened, so
    /// nothing should move.</b> <see cref="Reinforce"/> has always guarded the STABILITY half against exactly
    /// this input and says why in its own comment — the increase term depends on
    /// <see cref="MemoryDecayState.Age"/>/<see cref="MemoryDecayState.Strength"/>/
    /// <see cref="MemoryDecayState.StrengthAge"/>, which arrive per-call in the caller's own state and so
    /// cannot be validated at construction the way <see cref="DsrOptions"/> validates its constants. The
    /// DIFFICULTY half took the opposite view and both cannot be right, because the derived grade is a
    /// function of the very <c>Age</c> the stability half declines to trust: <c>Math.Clamp</c> propagates
    /// <c>NaN</c> (IEEE-754), so a <c>NaN</c> age produced a <c>NaN</c> grade, a <c>NaN</c>
    /// <see cref="MemoryDecayState.Difficulty"/> written straight back by
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>, and a <c>NaN</c> row in the review log that
    /// exists to make parameter fitting possible at all.</para>
    /// <para>Reachable through the public <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/> seam,
    /// whose <c>Age</c> is an interface member on somebody else's implementation. The engine's recall path
    /// happened to be covered — <c>MemoryRankingContract.Rankable</c> drops a non-finite candidate before
    /// reinforcement — but <c>ExpandAsync</c> reinforces a named node with no ranking in between, and that is
    /// the act this library's own documentation recommends reinforcing on.</para></remarks>
    public double? DerivedGrade(in MemoryDecayState state)
    {
        if (state.Age <= 0) return null;
        var retrievability = Retrievability(state);
        return double.IsFinite(retrievability) ? DerivedGrade(retrievability) : null;
    }

    /// <summary>FSRS's rating scale RESTRICTED to the success sub-range (Hard=2 .. Easy=4 — never
    /// <c>Again</c>=1, a LAPSE this library can never observe by construction: an entry that is not returned
    /// never reaches <see cref="Reinforce"/>), as a CONTINUOUS function of retrievability at recall — exact
    /// at both ends (<c>r=0 → 2</c>, <c>r=1 → 4</c>), linear in between, landing the no-change reference
    /// (<c>g=3</c>) at <c>r=0.5</c> — this curve's OWN half-life anchor. See <see cref="Reinforce"/>'s own
    /// remarks for the fix-round-1 correction this replaced (<c>g = 1 + 3r</c>, which reached the lapse
    /// rating at <c>r=0</c>). <paramref name="retrievability"/> is clamped to <c>[0,1]</c> by
    /// <see cref="Retrievability"/> itself <b>whenever the state it was computed from was finite</b>, so this
    /// needs no defensive clamp of its own — but that qualifier is load-bearing rather than pedantic, and
    /// omitting it is what let a <c>NaN</c> through for a release: <c>Math.Clamp</c> PROPAGATES <c>NaN</c>,
    /// so a non-finite <see cref="MemoryDecayState.Age"/> arrives here as a <c>NaN</c> that is "clamped" in
    /// name only. The public <see cref="DerivedGrade(in MemoryDecayState)"/> overload is where that is now
    /// screened out, before this formula runs at all.</summary>
    private static double DerivedGrade(double retrievability) => 2 + 2 * retrievability;

    /// <summary>FSRS-5/6's <c>next_difficulty</c> law: <c>ΔD = -w6·(g-3)</c>, linear damping toward the
    /// ceiling (<c>D' = D + ΔD·(10-D)/9</c>), then MEAN REVERSION toward
    /// <see cref="DsrOptions.DifficultyReversionTarget"/> (<c>D'' = w7·target + (1-w7)·D'</c>), then clamped
    /// to <c>[1,10]</c>. See <see cref="Reinforce"/>'s own remarks for why reversion is restored (fix round
    /// 1, C2 — without it, damping's own factor is identically zero at <c>D=10</c>, making the ceiling
    /// ABSORBING).
    /// <para>No separate finiteness guard on <c>D''</c>: <paramref name="difficulty"/> arrives already
    /// coerced into <c>[1,10]</c> (finite) by <see cref="Reinforce"/>, <paramref name="grade"/> is finite
    /// <b>because <see cref="DerivedGrade(in MemoryDecayState)"/> screens a non-finite retrievability out
    /// before this method is reached</b> (2026-08-14 review — this paragraph previously said the grade was
    /// finite "by construction", which was false: it is a bounded linear function of a value that is only
    /// bounded when the STATE was finite, and the very next clause of this sentence concedes that
    /// <see cref="MemoryDecayState.Age"/> cannot be proven finite in advance),
    /// <see cref="DsrOptions.DifficultyChangeWeight"/> and <see cref="DsrOptions.DifficultyReversionWeight"/>
    /// are guarded finite (the latter additionally to <c>[0,1]</c>) at construction, and
    /// <see cref="DsrOptions.DifficultyReversionTarget"/> is guarded finite too — so every term feeding
    /// <c>D''</c> is provably finite before the clamp runs, unlike <see cref="Reinforce"/>'s own stability
    /// `increase`, which depends on <see cref="MemoryDecayState.Age"/> and so cannot be proven finite in
    /// advance.</para></summary>
    private double NextDifficulty(double difficulty, double grade)
    {
        var delta = -_options.DifficultyChangeWeight * (grade - 3);
        var damped = difficulty + (10 - difficulty) * delta / 9;
        var reverted = _options.DifficultyReversionWeight * _options.DifficultyReversionTarget
            + (1 - _options.DifficultyReversionWeight) * damped;
        return Math.Clamp(reverted, 1, 10);
    }

    /// <inheritdoc />
    /// <remarks>The result is nudged up by a small relative epsilon after the exact inversion. Unlike the
    /// deleted exponential curve's own cutoff, where <c>2^x</c> and <c>Math.Log2</c> were exact inverses of
    /// each other by construction, this curve's forward (<see cref="Retrievability"/>) and inverse are two
    /// SEPARATE <c>Math.Pow</c> calls with no algebraic relationship the runtime enforces — so their rounding
    /// does not cancel. Measured: the raw inversion lands a couple of ULPs short of the exact boundary
    /// (<c>531.9999999999999</c> instead of the true <c>532</c> at the default options and a 0.05 floor),
    /// which is enough for an entry sitting exactly AT the floor to read as excluded and be permanently
    /// deleted by <c>PruneAsync</c> even though the curve itself still rates it retrievable — the same
    /// superset failure <see cref="DsrOptions.Decay"/>'s guard exists to prevent, here from floating-point
    /// rounding rather than a bad option. The epsilon is many orders of magnitude larger than the measured gap
    /// and still negligible next to how much wider this cutoff already runs (see the class doc) —
    /// conservatism costs nothing here.</remarks>
    public double CandidateCutoff(double minRetrievability) =>
        minRetrievability is <= 0 or > 1
            ? double.PositiveInfinity
            // the curve inverted, widened by the boost ceiling for the same reason the exponential curve
            // widens: a store filters against the STORED stability while a connected entry's effective
            // half-life is up to MaxConnectionBoost times that, then nudged up — see the <remarks> above
            : (Math.Pow(minRetrievability, 1 / _options.Decay) - 1) / _factor
              * Math.Max(1, _options.MaxConnectionBoost)
              * (1 + 1e-9);
}
