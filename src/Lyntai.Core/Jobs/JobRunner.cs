using System.Diagnostics;
using Lyntai.Diagnostics;
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
/// backoff up to max attempts, else DEAD-LETTERED (inspectable/replayable); Fail → terminal Failed; a
/// thrown handler exception → transient retry up to max. Writes are fenced by the worker id; a write the
/// store ignores (lease lost) is logged and the
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
    private int _rotation; // rotates the lane start each pass so no lane is perpetually first under the cap

    private JobOptions Opts => options.Jobs;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        // Claim a bounded set across ALL active lanes and run them CONCURRENTLY — including across lanes —
        // with the per-lane + global MaxConcurrency limits as the control logic. Claiming is ROUND-ROBIN
        // (one job per lane per round, rotating the start each pass), so when the global cap binds no lane
        // starves. Claims are quick; the atomic claim is the cross-worker mutual exclusion (no
        // count-then-claim race).
        var lanes = await _store.ActiveLanesAsync(ct).ConfigureAwait(false);
        if (lanes.Count == 0) return 0;

        var offset = Interlocked.Increment(ref _rotation) % lanes.Count;
        var ordered = offset == 0 ? lanes : [.. lanes.Skip(offset), .. lanes.Take(offset)];

        var cap = Opts.MaxConcurrency; // 0 = unbounded
        var remaining = ordered.ToDictionary(l => l, Opts.LimitFor, StringComparer.Ordinal);
        var claimed = new List<JobRecord>();
        bool progressed;
        do
        {
            progressed = false;
            foreach (var lane in ordered)
            {
                if (cap > 0 && claimed.Count >= cap) break;
                if (remaining[lane] <= 0) continue;
                var job = await _store.ClaimNextAsync(lane, _workerId, Opts.Lease, ct).ConfigureAwait(false);
                if (job is null) { remaining[lane] = 0; continue; } // this lane is drained for now
                claimed.Add(job);
                remaining[lane]--;
                progressed = true;
            }
        }
        while (progressed && (cap <= 0 || claimed.Count < cap));

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
        using var activity = LyntaiDiagnostics.StartJob(job.Lane, job.Type, job.Id, job.Attempts);
        var start = Stopwatch.GetTimestamp();
        var outcome = "failed"; // pessimistic default so an unexpected path is recorded as a failure
        try
        {
            var handler = handlers.Find(job.Type);
            if (handler is null)
            {
                _logger.LogWarning("no handler registered for job type '{Type}' (job {Id}) — failing it", job.Type, job.Id);
                await _store.FailAsync(job.Id, _workerId, $"no handler registered for type '{job.Type}'", ct: ct).ConfigureAwait(false);
                return;
            }

            // a cancel could already be pending on this record (e.g. it was requested, then the worker
            // crashed and a stale-lease reclaim handed it here) — don't run it, just finalize the cancel
            if (job.CancelRequested)
            {
                await _store.CancelRunningAsync(job.Id, _workerId, ct).ConfigureAwait(false);
                outcome = "cancelled";
                return;
            }

            // poison-pill bound: MaxAttempts is otherwise enforced only in ApplyAsync (runs when the handler
            // returns/throws). A handler that CRASHES the worker never reaches it, so the stale-lease reclaim
            // — which increments attempts on every claim — would re-run it forever. Trip the bound at claim
            // time: past MaxAttempts, dead-letter WITHOUT invoking the handler.
            if (job.Attempts > job.MaxAttempts)
            {
                _logger.LogWarning("job {Id} ('{Type}') exceeded max attempts ({Attempts} > {Max}) — dead-lettering without running", job.Id, job.Type, job.Attempts, job.MaxAttempts);
                await _store.DeadLetterAsync(job.Id, _workerId, $"exceeded max attempts ({job.MaxAttempts})", ct).ConfigureAwait(false);
                outcome = "dead";
                return;
            }

            var ctx = new JobContext(job.Id, job.Payload, job.Checkpoint, job.Attempts,
                (cp, c) => _store.SaveCheckpointAsync(job.Id, _workerId, cp, c));

            // link a per-job token so a cancel REQUEST (polled from the store) cancels the handler's ct,
            // separately from shutdown (the outer ct). The poll stops the moment the handler returns.
            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var pollStop = new CancellationTokenSource();
            var cancelPoll = PollForCancelAsync(job.Id, jobCts, pollStop.Token);
            try
            {
                var result = await handler.HandleAsync(ctx, jobCts.Token).ConfigureAwait(false);
                outcome = await ApplyAsync(job, result, result.Error, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // a cancel was REQUESTED (not a shutdown) and the handler honored it → mark Cancelled (fenced)
                _logger.LogInformation("job {Id} ('{Type}') cancelled on request", job.Id, job.Type);
                await _store.CancelRunningAsync(job.Id, _workerId, ct).ConfigureAwait(false);
                outcome = "cancelled";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutdown mid-job: leave it Running; its lease lapses and another worker resumes it
                outcome = "cancelled";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "job {Id} ('{Type}') threw; scheduling a retry", job.Id, job.Type);
                outcome = await ApplyAsync(job, JobOutcome.Retry(), ex.Message, ct).ConfigureAwait(false); // a throw is transient
            }
            finally
            {
                pollStop.Cancel();           // stop the cancel poll now the handler is done
                await cancelPoll.ConfigureAwait(false);
            }
        }
        finally
        {
            LyntaiDiagnostics.EndJob(activity, job.Lane, outcome, Stopwatch.GetElapsedTime(start).TotalSeconds);
        }
    }

    /// <summary>While a job runs, poll the store for a cancel request; on seeing one, cancel the job's
    /// linked token so the handler stops cooperatively. Ends when <paramref name="stop"/> fires (the handler
    /// returned) or the cancel is observed. Swallows its own cancellation + any transient store error.</summary>
    private async Task PollForCancelAsync(Guid id, CancellationTokenSource jobCts, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(Opts.PollInterval, stop).ConfigureAwait(false);
                var current = await _store.GetAsync(id, stop).ConfigureAwait(false);
                if (current?.CancelRequested == true) { await jobCts.CancelAsync().ConfigureAwait(false); return; }
            }
        }
        catch (OperationCanceledException) { /* the handler finished (stop fired) or shutdown */ }
        catch (Exception ex) { _logger.LogDebug(ex, "cancel poll for job {Id} errored (ignored)", id); }
    }

    /// <summary>Maps the outcome onto the store and returns a telemetry label
    /// (succeeded / retry / failed / lost_lease).</summary>
    private async Task<string> ApplyAsync(JobRecord job, JobOutcome outcome, string? error, CancellationToken ct)
    {
        bool ok;
        string label;
        switch (outcome.Result)
        {
            case JobOutcome.Kind.Complete:
                ok = await _store.CompleteAsync(job.Id, _workerId, ct).ConfigureAwait(false);
                label = "succeeded";
                break;
            case JobOutcome.Kind.Retry when job.Attempts < job.MaxAttempts:
                ok = await _store.FailAsync(job.Id, _workerId, error ?? "retrying",
                    _clock() + (outcome.RetryDelay ?? Opts.RetryBackoff), ct).ConfigureAwait(false);
                label = "retry";
                break;
            case JobOutcome.Kind.Retry: // attempts exhausted → dead-letter (inspectable + replayable)
                ok = await _store.DeadLetterAsync(job.Id, _workerId, error ?? "retries exhausted", ct).ConfigureAwait(false);
                label = "dead";
                break;
            default: // Fail
                ok = await _store.FailAsync(job.Id, _workerId, error ?? "failed", ct: ct).ConfigureAwait(false);
                label = "failed";
                break;
        }
        if (!ok)
        {
            _logger.LogWarning("job {Id} outcome ignored — lease lost (re-claimed by another worker)", job.Id);
            return "lost_lease";
        }
        return label;
    }
}
