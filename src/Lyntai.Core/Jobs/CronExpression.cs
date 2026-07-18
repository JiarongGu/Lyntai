using System.Globalization;

namespace Lyntai.Jobs;

/// <summary>
/// A parsed 5-field Unix cron expression (<c>minute hour day-of-month month day-of-week</c>), evaluated in
/// UTC. Supports <c>*</c>, single values, ranges <c>a-b</c>, steps <c>*/n</c> and <c>a-b/n</c> and
/// <c>n/step</c>, and comma lists <c>a,b,c</c> per field; day-of-week is 0–6 (Sunday=0, and 7 also = Sunday);
/// and the macros <c>@hourly @daily @midnight @weekly @monthly @yearly/@annually</c>. Day-of-month and
/// day-of-week follow the standard cron rule: when BOTH are restricted (not <c>*</c>), a time matches if
/// EITHER matches. Dependency-free — no NuGet cron library pulled into Core.
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] _minute; // [0..59]
    private readonly bool[] _hour;   // [0..23]
    private readonly bool[] _dom;    // [1..31]
    private readonly bool[] _month;  // [1..12]
    private readonly bool[] _dow;    // [0..6]
    private readonly bool _domRestricted;
    private readonly bool _dowRestricted;

    private CronExpression(bool[] minute, bool[] hour, bool[] dom, bool[] month, bool[] dow, bool domR, bool dowR)
    {
        _minute = minute; _hour = hour; _dom = dom; _month = month; _dow = dow;
        _domRestricted = domR; _dowRestricted = dowR;
    }

    public static CronExpression Parse(string expression)
    {
        var expr = (expression ?? "").Trim();
        expr = expr.ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            "@yearly" or "@annually" => "0 0 1 1 *",
            _ => expr,
        };

        var f = expr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5)
            throw new FormatException($"cron must have 5 fields (min hour dom month dow), got {f.Length}: '{expression}'");

        return new CronExpression(
            Field(f[0], 0, 59),
            Field(f[1], 0, 23),
            Field(f[2], 1, 31),
            Field(f[3], 1, 12),
            Field(f[4], 0, 6, sundayIsSeven: true),
            domR: f[2] != "*",
            dowR: f[4] != "*");
    }

    /// <summary>The next UTC minute STRICTLY after <paramref name="after"/> that matches, as a
    /// <see cref="DateTimeOffset"/> at offset zero.</summary>
    public DateTimeOffset Next(DateTimeOffset after)
    {
        var u = after.UtcDateTime;
        var t = new DateTime(u.Year, u.Month, u.Day, u.Hour, u.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
        var limit = t.AddYears(5); // bound: an impossible expression (e.g. Feb 30) throws rather than spinning
        while (t < limit)
        {
            if (!_month[t.Month]) { t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1); continue; }
            if (!DayMatches(t)) { t = t.Date.AddDays(1); continue; }
            if (!_hour[t.Hour]) { t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc).AddHours(1); continue; }
            if (!_minute[t.Minute]) { t = t.AddMinutes(1); continue; }
            return new DateTimeOffset(t, TimeSpan.Zero);
        }
        throw new InvalidOperationException($"cron '{after}' has no occurrence within 5 years — impossible expression?");
    }

    private bool DayMatches(DateTime t)
    {
        var dom = _dom[t.Day];
        var dow = _dow[(int)t.DayOfWeek];
        // standard cron: both restricted → OR; otherwise the unrestricted field is '*' (always true), so AND
        return _domRestricted && _dowRestricted ? dom || dow : dom && dow;
    }

    private static bool[] Field(string spec, int min, int max, bool sundayIsSeven = false)
    {
        var set = new bool[max + 1];
        foreach (var part in spec.Split(','))
        {
            var body = part;
            var step = 1;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                body = part[..slash];
                step = int.Parse(part[(slash + 1)..], CultureInfo.InvariantCulture);
                if (step <= 0) throw new FormatException($"cron step must be positive: '{part}'");
            }

            int lo, hi;
            if (body == "*") { lo = min; hi = max; }
            else if (body.Contains('-'))
            {
                var r = body.Split('-');
                lo = int.Parse(r[0], CultureInfo.InvariantCulture);
                hi = int.Parse(r[1], CultureInfo.InvariantCulture);
            }
            else
            {
                lo = int.Parse(body, CultureInfo.InvariantCulture);
                hi = slash >= 0 ? max : lo; // "n/step" means from n to max by step; bare "n" is just n
            }

            for (var v = lo; v <= hi; v += step)
            {
                var value = sundayIsSeven && v == 7 ? 0 : v;
                if (value < min || value > max)
                    throw new FormatException($"cron value {v} out of range [{min},{max}] in '{spec}'");
                set[value] = true;
            }
        }
        return set;
    }
}
