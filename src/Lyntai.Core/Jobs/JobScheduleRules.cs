using System.Globalization;

namespace Lyntai.Jobs;

/// <summary>What makes a <see cref="JobSchedule"/> valid, and the key-value keys the scheduler and the schedule store
/// share — one place, so the builder, the store and the scheduler cannot disagree.</summary>
internal static class JobScheduleRules
{
    /// <summary>A schedule's definition in <see cref="KeyValueJobScheduleStore"/>.</summary>
    public const string DefinitionPrefix = "lyntai:job-schedule:";

    public static string DefinitionKey(string name) => DefinitionPrefix + name;

    /// <summary>The scheduler's persisted next run.</summary>
    public static string NextRunKey(string name) => $"lyntai:schedule:{name}";

    /// <summary>The trigger the persisted next run was computed from — the scheduler re-anchors when it changes.</summary>
    public static string TriggerKey(string name) => $"lyntai:schedule-trigger:{name}";

    /// <summary>The trigger as one comparable string: the cron text, else the interval in ticks.</summary>
    public static string Fingerprint(JobSchedule schedule) =>
        schedule.Cron ?? "every:" + schedule.Interval!.Value.Ticks.ToString(CultureInfo.InvariantCulture);

    /// <exception cref="ArgumentException">The name is blank, or the schedule sets both triggers or neither.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="FormatException">The cron does not parse, or parses and never occurs (Feb 30).</exception>
    public static void Validate(JobSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Name, nameof(schedule));
        if ((schedule.Interval is null) == (schedule.Cron is null))
            throw new ArgumentException(
                $"Schedule '{schedule.Name}' must set exactly one of Interval or Cron.", nameof(schedule));
        if (schedule.Cron is { } cron) RequireOccurrence(schedule.Name, CronExpression.Parse(cron), cron);
        else if (schedule.Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Interval,
                $"Schedule '{schedule.Name}' needs a positive Interval.");
    }

    // Cron has no year, so an expression that matches at all matches within 8 years of ANY instant (the longest gap between
    // two Feb 29ths): the epoch stands for every start, and one that never occurs is refused whatever the clock reads.
    private static void RequireOccurrence(string name, CronExpression parsed, string cron)
    {
        try { _ = parsed.Next(DateTimeOffset.UnixEpoch); }
        catch (InvalidOperationException)
        {
            throw new FormatException($"Schedule '{name}': cron '{cron}' never occurs (a day that no listed month has?).");
        }
    }
}
