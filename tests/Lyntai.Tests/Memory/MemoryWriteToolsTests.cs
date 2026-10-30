using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>An agent writes and ends what it remembers, through tools the host registered</b> (<c>docs/DECISIONS.md</c> D239,
/// D241). The host owns the task, the grade and the stamp; <c>disputes</c> and <c>amends</c> apply alone, while
/// <c>supersedes</c> and the end tool go through the approver and apply only to the revision the model read.
/// </summary>
public class MemoryWriteToolsTests
{
    private const string Engine = "project/graph";

    private static ServiceProvider Build(Action<LyntaiBuilder>? more = null, MemoryWriteToolOptions? options = null,
        bool writeTools = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeTextProvider("p"));
            b.AddMemoryEngine("project", e => e.UseGraph());
            b.AddMemoryTools(Engine, taskKey: "t", scope: "s");
            if (writeTools) b.AddMemoryWriteTools(Engine, taskKey: "t", scope: "s", options: options);
            more?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    private static ITool Tool(ServiceProvider sp, string name) =>
        sp.GetRequiredService<IToolRegistry>().Find(name) ?? throw new InvalidOperationException($"no tool '{name}'");

    private static async Task<JsonElement> CallAsync(ServiceProvider sp, string tool, string arguments) =>
        JsonDocument.Parse(await Tool(sp, tool).InvokeAsync(arguments)).RootElement.Clone();

    private static async Task<(string Ref, string Revision)> ReadAsync(ServiceProvider sp, string text)
    {
        var recall = await CallAsync(sp, "project_graph_recall", """{"query":"client","limit":50}""");
        var item = recall.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("headline").GetString()!.Contains(text, StringComparison.Ordinal));
        return (item.GetProperty("ref").GetString()!, item.GetProperty("revision").GetString()!);
    }

    private static async Task<string[]> LiveAsync(ServiceProvider sp) =>
        [.. (await CallAsync(sp, "project_graph_recall", """{"query":"client","limit":50}""")).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("headline").GetString()!).Order(StringComparer.Ordinal)];

    private sealed class Approves : IMemoryChangeApprover
    {
        public Task<MemoryChangeDecision> ApproveAsync(MemoryChangeRequest request, CancellationToken ct = default) =>
            Task.FromResult(MemoryChangeDecision.Approve);
    }

    [Fact]
    public void A_host_that_registered_only_the_read_tools_exposes_no_write_and_the_write_tools_take_no_task_or_grade()
    {
        using var readOnly = Build(writeTools: false);
        var names = readOnly.GetRequiredService<IToolRegistry>().Tools.Select(t => t.Name).ToList();
        Assert.DoesNotContain(names, n => n.EndsWith("_remember", StringComparison.Ordinal) || n.EndsWith("_end", StringComparison.Ordinal));

        using var sp = Build();
        foreach (var name in new[] { "project_graph_remember", "project_graph_end" })
        {
            var schema = Tool(sp, name).ParametersJsonSchema!;
            Assert.DoesNotContain("\"grade\"", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("\"task", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("\"scope\"", name == "project_graph_end" ? schema : "", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Remember_writes_associative_stamped_in_the_hosts_task_and_shows_what_it_may_update()
    {
        using var sp = Build(options: new MemoryWriteToolOptions { Metadata = new Dictionary<string, string> { ["by"] = "agent-7" } });
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the client lives in Paris"));

        // a model that names another task or an authoritative grade is simply not heard
        var result = await CallAsync(sp, "project_graph_remember",
            """{"content":"the client lives in Berlin","task":"other","grade":"authoritative","scope":"elsewhere"}""");

        var written = (await engine.RecallAsync(new MemoryQuery("t", "s", "Berlin", Detail: MemoryDetail.Full))).Items.Single();
        Assert.Equal(MemoryGrade.Associative, written.Grade);
        Assert.Equal("agent-7", written.Metadata!["by"]);
        Assert.Equal(MemoryTools.Format(written.Reference), result.GetProperty("ref").GetString());
        var may = result.GetProperty("mayUpdate").EnumerateArray().Single();
        Assert.Contains("Paris", may.GetProperty("headline").GetString()!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(may.GetProperty("revision").GetString()));
        // and it was not applied: both are current
        Assert.Equal(2, (await LiveAsync(sp)).Length);
    }

    [Fact]
    public async Task The_task_comes_from_the_turn_and_a_long_content_is_refused()
    {
        using var sp = Build(options: new MemoryWriteToolOptions { MaxContentChars = 40 });
        using (MemoryToolScope.Use("conversation-9", "s"))
            await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Oslo"}""");

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        Assert.Single((await engine.RecallAsync(new MemoryQuery("conversation-9", "s", "Oslo"))).Items);
        Assert.Empty((await engine.RecallAsync(new MemoryQuery("t", "s", "Oslo"))).Items);

        var tooLong = await CallAsync(sp, "project_graph_remember", $$"""{"content":"{{new string('x', 41)}}"}""");
        Assert.Contains("at most 40", tooLong.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disputes_and_amends_apply_alone_and_a_bad_ref_is_an_error_the_model_can_read()
    {
        using var sp = Build();
        await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Paris"}""");
        var paris = await ReadAsync(sp, "Paris");

        var disputed = await CallAsync(sp, "project_graph_remember",
            $$"""{"content":"the client lives in Berlin","disputes":["{{paris.Ref}}"]}""");
        Assert.True(disputed.TryGetProperty("ref", out _));
        var recall = await CallAsync(sp, "project_graph_recall", """{"query":"client"}""");
        Assert.All(recall.GetProperty("items").EnumerateArray(), i => Assert.True(i.TryGetProperty("relations", out _)));

        var elsewhere = await CallAsync(sp, "project_graph_remember", """{"content":"x","disputes":["nonsense"]}""");
        Assert.Contains("not a ref", elsewhere.GetProperty("error").GetString()!, StringComparison.Ordinal);
        var missing = await CallAsync(sp, "project_graph_remember", """{"content":"the client has a dog","amends":["project/graph::9999"]}""");
        Assert.Contains("has no entry", missing.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_approver_a_supersession_lands_live_and_unlinked_and_an_end_is_refused()
    {
        using var sp = Build();
        await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Paris"}""");
        var paris = await ReadAsync(sp, "Paris");

        var superseded = await CallAsync(sp, "project_graph_remember",
            $$"""{"content":"the client lives in Berlin","supersedes":[{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}"}],"reason":"moved"}""");
        Assert.Equal("NoApprover", superseded.GetProperty("status").GetString());
        Assert.Equal(["the client lives in Berlin", "the client lives in Paris"], await LiveAsync(sp));

        var end = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}","reason":"moved"}""");
        Assert.Equal("NoApprover", end.GetProperty("status").GetString());
        Assert.Equal(2, (await LiveAsync(sp)).Length);
    }

    [Fact]
    public async Task An_approved_supersession_and_end_apply_and_read_back_under_history_with_their_reason()
    {
        using var sp = Build(b => b.AddMemoryChangeApprover<Approves>());
        await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Paris"}""");
        await CallAsync(sp, "project_graph_remember", """{"content":"the client has a dog"}""");
        var paris = await ReadAsync(sp, "Paris");
        var dog = await ReadAsync(sp, "dog");

        var superseded = await CallAsync(sp, "project_graph_remember",
            $$"""{"content":"the client lives in Berlin","supersedes":[{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}"}],"reason":"moved"}""");
        Assert.Equal("Applied", superseded.GetProperty("status").GetString());
        var ended = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{dog.Ref}}","revision":"{{dog.Revision}}","reason":"the dog died"}""");
        Assert.Equal("Applied", ended.GetProperty("status").GetString());

        Assert.Equal(["the client lives in Berlin"], await LiveAsync(sp));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        var history = (await engine.RecallAsync(new MemoryQuery("t", "s", "client") { Validity = MemoryValidityReading.History, Limit = 20 })).Items;
        Assert.Equal("moved", history.Single(i => i.Headline.Contains("Paris", StringComparison.Ordinal)).Validity!.EndedReason);
        Assert.Equal("the dog died", history.Single(i => i.Headline.Contains("dog", StringComparison.Ordinal)).Validity!.EndedReason);
    }

    [Fact]
    public async Task A_stale_revision_is_told_what_to_read_again_and_a_second_successor_or_a_foreign_ref_is_refused()
    {
        using var sp = Build(b => b.AddMemoryChangeApprover<Approves>());
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Paris"}""");
        var paris = await ReadAsync(sp, "Paris");
        await engine.RememberAsync(new MemoryWrite("t", "s", "the client lives in Paris") { Headline = "the client lives in Paris (reworded)" });

        var stale = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}","reason":"moved"}""");
        Assert.Equal("Changed", stale.GetProperty("status").GetString());
        var fresh = stale.GetProperty("currentRevisions").GetProperty(paris.Ref).GetString()!;
        Assert.NotEqual(paris.Revision, fresh);
        Assert.Single(await LiveAsync(sp));

        var berlin = (await engine.RememberAsync(new MemoryWrite("t", "s", "the client lives in Berlin"))).Reference;
        var rome = (await engine.RememberAsync(new MemoryWrite("t", "s", "the client lives in Rome"))).Reference;
        var first = await CallAsync(sp, "project_graph_end",
            $$"""{"ref":"{{paris.Ref}}","revision":"{{fresh}}","reason":"moved","by":"{{MemoryTools.Format(berlin)}}"}""");
        Assert.Equal("Applied", first.GetProperty("status").GetString());

        // an entry has one successor: a second is refused with the engine's own message, as a result the model can read
        var endedRevision = (await engine.RecallAsync(new MemoryQuery("t", "s", "Paris") { Validity = MemoryValidityReading.History })).Items
            .Single(i => i.Reference.Id == MemoryTools.Parse(paris.Ref)!.Value.Id).Revision;
        var second = await CallAsync(sp, "project_graph_end",
            $$"""{"ref":"{{paris.Ref}}","revision":"{{endedRevision}}","reason":"moved again","by":"{{MemoryTools.Format(rome)}}"}""");
        Assert.Equal("Refused", second.GetProperty("status").GetString());
        Assert.Contains("one successor", second.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // a ref of another task or engine is refused, and nothing in the other task moved
        var elsewhere = (await engine.RememberAsync(new MemoryWrite("other-task", "s", "the client lives in Oslo"))).Reference;
        foreach (var foreign in new[] { MemoryTools.Format(elsewhere), "another-engine::1" })
        {
            var refused = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{foreign}}","revision":"1","reason":"x"}""");
            Assert.Equal("Refused", refused.GetProperty("status").GetString());
        }
        Assert.Single((await engine.RecallAsync(new MemoryQuery("other-task", "s", "Oslo"))).Items);
    }
}
