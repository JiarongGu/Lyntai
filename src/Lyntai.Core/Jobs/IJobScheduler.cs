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
/// more slots, the missed runs are COALESCED into a single enqueue — not replayed as a burst.</summary>
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
    private readonly IReadOnlyList<JobSchedule> _schedules = [.. schedules];
    private readonly ILogger _logger = logger ?? NullLogger<JobScheduler>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, DateTimeOffset> _memory = new(StringComparer.Ordinal); // fallback

    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var now = _clock();
        var enqueued = 0;
        foreach (var s in _schedules)
        {
            if (s.Interval <= TimeSpan.Zero)
            {
                _logger.LogWarning("scheduler: skipping '{Name}' — interval must be positive (was {Interval})", s.Name, s.Interval);
                continue; // a non-positive interval would never advance past 'now' → skip it, don't spin
            }
            var next = await GetNextAsync(s.Name, ct).ConfigureAwait(false);
            if (next is null)
            {
                // first sight → the first run is one interval out (don't fire immediately on startup)
                await SetNextAsync(s.Name, now + s.Interval, ct).ConfigureAwait(false);
                continue;
            }
            if (next.Value > now) continue; // not due yet

            await queue.EnqueueAsync(s.Lane, s.Type, s.Payload, s.Priority, ct).ConfigureAwait(false);
            enqueued++;
            _logger.LogDebug("scheduler: enqueued '{Name}' ({Type})", s.Name, s.Type);

            // advance past now to the next future slot — coalescing any missed slots into ONE run
            var advanced = next.Value;
            do { advanced += s.Interval; } while (advanced <= now);
            await SetNextAsync(s.Name, advanced, ct).ConfigureAwait(false);
        }
        return enqueued;
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
