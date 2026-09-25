using Lyntai.Jobs;
using Lyntai.Memory;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Jobs;

/// <summary>The memory-prune job reaches the ENGINES, not only the keyword store — the graph engine is the
/// default under <c>AddMemory()</c>, and a background GC that never visits it prunes nothing that matters.</summary>
public class MemoryPruneJobEngineTests
{
    private static JobContext Ctx(string payload) =>
        new(Guid.NewGuid(), payload, null, 1, (_, _) => Task.FromResult(true));

    private static string Payload(string? taskKey, double? olderThanSeconds = null) =>
        new MemoryPruneRequest(taskKey, olderThanSeconds).ToJson();

    [Fact]
    public async Task A_task_scoped_job_prunes_every_engine_that_can_prune()
    {
        var engine = new ForgettableEngine("chat", pruneCount: 3);
        var handler = new MemoryPruneJobHandler(engines: [engine]);

        var outcome = await handler.HandleAsync(Ctx(Payload("t1", 60)));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(("t1", (string?)null), Assert.Single(engine.Prunes));
    }

    [Fact]
    public async Task An_all_tasks_job_leaves_engines_alone_because_an_engine_prunes_within_one_task()
    {
        var engine = new ForgettableEngine("chat");
        var handler = new MemoryPruneJobHandler(engines: [engine]);

        await handler.HandleAsync(Ctx(Payload(taskKey: null)));

        Assert.Empty(engine.Prunes);
    }

    [Fact]
    public async Task A_blend_that_refuses_to_prune_does_not_cost_the_other_engines_their_prune()
    {
        var refusing = new CompositeMemoryEngine("mixed",
            [new ForgettableEngine("mixed/a"), new ForgetOnlyEngine("mixed/b")]);
        var healthy = new ForgettableEngine("chat");
        var handler = new MemoryPruneJobHandler(engines: [refusing, healthy]);

        var outcome = await handler.HandleAsync(Ctx(Payload("t1", 60)));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Single(healthy.Prunes);
    }

    [Fact]
    public async Task The_AddMemory_graph_engine_is_pruned_by_the_registered_job()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddMemory()
            .AddMemoryPruneJob(cron: "0 3 * * *", olderThan: TimeSpan.FromMilliseconds(1), taskKey: "t1"));
        await using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        await engine.RememberAsync(new MemoryWrite("t1", "s", "an aged-out entry"));
        await Task.Delay(50); // older than the 1 ms cutoff by a margin no scheduler hiccup closes

        var handler = Assert.Single(sp.GetServices<IJobHandler>(), h => h.Type == MemoryPruneJobHandler.JobType);
        var schedule = Assert.Single(sp.GetServices<JobSchedule>(), s => s.Type == MemoryPruneJobHandler.JobType);
        await handler.HandleAsync(Ctx(schedule.Payload));

        var recall = await engine.RecallAsync(new MemoryQuery("t1", "s", "aged"));
        Assert.Empty(recall.Items);
    }

    [Fact]
    public void The_graph_engine_declares_the_prune_capability_it_implements()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).UseInMemoryStorage().AddMemory());
        using var sp = services.BuildServiceProvider();

        Assert.IsAssignableFrom<IPrunableMemory>(sp.GetRequiredService<IMemoryEngineFactory>().Get("default/memory"));
    }
}
