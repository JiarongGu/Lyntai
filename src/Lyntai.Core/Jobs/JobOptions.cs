namespace Lyntai.Jobs;

/// <summary>Durable-job tuning (on <see cref="LyntaiOptions.Jobs"/>). <see cref="LaneConcurrency"/> and
/// <see cref="MaxConcurrency"/> are <b>per-process</b>; <see cref="GlobalMaxConcurrency"/> bounds every
/// process sharing one store.</summary>
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

    /// <summary>A cap on concurrent jobs across EVERY process sharing this store (0 = unbounded, the
    /// default and the pre-3.0 behaviour). Where <see cref="MaxConcurrency"/> bounds one runner, this bounds
    /// the deployment: three workers with a global cap of 5 run five jobs between them, not fifteen.</summary>
    /// <remarks>Enforced by a shared slot table rather than by counting running jobs — a count cannot gate a
    /// claim without racing (<see cref="Lyntai.Storage.IJobStore.TryAcquireSlotAsync"/> says why, and why a
    /// row-per-slot is exact on every backend where a count is not).
    /// <para>A slot is held for the job's execution and released when it ends; a crashed worker's slot is
    /// reclaimed after <see cref="SlotLease"/> — which a live worker keeps renewing, so it needs no relation
    /// to how long a job runs.</para>
    /// <para>It costs one extra round-trip per job claimed, plus one heartbeat per
    /// <see cref="SlotLease"/>-third while any job is in flight, and only when set.</para></remarks>
    public int GlobalMaxConcurrency { get; set; }

    /// <summary>How long a held slot survives WITHOUT a heartbeat. Short on purpose: it measures how quickly
    /// a dead worker's slot returns to the pool, NOT how long a job may take.</summary>
    /// <remarks><b>Why this is separate from <see cref="Lease"/>.</b> One expiry cannot serve both
    /// questions. Tuned long enough for a job that legitimately runs for hours, a crashed worker throttles
    /// the whole deployment for hours; tuned short enough to recover quickly, that same long job loses its
    /// slot to another worker while it is still running. A live runner renews continuously (every third of
    /// this window), so expiry becomes purely "how long since we last heard from you" and a job may run as
    /// long as it likes.
    /// <para>Raise it if a runner's process is prone to long stalls — a full GC pause or a saturated thread
    /// pool can delay a heartbeat, and a missed one costs the slot rather than the job.</para></remarks>
    public TimeSpan SlotLease { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a claim stays valid; a Running job claimed longer ago than this is reclaimable
    /// (a crashed worker's job resumes from its checkpoint). Keep it comfortably above a job's expected
    /// runtime + its checkpoint cadence.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Idle poll interval for <see cref="IJobRunner.RunAsync"/> — only waited when a pass found
    /// no work (a productive pass immediately runs the next).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Attempt budget <see cref="IJobQueue"/> stamps onto a <see cref="JobSpec"/> that names none.
    /// Seeded from <see cref="JobSpec.DefaultMaxAttempts"/> — the same constant every store falls back to for
    /// a spec that reached it without passing through the queue, so the two can't disagree by default.</summary>
    public int DefaultMaxAttempts { get; set; } = JobSpec.DefaultMaxAttempts;

    /// <summary>Retry delay used when a handler returns <c>Retry()</c> with no explicit delay, or throws.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Max entries retained in a job's live step log (<c>ReportStepAsync</c>); older steps are
    /// dropped so a long-running job can't grow the row unbounded.</summary>
    public int MaxStepLog { get; set; } = Lyntai.Jobs.JobStepLog.DefaultCap;

    public int LimitFor(string lane) => LaneConcurrency.TryGetValue(lane, out var n) ? n : DefaultLaneConcurrency;
}
