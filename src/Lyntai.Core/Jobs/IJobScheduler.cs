using System.Collections.Concurrent;
using System.Globalization;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Jobs;

/// <summary>Drives the registered <see cref="JobSchedule"/>s: enqueues the due ones and advances their
/// next run. The app owns the pump — call <see cref="TickAsync"/> from its own loop, or <see cref="RunAsync"/>
/// (no background threads are started for you). Each schedule's next-run time is persisted (via
/// <see cref="IKeyValueStore"/>) so a restart resumes the cadence instead of re-anchoring; with no key-value
/// store wired it falls back to in-memory (cadence resets on restart). If the ticker was down across one or
/// more slots, the missed runs are COALESCED into a single enqueue — not replayed as a burst.
/// <para>Firing is <b>at-least-once</b>: each tick enqueues the due job first, then persists the advanced
/// next-run. A crash in that window (or a failed <c>SetNextAsync</c>) leaves the slot due, so the next
/// tick fires it again — one slot can enqueue more than one job. This mirrors the durable-job
/// at-least-once contract: the enqueued job's handler must be idempotent (dedup on a slot key derived
/// from the schedule name + fire time if a duplicate run would be harmful).</para></summary>
public interface IJobScheduler
{
    /// <summary>Enqueue a job for each schedule that is due, advancing each. Returns how many were enqueued.</summary>
    Task<int> TickAsync(CancellationToken ct = default);

    /// <summary>Loop <see cref="TickAsync"/> every <c>Jobs.PollInterval</c> until cancelled.</summary>
    Task RunAsync(CancellationToken ct = default);
}

/// <inheritdoc/>
public sealed class JobScheduler(
    IJobQueue queue,
    IEnumerable<JobSchedule> schedules,
    LyntaiOptions options,
    IKeyValueStore? store = null,
    ILogger<JobScheduler>? logger = null,
    Func<DateTimeOffset>? clock = null) : IJobScheduler
{
    // ConcurrentDictionary so the caches don't corrupt if the app happens to drive the pump from more than
    // one thread (the intended model is a single pump, but a Dictionary write-race would be nastier than
    // the benign duplicate-work a concurrent one allows).
    private readonly ConcurrentDictionary<string, DateTimeOffset> _memory = new(StringComparer.Ordinal); // no-KV fallback
    private readonly ConcurrentDictionary<string, CronExpression?> _cron = new(StringComparer.Ordinal);  // parsed cron cache
    private readonly IReadOnlyList<JobSchedule> _schedules = [.. schedules];
    private readonly ILogger _logger = logger ?? NullLogger<JobScheduler>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var now = _clock();
        var enqueued = 0;
        foreach (var s in _schedules)
        {
            if (!IsValid(s)) continue; // malformed schedule (no trigger / bad cron / non-positive interval)

            // NextAfter can throw for a parseable-but-impossible cron (e.g. Feb 30) — quarantine that ONE
            // schedule so it neither aborts the tick (skipping later schedules) nor spins on every poll
            try
            {
                var next = await GetNextAsync(s.Name, ct).ConfigureAwait(false);
                if (next is null)
                {
                    // first sight → schedule the first run (one interval out / the cron's next), don't fire now
                    await SetNextAsync(s.Name, NextAfter(s, now), ct).ConfigureAwait(false);
                    continue;
                }
                if (next.Value > now) continue; // not due yet

                await queue.EnqueueAsync(s.Lane, s.Type, s.Payload, s.Priority, ct).ConfigureAwait(false);
                enqueued++;
                _logger.LogDebug("scheduler: enqueued '{Name}' ({Type})", s.Name, s.Type);

                // advance to the next slot strictly after now — missed slots coalesce into this ONE run
                await SetNextAsync(s.Name, NextAfter(s, now), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "scheduler: skipping '{Name}' this tick — its next-run couldn't be computed", s.Name);
            }
        }
        return enqueued;
    }

    /// <summary>The next fire time strictly after <paramref name="from"/> — the cron's next occurrence, or
    /// the interval advanced past <paramref name="from"/> (so a lapsed ticker coalesces to one slot).</summary>
    private DateTimeOffset NextAfter(JobSchedule s, DateTimeOffset from)
    {
        if (Cron(s) is { } cron) return cron.Next(from);
        var interval = s.Interval!.Value;
        var t = from + interval;
        return t; // interval schedules fire one interval out; coalescing is implicit (from is 'now')
    }

    private bool IsValid(JobSchedule s)
    {
        if (s.Cron is not null)
        {
            if (Cron(s) is not null) return true;
            _logger.LogWarning("scheduler: skipping '{Name}' — invalid cron '{Cron}'", s.Name, s.Cron);
            return false;
        }
        if (s.Interval is { } iv && iv > TimeSpan.Zero) return true;
        _logger.LogWarning("scheduler: skipping '{Name}' — needs a positive Interval or a Cron", s.Name);
        return false;
    }

    // parse + cache the cron (null = no cron or a parse failure); cached so a bad cron doesn't re-throw/warn each tick
    private CronExpression? Cron(JobSchedule s)
    {
        if (s.Cron is null) return null;
        if (_cron.TryGetValue(s.Cron, out var parsed)) return parsed;
        try { return _cron[s.Cron] = CronExpression.Parse(s.Cron); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return _cron[s.Cron] = null; // remember the failure; IsValid logs it
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "scheduler tick failed; continuing"); }

            try { await Task.Delay(options.Jobs.PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<DateTimeOffset?> GetNextAsync(string name, CancellationToken ct)
    {
        if (store is null) return _memory.TryGetValue(name, out var t) ? t : null;
        var raw = await store.GetAsync(Key(name), ct).ConfigureAwait(false);
        return raw is null
            ? null
            : DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private async Task SetNextAsync(string name, DateTimeOffset when, CancellationToken ct)
    {
        if (store is null) { _memory[name] = when; return; }
        await store.SetAsync(Key(name), when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
    }

    private static string Key(string name) => $"lyntai:schedule:{name}";
}
