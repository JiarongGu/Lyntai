using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Generation.Jobs;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>ONE gate decides whether a media render is billed, on every door. The router's budget decorator
/// records only when <c>AddMediaUsageBudget()</c> is configured, while the durable job handler and
/// <c>generate_fetch</c> recorded whenever ANY <see cref="IUsageTracker"/> was registered — which a text-only
/// <c>AddUsageBudget()</c> and the storage packages' usage tracking both do. So a host with a chat budget and no
/// media budget had its queued renders billed into the chat wallet, and counted against the chat cap, while its
/// inline renders were not (<c>pitfalls.md</c>, "a SPEND cap is a capability too").</summary>
public class GenerationSpendGateTests
{
    private sealed class NullSink : IGenerationArtifactSink
    {
        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ServiceProvider Host(Action<LyntaiBuilder> budget, FakeGenerationJobProvider backend)
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.AddProvider(_ => backend);
            cfg.UseDefaultMediaCandidates(backend.Id);
            cfg.AddMediaRouting();
            cfg.AddGenerationTools();
            cfg.AddJobHandler<GenerationPipelineJobHandler>();
            budget(cfg);
        });
        services.AddSingleton<IGenerationArtifactSink>(new NullSink());
        return services.BuildServiceProvider();
    }

    /// <summary>Submit, then poll to completion — the two dispatches a runner makes.</summary>
    private static async Task RunPipelineJob(ServiceProvider sp, string backendId)
    {
        var handler = sp.GetServices<IJobHandler>().Single(h => h.Type == GenerationPipelineJobHandler.JobType);
        var payload = new GenerationPipelineJob(
            [new GenerationPipelineJobStage([backendId], new MediaRequest { Kind = ProviderKinds.Video, Prompt = "x" })])
            .ToJson();
        string? checkpoint = null;
        for (var dispatch = 0; dispatch < 3; dispatch++)
        {
            var ctx = new JobContext(Guid.NewGuid(), payload, checkpoint, attempts: 1,
                saveCheckpoint: (c, _) => { checkpoint = c; return Task.FromResult(true); },
                reportProgress: (_, _, _, _) => Task.FromResult(true));
            var outcome = await handler.HandleAsync(ctx);
            if (outcome.Result == JobOutcome.Kind.Complete) return;
            Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        }
        Assert.Fail("the job did not complete");
    }

    [Fact]
    public async Task A_text_only_budget_does_not_bill_a_queued_render_through_the_job()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        using var sp = Host(cfg => cfg.AddUsageBudget(), backend);

        await RunPipelineJob(sp, "video");

        Assert.Equal(0, (await sp.GetRequiredService<IUsageTracker>().TotalAsync()).CostUsd);
    }

    [Fact]
    public async Task A_media_budget_bills_a_queued_render_through_the_job()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        using var sp = Host(cfg => cfg.AddMediaUsageBudget(), backend);

        await RunPipelineJob(sp, "video");

        Assert.Equal(0.50, (await sp.GetRequiredService<IUsageTracker>().TotalAsync()).CostUsd, precision: 6);
    }

    [Fact]
    public async Task A_text_only_budget_does_not_bill_a_render_fetched_through_the_tool()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        using var sp = Host(cfg => cfg.AddUsageBudget(), backend);
        var tools = sp.GetServices<ITool>().ToList();

        var submitted = JsonDocument.Parse(await tools.Single(t => t.Name == "generate_submit")
            .InvokeAsync("""{"kind":"video","prompt":"x"}""")).RootElement;
        var fetched = JsonDocument.Parse(await tools.Single(t => t.Name == "generate_fetch").InvokeAsync(
            $$"""{"backend":"video","operationId":"{{submitted.GetProperty("operationId").GetString()}}"}""")).RootElement;

        Assert.True(fetched.GetProperty("ok").GetBoolean());
        Assert.Equal(0, (await sp.GetRequiredService<IUsageTracker>().TotalAsync()).CostUsd);
    }
}
