using System.Net;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>What a shipped backend reports when its own HTTP call THROWS, and what the router then does with it.
///
/// <para>The router applies two rules to a throw it catches itself: a SUBMIT that may have been delivered is
/// <see cref="QueuedOperation.Inconclusive"/> (the queue may already hold a billable render, so the next candidate
/// would buy it twice), and a throw is never <see cref="ProviderVerdict.Refused"/> (a proxy page mentioning a
/// content policy must not end the run with no fallback). Every shipped backend catches its own exceptions first,
/// so each has to apply the same rules or the router never sees the throw it would have handled.</para></summary>
public class GenerationBackendThrowTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>The request reached the server and the answer never arrived whole — the "may have been
    /// delivered" case, and what a server dying mid-response looks like to <see cref="HttpClient"/>.</summary>
    private static HttpRequestException DroppedMidResponse() =>
        new(HttpRequestError.ResponseEnded, "the connection dropped mid-response");

    /// <summary>Nothing listening: provably never delivered.</summary>
    private static HttpRequestException Refused() =>
        new(HttpRequestError.ConnectionError, "connection refused");

    private static Func<HttpClient> Client(StubHttpHandler handler) =>
        () => new HttpClient(handler, disposeHandler: false);

    private static ProviderCandidate[] Order(params string[] ids) => [.. ids.Select(id => new ProviderCandidate(id))];

    // ---- fal -----------------------------------------------------------------------------------------

    private static FalProvider Fal(StubHttpHandler http, FalOptions? options = null) =>
        new(options ?? new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" }, Client(http));

    private static MediaRequest Video() => new() { Kind = ProviderKinds.Video, Prompt = "a wave" };

    [Fact]
    public async Task Fal_a_submit_that_throws_after_sending_is_Inconclusive_and_no_second_backend_is_asked()
    {
        var http = new StubHttpHandler().Enqueue(_ => throw DroppedMidResponse());
        var second = new FakeGenerationJobProvider { Id = "fake-video" };

        var submission = await new MediaRouter([Fal(http), second]).SubmitAsync(Order("fal", "fake-video"), Video());

        Assert.True(submission.Operation.Inconclusive);
        Assert.Equal("fal", submission.ProviderId);     // who may hold it
        Assert.Equal(0, second.SubmitCalls);            // nobody pays twice
    }

    [Fact]
    public async Task Fal_a_submit_whose_connection_was_refused_is_a_plain_failure_the_router_advances_past()
    {
        // the counterweight: nothing left this process, so nothing can be queued and the next backend is free
        var http = new StubHttpHandler().Enqueue(_ => throw Refused());
        var second = new FakeGenerationJobProvider { Id = "fake-video" };

        var alone = await Fal(http).SubmitAsync(Video());
        var routed = await new MediaRouter([Fal(http), second]).SubmitAsync(Order("fal", "fake-video"), Video());

        Assert.Equal(QueuedOperationStatus.Failed, alone.Status);
        Assert.False(alone.Inconclusive);
        Assert.Equal("fake-video", routed.ProviderId);
    }

    [Fact]
    public async Task Fal_a_2xx_submit_with_no_request_id_is_Inconclusive_because_the_queue_accepted_something()
    {
        var http = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"status":"IN_QUEUE"}""");

        var operation = await Fal(http).SubmitAsync(Video());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.True(operation.Inconclusive);
        Assert.Contains("request_id", operation.Detail);
    }

    [Fact]
    public async Task Fal_unconfigured_is_NotConfigured_so_the_router_skips_it_without_a_strike()
    {
        // answered before any socket opens — the blameless case D31's verdict exists for
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var fal = Fal(new StubHttpHandler(), new FalOptions { Model = "fal-ai/wan-t2v" });   // no ApiKey
        var router = new MediaRouter([fal, new FakeGenerationJobProvider { Id = "fake-video" }], deadHosts: tracker);

        var operation = await fal.SubmitAsync(Video());
        await router.SubmitAsync(Order("fal", "fake-video"), Video());

        Assert.Equal(ProviderVerdict.NotConfigured, operation.Verdict);
        Assert.False(tracker.IsDead("generation::fal"));
    }

    // ---- ComfyUI -------------------------------------------------------------------------------------

    private const string Workflow =
        """{"1":{"class_type":"LoadImage","inputs":{"image":"placeholder.png"}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}}}""";

    private static ComfyUiProvider Comfy(StubHttpHandler http) =>
        new(new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" }, Client(http));

    private static MediaRequest Graph(params MediaInput[] inputs) => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = "a red square",
        Inputs = inputs,
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflow"] = Workflow,
            ["prompt-path"] = "6.inputs.text",
            ["input-path"] = "1.inputs.image",
        },
    };

    private static FakeGenerationJobProvider SecondImageQueue() => new()
    {
        Id = "fake-image",
        Capabilities = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Image],
            Operations = [ProviderOperation.Queued],
            SupportsInputs = true,
        },
    };

    [Fact]
    public async Task ComfyUi_a_submit_that_throws_after_the_workflow_is_sent_is_Inconclusive_and_no_second_backend_is_asked()
    {
        var http = new StubHttpHandler().Enqueue(_ => throw DroppedMidResponse());
        var second = SecondImageQueue();

        var submission = await new MediaRouter([Comfy(http), second])
            .SubmitAsync(Order("comfyui", "fake-image"), Graph());

        Assert.True(submission.Operation.Inconclusive);
        Assert.Equal("comfyui", submission.ProviderId);
        Assert.Equal(0, second.SubmitCalls);
    }

    [Fact]
    public async Task ComfyUi_a_throw_while_UPLOADING_an_input_is_a_plain_failure_because_no_workflow_was_queued()
    {
        // the stage decides it: an upload stores a file, and only the queue call can start a run
        var http = new StubHttpHandler().Enqueue(_ => throw DroppedMidResponse());

        var operation = await Comfy(http).SubmitAsync(Graph(new MediaInput("image/png", Data: Png)));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.False(operation.Inconclusive);
        Assert.Single(http.Requests);                  // the upload, and nothing queued after it
    }

    [Fact]
    public async Task ComfyUi_a_2xx_submit_with_no_prompt_id_is_Inconclusive_because_the_queue_accepted_something()
    {
        var http = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"number":1,"node_errors":{}}""");

        var operation = await Comfy(http).SubmitAsync(Graph());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.True(operation.Inconclusive);
        Assert.Contains("prompt_id", operation.Detail);
    }

    // ---- the inline backends: a throw is never a Refusal ---------------------------------------------

    /// <summary>A corporate proxy's block page surfacing as an exception — content-policy words on a TRANSPORT
    /// failure, which the router's own rule classifies as <see cref="ProviderVerdict.Failed"/>.</summary>
    private static HttpRequestException ProxyPage() =>
        new("<html>Blocked by corporate content filter</html>");

    [Fact]
    public async Task OpenAi_images_a_thrown_proxy_page_is_Failed_not_Refused_so_the_router_falls_over()
    {
        var http = new StubHttpHandler().Enqueue(_ => throw ProxyPage());
        var openAi = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k", Model = "gpt-image-1" },
            Client(http));
        var second = new FakeGenerationProvider { Id = "local" };

        var alone = await openAi.GenerateAsync(new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a cat" });
        var routed = await new MediaRouter([openAi, second]).GenerateAsync(
            Order("openai-images", "local"), new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a cat" });

        Assert.Equal(ProviderVerdict.Failed, alone.Verdict);
        Assert.True(routed.IsOk);
        Assert.Equal(1, second.GenerateCalls);
    }

    [Fact]
    public async Task Automatic1111_a_WebUI_that_drops_the_connection_mid_render_is_Failed_not_NotConfigured()
    {
        // only a server that is not LISTENING is an unconfigured candidate; one that crashed mid-render is a
        // fault, and NotConfigured would keep it out of the dead-host count forever
        var http = new StubHttpHandler().Enqueue(_ => throw DroppedMidResponse());
        var a1111 = new Automatic1111Provider(new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" }, Client(http));

        var dropped = await a1111.GenerateAsync(new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a cat" });

        var down = await new Automatic1111Provider(
                new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
                Client(new StubHttpHandler().Enqueue(_ => throw Refused())))
            .GenerateAsync(new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a cat" });

        Assert.Equal(ProviderVerdict.Failed, dropped.Verdict);
        Assert.Equal(ProviderVerdict.NotConfigured, down.Verdict);
    }

    [Fact]
    public async Task Automatic1111_a_thrown_proxy_page_is_Failed_not_Refused()
    {
        var http = new StubHttpHandler().Enqueue(_ => throw new InvalidOperationException("content policy violation"));
        var a1111 = new Automatic1111Provider(new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" }, Client(http));

        var result = await a1111.GenerateAsync(new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a cat" });

        Assert.Equal(ProviderVerdict.Failed, result.Verdict);
    }
}
