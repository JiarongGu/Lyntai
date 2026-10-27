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

    /// <summary>The recovery the text states, or null when it states none this can read.</summary>
    public static RestrictionRecovery? Read(string text, TimeProvider time)
    {
        if (RetryAfter().Match(text) is { Success: true } retry)
        {
            var n = int.Parse(retry.Groups["n"].Value, CultureInfo.InvariantCulture);
            var span = char.ToLowerInvariant(retry.Groups["u"].Value[0]) == 'm' ? TimeSpan.FromMinutes(n) : TimeSpan.FromSeconds(n);
            return RestrictionRecovery.RetryNotBefore(time.GetUtcNow() + span);
        }

        return ClockTime().Match(text) is { Success: true } m && ResetMoment(m, time) is { } at
            ? RestrictionRecovery.ResetAt(at)
            : null;
    }

    private static DateTimeOffset? ResetMoment(Match m, TimeProvider time)
    {
        var zone = time.LocalTimeZone;
        if (m.Groups["zone"] is { Success: true } named)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(named.Value.Trim()); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
        }

        var hour12 = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour12 is < 1 or > 12 || minute > 59) return null;
        var hour = hour12 % 12 + (char.ToLowerInvariant(m.Groups["ap"].Value[0]) == 'p' ? 12 : 0);

        var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        DateTime local;
        try
        {
            if (m.Groups["year"].Success)
            {
                var month = Array.IndexOf(Months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
                local = new DateTime(int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture), month,
                    int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture), hour, minute, 0, DateTimeKind.Unspecified);
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
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
