using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The two <c>"WxH"</c> parsers in this package — <c>Automatic1111Provider.Size</c> and
/// <see cref="LocalDiffusionProvider.ClampSize"/> — must agree on WHICH hints are usable, even though they
/// deliberately disagree on what to do with a usable one (the local engine clamps to multiples of 64 within
/// a CONFIGURED ceiling because <c>sd-cli</c> requires it — the bound is derived from the accelerator rather
/// than fixed at 768, <c>docs/DECISIONS.md</c> D68; the WebUI serves arbitrary sizes and must not be clamped).
///
/// The half they share is that a size must be POSITIVE, not merely numeric. <c>"0x0"</c> parses, and forwarding
/// it hands the backend a render it can only reject — so a bad hint falls back to the configured default, which
/// is what both XML docs promise. They are pinned together, in one file, because the defect was precisely that
/// one of them enforced the shared half and the other did not: a per-provider test would not have caught the
/// divergence. No shared helper is extracted, deliberately — the two share two lines and diverge on the whole
/// clamp policy.</summary>
public class GenerationSizeHintParsingTests
{
    private const string OneByteBase64 = "iVBORw==";

    private static (Automatic1111Provider Provider, StubHttpHandler Http) WebUi()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, $"{{\"images\":[\"{OneByteBase64}\"]}}");
        return (new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    private static GenerationRequest Ask(string size) => new()
    {
        Kind = GenerationKinds.Image,
        Prompt = "a red square",
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = size },
    };

    [Theory]
    [InlineData("0x0")]
    [InlineData("0x512")]
    [InlineData("512x0")]
    [InlineData("-64x-64")]
    [InlineData("-1x1")]
    public async Task The_WebUI_backend_falls_back_to_its_default_rather_than_posting_a_non_positive_size(string size)
    {
        var (provider, http) = WebUi();

        var result = await provider.GenerateAsync(Ask(size));

        Assert.True(result.IsOk, result.Detail);
        // the option's documented defaults — NOT the hint, which the WebUI could only answer with an error
        Assert.Contains("\"width\":512", http.Requests[0].Body);
        Assert.Contains("\"height\":512", http.Requests[0].Body);
    }

    [Theory]
    [InlineData("0x0")]
    [InlineData("0x512")]
    [InlineData("512x0")]
    [InlineData("-64x-64")]
    [InlineData("-1x1")]
    public void The_local_engine_refuses_exactly_the_same_hints(string size)
    {
        Assert.Equal((512, 512), LocalDiffusionProvider.ClampSize(size));
    }

    [Fact]
    public async Task A_usable_hint_still_reaches_the_WebUI_unclamped()
    {
        // the guard must not cost the WebUI its arbitrary sizes: 1280x720 is legal there, while the local
        // engine's own rules would turn it into 768x448
        var (provider, http) = WebUi();

        await provider.GenerateAsync(Ask("1280x720"));

        Assert.Contains("\"width\":1280", http.Requests[0].Body);
        Assert.Contains("\"height\":720", http.Requests[0].Body);
        Assert.Equal((768, 448), LocalDiffusionProvider.ClampSize("1280x720"));
    }
}
