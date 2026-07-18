using Lyntai.Jobs;

namespace Lyntai.Tests.Jobs;

/// <summary>The dependency-free 5-field cron parser + next-occurrence math, evaluated in UTC against a
/// fixed reference instant.</summary>
public class CronExpressionTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 18, 10, 30, 0, TimeSpan.Zero); // a Saturday

    private static DateTimeOffset Next(string cron) => CronExpression.Parse(cron).Next(At);

    [Fact]
    public void Every_minute_is_the_next_minute()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 10, 31, 0, TimeSpan.Zero), Next("* * * * *"));
    }

    [Fact]
    public void Hourly_is_the_next_top_of_hour()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 11, 0, 0, TimeSpan.Zero), Next("0 * * * *"));
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 11, 0, 0, TimeSpan.Zero), Next("@hourly"));
    }

    [Fact]
    public void Daily_at_nine_rolls_to_tomorrow_when_already_past()
    {
        // 10:30 is past 09:00, so the next 09:00 is the following day
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 9, 0, 0, TimeSpan.Zero), Next("0 9 * * *"));
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero), Next("@daily"));
    }

    [Fact]
    public void Step_every_fifteen_minutes()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 10, 45, 0, TimeSpan.Zero), Next("*/15 * * * *"));
    }

    [Fact]
    public void Next_is_strictly_after_even_when_the_reference_matches()
    {
        // 10:30 matches "30 10 * * *" exactly, but Next must be the FOLLOWING occurrence (tomorrow)
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 10, 30, 0, TimeSpan.Zero), Next("30 10 * * *"));
    }

    [Fact]
    public void Monthly_first_of_next_month()
    {
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Next("0 0 1 * *"));
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Next("@monthly"));
    }

    [Fact]
    public void Yearly_is_next_jan_first()
    {
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), Next("@yearly"));
    }

    [Fact]
    public void Range_and_list_hours()
    {
        // 9,12,15,18 at minute 0; from 10:30 the next is 12:00
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero), Next("0 9,12,15,18 * * *"));
        // business hours 9-17: from 10:30 the next :00 is 11:00
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 11, 0, 0, TimeSpan.Zero), Next("0 9-17 * * *"));
    }

    [Fact]
    public void Weekly_lands_on_the_named_weekday_at_midnight()
    {
        var next = Next("0 0 * * 1"); // Mondays 00:00
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.Equal(0, next.Hour);
        Assert.Equal(0, next.Minute);
        Assert.True(next > At);
    }

    [Fact]
    public void Dom_and_dow_both_restricted_is_an_OR()
    {
        // classic cron: "13th OR any Friday" — the result's day is 13 OR it's a Friday
        var next = Next("0 0 13 * 5");
        Assert.True(next.Day == 13 || next.DayOfWeek == DayOfWeek.Friday, $"got {next:o}");
        Assert.True(next > At);
    }

    [Fact]
    public void Sunday_is_zero_or_seven()
    {
        var byZero = Next("0 0 * * 0");
        var bySeven = Next("0 0 * * 7");
        Assert.Equal(DayOfWeek.Sunday, byZero.DayOfWeek);
        Assert.Equal(byZero, bySeven);
    }

    [Theory]
    [InlineData("* * * *")]        // too few fields
    [InlineData("* * * * * *")]    // too many fields
    [InlineData("60 * * * *")]     // minute out of range
    [InlineData("* 24 * * *")]     // hour out of range
    [InlineData("* * * * 9")]      // day-of-week out of range
    [InlineData("5-3 * * * *")]    // inverted range (would silently never fire)
    [InlineData("70/5 * * * *")]   // step base past max (empty set)
    [InlineData("* * 10-40 * *")]  // range upper bound out of [1,31]
    public void Malformed_expressions_throw(string bad)
    {
        Assert.ThrowsAny<Exception>(() => CronExpression.Parse(bad));
    }
}
