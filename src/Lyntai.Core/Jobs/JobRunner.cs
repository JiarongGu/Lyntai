using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Jobs;

/// <summary>
/// Default <see cref="IJobRunner"/>. Per pass it claims a bounded set of jobs across all active lanes
/// (each lane capped by its limit, the whole set by the global <see cref="JobOptions.MaxConcurrency"/>)
/// and runs them ALL concurrently — including across lanes — so parallel work (e.g. many agent runs) truly
/// runs in parallel, with the per-lane + global limits as the control knobs. Scale further by running
/// several runner instances (one process or many): each has a distinct worker id and the store's atomic
/// claim hands every job to exactly one. There is deliberately NO count-then-claim gate (it would race);
/// the atomic claim is the mutual exclusion. Outcome mapping: Complete → done; Retry → requeue with
/// backoff up to max attempts, else Failed; Fail → terminal; a thrown handler exception → transient retry
/// up to max. Writes are fenced by the worker id; a write the store ignores (lease lost) is logged and the
/// job abandoned.
/// </summary>
public sealed class JobRunner(
    IJobStore? store,
    IJobHandlerRegistry handlers,
    LyntaiOptions options,
    ILogger<JobRunner>? logger = null,
    Func<DateTimeOffset>? clock = null) : IJobRunner
{
    private readonly IJobStore _store = store ?? throw new InvalidOperationException(
        "Durable jobs require a storage backend — call UseSqliteStorage / UsePostgresStorage / UseInMemoryStorage.");
    private readonly ILogger _logger = logger ?? NullLogger<JobRunner>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly string _workerId = Guid.NewGuid().ToString("N");

    private JobOptions Opts => options.Jobs;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        // Claim a bounded set across ALL active lanes (each capped by its lane limit, the whole set capped
        // by the global MaxConcurrency), then run every claimed job CONCURRENTLY — including across lanes,
        // so parallel work (e.g. many agent runs) actually runs in parallel, with the per-lane + global
        // limits as the control logic. Claims are quick; the atomic claim is the cross-worker mutual
        // exclusion, so there's no count-then-claim race.
        var lanes = await _store.ActiveLanesAsync(ct).ConfigureAwait(false);
        var cap = Opts.MaxConcurrency; // 0 = unbounded
        var claimed = new List<JobRecord>();
        foreach (var lane in lanes)
        {
            var laneLimit = Opts.LimitFor(lane);
            for (var i = 0; i < laneLimit; i++)
            {
                if (cap > 0 && claimed.Count >= cap) break;
                var job = await _store.ClaimNextAsync(lane, _workerId, Opts.Lease, ct).ConfigureAwait(false);
                if (job is null) break; // this lane is drained for now
                claimed.Add(job);
            }
            if (cap > 0 && claimed.Count >= cap) break;
        }
        if (claimed.Count == 0) return 0;
        await Task.WhenAll(claimed.Select(j => RunJobAsync(j, ct))).ConfigureAwait(false);
        return claimed.Count;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            int ran;
            try
            {
                ran = await RunOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "job runner pass failed; continuing after the poll interval");
                ran = 0;
            }
            if (ran == 0)
            {
                try { await Task.Delay(Opts.PollInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunJobAsync(JobRecord job, CancellationToken ct)
    {
        var handler = handlers.Find(job.Type);
        if (handler is null)
        {
            _logger.LogWarning("no handler registered for job type '{Type}' (job {Id}) — failing it", job.Type, job.Id);
            await _store.FailAsync(job.Id, _workerId, $"no handler registered for type '{job.Type}'", ct: ct).ConfigureAwait(false);
            return;
        }

        var ctx = new JobContext(job.Id, job.Payload, job.Checkpoint, job.Attempts,
            (cp, c) => _store.SaveCheckpointAsync(job.Id, _workerId, cp, c));

        try
        {
            var outcome = await handler.HandleAsync(ctx, ct).ConfigureAwait(false);
            await ApplyAsync(job, outcome, outcome.Error, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutdown mid-job: leave it Running; its lease lapses and another worker resumes it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "job {Id} ('{Type}') threw; scheduling a retry", job.Id, job.Type);
            await ApplyAsync(job, JobOutcome.Retry(), ex.Message, ct).ConfigureAwait(false); // a throw is transient
        }
    }

    private async Task ApplyAsync(JobRecord job, JobOutcome outcome, string? error, CancellationToken ct)
    {
        var ok = outcome.Result switch
        {
            JobOutcome.Kind.Complete => await _store.CompleteAsync(job.Id, _workerId, ct).ConfigureAwait(false),
            JobOutcome.Kind.Retry when job.Attempts < job.MaxAttempts =>
                await _store.FailAsync(job.Id, _workerId, error ?? "retrying",
                    _clock() + (outcome.RetryDelay ?? Opts.RetryBackoff), ct).ConfigureAwait(false),
            JobOutcome.Kind.Retry => // attempts exhausted → terminal
                await _store.FailAsync(job.Id, _workerId, error ?? "retries exhausted", ct: ct).ConfigureAwait(false),
            _ => await _store.FailAsync(job.Id, _workerId, error ?? "failed", ct: ct).ConfigureAwait(false), // Fail
        };
        if (!ok)
            _logger.LogWarning("job {Id} outcome ignored — lease lost (re-claimed by another worker)", job.Id);
    }
}
