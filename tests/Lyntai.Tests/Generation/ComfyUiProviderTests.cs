using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The local ComfyUI backend — the GRAPH-shaped one, and the platform's only ASYNC-JOB backend so
/// far. It matters for three reasons the other two don't cover: the caller supplies a workflow rather than a
/// bare prompt (so <c>Prompt</c> may be null and <c>Options</c> carries the graph), delivery is
/// submit → poll → fetch on a LOCAL server, and it is the candidate a host pairs with a hosted one when a
/// refusal should be picked up locally.
///
/// UNVERIFIED SURFACE: no ComfyUI was available to measure against on the dev machine, so the endpoint paths
/// and response fields come from documented surface, not observation — which is exactly why every one of them
/// is a settable option (see <see cref="ComfyUiOptions"/>). These tests pin the CONTRACT (what the provider
/// does with what it gets), not a claim that the real server speaks precisely this.</summary>
public class ComfyUiProviderTests
{
    private const string Workflow = """{"3":{"class_type":"KSampler","inputs":{"seed":0}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}}}""";

    private static (ComfyUiProvider Provider, StubHttpHandler Http) Provider(ComfyUiOptions? options = null)
    {
        var handler = new StubHttpHandler();
        var provider = new ComfyUiProvider(
            options ?? new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(handler, disposeHandler: false));
        return (provider, handler);
    }

    private static GenerationRequest Ask(string? prompt = "a red square", string? workflow = Workflow)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (workflow is not null) options["workflow"] = workflow;
        options["prompt-path"] = "6.inputs.text";
        return new GenerationRequest { Kind = GenerationKinds.Image, Prompt = prompt, Options = options };
    }

    [Fact]
    public void It_declares_itself_a_JOB_backend_across_image_and_video()
    {
        var (provider, _) = Provider();

        Assert.Equal("comfyui", provider.Id);
        Assert.Contains(GenerationKinds.Image, provider.Capabilities.Kinds);
        Assert.Contains(GenerationKinds.Video, provider.Capabilities.Kinds);   // local video via a workflow
        Assert.Equal([GenerationDelivery.Job], provider.Capabilities.Deliveries);
        Assert.IsAssignableFrom<IGenerationJobProvider>(provider);
    }

    [Fact]
    public async Task Inline_generation_is_declined_because_this_backend_is_a_job_backend()
    {
        // the base seam must answer honestly rather than hiding a poll loop
        var (provider, http) = Provider();

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Contains("submit", result.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_submit_posts_the_callers_workflow_and_returns_the_prompt_id()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"abc-123\",\"number\":1}");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal("abc-123", operation.Id);
        Assert.Equal(GenerationOperationStatus.Queued, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/prompt", http.Requests[0].Uri?.ToString());
        Assert.Contains("KSampler", http.Requests[0].Body);       // the caller's graph went through
    }

    [Fact]
    public async Task The_prompt_is_substituted_into_the_node_the_caller_named()
    {
        // a graph backend has no "prompt" field of its own — the caller says WHERE the text belongs
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"abc-123\"}");

        await provider.SubmitAsync(Ask("a blue circle"));

        Assert.Contains("a blue circle", http.Requests[0].Body);
        Assert.DoesNotContain("placeholder", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_submit_without_a_workflow_is_refused_rather_than_invented()
    {
        // there is no sensible default graph: guessing one would silently produce something the caller
        // never described
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask(workflow: null));

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("workflow", operation.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_invalid_workflow_json_is_refused_before_it_is_sent()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask(workflow: "{not json"));

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Polling_an_unfinished_run_reports_running_not_a_failure()
    {
        // ComfyUI's history is EMPTY until the run lands; "not there yet" must not read as "it failed"
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/history/abc-123", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task A_transport_failure_while_polling_keeps_the_run_alive_rather_than_abandoning_it()
    {
        // the server not answering says nothing about the render. Failed here reaches the job handler as
        // JobOutcome.Fail with no retry, so a restarted ComfyUI would abandon a run that is still going —
        // the same rule fal's poll already follows.
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "{\"error\":\"upstream hiccup\"}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
        Assert.Contains("hiccup", operation.Detail);
    }

    [Fact]
    public async Task A_4xx_while_polling_is_terminal_so_a_wrong_id_or_path_cannot_poll_forever()
    {
        // the other half of the rule: a 404 means THIS id (or the guessed HistoryPath) will never resolve,
        // and reporting Running would leave the job polling a render nobody holds
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.NotFound, "{\"error\":\"unknown prompt id\"}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
    }

    [Fact]
    public async Task Polling_an_unconfigured_backend_is_terminal_rather_than_perpetually_running()
    {
        var (provider, http) = Provider(new ComfyUiOptions { BaseUrl = "" });   // never configured

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("BaseUrl", operation.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Polling_a_finished_run_reports_succeeded()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}
            """);

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.Progress);
    }

    [Fact]
    public async Task A_fetch_turns_the_history_outputs_into_VIEW_uri_artifacts()
    {
        // the bytes stay on the local server until the caller wants them — same rule as a signed URL from a
        // hosted backend, and it keeps a 100 MB video out of memory by default
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"sub","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
        var uri = result.Artifacts[0].Uri;
        Assert.Contains("/view?", uri);
        Assert.Contains("filename=out.png", uri);
        Assert.Contains("subfolder=sub", uri);
        Assert.Contains("type=output", uri);
    }

    [Fact]
    public async Task A_fetch_of_a_video_output_is_recognised_by_its_extension()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"gifs":[{"filename":"clip.mp4","subfolder":"","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
        Assert.Equal("video/mp4", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_fetch_before_the_run_finishes_is_a_failure_with_a_reason_not_an_empty_success()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var result = await provider.FetchAsync("abc-123");

        Assert.False(result.IsOk);
        Assert.Contains("not finished", result.Detail);
    }

    [Fact]
    public async Task Cancelling_interrupts_the_running_job()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("abc-123");

        Assert.Equal(GenerationOperationStatus.Cancelled, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/interrupt", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task An_unreachable_local_server_is_NOT_CONFIGURED_on_every_path()
    {
        var handler = new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("connection refused"));
        var provider = new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(handler, disposeHandler: false));

        var submitted = await provider.SubmitAsync(Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(GenerationOperationStatus.Failed, submitted.Status);
        Assert.Contains("not reachable", submitted.Detail);
        Assert.False(probe.Available);
    }

    [Fact]
    public async Task A_probe_reads_system_stats_and_never_generates()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"system\":{\"comfyui_version\":\"0.3.40\",\"python_version\":\"3.12\"}}");

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("http://127.0.0.1:8188/system_stats", http.Requests[0].Uri?.ToString());
        Assert.Equal("0.3.40", probe.Version);
    }

    [Fact]
    public async Task Every_endpoint_path_is_overridable_because_the_surface_is_unverified()
    {
        // the guard against having guessed wrong: a host can retarget any path without waiting for a release
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            SubmitPath = "api/prompt",
            HistoryPath = "api/history",
            ViewPath = "api/view",
            InterruptPath = "api/interrupt",
            SystemStatsPath = "api/system_stats",
        });
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"x\"}");

        await provider.SubmitAsync(Ask());

        Assert.Equal("http://127.0.0.1:8188/api/prompt", http.Requests[0].Uri?.ToString());
    }
}
