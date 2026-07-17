using Lyntai;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Jobs;

/// <summary>The durable-jobs machinery resolves out of <c>AddLyntai</c> and runs end to end; the queue
/// fails loudly when no storage backend is wired (durable work must be persisted).</summary>
public class JobsDiTests
{
    [Fact]
    public async Task AddLyntai_wires_queue_runner_and_handlers_end_to_end()
    {
        var ran = false;
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddJobHandler(_ => new FakeJobHandler("greet", _ => { ran = true; return Task.FromResult(JobOutcome.Complete); })));
        using var sp = services.BuildServiceProvider();

        var id = await sp.GetRequiredService<IJobQueue>().EnqueueAsync("default", "greet", "{}");
        var n = await sp.GetRequiredService<IJobRunner>().RunOnceAsync();

        Assert.Equal(1, n);
        Assert.True(ran);
        Assert.Equal(JobStatus.Succeeded, (await sp.GetRequiredService<IJobStore>().GetAsync(id))!.Status);
    }

    [Fact]
    public void Queue_without_a_storage_backend_throws_clearly()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p"))); // no Use*Storage
        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IJobQueue>());
        Assert.Contains("storage backend", ex.Message);
    }
}
