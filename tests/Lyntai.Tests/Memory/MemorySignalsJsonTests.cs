using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The SQL backends' JSON materialization of <see cref="MemorySignals"/>. The load-bearing facts:
/// round-trip fidelity, defensive deserialize (a lost signal is recoverable, a lost memory is not), and —
/// what this file exists to pin — a non-finite value must not sink the whole write, even though
/// <see cref="MemorySignals"/> itself happily stores one (<c>MemorySignalsTests.A_bag_holding_NaN_still_equals_itself</c>),
/// so it is reachable through the public <c>IMemorySaliencePolicy</c>/<c>IMemoryRetentionPolicy</c> extension
/// points, not just a theoretical input.</summary>
public class MemorySignalsJsonTests
{
    [Fact]
    public void Round_trips_ordinary_values()
    {
        var signals = MemorySignals.Empty.With("a", 1).With("b", -2.5);

        var json = MemorySignalsJson.Serialize(signals);
        var back = MemorySignalsJson.Deserialize(json);

        Assert.Equal(1, back.Get("a"));
        Assert.Equal(-2.5, back.Get("b"));
        Assert.Equal(2, back.Count);
    }

    [Fact]
    public void An_empty_bag_serializes_to_null_not_an_empty_object()
    {
        Assert.Null(MemorySignalsJson.Serialize(MemorySignals.Empty));
    }

    [Fact]
    public void Null_blank_and_non_object_json_deserialize_to_an_empty_bag_rather_than_throwing()
    {
        Assert.Equal(0, MemorySignalsJson.Deserialize(null).Count);
        Assert.Equal(0, MemorySignalsJson.Deserialize("").Count);
        Assert.Equal(0, MemorySignalsJson.Deserialize("   ").Count);
        Assert.Equal(0, MemorySignalsJson.Deserialize("not json at all {{{").Count);
        Assert.Equal(0, MemorySignalsJson.Deserialize("[1,2,3]").Count); // an array, not an object
        Assert.Equal(0, MemorySignalsJson.Deserialize("42").Count); // a bare number
    }

    [Fact]
    public void A_non_numeric_member_is_skipped_rather_than_sinking_the_whole_bag()
    {
        var back = MemorySignalsJson.Deserialize("""{"good": 1, "bad": "not a number"}""");

        Assert.Equal(1, back.Count);
        Assert.Equal(1, back.Get("good"));
    }

    /// <summary>The regression this file exists to pin: before this fix, a NaN/Infinity signal made
    /// <c>Utf8JsonWriter.WriteNumber</c> throw <see cref="ArgumentException"/> — uncaught, that failed the
    /// ENTIRE write (the whole memory, not just the one signal), which is exactly backwards for a value the
    /// codec is supposed to treat as recoverable.</summary>
    [Fact]
    public void A_non_finite_value_is_skipped_rather_than_throwing()
    {
        var signals = MemorySignals.Empty.With("good", 3).With("broken", double.NaN)
            .With("also-broken", double.PositiveInfinity);

        var json = MemorySignalsJson.Serialize(signals);

        Assert.NotNull(json);
        var back = MemorySignalsJson.Deserialize(json);
        Assert.Equal(1, back.Count);
        Assert.Equal(3, back.Get("good"));
    }

    /// <summary>The empty-bag-is-null convention holds even when EVERY member is non-finite — the result
    /// must not become the one case that serializes to <c>"{}"</c>.</summary>
    [Fact]
    public void A_bag_of_only_non_finite_values_serializes_to_null()
    {
        var signals = MemorySignals.Empty.With("broken", double.NaN).With("also-broken", double.NegativeInfinity);

        Assert.Null(MemorySignalsJson.Serialize(signals));
    }

    [Fact]
    public void Unicode_keys_and_extreme_magnitudes_round_trip_exactly()
    {
        var signals = MemorySignals.Empty.With("unicode-名前", 3).With("very.small", 0.000001);

        var back = MemorySignalsJson.Deserialize(MemorySignalsJson.Serialize(signals));

        Assert.Equal(3, back.Get("unicode-名前"), 9);
        Assert.Equal(0.000001, back.Get("very.small"), 9);
    }
}
