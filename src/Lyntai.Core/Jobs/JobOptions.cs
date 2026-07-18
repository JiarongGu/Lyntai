namespace Lyntai.Jobs;

/// <summary>Durable-job tuning (on <see cref="LyntaiOptions.Jobs"/>). Concurrency limits are
/// <b>per-process</b> (cross-process global limits are out of scope for now).</summary>
public sealed class JobOptions
{
    /// <summary>Max concurrent jobs per lane, per process. A lane not listed uses
    /// <see cref="DefaultLaneConcurrency"/>.</summary>
    public Dictionary<string, int> LaneConcurrency { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int DefaultLaneConcurrency { get; set; } = 1;

    /// <summary>A global cap on the size of one runner pass's concurrent batch, across ALL lanes (0 =
    /// unbounded, i.e. the sum of the active lanes' limits). The top-level throttle for parallel work —
    /// e.g. cap the concurrent agent runs a single process drives, regardless of lane spread; claiming is
    /// round-robin across lanes so no lane starves the cap. (It bounds the per-pass batch, which the runner
    /// awaits before the next pass — not a continuously-topped-up in-flight ceiling.)</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>How long a claim stays valid; a Running job claimed longer ago than this is reclaimable
    /// (a crashed worker's job resumes from its checkpoint). Keep it comfortably above a job's expected
    /// runtime + its checkpoint cadence.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Idle poll interval for <see cref="IJobRunner.RunAsync"/> — only waited when a pass found
    /// no work (a productive pass immediately runs the next).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int DefaultMaxAttempts { get; set; } = 3;

    /// <summary>Retry delay used when a handler returns <c>Retry()</c> with no explicit delay, or throws.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Max entries retained in a job's live step log (<c>ReportStepAsync</c>); older steps are
    /// dropped so a long-running job can't grow the row unbounded.</summary>
    public int MaxStepLog { get; set; } = Lyntai.Jobs.JobStepLog.DefaultCap;

    public int LimitFor(string lane) => LaneConcurrency.TryGetValue(lane, out var n) ? n : DefaultLaneConcurrency;
}
