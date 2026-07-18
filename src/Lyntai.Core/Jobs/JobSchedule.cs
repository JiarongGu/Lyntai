namespace Lyntai.Jobs;

/// <summary>A recurring job: every <see cref="Interval"/>, the scheduler enqueues a job
/// (<see cref="Lane"/> / <see cref="Type"/> / <see cref="Payload"/> at <see cref="Priority"/>). Registered
/// with <c>builder.AddJobSchedule(...)</c>; the app drives <see cref="IJobScheduler"/> (host-free). The
/// next-run time is persisted keyed by <see cref="Name"/>, so it must be STABLE + UNIQUE across schedules.</summary>
public sealed record JobSchedule(string Name, string Lane, string Type, string Payload, TimeSpan Interval, int Priority = 0);
