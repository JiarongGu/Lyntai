using Lyntai.Memory;
using Lyntai.Memory.Forgetting;

namespace Lyntai.Tests.Memory;

/// <summary>The shared option-domain guard (<c>docs/DECISIONS.md</c> D78). Internal surface, reached
/// through <c>InternalsVisibleTo</c>.
/// <para>The facts worth pinning are not "does it throw" — the options records' own tests already cover
/// that per property. They are the two things the extraction is FOR: that the described domain and the
/// tested domain are the same domain, and that finiteness is asked separately from the bounds.</para></summary>
public class MemoryOptionTests
{
    [Fact]
    public void A_range_rejects_every_non_finite_value_whatever_its_bounds()
    {
        // The reason Contains asks IsFinite FIRST rather than relying on the comparisons: every comparison
        // against NaN is false, so `value < Min` alone ACCEPTS it. pitfalls.md §Storage records this exact
        // trap landing three times in one subsystem, twice in a file whose neighbour documents it.
        foreach (var range in AllRanges())
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.False(range.Contains(bad), $"{range.Describe()} must reject {bad}");
    }

    [Theory]
    // The zero-anchored shapes keep the English names every hand-written message already used, so the
    // extraction changed no consumer-visible phrasing for the thirty-one properties it replaced.
    [InlineData(0, false, double.PositiveInfinity, false, "a finite positive number")]
    [InlineData(0, true, double.PositiveInfinity, false, "a finite non-negative number")]
    [InlineData(double.NegativeInfinity, false, 0, false, "a finite negative number")]
    [InlineData(double.NegativeInfinity, false, double.PositiveInfinity, false, "a finite number")]
    // …and the bounded shapes render as intervals, with the bracket carrying the inclusivity.
    [InlineData(0, true, 1, true, "a finite number in [0, 1]")]
    [InlineData(0, true, 1, false, "a finite number in [0, 1)")]
    [InlineData(0, false, 1, true, "a finite number in (0, 1]")]
    [InlineData(1, true, 10, true, "a finite number in [1, 10]")]
    [InlineData(1, true, double.PositiveInfinity, false, "a finite number at or above 1")]
    public void A_range_describes_itself(double min, bool minIn, double max, bool maxIn, string expected)
    {
        // The negative case is here because it was WRONG on the first draft: an arm ordered after the
        // generic unbounded-lower case rendered it "a finite number below 0", which is true, reads oddly,
        // and silently replaced the phrase every Decay message had used for two releases.
        Assert.Equal(expected, new MemoryOptionRange(min, minIn, max, maxIn).Describe());
    }

    [Fact]
    public void The_described_domain_is_the_TESTED_domain_at_both_ends()
    {
        // The whole point of deriving the phrase. A property whose text says [0, 1] while its code tests
        // `< 0 || >= 1` compiles, passes, and misleads — which is reachable whenever the two are written
        // side by side, and unreachable once one produces the other.
        foreach (var range in AllRanges())
        {
            var text = range.Describe();

            if (double.IsFinite(range.Min))
                Assert.Equal(range.MinInclusive, range.Contains(range.Min));
            if (double.IsFinite(range.Max))
                Assert.Equal(range.MaxInclusive, range.Contains(range.Max));

            // A closed end renders with a square bracket and an open one with a round bracket, so the
            // rendering cannot claim an inclusivity the comparison does not honour.
            if (text.Contains(" in ", StringComparison.Ordinal))
            {
                Assert.Equal(range.MinInclusive, text.Contains('['));
                Assert.Equal(range.MaxInclusive, text.Contains(']'));
            }
        }
    }

    [Fact]
    public void A_rejected_value_is_named_by_its_PROPERTY_not_by_the_setter_parameter()
    {
        // The inconsistency the extraction closed: an inline `nameof(value)` inside an init accessor is the
        // literal string "value", so 21 of the 31 sites reported a ParamName no caller could act on, while
        // the ones routed through a file's own local helper reported the property name.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { Decay = 0 });

        Assert.Equal(nameof(DsrOptions.Decay), ex.ParamName);
        Assert.Contains("DsrOptions.Decay", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a finite negative number", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_survives_into_the_message_so_the_domain_alone_is_never_the_whole_answer()
    {
        // `why` is the one part that stays per-property, because it is the part a reader cannot derive from
        // the bounds. If it stopped reaching the message the guard would still be correct and would have
        // lost everything that made these messages worth writing.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new DsrOptions { MaxStability = 0 });

        var domain = ex.Message.IndexOf("a finite positive number", StringComparison.Ordinal);
        Assert.True(domain >= 0, ex.Message);
        Assert.True(ex.Message.Length > domain + 60, $"no reason followed the domain: {ex.Message}");
    }

    private static MemoryOptionRange[] AllRanges() =>
    [
        MemoryOptionRange.Finite, MemoryOptionRange.Positive, MemoryOptionRange.Negative,
        MemoryOptionRange.NonNegative, MemoryOptionRange.AtLeast(1), MemoryOptionRange.Closed(0, 1),
        MemoryOptionRange.Closed(1, 10), MemoryOptionRange.Closed(0, 700),
        MemoryOptionRange.FromInclusive(0, 1), MemoryOptionRange.ToInclusive(0, 1),
    ];
}
