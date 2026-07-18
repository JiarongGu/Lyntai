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
    private static (JobScheduler sched, InMemoryJobStore jobs, MutableClock clock, FakeKvStore kv) Build(
        params JobSchedule[] schedules)
    {
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        var kv = new FakeKvStore();
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
        var kv = new FakeKvStore(); // shared store = the durable next-run
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
        var (sched, jobs, clock, _) = Build(new JobSchedule("bad", "l", "t", "{}", TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, await sched.TickAsync()); // no enqueue, no infinite advance loop
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
    public async Task An_invalid_cron_schedule_is_skipped_not_thrown_at_tick()
    {
        var (sched, jobs, clock, _) = Build(new JobSchedule("bad", "l", "t", "{}", Cron: "not a cron"));
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, await sched.TickAsync()); // skipped, no throw
        Assert.Empty(await jobs.ListAsync());
    }

    [Fact]
    public void AddCronSchedule_validates_the_expression_eagerly()
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<Exception>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddCronSchedule("bad", "l", "t", "{}", "not a cron"))); // throws at composition, not at tick
    }

    [Fact]
    public async Task AddJobSchedule_wires_the_scheduler()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddJobSchedule("hourly", "lane", "t", "{}", TimeSpan.FromMinutes(60)));
        using var sp = services.BuildServiceProvider();

        var scheduler = sp.GetRequiredService<IJobScheduler>();
        Assert.IsType<JobScheduler>(scheduler);
        Assert.Equal(0, await scheduler.TickAsync()); // first tick schedules without throwing
    }

    /// <summary>A dict-backed <see cref="IKeyValueStore"/> that persists across scheduler instances.</summary>
    private sealed class FakeKvStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _d = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_d.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { _d[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { _d.Remove(key); return Task.CompletedTask; }
    }
}
