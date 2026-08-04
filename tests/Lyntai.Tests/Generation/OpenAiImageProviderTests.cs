using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Http;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The OpenAI-compatible images backend. Wire shapes are PORTED from a sibling app's
/// production code (both response variants — inline base64 and a URL — occur in practice), and driven here
/// through a stubbed handler so no request leaves the machine and no generation is billed.</summary>
public class OpenAiImageProviderTests
{
    private const string OneByteBase64 = "iVBORw==";   // not a real PNG; only its round-trip matters

    private static (OpenAiImageProvider Provider, StubHttpHandler Http) Provider(
        OpenAiImageOptions? options = null)
    {
        var handler = new StubHttpHandler();
        var provider = new OpenAiImageProvider(
            options ?? new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k", Model = "gpt-image-1" },
            () => new HttpClient(handler, disposeHandler: false));
        return (provider, handler);
    }

    private static GenerationRequest Ask(string prompt = "a red square") =>
        new() { Kind = GenerationKinds.Image, Prompt = prompt };

    [Fact]
    public void It_declares_only_what_it_can_do()
    {
        var (provider, _) = Provider();

        Assert.Equal("openai-images", provider.Id);
        Assert.Equal([GenerationKinds.Image], provider.Capabilities.Kinds);
        Assert.Equal([GenerationDelivery.Inline], provider.Capabilities.Deliveries);
        Assert.True(provider.Capabilities.SupportsInputs);          // /images/edits
        Assert.Empty(provider.Capabilities.Models);                 // catalogue not mirrored
        Assert.IsNotAssignableFrom<IGenerationJobProvider>(provider);
        Assert.IsNotAssignableFrom<IGenerationStreamProvider>(provider);
    }

    [Fact]
    public async Task A_text_to_image_call_posts_the_documented_body_and_returns_bytes()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"data\":[{{\"b64_json\":\"{OneByteBase64}\"}}]}}");

        var result = await provider.GenerateAsync(Ask());

        Assert.True(result.IsOk);
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
        Assert.Equal(Convert.FromBase64String(OneByteBase64), result.Artifacts[0].Data);
        Assert.Equal("https://example.invalid/v1/images/generations", http.Requests[0].Uri?.ToString());
        Assert.Contains("\"prompt\":\"a red square\"", http.Requests[0].Body);
        Assert.Contains("\"model\":\"gpt-image-1\"", http.Requests[0].Body);
        Assert.Contains("\"size\":\"1024x1024\"", http.Requests[0].Body);   // the documented default
        Assert.Equal("Bearer k", http.Requests[0].Auth);
    }

    [Fact]
    public async Task A_requested_size_overrides_the_default()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"data\":[{{\"b64_json\":\"{OneByteBase64}\"}}]}}");

        await provider.GenerateAsync(Ask() with
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "1792x1024" },
        });

        Assert.Contains("\"size\":\"1792x1024\"", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_url_response_is_returned_as_a_URI_artifact_not_silently_downloaded()
    {
        // the platform never fetches megabytes the caller didn't ask for — the artifact carries the location
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"url\":\"https://example.invalid/out.png\"}]}");

        var result = await provider.GenerateAsync(Ask());

        Assert.True(result.IsOk);
        Assert.Null(result.Artifacts[0].Data);
        Assert.Equal("https://example.invalid/out.png", result.Artifacts[0].Uri);
        Assert.Single(http.Requests);                                // no second request to fetch it
    }

    [Fact]
    public async Task An_input_image_switches_to_the_edits_endpoint_as_multipart()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"data\":[{{\"b64_json\":\"{OneByteBase64}\"}}]}}");

        var result = await provider.GenerateAsync(Ask("remove the background") with
        {
            Inputs = [new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.Init)],
        });

        Assert.True(result.IsOk);
        Assert.Equal("https://example.invalid/v1/images/edits", http.Requests[0].Uri?.ToString());
        Assert.Contains("remove the background", http.Requests[0].Body);   // multipart form field
    }

    [Fact]
    public async Task An_input_supplied_only_as_a_uri_is_refused_rather_than_fetched()
    {
        // this endpoint needs the BYTES; silently downloading the caller's URI would be the platform
        // deciding to spend bandwidth, and guessing at auth for someone else's host
        var (provider, http) = Provider();

        var result = await provider.GenerateAsync(Ask() with
        {
            Inputs = [new GenerationInput("image/png", Uri: "https://example.invalid/in.png")],
        });

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_missing_base_url_is_NOT_CONFIGURED_rather_than_a_failure()
    {
        // routing skips a not-configured backend without blame; calling it a failure would penalise a
        // backend the host simply hasn't set up
        var (provider, http) = Provider(new OpenAiImageOptions { BaseUrl = "" });

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GenerationVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, GenerationVerdict.Failed)]
    public async Task A_transport_failure_is_classified_not_swallowed(HttpStatusCode status, GenerationVerdict expected)
    {
        var (provider, http) = Provider();
        http.Enqueue(status, "{\"error\":{\"message\":\"nope\"}}");

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(expected, result.Verdict);
        Assert.Contains("nope", result.Detail);
    }

    [Fact]
    public async Task A_content_policy_refusal_is_a_REFUSAL_so_routing_can_honour_it()
    {
        // this is the verdict the routing policy hangs off — it must not arrive as a generic failure
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Your request was rejected as a result of our safety system / content policy\"}}");

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
    }

    [Fact]
    public async Task A_response_with_no_image_is_a_failure_not_an_empty_success()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"data\":[]}");

        var result = await provider.GenerateAsync(Ask());

        Assert.False(result.IsOk);
        Assert.Equal(GenerationVerdict.Failed, result.Verdict);
    }

    [Fact]
    public async Task A_probe_lists_models_and_never_generates()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"gpt-image-1\"}]}");

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("https://example.invalid/v1/models", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task A_probe_reports_unavailable_without_throwing_when_the_endpoint_rejects_it()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}");

        var probe = await provider.ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("401", probe.Detail);
    }

    [Fact]
    public async Task A_probe_on_an_unconfigured_backend_says_so_without_a_request()
    {
        var (provider, http) = Provider(new OpenAiImageOptions { BaseUrl = "" });

        var probe = await provider.ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("not configured", probe.Detail);
        Assert.Empty(http.Requests);
    }
}
