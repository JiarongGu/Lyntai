using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>The power-law forgetting curve — the only shipped forgetting curve as of 3.0. It shared the
/// domain, through 2.5.x, with an exponential one (<c>HalfLifeRetrievability</c>, deleted in 3.0 —
/// <c>docs/DECISIONS.md</c> D49 made this curve the registered default first; a later decision then deleted
/// the alternative it had been measured against).
/// <para>Why a power law at all: a superposition of exponentials with DIFFERENT stabilities is better
/// approximated by a power function, so a heterogeneous corpus — identity facts beside booking details
/// beside conversational noise — decays as a power law even if each individual memory does not. FSRS moved
/// from exponential to power at v4 for exactly that reason, fitted against real review logs.</para></summary>
public class DsrRetrievabilityTests
{
    private static MemoryDecayState State(double age, double stability) => new(age, 0, stability);

    /// <summary>A policy with the three stability-increase laws switched ON.
    /// <para><b>They default to OFF as of 3.0</b> (<c>DsrOptions.ReinforceGain = 0</c>,
    /// <c>docs/DECISIONS.md</c> D54 — a measured default, not a cautious one: retrieval-driven growth made
    /// recall measurably worse on every corpus shape, and capped and non-compounding variants both lost to
    /// not growing at all). The laws themselves are unchanged and correct, so a fact ABOUT a law switches it
    /// on explicitly — otherwise it is testing the default rather than the law, and would pass on a build
    /// that had deleted the arithmetic entirely.</para></summary>
    private static DsrRetrievability Reinforcing(DsrOptions? options = null) =>
        new((options ?? new DsrOptions()) with { ReinforceGain = 2.0 });

    /// <summary><b>The difficulty axis has NO EFFECT at the shipped defaults, and that is worth an explicit
    /// fact rather than an inference.</b>
    /// <para><c>Reinforce</c>'s growth term is
    /// <c>ReinforceGain × exp(−DifficultyWeight × (difficulty − 1)) × …</c> — difficulty is the first factor
    /// multiplied by <c>ReinforceGain</c>, which ships at <c>0</c> (D54, a measured ruling). So the whole
    /// product is zero whatever the difficulty, and two entries differing only in difficulty decay
    /// identically.</para>
    /// <para>Difficulty is still LIVE in the sense <c>CLAUDE.md</c> claims — it is maintained and persisted
    /// per review, which is what makes later parameter fitting possible. It simply changes nothing about
    /// retrievability today. Both halves are asserted here so neither can be quietly assumed.</para>
    /// <para><b>Why this is pinned:</b> <c>memory-sweep</c> runs a <c>{difficulty-live, difficulty-inert}</c>
    /// axis whose arms are therefore bit-identical at shipped defaults — measured 2026-08-14, all cells equal
    /// to three decimals. An instrument reporting two arms it cannot distinguish is the shape
    /// <c>pitfalls.md</c> records as "a measurement that cannot observe a change reports nothing moved, which
    /// reads exactly like no regression". If <c>ReinforceGain</c>'s default ever moves off zero, this fact
    /// fails and the sweep's own disclosure has to be revisited with it.</para></summary>
    [Fact]
    public void Difficulty_changes_nothing_while_ReinforceGain_is_zero_and_something_once_it_is_not()
    {
        var shipped = new DsrRetrievability();                       // ReinforceGain = 0, the shipped default
        Assert.Equal(0, new DsrOptions().ReinforceGain);

        var easy = State(10, 20) with { Difficulty = 1 };
        var hard = State(10, 20) with { Difficulty = 10 };

        Assert.Equal(shipped.Reinforce(easy).Stability, shipped.Reinforce(hard).Stability, precision: 12);

        // ...and the law itself is real: switch the gain on and the two diverge
        var growing = Reinforcing();
        Assert.NotEqual(growing.Reinforce(easy).Stability, growing.Reinforce(hard).Stability);
    }

    /// <summary>The 3.0 default itself, pinned so it cannot drift back without someone deciding to.
    /// <para>Two assertions, because the value alone would not catch a build where the laws still ran: the
    /// constant is checked AND a recall of a genuinely faded entry — the case law 3 rewards most — is shown
    /// to leave stability exactly where it was. The age reset that a recall also performs is the engine's
    /// job, not this policy's, and is unaffected.</para></summary>
    [Fact]
    public void Reinforcement_is_OFF_by_default_as_of_3_0()
    {
        Assert.Equal(0, new DsrOptions().ReinforceGain);

        var policy = new DsrRetrievability();
        var faded = new MemoryDecayState(Age: 900, RecallCount: 3, Stability: 100);

        Assert.Equal(faded.Stability, policy.Reinforce(faded).Stability, precision: 9);
        Assert.True(Reinforcing().Reinforce(faded).Stability > faded.Stability,
            "the laws must still WORK when switched on — this default is a default, not a deletion");
    }

    [Fact]
    public void Stability_still_means_HALF_LIFE_not_FSRS_ninety_percent_point()
    {
        // THE convention anchor. The stability column is already populated, so a policy reinterpreting it
        // would silently change what every stored value means the moment someone swapped policies.
        var policy = new DsrRetrievability();

        Assert.Equal(0.5, policy.Retrievability(State(age: 20, stability: 20)), precision: 9);
    }

    [Fact]
    public void The_half_life_anchor_holds_for_any_decay_exponent()
    {
        // F is DERIVED from C precisely so this holds. If F were configurable the two could drift into a
        // curve that is neither a half-life nor FSRS's.
        foreach (var decay in new[] { -0.3, -0.5, -0.9, -1.5 })
        {
            var policy = new DsrRetrievability(new DsrOptions { Decay = decay });

            Assert.Equal(0.5, policy.Retrievability(State(30, 30)), precision: 9);
        }
    }

    /// <summary>The plain <c>r = 2^(-age/stability)</c> exponential — the textbook curve DSR is compared
    /// against below, not the deleted <c>HalfLifeRetrievability</c> class (gone in 3.0, <c>docs/DECISIONS.md</c>
    /// D49): that class carried a reinforcement/connection-boost model this comparison never exercised (both
    /// facts below use <c>Strength = 0</c>), so the formula alone is the honest, minimal stand-in — not a
    /// resurrection of the deleted type.</summary>
    private static double PureExponential(MemoryDecayState state) =>
        state.Age <= 0 ? 1 : Math.Pow(2, -state.Age / state.Stability);

    [Fact]
    public void It_has_a_heavier_tail_than_the_exponential_curve()
    {
        // the reason to adopt it: old memories resist forgetting more than an exponential predicts
        var dsr = new DsrRetrievability();
        var farFuture = State(age: 200, stability: 20); // ten half-lives

        Assert.True(dsr.Retrievability(farFuture) > PureExponential(farFuture) * 10,
            $"dsr {dsr.Retrievability(farFuture)} vs exponential {PureExponential(farFuture)}");
    }

    [Fact]
    public void Its_cutoff_is_correspondingly_wider_and_that_direction_is_safe()
    {
        // CandidateCutoff feeds PruneAsync, which DELETES rows above it — so wider means fewer deletions.
        // Narrower than the curve requires would silently delete retrievable entries. The unboosted
        // exponential's own cutoff is the exact inverse of PureExponential above: 2^x and Math.Log2 are exact
        // inverses of each other by construction (DsrRetrievability's own CandidateCutoff remarks explain why
        // that is NOT true of the power law, which is what needs the nudge-up epsilon there).
        var dsr = new DsrRetrievability().CandidateCutoff(0.05);
        var exponential = Math.Log2(1 / 0.05);

        Assert.True(dsr > exponential, $"dsr {dsr} should exceed exponential {exponential}");
    }

    [Fact]
    public void An_out_of_range_floor_yields_the_documented_escape_hatch()
    {
        var policy = new DsrRetrievability();

        Assert.Equal(double.PositiveInfinity, policy.CandidateCutoff(0));
        Assert.Equal(double.PositiveInfinity, policy.CandidateCutoff(1.5));
    }

    [Fact]
    public void An_entry_sitting_exactly_at_the_floor_is_not_excluded_by_floating_point_rounding()
    {
        // The general contract grid (CandidateCutoff_is_a_conservative_superset, below) never lands exactly
        // ON the boundary, so it cannot catch a cutoff that is a few ULPs narrow. This one is built to land
        // there deliberately: stability 0.5 with strength saturating MaxConnectionBoost at 4 gives an
        // effective stability of 2, and age 266 makes age/effectiveStability = 399, i.e.
        // r = (1 + 399)^-0.5 = 400^-0.5 = 0.05 EXACTLY at the default options and a 0.05 floor. The STORED
        // ratio the store actually filters on — age/stability, unboosted — is 266/0.5 = 532.
        //
        // Forward (Retrievability) and inverse (CandidateCutoff) are two separate Math.Pow calls, so their
        // rounding does not cancel the way HalfLifeRetrievability's 2^x/Log2 pair does: the raw inversion
        // lands at 531.9999999999999, one ULP short of 532, which would silently exclude this entry from
        // its own cutoff and let PruneAsync delete it while the curve itself still rates it retrievable.
        var policy = new DsrRetrievability();
        const double floor = 0.05;
        var cutoff = policy.CandidateCutoff(floor);
        var state = new MemoryDecayState(Age: 266, RecallCount: 0, Stability: 0.5, Strength: 10_000, StrengthAge: 0);

        Assert.True(policy.Retrievability(state) >= floor,
            $"test setup drifted off the boundary: r = {policy.Retrievability(state)}, expected >= {floor}");
        Assert.True(state.Age / state.Stability <= cutoff,
            $"an entry sitting exactly at the floor (age/stability = {state.Age / state.Stability}) " +
            $"falls outside cutoff {cutoff}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void An_out_of_domain_decay_is_rejected_at_construction(double decay)
    {
        // Decay = 0 makes Math.Pow(x, 0) == 1 for every x: retrievability would be permanently 1 while
        // CandidateCutoff still reports a finite bound from the same formula, so PruneAsync would delete
        // everything past that bound that the curve itself still calls fully retrievable.
        // A positive Decay drives the derived F below -1, so the curve's base goes negative for a large
        // enough age and Math.Pow of a negative base to a non-integer exponent is NaN, which Math.Clamp
        // propagates straight through. NaN itself must be rejected explicitly: every comparison against NaN
        // is false, so a bare `>= 0` guard alone would let it silently through.
        // NegativeInfinity is the sharpest case: `< 0` alone would ACCEPT it (it reads as "very negative"),
        // but 1/NegativeInfinity is -0.0, so F = 0.5^(-0.0) - 1 = 0, the curve's base collapses to 1, and
        // Math.Pow(1.0, NegativeInfinity) is 1.0 by IEEE-754's special-case rules -- the exact Decay = 0
        // failure (r permanently 1) by a different route, plus CandidateCutoff computing 0/0 = NaN, which
        // compares false against everything and makes PruneAsync silently stop pruning at all.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { Decay = decay });
    }

    [Fact]
    public void A_negative_decay_is_accepted() =>
        // sanity check: the guard rejects the bad domain without also rejecting the valid one
        Assert.Equal(-1.2, new DsrOptions { Decay = -1.2 }.Decay);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_InitialStability_is_rejected_at_construction(double initialStability)
    {
        // Reinforce substitutes InitialStability for a non-positive stored stability, then raises it to a
        // negative power (S^(-StabilizationDecay)). At zero that is +Infinity, and the substituted
        // stability is itself zero, so stability * (1 + increase) becomes 0 * Infinity = NaN - a NaN
        // written back to a store is PERMANENT. A negative value hits the same power with a negative base,
        // which is NaN directly for a non-integer exponent. Same failure shape Decay's guard prevents.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { InitialStability = initialStability });
    }

    [Fact]
    public void A_positive_InitialStability_is_accepted() =>
        // sanity check: the guard rejects the bad domain without also rejecting the valid one
        Assert.Equal(5.0, new DsrOptions { InitialStability = 5.0 }.InitialStability);

    [Theory]
    [InlineData(-0.0005)]
    [InlineData(-1e16)]
    public void A_decay_whose_DERIVED_factor_collapses_is_rejected_at_construction(double decay)
    {
        // DsrOptions.Decay's own guard fixes the SIGN and rejects non-finite values, which is all it can
        // see. MAGNITUDE only becomes visible after F = 0.5^(1/decay) - 1 is derived, and BOTH extremes
        // reproduce exactly the catastrophes that guard exists to prevent — from values it accepts:
        //   -0.0005 -> 1/decay = -2000 -> 0.5^-2000 overflows -> F = +Infinity -> r = 0 for EVERY age > 0,
        //              so recall returns nothing and PruneAsync reaps the corpus;
        //   -1e16   -> 1/decay = -1e-16 -> 0.5^-1e-16 rounds to exactly 1.0 in a double -> F = 0, the
        //              curve's base collapses to 1 and r == 1 forever, so nothing is ever forgotten.
        // Both are the OPPOSITE of what Decay's own doc promises a consumer ("a less negative value is a
        // heavier tail"), silently — which is why the assertion is on the derived value, not on the input.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DsrRetrievability(new DsrOptions { Decay = decay }));
    }

    [Fact]
    public void A_normal_magnitude_decay_is_still_accepted()
    {
        // the guard must reject only the collapsing magnitudes: an ordinary exponent still constructs AND
        // still anchors r = 0.5 at age == stability, which is the whole point of deriving F
        var policy = new DsrRetrievability(new DsrOptions { Decay = -1.2 });

        Assert.Equal(0.5, policy.Retrievability(State(30, 30)), precision: 9);
    }

    [Theory]
    [InlineData(-0.4)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_StabilizationDecay_is_rejected_at_construction(double stabilizationDecay)
    {
        // A negative exponent INVERTS law 2 rather than weakening it: S^(+k) grows with stability, so the
        // most durable entry gains the most. At -0.4 with every other default, an entry recalled at each
        // half-life runs 20 -> 168 -> the 2000 ceiling in TWO reinforcements (the correct sign runs
        // 20 -> 33.5 -> 51.8) — a permanently-retrievable ASSOCIATIVE entry, which is the exact defect
        // MaxStability exists to prevent, arriving through a different door.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DsrOptions { StabilizationDecay = stabilizationDecay });
    }

    [Fact]
    public void A_zero_StabilizationDecay_is_accepted_because_off_is_not_inverted() =>
        // the guard must not reject "turn law 2 off" — a gain independent of stability is degenerate, not
        // backwards, and it reads as deliberate at the configuring line
        Assert.Equal(0, new DsrOptions { StabilizationDecay = 0 }.StabilizationDecay);

    [Theory]
    [InlineData(-1.5)]
    [InlineData(-1e-9)]
    [InlineData(701)]
    [InlineData(1e9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_SpacingWeight_is_rejected_at_construction(double spacingWeight)
    {
        // BOTH ends are load-bearing. A negative weight makes e^(w(1-r)) - 1 negative for every r < 1, so
        // Reinforce's Math.Max(0, .) floor zeroes the whole increase and reinforcement becomes a silent
        // NO-OP — recall never strengthens anything and the corpus decays to prunable with no error. Past
        // Math.Exp's overflow point (~709.78, reachable since (1-r) <= 1) the term goes infinite for LOW r
        // first, and Reinforce's finiteness floor reads THAT as no growth: a nearly-forgotten entry gains
        // nothing while a fresh one still gains, inverting law 3 in the one regime it exists for.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { SpacingWeight = spacingWeight });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(700)]
    public void The_SpacingWeight_bounds_themselves_are_accepted(double spacingWeight) =>
        // inclusive at both ends: 0 is "law 3 off" and 700 is the largest weight Math.Exp cannot overflow on
        Assert.Equal(spacingWeight, new DsrOptions { SpacingWeight = spacingWeight }.SpacingWeight);

    [Theory]
    [InlineData(-0.08)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_DifficultyWeight_is_rejected_at_construction(double difficultyWeight) =>
        // a negative weight flips e^(-w(d-1)) above 1, so HARDER material gains MORE — law 1 exactly
        // backwards, and undetectably: the result stays finite, in range, and monotone in difficulty
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DsrOptions { DifficultyWeight = difficultyWeight });

    [Fact]
    public void A_zero_DifficultyWeight_is_accepted_because_off_is_not_inverted() =>
        // "treat everything as neutral difficulty" is what an application writing no difficulty signal
        // already gets, so it cannot be an error to ask for it explicitly
        Assert.Equal(0, new DsrOptions { DifficultyWeight = 0 }.DifficultyWeight);

    // ---- the five options that shipped UNGUARDED beside the guarded ones above (archive Part 54, DSR3) ----
    //
    // A record where one field is domain-guarded and its neighbours are not is more dangerous than one with
    // no guards at all, because the guard reads as evidence the record was audited
    // (`.claude/knowledge/pitfalls.md`). These five were the remaining half.

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2000.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_MaxStability_is_rejected_at_construction(double maxStability) =>
        // THE one that reaches persisted state. Reinforce ends in Math.Min(grown, MaxStability) and
        // GraphMemoryEngine feeds that return straight into TouchAsync, so NaN is WRITTEN BACK — and a NaN
        // stability compares false against every threshold, so the entry then neither ranks, nor prunes, nor
        // reports as broken. A zero or negative ceiling clamps every reinforcement down to itself, so
        // Reinforce returns something SMALLER than it was handed — the existing contract fact
        // Reinforcement_never_shortens_a_memory, violated for the whole corpus at once. PositiveInfinity is
        // rejected because Math.Min(x, Infinity) is x: it removes the ceiling entirely, which is the
        // unbounded compounding the property exists to prevent (double.MaxValue is the honest way to say it).
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { MaxStability = maxStability });

    [Fact]
    public void A_positive_MaxStability_is_accepted() =>
        // sanity check: the guard rejects the bad domain without also rejecting the valid one
        Assert.Equal(500.0, new DsrOptions { MaxStability = 500.0 }.MaxStability);

    [Fact]
    public void A_poisoned_MaxStability_can_no_longer_reach_a_stored_stability()
    {
        // The defect stated end-to-end rather than as a domain check: before the guard, `new DsrOptions
        // { MaxStability = double.NaN }` constructed happily and every Reinforce returned NaN — the value
        // GraphMemoryEngine writes back through TouchAsync. Mutation-checking this is the point: delete the
        // guard's IsFinite clause and this fact fails on the FIRST assertion (construction succeeds), which
        // the domain Theory above cannot distinguish from a guard that merely throws the wrong type.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { MaxStability = double.NaN });

        // and the shipped default still reinforces to a finite, non-shrinking stability
        var reinforced = new DsrRetrievability().Reinforce(State(age: 40, stability: 20));
        Assert.True(double.IsFinite(reinforced.Stability), $"stability was {reinforced.Stability}");
        Assert.True(reinforced.Stability >= 20, $"stability shrank to {reinforced.Stability}");
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_ConnectionBoost_is_rejected_at_construction(double connectionBoost) =>
        // A negative factor does NOT invert the protection — EffectiveStability's outer Math.Max(stability, .)
        // floors the boosted half-life at the stored one, so Connectedness_never_lowers_retrievability keeps
        // passing. It makes connectedness a silent NO-OP instead, while MaxConnectionBoost still widens
        // CandidateCutoff for a protection that is no longer happening. PositiveInfinity is the sharp case:
        // at Strength = 0 (most entries) the term is Infinity * ln(1) = Infinity * 0 = NaN.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { ConnectionBoost = connectionBoost });

    [Fact]
    public void A_zero_ConnectionBoost_is_accepted_because_off_is_not_inverted() =>
        // 1 + 0 * ln(1 + strength) = 1 — "connectedness lengthens nothing", degenerate but deliberate, and it
        // reads that way at the configuring line where a sign error does not
        Assert.Equal(0, new DsrOptions { ConnectionBoost = 0 }.ConnectionBoost);

    [Theory]
    [InlineData(0.999)]
    [InlineData(0.0)]
    [InlineData(-4.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_MaxConnectionBoost_is_rejected_at_construction(double maxConnectionBoost) =>
        // Both readers already take Math.Max(1, MaxConnectionBoost), so a value below 1 never reduces a
        // half-life — it is a configured number that silently means a different number, which is harder to
        // diagnose than either outcome. NaN defeats those floors (Math.Max(1, NaN) is NaN) and poisons BOTH
        // Retrievability and CandidateCutoff, where a NaN bound compares false against every candidate and
        // PruneAsync silently stops pruning at all.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DsrOptions { MaxConnectionBoost = maxConnectionBoost });

    [Fact]
    public void The_MaxConnectionBoost_lower_bound_itself_is_accepted() =>
        // inclusive: exactly 1 is "connectedness may never multiply a half-life", the neutral setting both
        // readers' own floors already resolve to
        Assert.Equal(1.0, new DsrOptions { MaxConnectionBoost = 1.0 }.MaxConnectionBoost);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-100.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_EdgeHalfLife_is_rejected_at_construction(double edgeHalfLife) =>
        // EffectiveStrength reads a non-positive half-life as "no decay at all" (its `|| EdgeHalfLife <= 0`
        // branch returns the raw stored strength), so zero or negative switches the mechanism OFF rather than
        // tuning it and a neighbourhood that went quiet keeps propping the memory up forever. PositiveInfinity
        // reaches the same "off" through Math.Pow(2, -0) == 1. NaN is caught by NEITHER — NaN <= 0 is false —
        // so it flows into Math.Pow(2, -age/NaN) and out as a NaN retrievability.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { EdgeHalfLife = edgeHalfLife });

    [Fact]
    public void A_positive_EdgeHalfLife_is_accepted() =>
        // sanity check: the guard rejects the bad domain without also rejecting the valid one
        Assert.Equal(50.0, new DsrOptions { EdgeHalfLife = 50.0 }.EdgeHalfLife);

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_ReinforceGain_is_rejected_at_construction(double reinforceGain) =>
        // A negative gain does not shrink a memory — Reinforce's own finiteness floor zeroes the whole
        // increase — so the "never smaller than the current one" guarantee holds. It produces the silent
        // NO-OP SpacingWeight's negative branch describes, one level out: because this multiplies the
        // COMBINED term it zeroes all three laws at once, so no recall ever strengthens anything and the
        // corpus decays to prunable with no error. Non-finite lands in the same no-op via that same floor.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { ReinforceGain = reinforceGain });

    [Fact]
    public void A_zero_ReinforceGain_is_accepted_because_off_is_not_inverted() =>
        // "reinforcement off" is a legitimate, legible configuration; a sign error is not
        Assert.Equal(0, new DsrOptions { ReinforceGain = 0 }.ReinforceGain);

    [Fact]
    public void Every_option_on_the_record_now_rejects_a_NaN()
    {
        // Prefer a test that walks the WHOLE options surface over one that pins the field just fixed
        // (`.claude/knowledge/pitfalls.md`, written after Decay was guarded and InitialStability was not).
        // Reflection over the record's own properties, so a property ADDED later without a guard fails here
        // rather than shipping as the next unguarded neighbour — the compiler cannot see that omission.
        var offenders = typeof(DsrOptions).GetProperties()
            .Where(p => p.PropertyType == typeof(double) && p.CanWrite)
            .Where(p => !ThrowsOnNaN(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "these DsrOptions properties accept NaN, which every reader propagates by IEEE-754 and " +
            $"GraphMemoryEngine can write back permanently: {string.Join(", ", offenders)}");

        // there is no reflective `with` for an init-only property, so set each by name through the record's
        // own initializer surface — a plain switch, which fails to compile if a property is renamed
        static bool ThrowsOnNaN(string name)
        {
            try
            {
                _ = name switch
                {
                    nameof(DsrOptions.InitialStability) => new DsrOptions { InitialStability = double.NaN },
                    nameof(DsrOptions.Decay) => new DsrOptions { Decay = double.NaN },
                    nameof(DsrOptions.MaxStability) => new DsrOptions { MaxStability = double.NaN },
                    nameof(DsrOptions.ConnectionBoost) => new DsrOptions { ConnectionBoost = double.NaN },
                    nameof(DsrOptions.MaxConnectionBoost) => new DsrOptions { MaxConnectionBoost = double.NaN },
                    nameof(DsrOptions.EdgeHalfLife) => new DsrOptions { EdgeHalfLife = double.NaN },
                    nameof(DsrOptions.ReinforceGain) => new DsrOptions { ReinforceGain = double.NaN },
                    nameof(DsrOptions.StabilizationDecay) => new DsrOptions { StabilizationDecay = double.NaN },
                    nameof(DsrOptions.SpacingWeight) => new DsrOptions { SpacingWeight = double.NaN },
                    nameof(DsrOptions.DifficultyWeight) => new DsrOptions { DifficultyWeight = double.NaN },
                    nameof(DsrOptions.DifficultyChangeWeight) => new DsrOptions { DifficultyChangeWeight = double.NaN },
                    nameof(DsrOptions.DifficultyReversionWeight) => new DsrOptions { DifficultyReversionWeight = double.NaN },
                    nameof(DsrOptions.DifficultyReversionTarget) => new DsrOptions { DifficultyReversionTarget = double.NaN },
                    nameof(DsrOptions.NeutralDifficulty) => new DsrOptions { NeutralDifficulty = double.NaN },
                    // an unlisted property is a NEW one nobody taught this fact about: report it as an
                    // offender rather than passing it silently
                    _ => null,
                };
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }
    }

    [Fact]
    public void The_cutoff_is_about_thirty_times_wider_than_the_exponential_curves()
    {
        // The "roughly thirty times wider" claim is stated in DsrRetrievability's own class doc and in
        // CHANGELOG.md, comparing against the exponential curve 2.5.x shipped — deleted in 3.0
        // (docs/DECISIONS.md D49), so there is no live instance to compute the denominator from any more.
        // Its cutoff at these defaults was the exact, unboosted inverse (Math.Log2(1/floor)) widened by its
        // own MaxConnectionBoost default of 4 — both hardcoded here rather than resurrecting the deleted
        // class, deliberately, so a future change to THIS curve's Decay/MaxConnectionBoost or to
        // MinRetrievability's usual floor still leaves the claim checkable against the frozen historical
        // baseline it was actually measured against, not a moving target.
        const double exponentialFloor = 0.05;
        const double exponentialMaxConnectionBoost = 4; // the deleted HalfLifeOptions.MaxConnectionBoost default
        var exponentialCutoff = Math.Log2(1 / exponentialFloor) * exponentialMaxConnectionBoost;
        var ratio = new DsrRetrievability().CandidateCutoff(exponentialFloor) / exponentialCutoff;

        Assert.Equal(30.77, ratio, precision: 2);
    }

    [Fact]
    public void A_non_finite_Age_does_not_poison_a_stored_stability()
    {
        // DsrOptions.InitialStability's own guard closes ONE route to a non-finite `increase`, but Age
        // arrives per-call in the caller's own MemoryDecayState, which DsrOptions cannot validate. A NaN
        // Age makes Retrievability return NaN, which would make `increase` NaN too - the floor in Reinforce
        // must catch this BEFORE flooring (Math.Max(0, NaN) is NaN, not 0), or the NaN reaches the store.
        var policy = new DsrRetrievability();

        var reinforced = policy.Reinforce(new MemoryDecayState(Age: double.NaN, RecallCount: 0, Stability: 50));

        // an un-computable increase is treated as no growth, not as an error: the entry keeps its stability
        Assert.Equal(50, reinforced.Stability, precision: 9);
    }

    [Fact]
    public void An_immediate_re_recall_gains_nothing()
    {
        // THE sharpest difference from the flat x1.5 multiplier: at r = 1 the spacing term is exactly zero,
        // so reviewing what you just saw teaches nothing. Dropping the "- 1" from the spacing term destroys
        // this while every other test stays green, which is why it needs its own.
        var policy = new DsrRetrievability();

        Assert.Equal(100, policy.Reinforce(new MemoryDecayState(Age: 0, RecallCount: 0, Stability: 100)).Stability,
            precision: 9);
    }

    [Fact]
    public void Law2_a_more_stable_memory_gains_proportionally_less()
    {
        // Stabilization decay: the potential to strengthen shrinks as stability grows, which is what stops a
        // frequently-recalled entry compounding into permanence.
        // Both states are deliberately at age == stability, so BOTH have r = 0.5 exactly. That holds the
        // spacing term equal and leaves stability as the only variable — without it, this test would pass
        // just as happily on law 3's arithmetic and prove nothing about law 2.
        var policy = Reinforcing();
        var young = new MemoryDecayState(Age: 10, RecallCount: 0, Stability: 10);
        var old = new MemoryDecayState(Age: 400, RecallCount: 0, Stability: 400);

        var youngGain = policy.Reinforce(young).Stability / young.Stability;
        var oldGain = policy.Reinforce(old).Stability / old.Stability;

        Assert.True(youngGain > oldGain, $"young gained ×{youngGain}, old ×{oldGain}");
    }

    [Fact]
    public void Law3_a_nearly_forgotten_memory_gains_more_than_a_fresh_one()
    {
        // THE spacing effect, and the sharpest gap in the flat-multiplier model: both states have identical
        // stability, so only retrievability at review differs.
        var policy = Reinforcing();
        var fresh = new MemoryDecayState(Age: 1, RecallCount: 0, Stability: 100);
        var faded = new MemoryDecayState(Age: 900, RecallCount: 0, Stability: 100);

        Assert.True(policy.Reinforce(faded).Stability > policy.Reinforce(fresh).Stability,
            $"faded {policy.Reinforce(faded).Stability} should exceed fresh {policy.Reinforce(fresh).Stability}");
    }

    [Fact]
    public void Law1_harder_material_gains_less()
    {
        // Difficulty is LIVE state now (2026-08-10, fsrs-properly plan Task 2) — Reinforce reads
        // state.Difficulty directly, never the signals bag (which only seeds/refreshes the live value at
        // WRITE time, a store's job, not this policy's — see MemoryDecayState.Difficulty's own remarks).
        var policy = Reinforcing();
        var baseline = new MemoryDecayState(50, 0, 50);
        var hard = baseline with { Difficulty = 9 };

        Assert.True(policy.Reinforce(hard).Stability < policy.Reinforce(baseline).Stability,
            $"hard {policy.Reinforce(hard).Stability} should be under baseline {policy.Reinforce(baseline).Stability}");
    }

    [Fact]
    public void An_absent_difficulty_is_neutral_and_never_throws()
    {
        // every entry written before this field existed defaults to the neutral value, and none may change
        // behaviour unpredictably because of it
        var policy = new DsrRetrievability();

        var reinforced = policy.Reinforce(new MemoryDecayState(50, 0, 50));

        Assert.True(reinforced.Stability >= 50);
        Assert.True(double.IsFinite(reinforced.Stability));
    }

    [Fact]
    public void A_non_finite_or_out_of_range_difficulty_is_coerced_not_propagated()
    {
        // Difficulty arrives through a public seam (a consumer may construct MemoryDecayState directly), so
        // a NaN would otherwise poison stability permanently - and a NaN stability makes every later
        // comparison false, which empties recalls silently.
        //
        // "finite and in range" alone is too weak an assertion here: removing the Math.Clamp while keeping
        // the IsFinite check still yields all-finite, in-range values, so it would pass right through this
        // check while law 1 quietly INVERTS - difficulty -5 would gain MORE than the floor, and 1e9 would
        // land somewhere past the difficulty=10 ceiling while still reading as "in range". Coercion must be
        // pinned EXACTLY: every out-of-range value collapses onto its clamped boundary, nothing in between.
        //
        // Non-finite is NOT the same boundary as below-range, corrected 2026-08-11: non-finite means "no
        // information" and coerces to the NEUTRAL mid-point (5, DsrOptions.NeutralDifficulty's own default),
        // while an EXPLICIT below-range value (a real judgement, just out of domain) still clamps to the
        // FLOOR (1) - the two used to coincide (both were 1) which is exactly how this defect went
        // unnoticed; they are now two different results and this fact tests both, separately.
        var policy = new DsrRetrievability();
        var neutral = policy.Reinforce(new MemoryDecayState(50, 0, 50)).Stability;
        var atFloorDifficulty = policy.Reinforce(new MemoryDecayState(50, 0, 50, Difficulty: 1)).Stability;
        var atMaxDifficulty = policy.Reinforce(new MemoryDecayState(50, 0, 50, Difficulty: 10)).Stability;

        foreach (var difficulty in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var state = new MemoryDecayState(50, 0, 50, Difficulty: difficulty);

            // non-finite coerces to the NEUTRAL (mid-point) result — the same one an unspecified Difficulty
            // (the record's own default) already produces, since both mean "no information".
            Assert.Equal(neutral, policy.Reinforce(state).Stability, precision: 9);
        }

        var belowMin = new MemoryDecayState(50, 0, 50, Difficulty: -5);
        // below-range coerces to EXACTLY the difficulty=1 (floor) result — an explicit judgement, clamped,
        // NOT the neutral value, even though both were "1" before this correction.
        Assert.Equal(atFloorDifficulty, policy.Reinforce(belowMin).Stability, precision: 9);

        var overMax = new MemoryDecayState(50, 0, 50, Difficulty: 1e9);
        // above-range coerces to EXACTLY the difficulty=10 result, not merely something finite and in range
        Assert.Equal(atMaxDifficulty, policy.Reinforce(overMax).Stability, precision: 9);
    }

    // ---- the grade signal (design spec §1) and the difficulty update it drives (design spec §2,
    // 2026-08-10 fsrs-properly plan Task 2) ----

    /// <summary>THE drift guard. Design spec §1: a curve graded by its own prediction can drift — one that
    /// systematically overestimates retrievability would derive "Easy" and lower its own difficulty,
    /// reinforcing the overestimate forever. The mitigation is cheap and absolute: the grade must come from
    /// the state BEFORE this reinforcement, never from a value this same call produces.
    /// <para>Pinned by computing the expected next difficulty INDEPENDENTLY — from
    /// <see cref="DsrRetrievability.Retrievability"/> applied to the UNTOUCHED incoming <c>state</c> (which
    /// is, by construction, the pre-reinforcement value: <c>Reinforce</c> never mutates its argument) — and
    /// comparing against <see cref="DsrRetrievability.Reinforce"/>'s actual output. The age/stability here
    /// (200/20, ten half-lives) is chosen so reinforcement grows stability substantially, which is exactly
    /// what makes pre- and post-reinforcement retrievability at the SAME age genuinely different numbers —
    /// a state where reinforcement grew nothing at all could not distinguish the two.</para>
    /// <para><b>Mutation-checked live</b> (task brief Step 2): temporarily changed this class's own
    /// <c>Reinforce</c> to derive the grade from <c>Retrievability(state with { Stability = grown })</c> —
    /// the POST-reinforcement stability at the same age — instead of the pre-reinforcement <c>retrievability</c>
    /// local already computed at the top of the method. This fact failed (the independently-computed
    /// <c>expected</c> no longer matched, because post-reinforcement retrievability at age 200 with the
    /// grown stability is measurably higher than the pre-reinforcement value). Reverted; re-ran; passes
    /// again — confirming the guard is load-bearing, not decoration.</para></summary>
    [Fact]
    public void The_derived_grade_is_computed_from_the_state_BEFORE_this_reinforcement()
    {
        // DifficultyReversionWeight: 0 isolates exactly what this fact is about — the pre/post-state
        // timing question — from the separate mean-reversion force (fix round 1, C2), which would otherwise
        // need reproducing here too just to keep this test's own independent computation in sync.
        var options = new DsrOptions { DifficultyChangeWeight = 0.5, DifficultyReversionWeight = 0 };
        var policy = new DsrRetrievability(options);
        var state = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20);

        // the PRE-reinforcement retrievability — `state` is never mutated, so this is definitionally the
        // value that made this recall succeed, not anything the reinforcement itself is about to produce.
        // g = 2 + 2r (fix round 1, C3 — restricted to the success sub-range [2,4], never emits FSRS's lapse
        // rating; see DsrOptions.DifficultyChangeWeight's and Reinforce's own remarks).
        var preReinforcementR = policy.Retrievability(state);
        var derivedGrade = 2 + 2 * preReinforcementR;
        var delta = -options.DifficultyChangeWeight * (derivedGrade - 3);
        // Starts from state.Difficulty itself, not a hardcoded literal — this fact is about the PRE/POST
        // timing question, not about which specific value the record's own default carries (2026-08-11: that
        // default moved from 1 to 5; referencing it directly keeps this fact correct across such a change).
        var expected = Math.Clamp(state.Difficulty + (10 - state.Difficulty) * delta / 9, 1, 10);

        Assert.Equal(expected, policy.Reinforce(state).Difficulty, precision: 6);
    }

    [Fact]
    public void A_recall_derived_as_hard_low_retrievability_raises_difficulty()
    {
        // recalled at low r -> nearly forgotten -> derives toward Hard, never Again (design spec §1, fix
        // round 1 C3 — every graded event here is a success by construction) -> difficulty rises, so future
        // reinforcement is damped harder (law 1) — the model treating this as material that needs more
        // support, not less
        var policy = new DsrRetrievability(new DsrOptions { DifficultyReversionWeight = 0 }); // isolate law 1's own direction from the separate reversion pull
        // age far past several half-lives at the neutral starting difficulty: r is low, so grade < 3
        var faded = new MemoryDecayState(Age: 400, RecallCount: 0, Stability: 20, Difficulty: 1);

        Assert.True(policy.Retrievability(faded) < 0.5,
            $"test setup drifted: r={policy.Retrievability(faded)} is not in the Hard zone (< 0.5, this " +
            "curve's own half-life anchor and, since fix round 1 C3, the grade mapping's neutral point too)");
        Assert.True(policy.Reinforce(faded).Difficulty > faded.Difficulty,
            $"difficulty did not rise on a derived-hard recall: {faded.Difficulty} -> {policy.Reinforce(faded).Difficulty}");
    }

    [Fact]
    public void A_recall_derived_as_easy_high_retrievability_lowers_difficulty()
    {
        // recalled at high r -> fresh -> derives toward Easy (design spec §1) -> difficulty falls
        var policy = new DsrRetrievability(new DsrOptions { DifficultyReversionWeight = 0 }); // isolate law 1's own direction from the separate reversion pull
        // starts away from the floor so a fall is actually observable; age 1 against stability 100 keeps r
        // close to 1, well into the Easy zone
        var fresh = new MemoryDecayState(Age: 1, RecallCount: 0, Stability: 100, Difficulty: 5);

        Assert.True(policy.Retrievability(fresh) > 0.5,
            $"test setup drifted: r={policy.Retrievability(fresh)} is not in the Easy zone (> 0.5)");
        Assert.True(policy.Reinforce(fresh).Difficulty < fresh.Difficulty,
            $"difficulty did not fall on a derived-easy recall: {fresh.Difficulty} -> {policy.Reinforce(fresh).Difficulty}");
    }

    /// <summary>THE mutation result behind the 2026-08-11 neutral-difficulty fix, kept permanently rather than
    /// only in the task report — a whole-plan review's own Critical caught that the migrations still
    /// backfilled the DEFECT default (<c>1</c>) after this fact's own sibling
    /// (<see cref="A_recall_derived_as_easy_high_retrievability_lowers_difficulty"/>, above) had already shown
    /// difficulty moving from a HAND-CONSTRUCTED starting point — never from the specific value a real
    /// migrated row would have carried. This fact closes that gap directly: two states, differing ONLY in
    /// their starting <see cref="MemoryDecayState.Difficulty"/> (<c>1</c>, what every row migrated before this
    /// fix carries; <c>5</c>, what the corrected migration and every fresh write carries), reinforced at the
    /// SAME realistic, Easy-leaning recall (this library's own corpus measured 89.6% of derived grades
    /// Easy-leaning, mean <c>g=3.81</c> — this fact's own <c>r≈0.93</c> derives <c>g≈3.87</c>, squarely in that
    /// band, not a contrived edge case) — using the SHIPPED FSRS-6 defaults throughout, not an isolated law.
    /// <para><b>The old default is PINNED, exactly, not merely "slow to move."</b> From <c>D=1</c>, an
    /// Easy-leaning grade's damped value lands BELOW the floor, and <c>Math.Clamp(_, 1, 10)</c> floors it
    /// right back to the IDENTICAL starting value — a migrated row that keeps being successfully recalled
    /// (the common case) never leaves <c>1</c>, ever, which is the exact defect the fix addresses. The new
    /// default genuinely moves under the IDENTICAL grade, because <c>5</c> is far enough from either bound
    /// for the SAME damped delta to land inside <c>[1, 10]</c> without clamping.</para></summary>
    [Fact]
    public void A_row_migrated_under_the_old_default_stays_pinned_while_the_corrected_default_moves()
    {
        var policy = new DsrRetrievability(); // FSRS-6's own shipped defaults, unisolated — the real behaviour
        var migratedUnderOldDefault = new MemoryDecayState(Age: 1, RecallCount: 0, Stability: 20, Difficulty: 1);
        var migratedUnderCorrectedDefault = migratedUnderOldDefault with { Difficulty = 5 };

        var r = policy.Retrievability(migratedUnderOldDefault);
        Assert.True(r > 0.499,
            $"test setup drifted: r={r} is not comfortably past the ~0.499 boundary below which even D=1 " +
            "could still move (the whole-plan review's own computed threshold) — this fact needs a realistic " +
            "Easy-leaning recall, not a contrived Hard one.");

        var oldStaysPinned = policy.Reinforce(migratedUnderOldDefault).Difficulty;
        var newMoves = policy.Reinforce(migratedUnderCorrectedDefault).Difficulty;

        Assert.Equal(1, oldStaysPinned, 9); // PINNED — the migration defect, in permanent test form
        Assert.True(newMoves < 5 - 1e-6,
            $"expected the corrected default (5) to fall under this Easy-leaning grade; got {newMoves}");
    }

    /// <summary>Fix-round-1 C3, the load-bearing property: every graded event here is a SUCCESS by
    /// construction (an entry that is not returned never reaches <see cref="DsrRetrievability.Reinforce"/>),
    /// so the derived grade must never emit FSRS's lapse rating (<c>Again = 1</c>) — not even at the
    /// theoretical floor of retrievability, <c>r = 0</c>. Pinned at the ACTUAL floor of what
    /// <see cref="DsrRetrievability.Retrievability"/> can return for <c>state.Age &gt; 0</c> (it is
    /// asymptotic, so <c>r</c> never reaches exactly 0 in practice, but the derived-grade FORMULA must still
    /// hold at the limit): a huge age/stability ratio drives <c>r</c> to the smallest positive value this
    /// policy's own arithmetic produces, and the resulting <c>Difficulty</c> must still be exactly what a
    /// grade of 2 (Hard, not 1/Again) predicts.
    /// <para><b>Mutation-checked live</b> (fix round 1): temporarily reverted <c>DerivedGrade</c> to
    /// <c>1 + 3 * retrievability</c> — this fact's own independently-computed <c>expected</c> (using the
    /// CORRECT <c>2 + 2r</c>) no longer matched <c>Reinforce</c>'s actual output. Reverted; re-ran; passes
    /// again.</para></summary>
    [Fact]
    public void The_derived_grade_never_emits_the_lapse_rating_even_at_the_practical_floor_of_r()
    {
        var options = new DsrOptions { DifficultyChangeWeight = 0.5, DifficultyReversionWeight = 0 };
        var policy = new DsrRetrievability(options);
        // an enormous age/stability ratio drives r arbitrarily close to (but never exactly) 0
        var state = new MemoryDecayState(Age: 1_000_000_000, RecallCount: 0, Stability: 1, Difficulty: 1);

        var r = policy.Retrievability(state);
        Assert.True(r < 1e-4, $"test setup drifted: r={r} is not close enough to the floor to mean anything");

        // g = 2 + 2r -> at r's practical floor, g is barely above 2 (Hard) - NEVER 1 (Again, a lapse)
        var expectedGrade = 2 + 2 * r;
        Assert.True(expectedGrade >= 2, $"the derived grade formula itself produced {expectedGrade} < 2 (a lapse)");

        var expectedDelta = -options.DifficultyChangeWeight * (expectedGrade - 3);
        var expectedDifficulty = Math.Clamp(1 + (10 - 1) * expectedDelta / 9, 1, 10);
        Assert.Equal(expectedDifficulty, policy.Reinforce(state).Difficulty, precision: 6);
    }

    /// <summary>Fix-round-1 I1: the across-call drift the pre-state guard cannot see by construction. A
    /// recall does not advance the engine's position, so a SESSION BURST — several recalls of the same
    /// entry with no intervening write — hands <see cref="DsrRetrievability.Reinforce"/> <c>Age = 0</c> every
    /// time after the first touch (the exact snapshot <c>GraphMemoryEngine</c>'s own age-resolution reports
    /// for an immediate subsequent recall). Without the Δt=0 bypass, every one of those calls would derive
    /// Easy (<c>r=1</c> at <c>Age&lt;=0</c>) and lower difficulty every time — a free difficulty-lowering
    /// pump driven by recall CADENCE, not by how hard the material actually is.
    /// <para><b>Mutation-checked live</b> (fix round 1): temporarily removed the <c>state.Age &lt;= 0</c>
    /// bypass in <c>Reinforce</c> (always computed <c>NextDifficulty(difficulty, DerivedGrade(retrievability))</c>).
    /// This fact failed — difficulty drifted from 5 down across the ten iterations instead of staying exactly
    /// 5. Reverted; re-ran; passes again.</para></summary>
    [Fact]
    public void A_session_burst_with_no_intervening_write_does_not_move_difficulty()
    {
        var policy = new DsrRetrievability();
        // Age: 0 on every iteration — what GraphMemoryEngine's own age-resolution hands Reinforce for every
        // recall in a burst once the FIRST touch has already stamped the current position (TouchAsync's own
        // "stamp the CURRENT position" contract; no write moves the position between recalls in a burst)
        var burst = new MemoryDecayState(Age: 0, RecallCount: 0, Stability: 20, Difficulty: 5);

        for (var i = 0; i < 10; i++) burst = policy.Reinforce(burst);

        Assert.Equal(5, burst.Difficulty, precision: 9);
    }

    [Fact]
    public void The_difficulty_update_never_leaves_the_one_to_ten_range()
    {
        // bounded regardless of how extreme the derived grade, the reversion pull, or the starting
        // difficulty are — the final Math.Clamp is what makes this true, not any property of the inputs
        var policy = new DsrRetrievability(new DsrOptions
        {
            DifficultyChangeWeight = 50, // deliberately huge
            DifficultyReversionWeight = 1, // deliberately huge (the max of its own valid domain)
        });
        var atFloor = new MemoryDecayState(Age: 1, RecallCount: 0, Stability: 1000, Difficulty: 1); // Easy zone
        var atCeiling = new MemoryDecayState(Age: 100_000, RecallCount: 0, Stability: 1, Difficulty: 10); // Hard zone

        Assert.InRange(policy.Reinforce(atFloor).Difficulty, 1, 10);
        Assert.InRange(policy.Reinforce(atCeiling).Difficulty, 1, 10);
    }

    /// <summary>THE fix-round-1 C2 fact: <c>Difficulty = 10</c> is NOT an absorbing state. Linear damping's
    /// own factor, <c>(10-D)/9</c>, is IDENTICALLY ZERO at <c>D = 10</c> regardless of grade — so with no
    /// reversion, reinforcing an entry already at the ceiling would return EXACTLY 10 forever, whatever the
    /// derived grade. Mean reversion is the only term that can still move <c>D</c> there.</summary>
    [Fact]
    public void Difficulty_at_the_ceiling_is_no_longer_absorbing()
    {
        var policy = new DsrRetrievability(); // FSRS-6's own defaults — DifficultyReversionWeight = 0.001
        // an EASY recall (r near 1) — the case where the OLD (reversion-dropped) law would have kept D at
        // 10 with the most conviction, since damping alone would push toward the ceiling, not away from it
        var atCeiling = new MemoryDecayState(Age: 1, RecallCount: 0, Stability: 1000, Difficulty: 10);

        var reinforced = policy.Reinforce(atCeiling).Difficulty;

        Assert.True(reinforced < 10,
            $"D=10 is still absorbing: reinforcing it returned {reinforced}, not something strictly below 10");
        Assert.InRange(reinforced, 1, 10);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-1e-9)]
    [InlineData(1.0000001)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_DifficultyReversionWeight_is_rejected_at_construction(double weight) =>
        // outside [0, 1] the blend `w7 * target + (1 - w7) * damped` stops being a weighted average BETWEEN
        // the target and the damped value — the whole point of "reversion" — and starts collapsing every
        // review toward one extreme regardless of the actual grade
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { DifficultyReversionWeight = weight });

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void The_DifficultyReversionWeight_bounds_themselves_are_accepted(double weight) =>
        // inclusive at both ends: 0 is "reversion off" and 1 is "difficulty snaps to the target every review"
        Assert.Equal(weight, new DsrOptions { DifficultyReversionWeight = weight }.DifficultyReversionWeight);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_DifficultyReversionTarget_is_rejected_at_construction(double target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { DifficultyReversionTarget = target });

    [Theory]
    [InlineData(-4.77)]
    [InlineData(3.22)]
    [InlineData(0)]
    [InlineData(100)]
    public void Any_finite_DifficultyReversionTarget_is_accepted(double target) =>
        // no sign or magnitude restriction — a "pull point" carries no probability/rate meaning to invert,
        // and the formula has no Math.Pow of this value anywhere for a bad magnitude to overflow through
        Assert.Equal(target, new DsrOptions { DifficultyReversionTarget = target }.DifficultyReversionTarget);

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void An_out_of_domain_DifficultyChangeWeight_is_rejected_at_construction(double weight) =>
        // a negative weight flips the sign of the derived delta, so a HARD recall would LOWER difficulty
        // and an EASY one would RAISE it — the update inverted, silently, because the result stays finite
        // and in range throughout (the same shape every other sign-guarded DsrOptions field in this class
        // already pins)
        Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { DifficultyChangeWeight = weight });

    /// <summary>Zero is allowed on EACH weight and is NOT decoration, but (fix round 1, C2) neither one
    /// ALONE is the difficulty-inert control any more: <c>DifficultyChangeWeight</c> gates law 1's
    /// grade-driven delta, <c>DifficultyReversionWeight</c> gates the SEPARATE restoring force, and with
    /// reversion restored (this fix round) both must be zero together for <c>Reinforce</c> to write
    /// <c>Difficulty</c> back UNCHANGED on every review. That pair is the "difficulty-inert" DSR the design
    /// spec's §2/§5 needs as the comparator against difficulty-live DSR, and it needs no second, test-only
    /// curve type: the same <see cref="DsrRetrievability"/> class with these two options isolates the exact
    /// change under measurement.</summary>
    [Fact]
    public void Both_difficulty_weights_at_zero_together_make_the_update_a_no_op_the_inert_control()
    {
        var policy = new DsrRetrievability(
            new DsrOptions { DifficultyChangeWeight = 0, DifficultyReversionWeight = 0 });
        var state = new MemoryDecayState(Age: 400, RecallCount: 0, Stability: 20, Difficulty: 3.7);

        var reinforced = state;
        for (var i = 0; i < 5; i++) reinforced = policy.Reinforce(reinforced);

        Assert.Equal(3.7, reinforced.Difficulty, precision: 9);
    }

    /// <summary>The mutation guard against the OLD (pre-fix-round-1) belief that
    /// <c>DifficultyChangeWeight = 0</c> was sufficient on its own: with the DEFAULT
    /// <c>DifficultyReversionWeight</c> (FSRS-6's own <c>0.001</c>, not zero) still active, difficulty is
    /// NOT held constant — confirming the fact above actually needs both weights zeroed, not just one.
    /// </summary>
    [Fact]
    public void DifficultyChangeWeight_alone_at_zero_is_NOT_the_inert_control()
    {
        var policy = new DsrRetrievability(new DsrOptions { DifficultyChangeWeight = 0 }); // reversion weight left at its FSRS-6 default
        var state = new MemoryDecayState(Age: 400, RecallCount: 0, Stability: 20, Difficulty: 3.7);

        var reinforced = policy.Reinforce(state);

        Assert.NotEqual(3.7, reinforced.Difficulty, precision: 9);
    }

    [Fact]
    public void Reinforcement_stops_at_the_ceiling()
    {
        var options = new DsrOptions();
        var policy = new DsrRetrievability(options);
        var atCeiling = new MemoryDecayState(5000, 0, options.MaxStability);

        Assert.Equal(options.MaxStability, policy.Reinforce(atCeiling).Stability, precision: 6);
    }

    /// <summary>THE Part-54 DSR2 fix (2026-08-11): an entry whose stored stability is ALREADY past the
    /// ceiling is FROZEN — it can no longer grow — rather than TRUNCATED down to the ceiling.
    /// <para><b>What shipped before:</b> <c>Reinforce</c> ended in a bare
    /// <c>Math.Min(grown, MaxStability)</c>, so this exact fixture returned <c>2000</c> for a stored
    /// <c>100000</c> — a 50× SHORTENING, in direct violation of
    /// <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>'s own written guarantee that the result "must never
    /// be smaller than the current one" (the interface DISCLOSED the exception rather than closing it; the
    /// disclosure is gone with this fix). The fix takes the shape
    /// <see cref="DsrRetrievability"/>'s own <c>EffectiveStability</c> has always used one method away —
    /// <c>Math.Max(stability, Math.Min(stability × …, MaxStability))</c> — where the outer floor makes the
    /// ceiling cap GROWTH without ever acting as a CUT. That is what
    /// <see cref="DsrOptions.MaxStability"/> is documented to be for: "unbounded compounding would let an
    /// ASSOCIATIVE entry become permanently retrievable while still labelled associative" is an argument
    /// about GROWTH, and freezing stops growth just as completely as truncation did.</para>
    /// <para>The equality is two-sided on purpose: <c>100000</c> exactly, so this fails both if the entry is
    /// cut back to the ceiling (the old defect) and if the floor were written in a way that let an
    /// over-ceiling entry keep compounding (the thing the ceiling exists to prevent).</para></summary>
    [Fact]
    public void An_entry_stored_ABOVE_the_ceiling_is_frozen_not_truncated()
    {
        var policy = new DsrRetrievability(); // MaxStability at its shipped default of 2000
        // the measured reproduction from archive Part 54 DSR2, verbatim: a stored 100000 came back as 2000
        var overCeiling = new MemoryDecayState(Age: 5000, RecallCount: 0, Stability: 100_000);

        Assert.Equal(100_000, policy.Reinforce(overCeiling).Stability, precision: 6);
    }

    /// <summary>The other half of the DSR2 fix, and the reason it is not simply "remove the clamp": the floor
    /// must not switch the ceiling OFF. An entry UNDER the ceiling still cannot be grown past it, however
    /// large the increase — here an age of a million against a stability of 99, which drives <c>r</c> to
    /// nearly zero and so produces the largest spacing term this curve can emit.</summary>
    [Fact]
    public void The_ceiling_still_caps_growth_from_below_it()
    {
        var policy = Reinforcing(new DsrOptions { MaxStability = 100 });
        var justUnder = new MemoryDecayState(Age: 1_000_000, RecallCount: 0, Stability: 99);

        // the unclamped growth is ~1.8x, so this only lands on the ceiling if the Math.Min is still in force
        Assert.Equal(100, policy.Reinforce(justUnder).Stability, precision: 6);
    }

    [Fact]
    public void A_zero_stability_entry_reinforces_from_InitialStability_not_from_zero()
    {
        // The stub Task 2 replaces deliberately dropped this: it returned state.Stability verbatim, so a
        // zero-stability entry reinforced to 0 * (1 + increase) = 0 - and a zero written back to the store
        // is permanent (the fsrs-properly plan's Task 2 stub named this in its own <remarks>;
        // local/superpowers/plans/2026-08-10-fsrs-properly-plan-7.md). EffectiveStability already
        // substitutes InitialStability for a non-positive stored stability; Reinforce must do the same.
        var policy = new DsrRetrievability();
        var zero = new MemoryDecayState(Age: 5, RecallCount: 0, Stability: 0);
        var fromInitial = new MemoryDecayState(Age: 5, RecallCount: 0, Stability: policy.InitialStability);

        var reinforced = policy.Reinforce(zero).Stability;

        Assert.True(reinforced > 0, $"zero-stability entry reinforced to {reinforced}");
        // substitution must be the SAME one Retrievability uses internally, so both halves of Reinforce
        // agree on what "the stability" is for this entry
        Assert.Equal(policy.Reinforce(fromInitial).Stability, reinforced, precision: 9);
    }

    // ---- the connection/Strength axis of REINFORCEMENT (docs/task-archive.md Part 54, DSR4) ----
    //
    // Every other state in this class carries Strength = 0, so the EffectiveStrength -> EffectiveStability
    // path was exercised only through Retrievability (the shared contract's
    // Connectedness_never_lowers_retrievability), never through the reinforcement laws that read it via `r`.
    // That is a real hole: connectedness changes r, and r drives all three FSRS stability-increase laws AND
    // the derived grade, so a connected entry reinforces differently from an isolated one and nothing pinned
    // the difference. Each fact below is written so it FAILS if the Strength term were dropped from
    // EffectiveStability's input — mutation-checked by making EffectiveStrength return 0 unconditionally.

    /// <summary>Connectedness raises effective stability, which raises <c>r</c> at review, which SHRINKS law
    /// 3's spacing term <c>e^(spacing·(1−r)) − 1</c> — so a well-connected entry gains LESS from one recall
    /// than an identically-aged, identically-stored isolated one.
    /// <para><b>Pinned as a CONSEQUENCE of the composed formula, not as a stated design intent</b>:
    /// connectedness both slows decay (<c>EffectiveStability</c>) and damps reinforcement (law 3 reading the
    /// raised <c>r</c>), which makes the mechanism self-limiting rather than compounding. Nothing in the
    /// design record asks for that second half; it falls out of the two laws composing. It is pinned here so
    /// that changing either half is a deliberate act rather than a silent side effect — and because it is the
    /// sharpest available discriminator of whether <c>Strength</c> reaches <c>Reinforce</c> at all.</para>
    /// </summary>
    [Fact]
    public void A_connected_entry_gains_LESS_per_recall_than_an_isolated_one_because_its_r_is_higher()
    {
        var policy = Reinforcing();
        var isolated = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20);
        var connected = isolated with { Strength = 20 };

        // sanity: the two states genuinely differ on r, or the comparison below would be about nothing
        Assert.True(policy.Retrievability(connected) > policy.Retrievability(isolated),
            $"test setup drifted: connected r={policy.Retrievability(connected)} is not above isolated " +
            $"r={policy.Retrievability(isolated)}, so this fact cannot be measuring the Strength axis");

        var isolatedGain = policy.Reinforce(isolated).Stability - isolated.Stability;
        var connectedGain = policy.Reinforce(connected).Stability - connected.Stability;

        Assert.True(connectedGain < isolatedGain,
            $"connectedness did not damp the spacing term: connected gained {connectedGain}, isolated " +
            $"{isolatedGain} — equal values mean Strength never reached Reinforce at all");
    }

    /// <summary>The <see cref="MemoryDecayState.StrengthAge"/> half of the same path, which is a SEPARATE
    /// mechanism from <see cref="MemoryDecayState.Strength"/> itself: <c>EffectiveStrength</c> decays the
    /// stored strength by <c>2^(−StrengthAge / EdgeHalfLife)</c>, so a neighbourhood that went quiet stops
    /// propping the entry up and its reinforcement drifts back toward the isolated case — without ever
    /// reaching it, because the decay is asymptotic and the strength is never zeroed.
    /// <para><b>Which <c>EdgeHalfLife</c>:</b> <see cref="DsrOptions.EdgeHalfLife"/>, the curve's own
    /// connection-strength half-life — NOT <see cref="GraphMemoryOptions.EdgeHalfLife"/>, which has the same
    /// name and the same default of 100 and decays an EDGE'S WEIGHT during traversal
    /// (<c>.claude/knowledge/pitfalls.md</c>).</para></summary>
    [Fact]
    public void A_neighbourhood_that_went_quiet_reinforces_more_like_an_isolated_entry()
    {
        var policy = Reinforcing(); // DsrOptions.EdgeHalfLife = 100
        var freshlyConnected = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20, Strength: 20,
            StrengthAge: 0);
        var quiet = freshlyConnected with { StrengthAge = 600 }; // six edge half-lives: strength x 2^-6
        var isolated = freshlyConnected with { Strength = 0 };

        var freshGain = policy.Reinforce(freshlyConnected).Stability - freshlyConnected.Stability;
        var quietGain = policy.Reinforce(quiet).Stability - quiet.Stability;
        var isolatedGain = policy.Reinforce(isolated).Stability - isolated.Stability;

        Assert.True(quietGain > freshGain,
            $"a quiet neighbourhood still propped the entry up at full strength: quiet gained {quietGain}, " +
            $"freshly connected {freshGain} — equal values mean the StrengthAge decay never ran");
        Assert.True(quietGain < isolatedGain,
            $"a decayed strength was treated as NO strength: quiet gained {quietGain}, isolated " +
            $"{isolatedGain} — the decay is asymptotic, so a quiet neighbourhood is weaker, never absent");
    }

    /// <summary>The shape worth covering DELIBERATELY rather than by accident (<c>docs/task-archive.md</c> Part 54, DSR4's
    /// own note): a state carrying <c>Strength &gt; 0</c> with <see cref="MemoryDecayState.StrengthAge"/>
    /// UNSET — its record default of <c>0</c>, which is what a caller who never tracked strength age hands
    /// over, and what <c>EffectiveStrength</c>'s own <c>state.StrengthAge &lt;= 0</c> branch is for.
    /// <para>That branch returns the RAW stored strength: "no age recorded" means fully connected, never
    /// "unknown, therefore ignore it". Both halves are asserted, because only the second one rejects a
    /// plausible-looking wrong reading in which an unset age is treated as no connection at all.</para>
    /// </summary>
    [Fact]
    public void A_state_carrying_Strength_with_StrengthAge_unset_reinforces_as_FULLY_connected()
    {
        var policy = Reinforcing();
        var ageUnset = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20, Strength: 20);
        var ageExplicitlyZero = ageUnset with { StrengthAge = 0 };
        var isolated = ageUnset with { Strength = 0 };

        Assert.Equal(policy.Reinforce(ageExplicitlyZero).Stability, policy.Reinforce(ageUnset).Stability,
            precision: 9);
        Assert.NotEqual(policy.Reinforce(isolated).Stability, policy.Reinforce(ageUnset).Stability,
            precision: 9);
    }

    /// <summary>The connection axis reaches the DERIVED GRADE too, not only the three stability laws — the
    /// half of <c>Reinforce</c> that writes <see cref="MemoryDecayState.Difficulty"/>. <c>g = 2 + 2·r</c>
    /// reads the same connectedness-raised <c>r</c>, so a connected entry derives a more Easy-leaning grade
    /// than an isolated one at the identical age and stored stability, and its difficulty therefore lands
    /// lower. Without this, the whole difficulty half of the Strength path would be unpinned even with the
    /// stability facts above in place.</summary>
    [Fact]
    public void Connectedness_reaches_the_derived_grade_and_therefore_the_difficulty_update()
    {
        // reversion off isolates the grade-driven delta from the separate restoring force, exactly as the
        // neighbouring difficulty facts in this class already do
        var policy = new DsrRetrievability(new DsrOptions { DifficultyReversionWeight = 0 });
        var isolated = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20, Difficulty: 5);
        var connected = isolated with { Strength = 20 };

        var isolatedGrade = policy.DerivedGrade(isolated);
        var connectedGrade = policy.DerivedGrade(connected);
        Assert.NotNull(isolatedGrade);
        Assert.NotNull(connectedGrade);

        Assert.True(connectedGrade!.Value > isolatedGrade!.Value,
            $"connectedness did not reach the grade: connected g={connectedGrade}, isolated g={isolatedGrade}");
        Assert.True(policy.Reinforce(connected).Difficulty < policy.Reinforce(isolated).Difficulty,
            $"connected difficulty {policy.Reinforce(connected).Difficulty} should land under isolated " +
            $"{policy.Reinforce(isolated).Difficulty} on the more Easy-leaning derived grade");
    }

    /// <summary><see cref="DsrOptions.MaxConnectionBoost"/> is in force on the REINFORCEMENT path too, not
    /// only on <see cref="DsrRetrievability.CandidateCutoff"/>'s widening — the two read the same
    /// <c>EffectiveStability</c>, and the cutoff's superset guarantee is derived from the assumption that an
    /// effective half-life never exceeds this multiple of the STORED one. A reinforcement that ignored the
    /// saturation would break that assumption from the write side rather than the read side.
    /// <para>At the defaults the boost <c>1 + 0.5·ln(1 + strength)</c> saturates at <c>4</c> once strength
    /// passes <c>e^6 − 1 ≈ 402</c>, so 1000 and a billion must reinforce IDENTICALLY while 100 (boost ≈ 3.3,
    /// under the ceiling) must not.</para></summary>
    [Fact]
    public void The_connection_boost_saturates_on_the_reinforcement_path_too()
    {
        var policy = Reinforcing();
        var saturated = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20, Strength: 1_000);
        var absurdlyConnected = saturated with { Strength = 1e9 };
        var belowSaturation = saturated with { Strength = 100 };

        Assert.Equal(policy.Reinforce(saturated).Stability, policy.Reinforce(absurdlyConnected).Stability,
            precision: 9);
        Assert.NotEqual(policy.Reinforce(belowSaturation).Stability, policy.Reinforce(saturated).Stability,
            precision: 9);
    }

    // ---- the shared contract, which every policy must satisfy ----

    [Fact] public void Probability() => RetrievabilityPolicyContract.Retrievability_is_a_probability(new DsrRetrievability());
    [Fact] public void One_at_zero() => RetrievabilityPolicyContract.It_is_one_at_zero_age(new DsrRetrievability());
    [Fact] public void Monotone() => RetrievabilityPolicyContract.It_never_increases_with_age(new DsrRetrievability());
    [Fact] public void Reinforce_grows() => RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory(new DsrRetrievability());
    [Fact] public void Cutoff_superset() => RetrievabilityPolicyContract.CandidateCutoff_is_a_conservative_superset(new DsrRetrievability());
    [Fact] public void Unbounded_ok() => RetrievabilityPolicyContract.An_unbounded_policy_is_still_correct(new DsrRetrievability());
    [Fact] public void Connectedness_helps() => RetrievabilityPolicyContract.Connectedness_never_lowers_retrievability(new DsrRetrievability());
    [Fact] public void Stability_unit() => RetrievabilityPolicyContract.Stability_is_the_position_delta_at_which_retrievability_is_half(new DsrRetrievability());
    [Fact] public void Reinforce_owns_only_stability_and_difficulty() => RetrievabilityPolicyContract.Reinforcement_leaves_every_field_it_does_not_own_unchanged(new DsrRetrievability());

    // ---- the load-bearing claim of the fsrs-properly plan's Task 1 (design §0): deleting
    // HalfLifeRetrievability strands NO data ----

    /// <summary>
    /// THE claim <c>docs/DECISIONS.md</c>'s deletion decision rests on: <c>Stability</c> means exactly one
    /// thing across every implementation — the position delta at which retrievability is 0.5 — enforced by
    /// <see cref="RetrievabilityPolicyContract.Stability_is_the_position_delta_at_which_retrievability_is_half"/>
    /// against every shipped curve (Plan 5, Task 5). Both curves therefore stored stability in the SAME
    /// units, so a row a 2.5.x deployment wrote under the deleted exponential curve is already valid under
    /// DSR with no migration and no conversion.
    /// <para>Writes a node the way 2.5.x actually wrote one — <c>InitialStability</c> in half-life units, and
    /// the retired <see cref="MemoryRetrievabilityProvenance.HalfLife"/> provenance bit set — through a REAL
    /// <see cref="SqliteMemoryGraphStore"/> round-trip, not a hand-built <see cref="MemoryDecayState"/>, so
    /// this also proves storage itself never branches on provenance to convert the stored value. Then ages it
    /// through a <see cref="GraphMemoryEngine"/> built the 3.0 way (bare constructor, no <c>policy:</c>
    /// argument — DSR is now what that defaults to) to EXACTLY the stored stability, and recalls it: the
    /// r(S) = 0.5 anchor's own fixture state, so a curve that reinterpreted what "stability" MEANS (the exact
    /// failure the unit contract exists to prevent) would read something other than 0.5 here, not merely "a
    /// plausible-looking number".</para>
    /// <para><b>Mutation-checked</b> (task brief Step 4): temporarily changing <see cref="DsrRetrievability"/>
    /// to anchor at a different age/stability ratio — for instance FSRS's own 90%-retention convention instead
    /// of the half-life one this fact and <c>Stability_unit</c> above both pin — makes the final assertion
    /// fail; confirmed while implementing this task, then reverted (see <c>task-1-report.md</c>).</para>
    /// </summary>
    [Fact]
    public async Task A_row_written_under_2_5s_HalfLife_curve_recalls_correctly_under_Dsr_with_no_migration()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        const double stability = 20; // 2.5.x's own shipped HalfLifeOptions.InitialStability default

        var legacyId = await store.UpsertAsync(new GraphNodeWrite(
            "legacy", "t", "s", "h", "a fact written under the old curve", MemoryGrade.Associative,
            InitialStability: stability, Advance: 1, Metadata: null,
            ProvenanceRetrievability: (long)MemoryRetrievabilityProvenance.HalfLife));

        var writtenBack = await store.GetAsync("legacy", legacyId);
        Assert.NotNull(writtenBack);
        // sanity: confirms the row genuinely carries the 2.5.x provenance bit this fact is about, not a
        // default the store silently substituted
        Assert.Equal((long)MemoryRetrievabilityProvenance.HalfLife, writtenBack!.ProvenanceRetrievability);

        // Age it through the ENGINE, the 3.0 way — a bare constructor with no `policy:` argument, exactly as
        // a consumer who never touched IMemoryRetrievabilityPolicy would build one, so nothing about this
        // path "knows" the row predates DSR.
        var engine = new GraphMemoryEngine("legacy", store, agePolicies: [new PerWriteAgePolicy()]);
        for (var i = 0; i < stability; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"filler{i} advances the position"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "old curve"));
        var item = Assert.Single(recalled.Items);

        Assert.True(double.IsFinite(item.Retrievability), $"non-finite retrievability: {item.Retrievability}");
        Assert.InRange(item.Retrievability, 0, 1);
        // THE unit-contract assertion: age == stability recomputes to r = 0.5 under DSR for a row DSR never
        // wrote itself.
        Assert.Equal(0.5, item.Retrievability, precision: 6);
    }

    // ---- IMemoryRetrievabilityPolicy.DerivedGrade (2026-08-11, fsrs-properly plan Task 3): the seam a
    // review log reads to record what THIS reinforcement actually used, without re-deriving it from a
    // separately-computed retrievability that could drift from Reinforce's own internal one. ----

    /// <summary>Computed on the pre-state, independently, from the documented formula
    /// (<c>g = 2 + 2·r</c>) — never by calling <c>DerivedGrade</c> a second time, so this does not just
    /// check that the member agrees with itself.</summary>
    [Fact]
    public void DerivedGrade_matches_the_documented_formula_on_the_pre_reinforcement_state()
    {
        var policy = new DsrRetrievability();
        var state = new MemoryDecayState(Age: 45, RecallCount: 0, Stability: 20, Difficulty: 3);

        var grade = policy.DerivedGrade(state);

        Assert.NotNull(grade);
        var expected = 2 + 2 * policy.Retrievability(state);
        Assert.Equal(expected, grade!.Value, precision: 9);
    }

    /// <summary>The same Δt=0 branch <see cref="A_session_burst_with_no_intervening_write_does_not_move_difficulty"/>
    /// pins for <c>Reinforce</c> itself, now for the seam a review log reads: null must mean "no grade was
    /// used," not "retrievability happened to be exactly 1."</summary>
    [Fact]
    public void DerivedGrade_is_null_on_a_same_position_review()
    {
        var policy = new DsrRetrievability();
        var state = new MemoryDecayState(Age: 0, RecallCount: 0, Stability: 20, Difficulty: 5);

        Assert.Null(policy.DerivedGrade(state));
    }

    /// <summary>The load-bearing guarantee <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/>'s own review
    /// log depends on: calling <c>DerivedGrade</c> on the SAME state <c>Reinforce</c> is about to receive
    /// yields the value <c>Reinforce</c> uses internally to update <see cref="MemoryDecayState.Difficulty"/>
    /// — checked here by reconstructing <c>NextDifficulty</c>'s own damping+reversion law from
    /// <c>DerivedGrade</c>'s return and comparing against <c>Reinforce</c>'s actual output, rather than
    /// trusting that the two "happen to agree."</summary>
    [Fact]
    public void DerivedGrade_is_the_exact_value_Reinforce_uses_to_update_difficulty()
    {
        var options = new DsrOptions { DifficultyChangeWeight = 0.5, DifficultyReversionWeight = 0 };
        var policy = new DsrRetrievability(options);
        var state = new MemoryDecayState(Age: 200, RecallCount: 0, Stability: 20, Difficulty: 1);

        var grade = policy.DerivedGrade(state);
        Assert.NotNull(grade);

        var delta = -options.DifficultyChangeWeight * (grade!.Value - 3);
        var expectedDifficulty = Math.Clamp(1 + (10 - 1) * delta / 9, 1, 10);

        Assert.Equal(expectedDifficulty, policy.Reinforce(state).Difficulty, precision: 9);
    }
}
