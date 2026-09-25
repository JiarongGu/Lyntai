using System.Net;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Whether the OpenAI images backend sends <c>response_format</c>. OpenAI's GPT-image family — the only
/// image models it still serves — is reported to REJECT the parameter (400) and always answers base64, while an
/// older or local OpenAI-shaped server answers a URL unless asked for <c>b64_json</c>. Unmeasured against the
/// service; these pin the rule the provider ships and the option that overrides it.</summary>
public class OpenAiImageResponseFormatTests
{
    private const string Reply = """{"data":[{"b64_json":"iVBORw=="}]}""";

    private static (OpenAiImageProvider Provider, StubHttpHandler Http) Provider(Action<OpenAiImageOptions>? configure = null)
    {
        var options = new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k" };
        configure?.Invoke(options);
        var http = new StubHttpHandler();
        http.Enqueue(HttpStatusCode.OK, Reply);
        return (new OpenAiImageProvider(options, () => new HttpClient(http, disposeHandler: false)), http);
    }

    private static MediaRequest Ask(string? model = null, params MediaInput[] inputs) =>
        new() { Kind = ProviderKinds.Image, Prompt = "a red square", Model = model, Inputs = inputs };

    [Theory]
    [InlineData("gpt-image-1")]
    [InlineData("gpt-image-2")]
    [InlineData("GPT-Image-1.5")]
    [InlineData("chatgpt-image-latest")]
    public async Task A_gpt_image_model_is_sent_no_response_format(string model)
    {
        var (provider, http) = Provider();

        var result = await provider.GenerateAsync(Ask(model));

        Assert.True(result.IsOk);
        Assert.DoesNotContain("response_format", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_gpt_image_edit_is_sent_no_response_format_either()
    {
        var (provider, http) = Provider(o => o.Model = "gpt-image-1");

        await provider.GenerateAsync(Ask(null, MediaInput.Init([1, 2, 3], "image/png")));

        Assert.EndsWith("/images/edits", http.Requests[0].Uri?.AbsolutePath);
        Assert.DoesNotContain("response_format", http.Requests[0].Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dall-e-3")]
    [InlineData("sd-turbo")]
    public async Task Any_other_model_is_still_asked_for_b64_json(string? model)
    {
        var (provider, http) = Provider();

        await provider.GenerateAsync(Ask(model));

        Assert.Contains("\"response_format\":\"b64_json\"", http.Requests[0].Body);
    }

    [Fact]
    public async Task The_option_overrides_the_rule_in_both_directions()
    {
        var (never, neverHttp) = Provider(o => o.ResponseFormat = "");
        await never.GenerateAsync(Ask("dall-e-3"));
        Assert.DoesNotContain("response_format", neverHttp.Requests[0].Body);

        var (always, alwaysHttp) = Provider(o => o.ResponseFormat = "b64_json");
        await always.GenerateAsync(Ask("gpt-image-1"));
        Assert.Contains("\"response_format\":\"b64_json\"", alwaysHttp.Requests[0].Body);
    }

    [Fact]
    public async Task A_url_reply_is_returned_as_a_uri_artifact()
    {
        var options = new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k" };
        var http = new StubHttpHandler();
        http.Enqueue(HttpStatusCode.OK, """{"data":[{"url":"https://cdn.invalid/a.png"}]}""");
        var provider = new OpenAiImageProvider(options, () => new HttpClient(http, disposeHandler: false));

        var result = await provider.GenerateAsync(Ask("gpt-image-1"));

        Assert.True(result.IsOk);
        Assert.Equal("https://cdn.invalid/a.png", result.Artifacts[0].Uri);
    }
}
