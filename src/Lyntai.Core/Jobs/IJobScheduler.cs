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
/// from the schedule name + fire time if a duplicate run would be harmful).</para>
/// <para><b>Run ONE scheduler process.</b> Unlike the job <see cref="IJobRunner"/> (which scales to N
/// instances via the store's atomic claim), the scheduler is single-instance: the "read due next-run →
/// enqueue → persist advanced next-run" sequence is NOT a compare-and-swap, so two scheduler processes
/// sharing the same key-value store would each read the same due slot and both enqueue, firing every
/// schedule once PER instance. Drive the pump from exactly one process; the runner fleet can still be N.
/// (The idempotent-handler guidance above is the backstop if a duplicate slips through.)</para></summary>
public interface IJobScheduler
{
    /// <summary>Enqueue a job for each schedule that is due, advancing each. Returns how many were enqueued.</summary>
    Task<int> TickAsync(CancellationToken ct = default);

    /// <summary>Loop <see cref="TickAsync"/> every <c>Jobs.PollInterval</c> until cancelled.</summary>
    Task RunAsync(CancellationToken ct = default);
}

/// <inheritdoc/>
/// <remarks>Schedules added at run time come from the registered <see cref="IJobScheduleStore"/>, listed on every
/// tick AFTER the build-time schedules, which therefore win a name clash. A store that throws is skipped for that
/// tick and warned about once per failure run. A schedule whose cron or interval changed since its next run was
/// computed is re-anchored rather than fired once more at the old slot.</remarks>
public sealed class JobScheduler : IJobScheduler
{
    // ConcurrentDictionary so the caches don't corrupt if the app happens to drive the pump from more than
    // one thread (the intended model is a single pump, but a Dictionary write-race would be nastier than
    // the benign duplicate-work a concurrent one allows).
    private readonly ConcurrentDictionary<string, DateTimeOffset> _memory = new(StringComparer.Ordinal); // no-KV fallback
    private readonly ConcurrentDictionary<string, string> _triggers = new(StringComparer.Ordinal);       // no-KV fallback
    private readonly ConcurrentDictionary<string, CronExpression?> _cron = new(StringComparer.Ordinal);  // parsed cron cache
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);           // already-reported bad schedules
    private readonly IJobQueue _queue;
    private readonly IReadOnlyList<JobSchedule> _schedules;
    private readonly IJobScheduleStore? _scheduleStore;
    private readonly LyntaiOptions _options;
    private readonly IKeyValueStore? _store;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private int _storeFailing;

    /// <summary>A scheduler over the build-time schedules alone.</summary>
    public JobScheduler(IJobQueue queue, IEnumerable<JobSchedule> schedules, LyntaiOptions options,
        IKeyValueStore? store = null, ILogger<JobScheduler>? logger = null, Func<DateTimeOffset>? clock = null)
        : this(queue, schedules, null, options, store, logger, clock)
    {
    }

    /// <summary>A scheduler over the build-time schedules and those <paramref name="scheduleStore"/> holds.</summary>
    public JobScheduler(IJobQueue queue, IEnumerable<JobSchedule> schedules, IJobScheduleStore? scheduleStore,
        LyntaiOptions options, IKeyValueStore? store = null, ILogger<JobScheduler>? logger = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(options);
        _queue = queue;
        _schedules = [.. schedules];
        _scheduleStore = scheduleStore;
        _options = options;
        _store = store;
        _logger = logger ?? NullLogger<JobScheduler>.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var now = _clock();
        var enqueued = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        // the build-time schedules go BEFORE the store is asked, so a slow or hung store never holds them up
        foreach (var s in _schedules)
            if (await EvaluateAsync(s, now, names, stored: false, ct).ConfigureAwait(false)) enqueued++;
        foreach (var s in await ListStoredAsync(ct).ConfigureAwait(false))
            if (await EvaluateAsync(s, now, names, stored: true, ct).ConfigureAwait(false)) enqueued++;
        return enqueued;
    }

    /// <summary>Enqueue <paramref name="s"/> if it is due, advancing it; true when it was enqueued. A stored schedule
    /// is first held to the rules <c>AddJobSchedule</c> applies, since an app's own store may not validate.</summary>
    private async Task<bool> EvaluateAsync(
        JobSchedule s, DateTimeOffset now, HashSet<string> names, bool stored, CancellationToken ct)
    {
        // a throw here — an impossible cron's NextAfter (Feb 30), anything else — quarantines that ONE
        // schedule, so it neither aborts the tick (skipping later schedules) nor spins on every poll
        try
        {
            if (stored && !Conforms(s)) return false;
            if (!IsValid(s)) return false; // malformed schedule (no trigger / bad cron / non-positive interval)
            if (!names.Add(s.Name))
            {
                // the name keys the persisted next-run, so a second schedule of it could never fire
                if (FirstSight("duplicate\n" + s.Name))
                    _logger.LogWarning("scheduler: ignoring a second schedule named '{Name}' — it shares the first one's next-run", s.Name);
                return false;
            }

            // the trigger is recorded on EVERY tick, first sight included, so a change after it is always seen
            var changed = await TriggerChangedAsync(s, ct).ConfigureAwait(false);
            var next = await GetNextAsync(s.Name, ct).ConfigureAwait(false);
            if (next is null || changed)
            {
                // first sight, or a changed cron/interval → anchor the next run, don't fire now
                await SetNextAsync(s.Name, NextAfter(s, now), ct).ConfigureAwait(false);
                return false;
            }
            if (next.Value > now) return false; // not due yet

            await _queue.EnqueueAsync(s.Lane, s.Type, s.Payload, s.Priority, ct: ct).ConfigureAwait(false);
            _logger.LogDebug("scheduler: enqueued '{Name}' ({Type})", s.Name, s.Type);

            // advance to the next slot strictly after now — missed slots coalesce into this ONE run
            await SetNextAsync(s.Name, NextAfter(s, now), ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scheduler: skipping '{Name}' this tick — its next-run couldn't be computed", s.Name);
            return false;
        }
    }

    /// <summary>Whether a stored schedule passes <c>AddJobSchedule</c>'s rules; one that does not is warned about
    /// once per name and skipped on every tick.</summary>
    private bool Conforms(JobSchedule s)
    {
        try
        {
            JobScheduleRules.Validate(s);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            if (FirstSight("invalid\n" + s.Name))
                _logger.LogWarning("scheduler: skipping the stored schedule '{Name}' — {Reason}", s.Name, ex.Message);
            return false;
        }
    }

    /// <summary>The next fire time strictly after <paramref name="from"/> — the cron's next occurrence, or
    /// the interval advanced past <paramref name="from"/> (so a lapsed ticker coalesces to one slot).</summary>
    private DateTimeOffset NextAfter(JobSchedule s, DateTimeOffset from) =>
        Cron(s)?.Next(from) ?? from + s.Interval!.Value;

    private bool IsValid(JobSchedule s)
    {
        if (s.Cron is not null)
        {
            if (Cron(s) is not null) return true;
            if (FirstSight(s.Name))
                _logger.LogWarning("scheduler: skipping '{Name}' — invalid cron '{Cron}'", s.Name, s.Cron);
            return false;
        }
        if (s.Interval is { } iv && iv > TimeSpan.Zero) return true;
        if (FirstSight(s.Name))
            _logger.LogWarning("scheduler: skipping '{Name}' — needs a positive Interval or a Cron", s.Name);
        return false;
    }

    // A malformed schedule stays malformed, so its warning is logged ONCE per schedule name: the pump ticks
    // every Jobs.PollInterval (2s by default), which would otherwise repeat the same line ~30x a minute forever.
    private bool FirstSight(string name) => _warned.TryAdd(name, 0);

    // parse + cache the cron (null = no cron or a parse failure); the cache suppresses the re-PARSE (and its
    // throw) each tick — suppressing the repeated WARNING is _warned's job, in IsValid
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

            try { await Task.Delay(_options.Jobs.PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>The stored schedules, or none when no store is registered or it failed this tick — warned about on
    /// the first failing tick after a success, so a store down for an hour does not log on every poll.</summary>
    private async Task<IReadOnlyList<JobSchedule>> ListStoredAsync(CancellationToken ct)
    {
        if (_scheduleStore is null) return [];
        try
        {
            // bounded: a hung store must not hold the pump, and a timeout is a failure like any other
            var stored = await _scheduleStore.ListAsync(ct).WaitAsync(_options.Jobs.ScheduleStoreTimeout, ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _storeFailing, 0);
            return stored;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _storeFailing, 1) == 0)
                _logger.LogWarning(ex, "scheduler: the schedule store failed; running the build-time schedules until it answers");
            return [];
        }
    }

    /// <summary>Whether <paramref name="s"/>'s trigger differs from the one its next run was computed from, recording
    /// the current one. None recorded — every schedule a scheduler before this saw — is recorded, not a change.</summary>
    private async Task<bool> TriggerChangedAsync(JobSchedule s, CancellationToken ct)
    {
        var current = JobScheduleRules.Fingerprint(s);
        var recorded = _store is null
            ? (_triggers.TryGetValue(s.Name, out var t) ? t : null)
            : await _store.GetAsync(JobScheduleRules.TriggerKey(s.Name), ct).ConfigureAwait(false);
        if (string.Equals(recorded, current, StringComparison.Ordinal)) return false;

        if (_store is null) _triggers[s.Name] = current;
        else await _store.SetAsync(JobScheduleRules.TriggerKey(s.Name), current, ct).ConfigureAwait(false);
        return recorded is not null;
    }

    private async Task<DateTimeOffset?> GetNextAsync(string name, CancellationToken ct)
    {
        if (_store is null) return _memory.TryGetValue(name, out var t) ? t : null;
        var raw = await _store.GetAsync(JobScheduleRules.NextRunKey(name), ct).ConfigureAwait(false);
        if (raw is null) return null;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        // corrupt/foreign KV value: treat as first sight so the caller re-anchors and OVERWRITES it
        // (self-healing) — throwing here would hit the per-schedule catch every tick and silently
        // freeze this schedule forever, with the bad value never repaired
        _logger.LogWarning("scheduler: next-run for '{Name}' is unparseable ({Raw}); re-anchoring", name, raw);
        return null;
    }

    private async Task SetNextAsync(string name, DateTimeOffset when, CancellationToken ct)
    {
        if (_store is null) { _memory[name] = when; return; }
        await _store.SetAsync(JobScheduleRules.NextRunKey(name), when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
    }
}
