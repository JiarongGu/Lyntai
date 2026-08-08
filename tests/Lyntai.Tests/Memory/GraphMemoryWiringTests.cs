using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The graph engine reached through the MEM1 seam: registered as a member like any other, blended
/// with an authoritative curated member, and expandable through the blend.</summary>
public class GraphMemoryWiringTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UseGraph_wires_a_working_graph_engine()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph()));

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
        await engine.RememberAsync(new MemoryWrite("t", "s", "the build gate runs seven checks"));
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.Single(recall.Items);
        Assert.Equal(MemorySources.Graph, recall.Ran);
    }

    [Fact]
    public void UseGraph_without_a_graph_store_fails_at_startup_naming_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("IMemoryGraphStore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMemory_prefers_the_graph_when_a_graph_store_is_present()
    {
        using var sp = Build(cfg => cfg.AddMemory());

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        var member = ((CompositeMemoryEngine)engine).Members[0];

        Assert.IsType<Lyntai.Memory.Engines.GraphMemoryEngine>(member);
    }

    [Fact]
    public void AddMemory_falls_back_to_the_keyword_store_when_there_is_no_graph_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).AddMemory());
        using var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        var member = ((CompositeMemoryEngine)engine).Members[0];

        Assert.IsType<Lyntai.Memory.Engines.LexicalMemoryEngine>(member);
    }

    [Fact]
    public async Task A_graph_member_blends_with_an_authoritative_curated_member()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e
                .UseCurated("glossary").Reserve(200)
                .UseGraph()
                .Budget(600))
            .UseMemoryComposer("project"));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        await factory.Get("project/glossary").RememberAsync(new MemoryWrite("t", "s",
            "the build gate is dev.mjs verify", Grade: MemoryGrade.Authoritative));
        for (var i = 0; i < 50; i++)
            await factory.Get("project/graph").RememberAsync(
                new MemoryWrite("t", "s", $"gate related chatter number {i} at some length"));

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "gate");

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expansion_routes_through_the_blend_to_the_graph_member()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseCurated().UseGraph()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        var blend = (IExpandableMemory)factory.Get("project");
        var reference = await factory.Get("project/graph").RememberAsync(
            new MemoryWrite("t", "s", "a long fact whose content is withheld until it is expanded"));

        var expanded = await blend.ExpandAsync(reference);

        Assert.Contains("withheld until it is expanded", expanded.Items[0].Content!,
            StringComparison.Ordinal);
    }
}
