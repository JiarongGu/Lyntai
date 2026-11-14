using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>An agent writes and ends what it remembers, through tools the host registered</b> (<c>docs/DECISIONS.md</c> D239,
/// D241). The host owns the task, the grade and the stamp; <c>disputes</c> and <c>amends</c> apply alone, while
/// <c>supersedes</c> and the end tool go through the approver and apply only to the revision the model read.
/// </summary>
public class MemoryWriteToolsTests : IDisposable
{
    private const string Engine = "project/graph";
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        GC.SuppressFinalize(this);
    }

    // a graph engine over each store that needs no container; the SQL stores' own rules are MemoryGraphStoreContract's
    private IMemoryGraphStore Store(string kind)
    {
        switch (kind)
        {
            case "file":
                var root = new TempRoot();
                _owned.Add(root);
                return new FileSystemMemoryGraphStore(root.Root);
            case "sqlite":
                var db = new TempDb();
                _owned.Add(db);
                return new SqliteMemoryGraphStore(db.Factory);
            default:
                return new InMemoryMemoryGraphStore();
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ServiceProvider Build(Action<LyntaiBuilder>? more = null, MemoryWriteToolOptions? options = null,
        bool writeTools = true, IMemoryGraphStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton(store ?? new InMemoryMemoryGraphStore());
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
    public async Task A_headline_a_reason_an_evidence_and_a_ref_list_past_their_bounds_are_refused_before_anything_is_written()
    {
        using var sp = Build(options: new MemoryWriteToolOptions { MaxContentChars = 40, MaxRefs = 2 });
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        var long41 = new string('x', 41);
        var refs = """["a:1","a:2","a:3"]""";

        var headline = await CallAsync(sp, "project_graph_remember", $$"""{"content":"the client lives in Oslo","headline":"{{long41}}"}""");
        var reason = await CallAsync(sp, "project_graph_remember",
            $$"""{"content":"the client lives in Oslo","supersedes":[{"ref":"a:1","revision":"1"}],"reason":"{{long41}}"}""");
        var evidence = await CallAsync(sp, "project_graph_remember", $$"""{"content":"the client lives in Oslo","evidence":"{{long41}}"}""");
        var disputes = await CallAsync(sp, "project_graph_remember", $$"""{"content":"the client lives in Oslo","disputes":{{refs}}}""");
        var ending = await CallAsync(sp, "project_graph_end", $$"""{"ref":"a:1","revision":"1","reason":"{{long41}}"}""");

        Assert.Contains("'headline' is 41", headline.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Contains("'reason' is 41", reason.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Contains("'evidence' is 41", evidence.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Contains("'disputes' lists 3", disputes.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Contains("'reason' is 41", ending.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Empty((await engine.RecallAsync(new MemoryQuery("t", "s", "Oslo"))).Items);
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

    [Fact]
    public async Task Another_tasks_entry_is_refused_in_the_words_a_missing_one_is_and_names_no_other_task()
    {
        using var sp = Build();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        var elsewhere = (await engine.RememberAsync(new MemoryWrite("other-task", "s", "the client lives in Oslo"))).Reference;

        var foreign = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{MemoryTools.Format(elsewhere)}}","revision":"1","reason":"x"}""");
        var missing = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{Engine}}::99999","revision":"1","reason":"x"}""");

        var foreignMessage = foreign.GetProperty("message").GetString()!;
        Assert.DoesNotContain("other-task", foreignMessage, StringComparison.Ordinal);
        Assert.Equal(missing.GetProperty("message").GetString()!.Replace("99999", "#", StringComparison.Ordinal),
            foreignMessage.Replace(elsewhere.Id, "#", StringComparison.Ordinal));

        // the same through a supersession named in a remember
        var superseded = await CallAsync(sp, "project_graph_remember",
            $$"""{"content":"the client lives in Rome","reason":"moved","supersedes":[{"ref":"{{MemoryTools.Format(elsewhere)}}","revision":"1"}]}""");
        Assert.DoesNotContain("other-task", superseded.ToString(), StringComparison.Ordinal);
    }

    // A model restating a fact the host made authoritative lands on that entry (its content is its identity, D241), and the tool's
    // fixed grade applies to a new entry only.
    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    [InlineData("sqlite")]
    public async Task Remember_OfAnAuthoritativeEntrysText_KeepsItsGrade(string store)
    {
        using var sp = Build(store: Store(store));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the client's deploy key rotates monthly", Grade: MemoryGrade.Authoritative));

        await CallAsync(sp, "project_graph_remember", """{"content":"the client's deploy key rotates monthly"}""");
        await CallAsync(sp, "project_graph_remember", """{"content":"the client's office is in Oslo"}""");

        var held = (await engine.RecallAsync(new MemoryQuery("t", "s", "client", Detail: MemoryDetail.Full))).Items;
        Assert.Equal(MemoryGrade.Authoritative, held.Single(i => i.Content!.Contains("deploy", StringComparison.Ordinal)).Grade);
        Assert.Equal(MemoryGrade.Associative, held.Single(i => i.Content!.Contains("Oslo", StringComparison.Ordinal)).Grade);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    [InlineData("sqlite")]
    public async Task Remember_StatingValidity_IsRefusedByDefault(string store)
    {
        using var sp = Build(store: Store(store));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the client lives in Paris") { ValidFrom = Jan });

        var schema = Tool(sp, "project_graph_remember").ParametersJsonSchema!;
        Assert.DoesNotContain("\"validFrom\"", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"validTo\"", schema, StringComparison.Ordinal);

        // a past end on a live entry's text, and a start on a new one: each refused, naming the tool that ends an entry
        foreach (var stated in new[]
                 {
                     """{"content":"the client lives in Paris","validTo":"2026-02-01T00:00:00Z"}""",
                     """{"content":"the client lives in Rome","validFrom":"2026-02-01T00:00:00Z"}""",
                 })
            Assert.Contains("project_graph_end", (await CallAsync(sp, "project_graph_remember", stated)).GetProperty("error").GetString()!,
                StringComparison.Ordinal);

        var history = (await engine.RecallAsync(new MemoryQuery("t", "s", "client") { Validity = MemoryValidityReading.History })).Items;
        var paris = Assert.Single(history);
        Assert.Equal((Jan, (DateTimeOffset?)null), (paris.Validity!.From, paris.Validity.To));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    [InlineData("sqlite")]
    public async Task Remember_StatingValidity_IsTakenWhenTheHostAllowsIt(string store)
    {
        using var sp = Build(options: new MemoryWriteToolOptions { ModelMayStateValidity = true }, store: Store(store));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);

        var schema = Tool(sp, "project_graph_remember").ParametersJsonSchema!;
        Assert.Contains("\"validFrom\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"validTo\"", schema, StringComparison.Ordinal);
        var written = await CallAsync(sp, "project_graph_remember",
            """{"content":"the client lives in Rome","validFrom":"2026-01-01T00:00:00Z","validTo":"2099-01-01T00:00:00Z"}""");

        Assert.True(written.TryGetProperty("ref", out _));
        var rome = (await engine.RecallAsync(new MemoryQuery("t", "s", "Rome"))).Items.Single();
        Assert.Equal((Jan, new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)), (rome.Validity!.From, rome.Validity.To!.Value));
    }

    // The approver reviewed a revision; a model restating the entry's text afterwards must not make that review stale. A SQL store
    // advances the revision on every write that lands (IEndableMemoryGraphStore), so this holds for the in-process stores.
    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    public async Task Remember_OfALiveEntrysText_LeavesItsRevision(string store)
    {
        using var sp = Build(b => b.AddMemoryChangeApprover<Approves>(), store: Store(store));
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(Engine);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the client's deploy key rotates monthly", Grade: MemoryGrade.Authoritative)
        { ValidFrom = Jan });
        var read = await ReadAsync(sp, "deploy key");

        await CallAsync(sp, "project_graph_remember", """{"content":"the client's deploy key rotates monthly"}""");

        Assert.Equal(read.Revision, (await ReadAsync(sp, "deploy key")).Revision);
        var ended = await CallAsync(sp, "project_graph_end", $$"""{"ref":"{{read.Ref}}","revision":"{{read.Revision}}","reason":"rotated weekly now"}""");
        Assert.Equal("Applied", ended.GetProperty("status").GetString());
    }

    // A model states no instant after now: a scheduled end is the host's to make, through the engine.
    [Fact]
    public async Task End_WithAFutureAt_IsRefused()
    {
        var now = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        using var sp = Build(b => b.AddMemoryChangeApprover<Approves>(), new MemoryWriteToolOptions { Clock = new FixedTime(now) });
        await CallAsync(sp, "project_graph_remember", """{"content":"the client lives in Paris"}""");
        var paris = await ReadAsync(sp, "Paris");

        var future = await CallAsync(sp, "project_graph_end",
            $$"""{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}","reason":"moving in August","at":"2026-08-01T00:00:00Z"}""");

        Assert.Contains("after now", future.GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.Equal(paris.Revision, (await ReadAsync(sp, "Paris")).Revision);
        var past = await CallAsync(sp, "project_graph_end",
            $$"""{"ref":"{{paris.Ref}}","revision":"{{paris.Revision}}","reason":"moved in June","at":"2026-06-01T00:00:00Z"}""");
        Assert.Equal("Applied", past.GetProperty("status").GetString());
    }
}
