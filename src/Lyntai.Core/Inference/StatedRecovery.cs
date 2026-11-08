using System.Globalization;
using System.Text.RegularExpressions;
using Lyntai.Agents;

namespace Lyntai.Inference;

/// <summary>A recovery time a failure SENTENCE states, as the two grammars measured so far write it: Codex's
/// <c>Try again at 4:05 PM.</c> / <c>Try again at Oct 3rd, 2026 4:05 PM.</c> (the machine's local time, no zone) and
/// Claude Code's <c>resets 3:45pm</c> / <c>resets Mon 12:00am</c> / <c>resets 3pm (Europe/London)</c>, plus a
/// service's <c>retry after 60s</c>. Anything it cannot read exactly is null — a deadline is a stated fact or
/// it is unknown, never a guess.</summary>
internal static partial class StatedRecovery
{
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    [GeneratedRegex(@"retry[\s-]?after\s+(?<n>\d{1,6})\s*(?<u>m(?:in(?:ute)?s?)?|s(?:ec(?:ond)?s?)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RetryAfter();

    [GeneratedRegex(@"(?:resets?|try\s+again)\s+(?:at\s+)?(?:(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s+(?<year>\d{4})\s+|(?<dow>sun|mon|tue|wed|thu|fri|sat)[a-z]*\.?,?\s+)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>[ap])\.?m\b\.?(?:\s*\((?<zone>[^)]+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ClockTime();

    /// <summary>The recovery the text states, or null when it states none this can read. A time it reads carries its
    /// <see cref="RestrictionRecovery.Stated"/> parts beside the resolution; one whose named zone this machine lacks is
    /// <see cref="RecoveryKind.Unknown"/> with the parts, never null.</summary>
    public static RestrictionRecovery? Read(string text, TimeProvider time)
    {
        if (RetryAfter().Match(text) is { Success: true } retry)
        {
            var n = int.Parse(retry.Groups["n"].Value, CultureInfo.InvariantCulture);
            var span = char.ToLowerInvariant(retry.Groups["u"].Value[0]) == 'm' ? TimeSpan.FromMinutes(n) : TimeSpan.FromSeconds(n);
            return RestrictionRecovery.RetryNotBefore(time.GetUtcNow() + span) with
            {
                Stated = new StatedRecoveryParts(StatedTimeShape.Wait) { Wait = span },
            };
        }

        return ClockTime().Match(text) is { Success: true } m ? ReadClock(m, time) : null;
    }

    private static RestrictionRecovery? ReadClock(Match m, TimeProvider time)
    {
        var hour12 = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour12 is < 1 or > 12 || minute > 59) return null;
        var hour = hour12 % 12 + (char.ToLowerInvariant(m.Groups["ap"].Value[0]) == 'p' ? 12 : 0);

        var zone = time.LocalTimeZone;
        var named = m.Groups["zone"] is { Success: true } said ? said.Value.Trim() : null;
        if (named is not null)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(named); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return new RestrictionRecovery(RecoveryKind.Unknown) { Stated = Parts(m, hour, minute, named, true) };
            }
        }

        var parts = Parts(m, hour, minute, named ?? zone.Id, named is not null);
        return ResetMoment(m, hour, minute, zone, time) is { } at ? RestrictionRecovery.ResetAt(at) with { Stated = parts } : null;
    }

    private static StatedRecoveryParts Parts(Match m, int hour, int minute, string zone, bool named)
    {
        var shape = m.Groups["year"].Success ? StatedTimeShape.DateAndTime
            : m.Groups["dow"].Success ? StatedTimeShape.WeekdayAndTime
            : StatedTimeShape.TimeOfDay;
        return new StatedRecoveryParts(shape)
        {
            Date = shape == StatedTimeShape.DateAndTime ? StatedDate(m) : null,
            Weekday = shape == StatedTimeShape.WeekdayAndTime ? (DayOfWeek)Array.IndexOf(Days, m.Groups["dow"].Value[..3].ToLowerInvariant()) : null,
            Time = new TimeOnly(hour, minute),
            Zone = zone,
            ZoneNamed = named,
        };
    }

    // null for a date that is no date (Feb 30), which is a sentence this cannot read
    private static DateOnly? StatedDate(Match m)
    {
        var month = Array.IndexOf(Months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
        var year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
        return year is >= 1 and <= 9999 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
    }

    private static DateTimeOffset? ResetMoment(Match m, int hour, int minute, TimeZoneInfo zone, TimeProvider time)
    {
        var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        DateTime local;
        if (m.Groups["year"].Success)
        {
            if (StatedDate(m) is not { } date) return null;
            local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
        }
        else
        {
            // the next occurrence at or after now (a minute of skew is allowed), on the stated weekday if any
            local = now.Date.AddHours(hour).AddMinutes(minute);
            var weekday = m.Groups["dow"].Success
                ? Array.IndexOf(Days, m.Groups["dow"].Value[..3].ToLowerInvariant())
                : -1;
            var floor = now.DateTime.AddMinutes(-1);
            while (local < floor || (weekday >= 0 && (int)local.DayOfWeek != weekday))
                local = local.AddDays(1);
        }

        // a stated date at the calendar's edge has no UTC instant in this offset: unreadable, never a throw
        try { return new DateTimeOffset(local, zone.GetUtcOffset(local)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
