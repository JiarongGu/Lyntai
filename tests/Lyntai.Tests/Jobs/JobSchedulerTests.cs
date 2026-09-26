using Lyntai;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Jobs;

/// <summary>Recurring job scheduling: the first run waits one interval, due schedules enqueue and advance,
/// missed slots coalesce into one run, the priority carries through, next-run persists across scheduler
/// instances (restart), and it degrades to in-memory without a key-value store — all deterministic via an
/// injected clock.</summary>
public class JobSchedulerTests
{
    private static (JobScheduler sched, InMemoryJobStore jobs, MutableClock clock, InMemoryKeyValueStore kv) Build(
        params JobSchedule[] schedules)
    {
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var kv = new InMemoryKeyValueStore();
        var scheduler = new JobScheduler(new JobQueue(jobs, options), schedules, options, kv, clock: clock.Get);
        return (scheduler, jobs, clock, kv);
    }

    private static JobSchedule Every(TimeSpan interval, int priority = 0) =>
        new("nightly", "reports", "report", "{}", interval, priority);

    [Fact]
    public async Task First_tick_schedules_but_does_not_fire_then_fires_when_due()
    {
        var (sched, jobs, clock, _) = Build(Every(TimeSpan.FromMinutes(10)));

        Assert.Equal(0, await sched.TickAsync());          // first sight → schedule the first run, don't fire
        Assert.Empty(await jobs.ListAsync());

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await sched.TickAsync());          // now due → enqueue
        Assert.Equal("report", Assert.Single(await jobs.ListAsync()).Type);

        Assert.Equal(0, await sched.TickAsync());          // not due again immediately
    }

    [Fact]
    public async Task Fires_once_per_interval_across_ticks()
    {
        var (sched, jobs, clock, _) = Build(Every(TimeSpan.FromMinutes(5)));
        await sched.TickAsync(); // schedule

        for (var i = 0; i < 3; i++) { clock.Advance(TimeSpan.FromMinutes(5)); await sched.TickAsync(); }

        Assert.Equal(3, (await jobs.ListAsync()).Count); // one per elapsed interval
    }

    [Fact] // A corrupt persisted next-run self-heals (re-anchor + overwrite) instead of freezing the schedule
    public async Task Corrupt_persisted_next_run_re_anchors_instead_of_freezing_the_schedule()
    {
        var (sched, jobs, clock, kv) = Build(Every(TimeSpan.FromMinutes(10)));
        await kv.SetAsync("lyntai:schedule:nightly", "not-a-timestamp"); // corrupt/foreign KV value

        Assert.Equal(0, await sched.TickAsync()); // treated as first sight: re-anchor + OVERWRITE, don't fire
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await sched.TickAsync()); // the repaired schedule fires on cadence again
        Assert.Single(await jobs.ListAsync());
    }

    [Fact]
    public async Task Missed_slots_are_coalesced_into_a_single_run()
    {
        var (sched, jobs, clock, _) = Build(Every(TimeSpan.FromMinutes(10)));
        await sched.TickAsync(); // next = t0 + 10m

        clock.Advance(TimeSpan.FromMinutes(35));            // slots at 10/20/30 all missed (ticker was down)
        Assert.Equal(1, await sched.TickAsync());          // ONE enqueue, not three
        Assert.Single(await jobs.ListAsync());

        // and it's re-anchored ahead of now, not still in the past → next tick doesn't immediately fire
        Assert.Equal(0, await sched.TickAsync());
    }

    [Fact]
    public async Task Priority_carries_through_to_the_enqueued_job()
    {
        var (sched, jobs, clock, _) = Build(Every(TimeSpan.FromMinutes(5), priority: 7));
        await sched.TickAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await sched.TickAsync();

        Assert.Equal(7, Assert.Single(await jobs.ListAsync()).Priority);
    }

    [Fact]
    public async Task Next_run_persists_across_scheduler_instances()
    {
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var kv = new InMemoryKeyValueStore(); // shared store = the durable next-run
        var sched = Every(TimeSpan.FromMinutes(10));

        // instance 1 schedules the first run, then the "process restarts"
        await new JobScheduler(new JobQueue(jobs, options), [sched], options, kv, clock: clock.Get).TickAsync();
        clock.Advance(TimeSpan.FromMinutes(10));

        // a FRESH instance reads the persisted next-run (doesn't re-anchor to now) → fires as due
        var restarted = new JobScheduler(new JobQueue(jobs, options), [sched], options, kv, clock: clock.Get);
        Assert.Equal(1, await restarted.TickAsync());
        Assert.Single(await jobs.ListAsync());
    }

    [Fact]
    public async Task Degrades_to_in_memory_without_a_key_value_store()
    {
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var sched = new JobScheduler(new JobQueue(jobs, options), [Every(TimeSpan.FromMinutes(10))], options, store: null, clock: clock.Get);

        await sched.TickAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await sched.TickAsync()); // works, just not durable across a real restart
    }

    [Fact]
    public async Task A_non_positive_interval_is_skipped_not_spun()
    {
        // several ticks with time passing between them: a first tick never fires anything, so one tick alone
        // cannot tell a skipped schedule from one that fires every tick
        var (sched, jobs, clock, _) = Build(new JobSchedule("bad", "l", "t", "{}", TimeSpan.Zero));
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.Equal(0, await sched.TickAsync()); // no enqueue, no infinite advance loop
            clock.Advance(TimeSpan.FromMinutes(10));
        }
        Assert.Empty(await jobs.ListAsync());
    }

    [Fact]
    public async Task Cron_schedule_fires_at_the_cron_time()
    {
        // MutableClock starts at 2026-07-18 12:00:00Z; "0 * * * *" = top of every hour
        var (sched, jobs, clock, _) = Build(new JobSchedule("hourly", "l", "t", "{}", Cron: "0 * * * *"));

        Assert.Equal(0, await sched.TickAsync());          // first sight → next = 13:00, no fire
        Assert.Empty(await jobs.ListAsync());

        clock.Advance(TimeSpan.FromHours(1));              // 13:00
        Assert.Equal(1, await sched.TickAsync());          // due
        Assert.Single(await jobs.ListAsync());
        Assert.Equal(0, await sched.TickAsync());          // next = 14:00, not due yet
    }

    [Fact]
    public async Task An_impossible_cron_is_quarantined_and_does_not_abort_the_tick()
    {
        // "0 0 30 2 *" = Feb 30 — parses fine but has no occurrence (Next throws). A valid schedule listed
        // AFTER it must still fire; the throw must not abort the whole tick.
        var impossible = new JobSchedule("feb30", "l", "t", "{}", Cron: "0 0 30 2 *");
        var good = new JobSchedule("hourly", "l2", "t2", "{}", Cron: "0 * * * *");
        var (sched, jobs, clock, _) = Build(impossible, good);

        await sched.TickAsync();                       // first sight: 'good' scheduled; feb30 quarantined
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await sched.TickAsync());      // 'good' fires despite feb30 throwing every tick
        Assert.Single(await jobs.ListAsync());
    }

    [Fact]
    public async Task An_invalid_cron_schedule_is_skipped_not_thrown_at_tick()
    {
        // skipped by validation, which says so ONCE — not left to the per-tick catch, which would warn every poll
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var logger = new CapturingLogger<JobScheduler>();
        var sched = new JobScheduler(new JobQueue(jobs, options),
            [new JobSchedule("bad", "l", "t", "{}", Cron: "not a cron")], options, new InMemoryKeyValueStore(), logger, clock.Get);

        for (var tick = 0; tick < 3; tick++)
        {
            Assert.Equal(0, await sched.TickAsync()); // skipped, no throw
            clock.Advance(TimeSpan.FromHours(2));
        }

        Assert.Empty(await jobs.ListAsync());
        Assert.Contains("invalid cron", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void AddCronSchedule_validates_the_expression_eagerly()
    {
        var services = new ServiceCollection();
        Assert.Throws<FormatException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddCronSchedule("bad", "l", "t", "{}", "not a cron"))); // throws at composition, not at tick
    }

    [Fact]
    public async Task AddJobSchedule_wires_the_scheduler()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddJobSchedule("hourly", "lane", "t", "{}", TimeSpan.FromMinutes(60)));
        using var sp = services.BuildServiceProvider();

        var scheduler = sp.GetRequiredService<IJobScheduler>();
        Assert.IsType<JobScheduler>(scheduler);
        Assert.Equal(0, await scheduler.TickAsync()); // first tick schedules without throwing
    }

    [Fact]
    public async Task AddJobScheduleStore_wires_the_store_into_the_scheduler()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddJobScheduleStore());
        using var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<IJobScheduleStore>().SetAsync(new JobSchedule("user", "lane", "t", "{}", TimeSpan.FromMinutes(1)));
        var scheduler = sp.GetRequiredService<IJobScheduler>();

        await scheduler.TickAsync();                                       // first sight of the stored schedule

        Assert.NotNull(await sp.GetRequiredService<IKeyValueStore>().GetAsync("lyntai:schedule:user"));
    }
}

/// <summary>Schedules the app adds at run time (<see cref="IJobScheduleStore"/>): listed on every tick after the
/// build-time ones, skipped for a tick when the store fails, and re-anchored when their trigger changes.</summary>
public class JobSchedulerStoreTests
{
    private readonly MutableClock _clock = new();
    private readonly InMemoryKeyValueStore _kv = new();
    private readonly CapturingLogger<JobScheduler> _logger = new();
    private InMemoryJobStore? _jobs;

    private InMemoryJobStore Jobs => _jobs ??= new InMemoryJobStore(_clock.Get);

    private JobScheduler Scheduler(IJobScheduleStore? store, params JobSchedule[] buildTime)
    {
        var options = new LyntaiOptions();
        return new JobScheduler(new JobQueue(Jobs, options), buildTime, store, options, _kv, _logger, _clock.Get);
    }

    private static JobSchedule Every(string name, TimeSpan interval, string type = "report") =>
        new(name, "reports", type, "{}", interval);

    [Fact]
    public async Task A_stored_schedule_fires_on_the_tick_after_it_is_set()
    {
        var store = new KeyValueJobScheduleStore(_kv);
        var scheduler = Scheduler(store);
        await store.SetAsync(Every("user", TimeSpan.FromMinutes(10)));

        Assert.Equal(0, await scheduler.TickAsync());                      // first sight: anchored, not fired
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(1, await scheduler.TickAsync());
        Assert.Equal("report", Assert.Single(await Jobs.ListAsync()).Type);
    }

    [Fact]
    public async Task A_removed_schedule_stops_firing()
    {
        var store = new KeyValueJobScheduleStore(_kv);
        var scheduler = Scheduler(store);
        await store.SetAsync(Every("user", TimeSpan.FromMinutes(10)));
        await scheduler.TickAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await scheduler.TickAsync());

        await store.RemoveAsync("user");
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(0, await scheduler.TickAsync());
    }

    [Fact]
    public async Task Build_time_wins_a_name_clash_with_the_store()
    {
        var store = new KeyValueJobScheduleStore(_kv);
        await store.SetAsync(Every("nightly", TimeSpan.FromMinutes(10), type: "stored"));
        var scheduler = Scheduler(store, Every("nightly", TimeSpan.FromMinutes(10), type: "built"));
        await scheduler.TickAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(1, await scheduler.TickAsync());
        Assert.Equal("built", Assert.Single(await Jobs.ListAsync()).Type);
    }

    [Fact]
    public async Task A_throwing_store_is_skipped_and_build_time_schedules_still_fire()
    {
        var scheduler = Scheduler(new FailingStore { Failing = true }, Every("nightly", TimeSpan.FromMinutes(10)));
        await scheduler.TickAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(1, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_store_failing_every_tick_is_warned_once_per_failure_run()
    {
        var store = new FailingStore { Failing = true };
        var scheduler = Scheduler(store);

        for (var i = 0; i < 3; i++) await scheduler.TickAsync();
        Assert.Single(_logger.Messages, m => m.Contains("schedule store", StringComparison.Ordinal));

        store.Failing = false;
        await scheduler.TickAsync();                                       // a success ends the run
        store.Failing = true;
        await scheduler.TickAsync();

        Assert.Equal(2, _logger.Messages.Count(m => m.Contains("schedule store", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_changed_trigger_re_anchors_instead_of_firing_at_the_old_slot()
    {
        var store = new KeyValueJobScheduleStore(_kv);
        var scheduler = Scheduler(store);
        await store.SetAsync(Every("user", TimeSpan.FromMinutes(10)));
        await scheduler.TickAsync();                                       // next run: +10 min

        await store.SetAsync(Every("user", TimeSpan.FromHours(1)));
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, await scheduler.TickAsync());                      // the old slot does not fire: re-anchored

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_missing_fingerprint_does_not_re_anchor()
    {
        // the first tick after the upgrade: a due next run written by a scheduler that kept no fingerprint
        await _kv.SetAsync("lyntai:schedule:nightly", _clock.Now.AddMinutes(-1).ToString("O"));
        var scheduler = Scheduler(null, Every("nightly", TimeSpan.FromMinutes(10)));

        Assert.Equal(1, await scheduler.TickAsync());
        Assert.NotNull(await _kv.GetAsync("lyntai:schedule-trigger:nightly"));
    }

    [Fact]
    public async Task A_build_time_schedule_edited_between_deployments_re_anchors()
    {
        await Scheduler(null, Every("nightly", TimeSpan.FromMinutes(10))).TickAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var redeployed = Scheduler(null, Every("nightly", TimeSpan.FromHours(1)));

        Assert.Equal(0, await redeployed.TickAsync());
    }

    [Fact]
    public async Task Cancellation_from_the_store_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scheduler(new FailingStore()).TickAsync(cts.Token));
    }

    private sealed class FailingStore : IJobScheduleStore
    {
        public bool Failing { get; set; }

        public Task<IReadOnlyList<JobSchedule>> ListAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Failing ? throw new InvalidOperationException("down") : Task.FromResult<IReadOnlyList<JobSchedule>>([]);
        }

        public Task<JobSchedule?> GetAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SetAsync(JobSchedule schedule, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
