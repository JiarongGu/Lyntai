using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Jobs;

/// <summary>The shipped <see cref="IJobScheduleStore"/>: schedules added, replaced and removed at run time, kept one
/// key per schedule in the key-value store, and never able to break a list with one bad entry.</summary>
public class KeyValueJobScheduleStoreTests
{
    private readonly InMemoryKeyValueStore _kv = new();

    private KeyValueJobScheduleStore Store() => new(_kv);

    private static JobSchedule Every(string name, TimeSpan interval) => new(name, "reports", "report", "{\"a\":1}", interval, Priority: 3);

    private static JobSchedule Cron(string name, string cron) => new(name, "reports", "digest", "{}", Cron: cron);

    [Fact]
    public async Task Set_then_Get_round_trips_an_interval_and_a_cron_schedule()
    {
        var store = Store();
        var interval = Every("nightly", TimeSpan.FromMinutes(90));
        var cron = Cron("weekday", "0 9 * * 1-5");

        await store.SetAsync(interval);
        await store.SetAsync(cron);

        Assert.Equal(interval, await store.GetAsync("nightly"));
        Assert.Equal(cron, await store.GetAsync("weekday"));
        Assert.Null(await store.GetAsync("absent"));
    }

    [Fact]
    public async Task List_returns_every_set_schedule_and_nothing_else_under_the_kv()
    {
        var store = Store();
        await store.SetAsync(Every("a", TimeSpan.FromMinutes(5)));
        await store.SetAsync(Cron("b", "@daily"));
        await _kv.SetAsync("lyntai:schedule:a", DateTimeOffset.UnixEpoch.ToString("O"));   // the scheduler's own key

        var names = (await store.ListAsync()).Select(s => s.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["a", "b"], names);
    }

    [Fact]
    public async Task Set_replaces_by_name()
    {
        var store = Store();
        await store.SetAsync(Every("nightly", TimeSpan.FromMinutes(5)));
        await store.SetAsync(Cron("nightly", "@hourly"));

        Assert.Equal(Cron("nightly", "@hourly"), Assert.Single(await store.ListAsync()));
    }

    [Fact]
    public async Task Remove_returns_whether_one_was_removed_and_clears_the_scheduler_keys()
    {
        var store = Store();
        await store.SetAsync(Every("nightly", TimeSpan.FromMinutes(5)));
        await _kv.SetAsync("lyntai:schedule:nightly", DateTimeOffset.UnixEpoch.ToString("O"));
        await _kv.SetAsync("lyntai:schedule-trigger:nightly", "every:3000000000");

        Assert.True(await store.RemoveAsync("nightly"));
        Assert.False(await store.RemoveAsync("nightly"));

        Assert.Empty(await store.ListAsync());
        Assert.Null(await _kv.GetAsync("lyntai:schedule:nightly"));          // re-added later, it starts fresh
        Assert.Null(await _kv.GetAsync("lyntai:schedule-trigger:nightly"));
    }

    [Fact]
    public async Task A_name_with_a_colon_does_not_collide()
    {
        var store = Store();
        await store.SetAsync(Every("a", TimeSpan.FromMinutes(5)));
        await store.SetAsync(Every("a:b", TimeSpan.FromMinutes(7)));

        Assert.True(await store.RemoveAsync("a"));

        Assert.Equal("a:b", Assert.Single(await store.ListAsync()).Name);
        Assert.Equal(TimeSpan.FromMinutes(7), (await store.GetAsync("a:b"))!.Interval);
    }

    [Fact]
    public async Task An_unparseable_definition_is_skipped_not_thrown()
    {
        var logger = new CapturingLogger<KeyValueJobScheduleStore>();
        var store = new KeyValueJobScheduleStore(_kv, logger);
        await store.SetAsync(Every("good", TimeSpan.FromMinutes(5)));
        await _kv.SetAsync("lyntai:job-schedule:broken", "{");
        await _kv.SetAsync("lyntai:job-schedule:invalid", """{"name":"invalid","lane":"l","type":"t","payload":"{}"}""");

        Assert.Equal("good", Assert.Single(await store.ListAsync()).Name);
        Assert.Equal("good", Assert.Single(await store.ListAsync()).Name);
        Assert.Null(await store.GetAsync("broken"));
        Assert.Equal(2, logger.Messages.Count);                                // once per bad key, not per list
    }

    [Fact]
    public async Task A_definition_with_an_unknown_field_still_reads()
    {
        await _kv.SetAsync("lyntai:job-schedule:nightly",
            """{"name":"nightly","lane":"reports","type":"report","payload":"{}","cron":"@daily","later":true}""");

        Assert.Equal(new JobSchedule("nightly", "reports", "report", "{}", Cron: "@daily"), await Store().GetAsync("nightly"));
    }

    [Fact]
    public async Task Set_refuses_an_invalid_schedule_with_the_builder_s_exceptions()
    {
        var store = Store();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(new JobSchedule(" ", "l", "t", "{}", TimeSpan.FromMinutes(1))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(new JobSchedule("n", "l", "t", "{}", TimeSpan.FromMinutes(1), Cron: "@daily")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(new JobSchedule("n", "l", "t", "{}")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetAsync(new JobSchedule("n", "l", "t", "{}", TimeSpan.Zero)));
        await Assert.ThrowsAsync<FormatException>(() => store.SetAsync(Cron("n", "not a cron")));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public void AddJobScheduleStore_registers_one_instance_as_itself_and_as_the_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(_kv);
        services.AddLyntai(b => b.AddJobScheduleStore());
        using var sp = services.BuildServiceProvider();

        Assert.Same(sp.GetRequiredService<KeyValueJobScheduleStore>(), sp.GetRequiredService<IJobScheduleStore>());
    }

    [Fact]
    public async Task Persists_across_store_instances_on_SQLite()
    {
        using var db = new TempDb();
        await new KeyValueJobScheduleStore(new SqliteKeyValueStore(db.Factory)).SetAsync(Cron("weekday", "0 9 * * 1-5"));

        var reopened = new KeyValueJobScheduleStore(new SqliteKeyValueStore(db.Factory));

        Assert.Equal(Cron("weekday", "0 9 * * 1-5"), Assert.Single(await reopened.ListAsync()));
    }
}
