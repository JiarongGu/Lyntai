using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

public class MemoryToolsTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeTextProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    private static ITool Tool(ServiceProvider sp, string name) =>
        sp.GetRequiredService<IToolRegistry>().Find(name)
        ?? throw new InvalidOperationException(
            $"no tool '{name}'; registered: {string.Join(", ", sp.GetRequiredService<IToolRegistry>().Tools.Select(t => t.Name))}");

    [Fact]
    public void A_hierarchical_engine_name_becomes_a_legal_tool_name()
    {
        // "project/graph" is a legal engine name and an illegal tool name on every provider
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "t"));

        Assert.NotNull(Tool(sp, "project_graph_recall"));
        Assert.NotNull(Tool(sp, "project_graph_expand"));
    }

    [Fact]
    public async Task Recall_returns_headlines_and_a_ref_but_no_content()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "t", scope: "s"));

        await sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph")
            .RememberAsync(new MemoryWrite("t", "s", "the build gate runs seven checks"));

        var json = await Tool(sp, "project_graph_recall").InvokeAsync("""{"query":"gate"}""");

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Contains("build gate", item.GetProperty("headline").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("content").ValueKind);
        Assert.StartsWith("project/graph::", item.GetProperty("ref").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expand_takes_the_ref_recall_handed_out_and_returns_the_full_text()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "t", scope: "s"));

        await sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph")
            .RememberAsync(new MemoryWrite("t", "s",
                "the build gate runs seven checks and stops at the first failure"));

        var recalled = await Tool(sp, "project_graph_recall").InvokeAsync("""{"query":"gate"}""");
        using var recallDoc = JsonDocument.Parse(recalled);
        var reference = recallDoc.RootElement.GetProperty("items")[0].GetProperty("ref").GetString();

        var expanded = await Tool(sp, "project_graph_expand")
            .InvokeAsync($$"""{"ref":"{{reference}}"}""");

        Assert.Contains("stops at the first failure", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expand_refuses_a_ref_of_another_task_and_reinforces_nothing()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "mine", scope: "s"));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
        var other = await engine.RememberAsync(new MemoryWrite("theirs", "s", "the vault key lives in the other task"));
        var own = await engine.RememberAsync(new MemoryWrite("mine", "s", "the build gate runs seven checks"));
        var store = sp.GetRequiredService<IMemoryGraphStore>();
        var before = await store.GetAsync("project/graph", long.Parse(other.Reference.Id));

        var refused = await Tool(sp, "project_graph_expand")
            .InvokeAsync($$"""{"ref":"{{MemoryTools.Format(other.Reference)}}"}""");
        var allowed = await Tool(sp, "project_graph_expand")
            .InvokeAsync($$"""{"ref":"{{MemoryTools.Format(own.Reference)}}"}""");

        Assert.Contains("error", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("vault key", refused, StringComparison.Ordinal);
        Assert.Contains("seven checks", allowed, StringComparison.Ordinal);
        var after = await store.GetAsync("project/graph", long.Parse(other.Reference.Id));
        Assert.Equal(before!.RecallCount, after!.RecallCount);
        Assert.Equal(before.Stability, after.Stability);
    }

    [Fact]
    public async Task Expand_follows_the_task_a_turn_pinned_not_only_the_registered_one()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "default-task", scope: "s"));
        var written = await sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph")
            .RememberAsync(new MemoryWrite("conversation-42", "s", "belongs to conversation 42"));
        var arguments = $$"""{"ref":"{{MemoryTools.Format(written.Reference)}}"}""";

        Assert.Contains("error", await Tool(sp, "project_graph_expand").InvokeAsync(arguments), StringComparison.Ordinal);
        using (MemoryToolScope.Use("conversation-42", "s"))
            Assert.Contains("belongs to conversation 42",
                await Tool(sp, "project_graph_expand").InvokeAsync(arguments), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expand_on_an_engine_that_cannot_say_its_task_expands_nothing()
    {
        // fails CLOSED: a BYO engine that is expandable but not task-expandable cannot prove the boundary
        var tool = MemoryTools.Expand(new ExpandOnlyEngine(), "x", "t", null);

        var result = await tool.InvokeAsync("""{"ref":"x::1"}""");

        Assert.Contains("error", result, StringComparison.Ordinal);
        Assert.DoesNotContain("leaked", result, StringComparison.Ordinal);
    }

    private sealed class ExpandOnlyEngine : IMemoryEngine, IExpandableMemory
    {
        public string Name => "x";
        public MemoryGrades Supported => MemoryGrades.Associative;
        public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
            Task.FromResult(MemoryRecall.Empty);
        public Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
            MemoryDetail detail = MemoryDetail.Headline, CancellationToken ct = default) =>
            Task.FromResult(new MemoryRecall(
                [new MemoryItem(reference, "leaked", "leaked", MemoryGrade.Associative, 1, 1, 0, null)],
                MemorySources.None));
    }

    [Fact]
    public async Task Expand_without_a_ref_explains_itself_rather_than_throwing()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "t"));

        var result = await Tool(sp, "project_graph_expand").InvokeAsync("{}");

        Assert.Contains("error", result, StringComparison.Ordinal);
        Assert.Contains("ref", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoryToolScope_overrides_the_registered_task_for_the_current_turn()
    {
        // a chat application's task is per-conversation, so a singleton tool cannot bind it once
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "default-task", scope: "s"));

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
        await engine.RememberAsync(new MemoryWrite("conversation-42", "s", "belongs to conversation 42"));

        var outside = await Tool(sp, "project_graph_recall").InvokeAsync("""{"query":"conversation"}""");
        Assert.DoesNotContain("belongs to conversation 42", outside, StringComparison.Ordinal);

        using (MemoryToolScope.Use("conversation-42", "s"))
        {
            var inside = await Tool(sp, "project_graph_recall").InvokeAsync("""{"query":"conversation"}""");
            Assert.Contains("belongs to conversation 42", inside, StringComparison.Ordinal);
        }

        var after = await Tool(sp, "project_graph_recall").InvokeAsync("""{"query":"conversation"}""");
        Assert.DoesNotContain("belongs to conversation 42", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_engines_get_two_non_colliding_tool_sets()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseGraph())
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("chat", taskKey: "t")
            .AddMemoryTools("project", taskKey: "t"));

        var names = sp.GetRequiredService<IToolRegistry>().Tools.Select(t => t.Name).ToList();

        Assert.Contains("chat_recall", names);
        Assert.Contains("project_recall", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void A_reference_round_trips_even_when_the_engine_name_is_hierarchical()
    {
        var reference = new MemoryRef("project/graph", "42");

        Assert.Equal(reference, MemoryTools.Parse(MemoryTools.Format(reference)));
    }

    [Fact]
    public async Task An_unparseable_arguments_object_does_not_throw()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseGraph())
            .AddMemoryTools("project/graph", taskKey: "t"));

        // the loop tolerates a throw, but a tool that reports nothing useful wastes an iteration
        var result = await Tool(sp, "project_graph_recall").InvokeAsync("not json at all");

        Assert.Contains("items", result, StringComparison.Ordinal);
    }
}
