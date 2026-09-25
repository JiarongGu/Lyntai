using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>The generation tools' billing tag is set where consumers compose them — <c>AddGenerationTools</c> —
/// not only by hand-constructing a tool, which DI never does. The tag is what a per-consumer cap binds, so a host
/// running several agents fences each one's spend off by registering its tools under its own tag.</summary>
public class GenerationToolConsumerTests
{
    private static ITool Tool(ServiceProvider sp, string name) => sp.GetServices<ITool>().Single(t => t.Name == name);

    [Fact]
    public async Task Every_tool_that_spends_bills_under_the_consumer_the_registration_names()
    {
        var inline = new FakeGenerationProvider { Id = "image", CostUsd = 0.10 };
        var queued = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.AddProvider(_ => inline);
            cfg.AddProvider(_ => queued);
            cfg.AddMediaUsageBudget();
            cfg.AddGenerationTools("nightly-batch");
        });
        using var sp = services.BuildServiceProvider();
        var usage = sp.GetRequiredService<IUsageTracker>();

        await Tool(sp, "generate").InvokeAsync("""{"kind":"image","prompt":"x","backends":["image"]}""");
        var submitted = JsonDocument.Parse(await Tool(sp, "generate_submit")
            .InvokeAsync("""{"kind":"video","prompt":"x","backends":["video"]}""")).RootElement;
        await Tool(sp, "generate_fetch").InvokeAsync(
            $$"""{"backend":"video","operationId":"{{submitted.GetProperty("operationId").GetString()}}"}""");

        Assert.Equal(0.60, (await usage.TotalAsync("nightly-batch")).CostUsd, precision: 6);
        Assert.Equal(0, (await usage.TotalAsync(ProviderConsumers.Agent)).CostUsd);
    }

    [Fact]
    public async Task The_default_tag_is_agent()
    {
        var inline = new FakeGenerationProvider { Id = "image", CostUsd = 0.10 };
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.AddProvider(_ => inline);
            cfg.AddMediaUsageBudget();
            cfg.AddGenerationTools();
        });
        using var sp = services.BuildServiceProvider();

        await Tool(sp, "generate").InvokeAsync("""{"kind":"image","prompt":"x","backends":["image"]}""");

        Assert.Equal(0.10, (await sp.GetRequiredService<IUsageTracker>().TotalAsync(ProviderConsumers.Agent)).CostUsd,
            precision: 6);
    }
}
