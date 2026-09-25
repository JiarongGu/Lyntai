using Lyntai.Jobs;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Jobs;

/// <summary>Every schedule door is validated at composition by ONE rule — exactly one trigger, a cron that
/// parses, a positive interval, a name nothing else uses — and the scheduler keeps its tolerance as the
/// backstop for a schedule that reaches it without going through the builder.</summary>
public sealed class JobScheduleValidationTests
{
    private static void Compose(Action<LyntaiBuilder> configure) =>
        new ServiceCollection().AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeTextProvider("p")).UseInMemoryStorage();
            configure(b);
        });

    [Fact]
    public void A_second_schedule_with_the_same_name_throws_naming_it()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Compose(b => b
            .AddJobSchedule("nightly", "a", "t", "{}", TimeSpan.FromHours(1))
            .AddCronSchedule("nightly", "b", "t", "{}", "0 3 * * *")));

        Assert.Contains("'nightly'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_memory_prune_jobs_on_the_default_name_throw_instead_of_dropping_one()
    {
        Assert.Throws<InvalidOperationException>(() => Compose(b => b
            .AddMemoryPruneJob("0 3 * * *", taskKey: "chat")
            .AddMemoryPruneJob("0 4 * * *", taskKey: "notes")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_interval_throws_at_composition(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(b => b
            .AddJobSchedule("bad", "l", "t", "{}", TimeSpan.FromMinutes(minutes))));
    }

    [Fact]
    public void A_schedule_record_with_no_trigger_throws()
    {
        Assert.Throws<ArgumentException>(() => Compose(b => b
            .AddJobSchedule(new JobSchedule("none", "l", "t", "{}"))));
    }

    [Fact]
    public void A_schedule_record_with_both_triggers_throws()
    {
        Assert.Throws<ArgumentException>(() => Compose(b => b
            .AddJobSchedule(new JobSchedule("both", "l", "t", "{}", TimeSpan.FromHours(1), Cron: "0 * * * *"))));
    }

    [Fact]
    public void A_schedule_records_cron_is_parsed_at_composition()
    {
        Assert.Throws<FormatException>(() => Compose(b => b
            .AddJobSchedule(new JobSchedule("bad", "l", "t", "{}", Cron: "not a cron"))));
    }

    [Theory]
    [InlineData("99999999999 * * * *")]   // overflows int
    [InlineData("1-2-3 * * * *")]         // a range has two ends
    public void A_malformed_cron_number_is_a_FormatException(string cron)
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(cron));
    }

    [Fact]
    public async Task An_overflowing_cron_handed_to_the_scheduler_directly_does_not_abort_the_tick()
    {
        var overflow = new JobSchedule("overflow", "l", "t", "{}", Cron: "99999999999 * * * *");
        var good = new JobSchedule("hourly", "l2", "t2", "{}", Cron: "0 * * * *");
        var (sched, jobs, clock) = Build(null, overflow, good);

        await sched.TickAsync();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, await sched.TickAsync());
        Assert.Equal("t2", Assert.Single(await jobs.ListAsync()).Type);
    }

    [Fact]
    public async Task A_duplicate_name_handed_to_the_scheduler_directly_is_reported_and_the_first_is_kept()
    {
        var logger = new RecordingLogger();
        var first = new JobSchedule("dup", "l", "first", "{}", TimeSpan.FromMinutes(10));
        var second = new JobSchedule("dup", "l", "second", "{}", TimeSpan.FromMinutes(1));
        var (sched, jobs, clock) = Build(logger, first, second);

        await sched.TickAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        await sched.TickAsync();

        Assert.Equal("first", Assert.Single(await jobs.ListAsync()).Type);
        Assert.Single(logger.Warnings, w => w.Contains("'dup'", StringComparison.Ordinal));
    }

    private static (JobScheduler Sched, InMemoryJobStore Jobs, MutableClock Clock) Build(
        ILogger<JobScheduler>? logger, params JobSchedule[] schedules)
    {
        var clock = new MutableClock();
        var jobs = new InMemoryJobStore(clock.Get);
        var options = new LyntaiOptions();
        return (new JobScheduler(new JobQueue(jobs, options), schedules, options, logger: logger, clock: clock.Get), jobs, clock);
    }

    private sealed class RecordingLogger : ILogger<JobScheduler>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
