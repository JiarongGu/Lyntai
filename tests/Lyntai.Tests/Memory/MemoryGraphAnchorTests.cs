using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Re-anchoring an imported age is one rule on every backend, and a portable file can carry any finite
/// number, so a hand-edited age must land as a clamped mark and never as an exception or a wrapped one.</summary>
public class MemoryGraphAnchorTests
{
    private static readonly DateTimeOffset Head = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_ordinary_age_sits_that_far_behind_the_head()
    {
        Assert.Equal(95, MemoryGraphAnchor.Behind(100L, 5.0));
        Assert.Equal(105, MemoryGraphAnchor.Behind(100L, -5.0));
        Assert.Equal(Head - TimeSpan.FromDays(2), MemoryGraphAnchor.Behind(Head, 2));
    }

    [Theory]
    [InlineData(1e19)]
    [InlineData(-1e19)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void A_count_age_past_the_range_is_clamped_so_a_head_can_still_be_subtracted_from_it(double age)
    {
        var mark = MemoryGraphAnchor.Behind(1_000L, age);

        // what a store computes on every read of the row: head - mark, which must not overflow
        _ = checked(1_000L - mark);
        _ = checked(mark - 1_000L);
    }

    [Theory]
    [InlineData(-1e300)]
    [InlineData(-3_000_000.0)]
    [InlineData(-3_700_000.0)]
    public void A_negative_age_in_days_past_the_range_is_clamped_to_the_latest_time_not_thrown(double days)
    {
        Assert.Equal(DateTimeOffset.MaxValue, MemoryGraphAnchor.Behind(Head, days));
    }

    [Theory]
    [InlineData(1e300)]
    [InlineData(4_000_000.0)]
    public void A_positive_age_in_days_past_the_range_is_clamped_to_the_earliest_time(double days)
    {
        Assert.Equal(DateTimeOffset.MinValue, MemoryGraphAnchor.Behind(Head, days));
    }
}
