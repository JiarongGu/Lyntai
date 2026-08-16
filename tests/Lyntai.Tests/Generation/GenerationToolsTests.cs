using System.Text.Json;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Generation;
using Lyntai.Generation.Jobs;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>The generation domain exposed as <see cref="ITool"/>s — the ENTIRE coupling between the generation
/// and LLM domains. Neither references the other's concrete types: the LLM side already knows <c>ITool</c>, so
/// these work in the in-process tool loop and, via the MCP host, for a CLI agent too.</summary>
public class GenerationToolsTests
{
    private sealed class CollectingSink : IGenerationArtifactSink
    {
        public List<GenerationArtifactDelivery> Received { get; } = [];

        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
        {
            Received.Add(delivery);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Host(CollectingSink? sink = null, params IGenerationProvider[] backends)
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            foreach (var backend in backends) cfg.AddGenerationProvider(_ => backend);
            cfg.UseDefaultGenerationCandidates([.. backends.Select(b => b.Id)]);
            cfg.AddGenerationTools();
        });
        if (sink is not null) services.AddSingleton<IGenerationArtifactSink>(sink);
        return services.BuildServiceProvider();
    }

    private static ITool Tool(ServiceProvider sp, string name) =>
        sp.GetServices<ITool>().Single(t => t.Name == name);

    /// <summary>A tool-driven async render is BILLED, exactly as the job-handler path is.
    ///
    /// <para>The defect: <c>GenerationFetchTool</c> called <c>backend.FetchAsync</c> directly, bypassing the
    /// router, and handed <c>result.Usage</c> to the artifact sink without ever reaching
    /// <see cref="Lyntai.Llm.Budgeting.IUsageTracker"/>. A queue backend prices at FETCH — that is the only
    /// point the total is known — so the entire cost of every tool-driven async render was invisible.</para>
    ///
    /// <para>Why that breaks a promise rather than merely under-reporting. <c>GenerationInlineTool.Consumer</c>
    /// tells the reader to "set <c>Budget.PerConsumer["agent"]</c> and it binds agent-driven renders", and
    /// <c>AddGenerationUsageBudget</c> promises that "what has this app spent" stays ONE number across chat
    /// and media. With the fetch unrecorded, <c>SubmitAsync</c> re-checks a cap against a total that never
    /// grows, so a configured cap never fires and spend is unbounded underneath it — while the SAME tool set's
    /// inline <c>generate</c> is billed correctly. One registration, two delivery modes, one of them metered.
    /// Textbook `pitfalls.md` §"Second doors".</para></summary>
    [Fact]
    public async Task Fetching_a_finished_render_through_the_TOOL_bills_it_like_the_job_handler_does()
    {
        var sink = new CollectingSink();
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.AddGenerationProvider(_ => backend);
            cfg.UseDefaultGenerationCandidates(["video"]);
            cfg.AddGenerationUsageBudget();   // the registration whose whole promise is ONE spend number
            cfg.AddGenerationTools();
        });
        services.AddSingleton<IGenerationArtifactSink>(sink);
        using var sp = services.BuildServiceProvider();
        var usage = sp.GetRequiredService<Lyntai.Llm.Budgeting.IUsageTracker>();

        var submitted = Json(await Tool(sp, "generate_submit")
            .InvokeAsync("""{"kind":"video","prompt":"a cat surfing"}"""));
        var operationId = submitted.GetProperty("operationId").GetString();

        var before = await usage.TotalAsync();
        var fetched = Json(await Tool(sp, "generate_fetch")
            .InvokeAsync($"{{\"backend\":\"video\",\"operationId\":\"{operationId}\"}}"));
        var after = await usage.TotalAsync();

        Assert.True(fetched.GetProperty("ok").GetBoolean());
        Assert.Equal(0.50, after.CostUsd - before.CostUsd, precision: 6);
    }

    private static JsonElement Json(string observation) => JsonDocument.Parse(observation).RootElement;

    [Fact]
    public void The_five_tools_register_into_the_shared_ITool_collection()
    {
        // registering into the SAME collection the tool loop reads is what makes the coupling work without
        // either domain knowing the other
        using var sp = Host(null, new FakeGenerationProvider { Id = "image" });

        var names = sp.GetServices<ITool>().Select(t => t.Name).ToList();

        Assert.Contains("generate_backends", names);
        Assert.Contains("generate", names);
        Assert.Contains("generate_submit", names);
        Assert.Contains("generate_status", names);
        Assert.Contains("generate_fetch", names);
    }

    [Fact]
    public void Every_tool_advertises_a_description_and_a_valid_json_schema()
    {
        // a model chooses tools by reading these; a malformed schema is a silent capability loss
        using var sp = Host(null, new FakeGenerationProvider { Id = "image" });

        foreach (var tool in sp.GetServices<ITool>().Where(t => t.Name.StartsWith("generate")))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.NotNull(tool.ParametersJsonSchema);
            var schema = JsonDocument.Parse(tool.ParametersJsonSchema!);   // throws if invalid
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task Backends_lists_what_each_can_do_so_the_model_need_not_guess()
    {
        using var sp = Host(null,
            new FakeGenerationProvider { Id = "image" },
            new FakeGenerationJobProvider { Id = "video" });

        var observation = Json(await Tool(sp, "generate_backends").InvokeAsync("{}"));

        var backends = observation.GetProperty("backends").EnumerateArray().ToList();
        Assert.Equal(2, backends.Count);
        var video = backends.Single(b => b.GetProperty("id").GetString() == "video");
        Assert.Equal("video", video.GetProperty("kinds")[0].GetString());
        Assert.Equal("job", video.GetProperty("delivery")[0].GetString());
        Assert.True(video.GetProperty("usable").GetBoolean());
    }

    [Fact]
    public async Task Generate_produces_media_and_hands_the_bytes_to_the_host_not_the_model()
    {
        // a base64 image in a tool observation would blow the context window for no benefit
        var sink = new CollectingSink();
        using var sp = Host(sink, new FakeGenerationProvider { Id = "image" });

        var observation = Json(await Tool(sp, "generate").InvokeAsync("""{"prompt":"a red square"}"""));

        Assert.True(observation.GetProperty("ok").GetBoolean());
        Assert.True(observation.GetProperty("delivered").GetBoolean());
        Assert.Equal("image/png", observation.GetProperty("artifacts")[0].GetProperty("mediaType").GetString());
        Assert.DoesNotContain("data", observation.GetProperty("artifacts")[0].EnumerateObject().Select(p => p.Name));
        Assert.Single(sink.Received);
    }

    [Fact]
    public async Task Generate_without_a_prompt_returns_a_readable_error_rather_than_throwing()
    {
        // the model should be able to read what it got wrong and retry
        using var sp = Host(null, new FakeGenerationProvider { Id = "image" });

        var observation = Json(await Tool(sp, "generate").InvokeAsync("{}"));

        Assert.False(observation.GetProperty("ok").GetBoolean());
        Assert.Contains("prompt", observation.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Generate_surfaces_a_refusal_verdict_instead_of_a_bare_failure()
    {
        var refusing = new FakeGenerationProvider { Id = "image" };
        refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
        using var sp = Host(null, refusing);

        var observation = Json(await Tool(sp, "generate").InvokeAsync("""{"prompt":"x"}"""));

        Assert.False(observation.GetProperty("ok").GetBoolean());
        Assert.Contains("Refused", observation.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_async_path_round_trips_submit_then_status_then_fetch()
    {
        var sink = new CollectingSink();
        using var sp = Host(sink, new FakeGenerationJobProvider { Id = "video" });

        var submitted = Json(await Tool(sp, "generate_submit")
            .InvokeAsync("""{"kind":"video","prompt":"a cat surfing"}"""));
        var backend = submitted.GetProperty("backend").GetString();
        var operationId = submitted.GetProperty("operationId").GetString();

        var status = Json(await Tool(sp, "generate_status")
            .InvokeAsync($"{{\"backend\":\"{backend}\",\"operationId\":\"{operationId}\"}}"));
        var fetched = Json(await Tool(sp, "generate_fetch")
            .InvokeAsync($"{{\"backend\":\"{backend}\",\"operationId\":\"{operationId}\"}}"));

        Assert.Equal("video", backend);
        Assert.Equal("queued", submitted.GetProperty("status").GetString());
        Assert.Equal("succeeded", status.GetProperty("status").GetString());
        Assert.True(fetched.GetProperty("ok").GetBoolean());
        Assert.Equal("video/mp4", fetched.GetProperty("artifacts")[0].GetProperty("mediaType").GetString());
        Assert.Single(sink.Received);
    }

    [Fact]
    public async Task Status_on_an_inline_only_backend_says_so_rather_than_crashing()
    {
        // a model that submits to the wrong backend gets one clear message
        using var sp = Host(null, new FakeGenerationProvider { Id = "image" });

        var observation = Json(await Tool(sp, "generate_status")
            .InvokeAsync("""{"backend":"image","operationId":"op-1"}"""));

        Assert.False(observation.GetProperty("ok").GetBoolean());
        Assert.Contains("asynchronous", observation.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Status_without_its_arguments_asks_for_them()
    {
        using var sp = Host(null, new FakeGenerationJobProvider { Id = "video" });

        var observation = Json(await Tool(sp, "generate_status").InvokeAsync("""{"backend":"video"}"""));

        Assert.False(observation.GetProperty("ok").GetBoolean());
        Assert.Contains("operationId", observation.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_model_can_name_the_backends_it_wants_in_preference_order()
    {
        var first = new FakeGenerationProvider { Id = "a" };
        first.Verdicts.Enqueue(GenerationVerdict.Failed);
        var second = new FakeGenerationProvider { Id = "b" };
        using var sp = Host(null, first, second);

        var observation = Json(await Tool(sp, "generate")
            .InvokeAsync("""{"prompt":"x","backends":["a","b"]}"""));

        Assert.True(observation.GetProperty("ok").GetBoolean());
        Assert.Equal(1, first.GenerateCalls);
        Assert.Equal(1, second.GenerateCalls);
    }

    [Fact]
    public async Task Unknown_arguments_pass_through_as_backend_options()
    {
        // a model can use a backend's own knobs (size, duration, voice) without Lyntai enumerating them
        var backend = new FakeGenerationProvider { Id = "image" };
        using var sp = Host(null, backend);

        var observation = Json(await Tool(sp, "generate")
            .InvokeAsync("""{"prompt":"x","size":"1024x1024","steps":"30"}"""));

        Assert.True(observation.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Without_a_sink_the_observation_reports_the_artifact_instead_of_claiming_delivery()
    {
        using var sp = Host(null, new FakeGenerationProvider { Id = "image" });

        var observation = Json(await Tool(sp, "generate").InvokeAsync("""{"prompt":"x"}"""));

        Assert.True(observation.GetProperty("ok").GetBoolean());
        Assert.False(observation.GetProperty("delivered").GetBoolean());
        Assert.True(observation.GetProperty("artifacts")[0].GetProperty("bytes").GetInt32() > 0);
    }
}
