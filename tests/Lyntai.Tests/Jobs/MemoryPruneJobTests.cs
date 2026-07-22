using System.Text.Json;
using Lyntai;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Jobs;

/// <summary>The opt-in memory-prune job (Part 15): a durable-job handler that calls
/// <see cref="IMemoryStore.PruneAsync"/>, plus the <c>AddMemoryPruneJob</c> cron registration. Lyntai owns
/// the prune WORK; the app owns the pump (drives <c>IJobScheduler</c>/<c>IJobRunner</c>).</summary>
public class MemoryPruneJobTests
{
    private sealed class FakeMemory : IMemoryStore
    {
        public (string? TaskKey, TimeSpan? OlderThan)? LastPrune;
        public int PruneCalls;

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
        {
            PruneCalls++;
            LastPrune = (taskKey, olderThan);
            return Task.FromResult(7);
        }

        public Task RememberAsync(string t, string s, string c, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string t, string? s = null, string? q = null, int? l = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MemoryEntry>>([]);
        public Task ForgetAsync(string t, string? s = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static JobContext Ctx(string payload) => new(Guid.NewGuid(), payload, null, 1, (_, _) => Task.FromResult(true));

    [Fact]
    public async Task Handler_prunes_with_payload_taskKey_and_olderThan()
    {
        var mem = new FakeMemory();
        var handler = new MemoryPruneJobHandler(mem);
        var payload = JsonSerializer.Serialize(new MemoryPruneRequest("chat", 3600));

        var outcome = await handler.HandleAsync(Ctx(payload));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(1, mem.PruneCalls);
        Assert.Equal(("chat", TimeSpan.FromSeconds(3600)), mem.LastPrune);
    }

    [Fact]
    public async Task Handler_empty_or_malformed_payload_reaps_only_expired()
    {
        var mem = new FakeMemory();
        var handler = new MemoryPruneJobHandler(mem);

        await handler.HandleAsync(Ctx(""));         // empty payload
        await handler.HandleAsync(Ctx("not json")); // malformed payload

        Assert.Equal(2, mem.PruneCalls);
        Assert.Equal((null, (TimeSpan?)null), mem.LastPrune); // taskKey null + olderThan null → reap only expired
    }

    [Fact]
    public void AddMemoryPruneJob_registers_the_handler_and_a_cron_schedule()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryPruneJob(cron: "0 3 * * *", olderThan: TimeSpan.FromDays(30), taskKey: "chat"));
        using var sp = services.BuildServiceProvider();

        Assert.Single(sp.GetServices<IJobHandler>(), h => h.Type == MemoryPruneJobHandler.JobType);

        var schedule = Assert.Single(sp.GetServices<JobSchedule>(), s => s.Type == MemoryPruneJobHandler.JobType);
        Assert.Equal("0 3 * * *", schedule.Cron);
        var req = JsonSerializer.Deserialize<MemoryPruneRequest>(schedule.Payload)!;
        Assert.Equal("chat", req.TaskKey);
        Assert.Equal(TimeSpan.FromDays(30).TotalSeconds, req.OlderThanSeconds);
    }

    [Fact]
    public void AddMemoryPruneJob_registers_the_handler_only_once_for_multiple_schedules()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryPruneJob(cron: "0 3 * * *", name: "prune-nightly")
            .AddMemoryPruneJob(cron: "0 * * * *", name: "prune-hourly", taskKey: "hot"));
        using var sp = services.BuildServiceProvider();

        Assert.Single(sp.GetServices<IJobHandler>(), h => h.Type == MemoryPruneJobHandler.JobType); // one handler
        Assert.Equal(2, sp.GetServices<JobSchedule>().Count(s => s.Type == MemoryPruneJobHandler.JobType)); // two schedules
    }

    [Fact]
    public void AddMemoryPruneJob_rejects_a_bad_cron()
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<Exception>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryPruneJob(cron: "not a cron")));
    }
}
