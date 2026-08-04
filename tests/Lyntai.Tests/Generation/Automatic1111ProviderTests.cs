using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The Stable Diffusion WebUI (Automatic1111) backend — a LOCALLY-run server, so no key and no
/// content policy in the path. Wire shapes ported from a sibling app's production implementation
/// (<c>/sdapi/v1/txt2img</c>, <c>/sdapi/v1/img2img</c>, <c>{ images: [base64] }</c>).</summary>
public class Automatic1111ProviderTests
{
    private const string OneByteBase64 = "iVBORw==";

    private static (Automatic1111Provider Provider, StubHttpHandler Http) Provider(
        Automatic1111Options? options = null)
    {
        var handler = new StubHttpHandler();
        var provider = new Automatic1111Provider(
            options ?? new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(handler, disposeHandler: false));
        return (provider, handler);
    }

    private static GenerationRequest Ask(string prompt = "a red square") =>
        new() { Kind = GenerationKinds.Image, Prompt = prompt };

    [Fact]
    public void It_declares_only_what_it_can_do()
    {
        var (provider, _) = Provider();

        Assert.Equal("a1111", provider.Id);
        Assert.Equal([GenerationKinds.Image], provider.Capabilities.Kinds);
        Assert.Equal([GenerationDelivery.Inline], provider.Capabilities.Deliveries);
        Assert.True(provider.Capabilities.SupportsInputs);
    }

    [Fact]
    public async Task Text_to_image_posts_txt2img_with_the_parsed_size()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"images\":[\"{OneByteBase64}\"]}}");

        var result = await provider.GenerateAsync(Ask() with
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "768x512" },
        });

        Assert.True(result.IsOk);
        Assert.Equal(Convert.FromBase64String(OneByteBase64), result.Artifacts[0].Data);
        Assert.Equal("http://127.0.0.1:7860/sdapi/v1/txt2img", http.Requests[0].Uri?.ToString());
        Assert.Contains("\"width\":768", http.Requests[0].Body);
        Assert.Contains("\"height\":512", http.Requests[0].Body);
        Assert.Contains("\"prompt\":\"a red square\"", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_malformed_size_falls_back_to_the_default_rather_than_failing()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"images\":[\"{OneByteBase64}\"]}}");

        await provider.GenerateAsync(Ask() with
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "enormous" },
        });

        Assert.Contains("\"width\":512", http.Requests[0].Body);   // the option's documented default
    }

    [Fact]
    public async Task An_input_image_switches_to_img2img_and_carries_it_as_init_images()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"images\":[\"{OneByteBase64}\"]}}");

        var result = await provider.GenerateAsync(Ask("brighten it") with
        {
            Inputs = [new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.Init)],
        });

        Assert.True(result.IsOk);
        Assert.Equal("http://127.0.0.1:7860/sdapi/v1/img2img", http.Requests[0].Uri?.ToString());
        Assert.Contains("init_images", http.Requests[0].Body);
        Assert.Contains(Convert.ToBase64String(new byte[] { 1, 2, 3 }), http.Requests[0].Body);
        Assert.Contains("denoising_strength", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_data_url_prefixed_image_is_still_decoded()
    {
        // the WebUI sometimes returns a data: URL rather than bare base64 — both come back as bytes
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"images\":[\"data:image/png;base64,{OneByteBase64}\"]}}");

        var result = await provider.GenerateAsync(Ask());

        Assert.True(result.IsOk);
        Assert.Equal(Convert.FromBase64String(OneByteBase64), result.Artifacts[0].Data);
    }

    [Fact]
    public async Task A_URI_only_input_is_refused_rather_than_fetched()
    {
        var (provider, http) = Provider();

        var result = await provider.GenerateAsync(Ask() with
        {
            Inputs = [new GenerationInput("image/png", Uri: "https://example.invalid/in.png")],
        });

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_unreachable_local_server_is_NOT_CONFIGURED_not_a_hard_failure()
    {
        // a local WebUI that simply isn't running is the normal case on a fresh machine: routing should skip
        // to the next candidate, and a host should be told to start it — not shown a stack trace
        var handler = new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("connection refused"));
        var provider = new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(handler, disposeHandler: false));

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.Contains("connection refused", result.Detail);
    }

    [Fact]
    public async Task A_server_error_is_a_failure_with_the_bodys_reason()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "{\"error\":\"OutOfMemoryError\",\"detail\":\"\"}");

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Failed, result.Verdict);
        Assert.Contains("OutOfMemory", result.Detail);
    }

    [Fact]
    public async Task An_empty_images_array_is_a_failure_not_an_empty_success()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"images\":[]}");

        var result = await provider.GenerateAsync(Ask());

        Assert.False(result.IsOk);
    }

    [Fact]
    public async Task A_probe_asks_for_the_loaded_checkpoints_and_reports_one_as_the_version()
    {
        // "which checkpoint is loaded" is the useful answer for a local server, and it is free
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "[{\"title\":\"sd_xl_base_1.0.safetensors\",\"model_name\":\"sd_xl_base_1.0\"}]");

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("http://127.0.0.1:7860/sdapi/v1/sd-models", http.Requests[0].Uri?.ToString());
        Assert.Contains("sd_xl_base_1.0", probe.Detail);
    }

    [Fact]
    public async Task A_probe_against_a_server_with_no_checkpoints_reports_unavailable()
    {
        // the server answered, but it cannot generate anything — "up" is not the same as "usable"
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "[]");

        var probe = await provider.ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("no checkpoint", probe.Detail);
    }

    [Fact]
    public async Task A_probe_on_a_down_server_fails_safe()
    {
        var handler = new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("connection refused"));
        var provider = new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(handler, disposeHandler: false));

        var probe = await provider.ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("connection refused", probe.Detail);
    }
}
