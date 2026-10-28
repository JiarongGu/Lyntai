using System.Diagnostics;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The OpenAI-images backend against a REAL local server that speaks the same shapes — NOT OpenAI,
/// so it proves the request this backend builds is one a second implementation accepts and answers, and
/// says nothing about OpenAI's current models (<see cref="OpenAiImageProviderTests"/> and
/// <see cref="OpenAiImageResponseFormatTests"/> hold the rest).
///
/// <para>Skipped without <c>LYNTAI_OPENAI_IMAGE_URL</c>, the API root including its version segment (e.g.
/// <c>http://127.0.0.1:17860/v1</c>). The run recorded in <c>docs/generation.md</c> used stable-diffusion.cpp's
/// <c>sd-server</c>; the OpenAI shape sends no step count, so the server's own default (20) applies.</para></summary>
public class OpenAiImageLiveTests(ITestOutputHelper output)
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_OPENAI_IMAGE_URL");

    private const string NotRunning = "set LYNTAI_OPENAI_IMAGE_URL to a local server's OpenAI-images root, e.g. http://127.0.0.1:17860/v1";

    private static OpenAiImageProvider Provider() =>
        new(new OpenAiImageOptions { BaseUrl = BaseUrl! }, () => new HttpClient());

    private static MediaRequest Ask(string prompt, byte[]? png = null) => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = prompt,
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "256x256" },
        Inputs = png is null ? [] : [new MediaInput("image/png", Data: png, Role: MediaInputRoles.Init)],
    };

    [SkippableFact]
    public async Task A_local_server_answers_the_models_probe()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);

        var probe = await Provider().ProbeAsync();

        Assert.True(probe.Available, probe.Detail);
    }

    [SkippableFact]
    public async Task A_local_server_renders_a_generation_as_inline_base64()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var clock = Stopwatch.StartNew();

        var rendered = await Provider().GenerateAsync(Ask("a red square on a white background"));

        Assert.True(rendered.IsOk, rendered.Detail);
        var artifact = Assert.Single(rendered.Artifacts);
        Assert.Equal((256, 256), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"generation 256x256 in {clock.Elapsed.TotalSeconds:F1} s");
    }

    [SkippableFact]
    public async Task A_local_server_accepts_the_multipart_edit_and_answers_with_the_edited_image()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var provider = Provider();

        var source = await provider.GenerateAsync(Ask("a red square on a white background"));
        Assert.True(source.IsOk, source.Detail);
        var clock = Stopwatch.StartNew();

        var edited = await provider.GenerateAsync(Ask("the same square, blue", source.Artifacts[0].Data));

        Assert.True(edited.IsOk, edited.Detail);
        var artifact = Assert.Single(edited.Artifacts);
        Assert.Equal((256, 256), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"edit 256x256 in {clock.Elapsed.TotalSeconds:F1} s");
    }
}
