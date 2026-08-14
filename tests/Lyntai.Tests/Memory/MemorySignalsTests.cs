using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The open signal bag. Its whole job is to make a future retention dimension a registration
/// rather than another breaking widening of <see cref="MemoryDecayState"/>, so the facts that matter are:
/// a default instance is usable, an absent signal yields the caller's fallback rather than throwing,
/// adding one never mutates a value someone else is holding, and two bags built from identical content
/// compare equal however they were assembled — <c>FrozenDictionary</c> gives no equality of its own, so
/// this last one is not free.</summary>
public class MemorySignalsTests
{
    [Fact]
    public void A_default_instance_is_usable_and_reports_nothing()
    {
        // `default(MemorySignals)` is what every existing MemoryDecayState construction produces, so this
        // must not throw — a NullReferenceException here would surface as "decay stopped working"
        MemorySignals signals = default;

        Assert.Equal(0, signals.Count);
        Assert.Equal(0, signals.Get(MemorySignals.WellKnown.Salience));
        Assert.Equal(1.5, signals.Get("absent", fallback: 1.5));
    }

    [Fact]
    public void With_returns_a_new_bag_and_leaves_the_original_alone()
    {
        var original = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);

        var extended = original.With("novelty", 0.25);

        Assert.Equal(2, extended.Get(MemorySignals.WellKnown.Salience));
        Assert.Equal(0.25, extended.Get("novelty"));
        Assert.Equal(0, original.Get("novelty")); // the original is unchanged
        Assert.Equal(1, original.Count);
    }

    [Fact]
    public void With_replaces_a_value_rather_than_duplicating_the_key()
    {
        var signals = MemorySignals.Empty.With("s", 1).With("s", 3);

        Assert.Equal(3, signals.Get("s"));
        Assert.Equal(1, signals.Count);
    }

    [Fact]
    public void With_works_on_a_genuine_default_not_just_Empty()
    {
        // every legacy MemoryDecayState carries the null backing store `default` produces, not `Empty`'s
        // real (if zero-length) one — `With` has to handle both identically
        MemorySignals uninitialized = default;

        var extended = uninitialized.With("x", 1);

        Assert.Equal(1, extended.Get("x"));
        Assert.Equal(1, extended.Count);
        Assert.Equal(0, uninitialized.Count); // the original default is still untouched
    }

    [Fact]
    public void From_dedups_last_wins_and_empty_input_yields_an_empty_bag()
    {
        var signals = MemorySignals.From([
            new("x", 1),
            new("x", 2), // last wins
            new("y", 3),
        ]);

        Assert.Equal(2, signals.Get("x"));
        Assert.Equal(3, signals.Get("y"));
        Assert.Equal(2, signals.Count);

        Assert.Equal(0, MemorySignals.From([]).Count);
    }

    [Fact]
    public void Values_exposes_every_signal_for_persistence()
    {
        var signals = MemorySignals.Empty.With("x", 1).With("y", 2);

        Assert.Equal(2, signals.Values.Count);
        Assert.Equal(1, signals.Values["x"]);
        Assert.Equal(2, signals.Values["y"]);
        Assert.Empty(MemorySignals.Empty.Values);
    }

    [Fact]
    public void Two_bags_with_the_same_content_are_equal_however_they_were_built()
    {
        var a = MemorySignals.Empty.With("x", 1).With("y", 2);
        var b = MemorySignals.Empty.With("y", 2).With("x", 1); // built in the other order

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(new MemoryDecayState(1, 0, 20, 0, 0, a), new MemoryDecayState(1, 0, 20, 0, 0, b));
    }

    [Fact]
    public void Bags_differing_in_a_value_or_a_key_are_not_equal()
    {
        var baseline = MemorySignals.Empty.With("x", 1);

        Assert.NotEqual(baseline, MemorySignals.Empty.With("x", 2));
        Assert.NotEqual(baseline, MemorySignals.Empty.With("z", 1));
        Assert.NotEqual(baseline, MemorySignals.Empty.With("x", 1).With("y", 1));
    }

    [Fact]
    public void A_default_bag_equals_an_empty_one_and_itself()
    {
        MemorySignals uninitialized = default;

        Assert.Equal(uninitialized, MemorySignals.Empty);
        Assert.Equal(uninitialized, uninitialized);
        Assert.Equal(uninitialized.GetHashCode(), MemorySignals.Empty.GetHashCode());
    }

    [Fact]
    public void A_bag_holding_NaN_still_equals_itself()
    {
        // `==` on double makes NaN unequal to itself, which would break Equals' required reflexivity
        var withNaN = MemorySignals.Empty.With("broken", double.NaN);

        Assert.Equal(withNaN, MemorySignals.Empty.With("broken", double.NaN));
    }

    [Theory]
    [InlineData(double.NaN, 1)]                  // Math.Max(1, NaN) is NaN (IEEE 754) — the poisoning case
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, 1)]
    [InlineData(0.5, 1)]                         // below neutral is not "less findable than unjudged"
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]                           // and a legitimate value is passed straight through
    [InlineData(2.5, 2.5)]
    [InlineData(1e9, 1e9)]                       // no ceiling here: bounding is SalienceRetentionPolicy's job
    public void Salience_coerces_the_values_that_would_poison_a_reader(double stored, double expected)
    {
        // ONE definition of the coercion, because four read sites once had three different rules — the two
        // SQL stores' promoted column, the in-process store's admission ordering, and the engine's rank
        // boost. Both coerced inputs are reachable through the public IMemorySaliencePolicy seam.
        Assert.Equal(expected,
            MemorySignals.Salience(MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, stored)));
    }

    [Fact]
    public void Salience_of_an_unjudged_bag_is_the_neutral_value()
    {
        // including `default`, which is every entry written before signals existed
        Assert.Equal(1, MemorySignals.Salience(MemorySignals.Empty));
        Assert.Equal(1, MemorySignals.Salience(default));
        Assert.Equal(1, MemorySignals.Salience(MemorySignals.Empty.With("some.other.signal", 9)));
    }

    [Theory]
    // Non-finite is NOT an explicit judgement — it means no real information was supplied, so it reads as
    // the NEUTRAL value (5, the mid-point — corrected 2026-08-11 from the floor 1; see
    // DsrOptions.NeutralDifficulty's own remarks for why "no information" and "known to be trivial" must
    // not share a value). Math.Clamp PROPAGATES NaN, which is why the finiteness check has to come first.
    [InlineData(double.NaN, 5)]
    [InlineData(double.PositiveInfinity, 5)]
    [InlineData(double.NegativeInfinity, 5)]
    // These four ARE explicit, finite judgements, clamped into the scale's own bounds exactly as before —
    // unaffected by the neutral correction above, which only changes what "no information" means.
    [InlineData(0, 1)]                           // below the scale clamps to the floor (trivial), not neutral
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(4.5, 4.5)]                       // a legitimate value passes straight through
    [InlineData(50, 10)]                         // above the scale reads as hardest
    public void Difficulty_coerces_onto_the_documented_one_to_ten_scale(double stored, double expected)
    {
        // WellKnown.Difficulty states this rule as a property of the SIGNAL, so it must live on the signal's
        // own type — the exact discipline MemorySignals.Salience exists to enforce after the same value was
        // once normalized three different ways at three read sites.
        Assert.Equal(expected,
            MemorySignals.Difficulty(MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, stored)));
    }

    [Fact]
    public void Difficulty_of_an_unjudged_bag_is_the_neutral_value()
    {
        // nothing judges difficulty yet, so the overwhelmingly common bag is one that never mentions it.
        // The neutral value is 5 (the mid-point), not 1 (the floor) — corrected 2026-08-11; see
        // DsrOptions.NeutralDifficulty's own remarks for why "absent" and "known to be trivial" must not
        // share a value.
        Assert.Equal(5, MemorySignals.Difficulty(MemorySignals.Empty));
        Assert.Equal(5, MemorySignals.Difficulty(default));
        Assert.Equal(5, MemorySignals.Difficulty(MemorySignals.Empty.With("some.other.signal", 9)));
    }

    [Fact]
    public void A_decay_state_carries_signals_and_defaults_them_to_empty()
    {
        // the widening must be source-compatible: every existing 3- and 5-argument construction still works
        var withoutSignals = new MemoryDecayState(Age: 10, RecallCount: 0, Stability: 20);
        var withSignals = new MemoryDecayState(10, 0, 20, 0, 0,
            MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 4));

        Assert.Equal(0, withoutSignals.Signals.Count);
        Assert.Equal(4, withSignals.Signals.Get(MemorySignals.WellKnown.Salience));
    }
}
