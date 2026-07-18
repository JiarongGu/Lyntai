namespace Lyntai.Jobs;

/// <summary>A recurring job: the scheduler enqueues a job (<see cref="Lane"/> / <see cref="Type"/> /
/// <see cref="Payload"/> at <see cref="Priority"/>) on a schedule — either a fixed <see cref="Interval"/>
/// OR a <see cref="Cron"/> expression (set exactly one; <see cref="Cron"/> wins if both are set). Registered
/// with <c>builder.AddJobSchedule</c>/<c>AddCronSchedule</c>; the app drives <see cref="IJobScheduler"/>
/// (host-free). The next-run time is persisted keyed by <see cref="Name"/>, so it must be STABLE + UNIQUE
/// across schedules.</summary>
public sealed record JobSchedule(
    string Name, string Lane, string Type, string Payload, TimeSpan? Interval = null, int Priority = 0, string? Cron = null);
