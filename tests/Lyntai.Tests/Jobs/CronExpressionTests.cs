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
        // from Saturday 2026-07-18, the next Monday midnight is two days on
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), Next("0 0 * * 1"));
    }

    [Fact]
    public void Dom_and_dow_both_restricted_is_an_OR()
    {
        // classic cron: "13th OR any Friday". The next Friday (07-24) comes before the next 13th (08-13); an
        // AND would wait for a Friday the 13th (2026-11-13), which the old containment check also accepted.
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero), Next("0 0 13 * 5"));
    }

    [Fact]
    public void Sunday_is_zero_or_seven()
    {
        var sunday = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(sunday, Next("0 0 * * 0"));
        Assert.Equal(sunday, Next("0 0 * * 7"));
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
        Assert.Throws<FormatException>(() => CronExpression.Parse(bad));
    }

    [Fact] // The impossible-cron error names the EXPRESSION (the thing to fix), not the search timestamp
    public void Impossible_cron_error_message_contains_the_expression()
    {
        var cron = CronExpression.Parse("0 0 30 2 *"); // Feb 30 — parseable but never occurs
        var ex = Assert.Throws<InvalidOperationException>(
            () => cron.Next(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Contains("0 0 30 2 *", ex.Message);
    }
}
