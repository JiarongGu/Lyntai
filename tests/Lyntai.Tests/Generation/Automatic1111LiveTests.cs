using System.Diagnostics;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The Automatic1111 backend against a REAL server speaking its API — the measurement behind
/// <see cref="Automatic1111ProviderTests"/>, whose stub can only echo the shapes this backend was written to.
///
/// <para>Skipped without <c>LYNTAI_A1111_URL</c> (e.g. <c>http://127.0.0.1:17860</c>). Any server that speaks
/// <c>/sdapi/v1</c> will do: the run recorded in <c>docs/generation.md</c> used stable-diffusion.cpp's
/// <c>sd-server</c>, which implements the WebUI's shapes and is NOT the WebUI. 256×256 at 4 steps, so the
/// suite measures the wire and not image quality; a cold Vulkan server compiles its shaders on the first
/// render and reads over a minute.</para></summary>
public class Automatic1111LiveTests(ITestOutputHelper output)
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_A1111_URL");

    private const string NotRunning = "set LYNTAI_A1111_URL to a running server that speaks /sdapi/v1";

    private static Automatic1111Provider Provider() =>
        new(new Automatic1111Options { BaseUrl = BaseUrl!, Steps = 4 }, () => new HttpClient());

    private static MediaRequest Ask(string prompt, string size, byte[]? png = null) => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = prompt,
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = size },
        Inputs = png is null ? [] : [new MediaInput("image/png", Data: png, Role: MediaInputRoles.Init)],
    };

    [SkippableFact]
    public async Task A_real_server_answers_the_probe_with_its_loaded_checkpoint()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);

        var probe = await Provider().ProbeAsync();

        Assert.True(probe.Available, probe.Detail);
        Assert.False(string.IsNullOrWhiteSpace(probe.Version),
            "sd-models answered but the probe found no model_name or title in it — the probe's fields are wrong");
        output.WriteLine($"probe: {probe.Detail}");
    }

    [SkippableFact]
    public async Task A_real_server_renders_txt2img_at_the_requested_size()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var clock = Stopwatch.StartNew();

        // not square, so a width/height swapped on the way out would show in the dimensions that come back
        var rendered = await Provider().GenerateAsync(Ask("a red square on a white background", "320x192"));

        Assert.True(rendered.IsOk, rendered.Detail);
        var artifact = Assert.Single(rendered.Artifacts);
        Assert.Equal((320, 192), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"txt2img 320x192 in {clock.Elapsed.TotalSeconds:F1} s");
    }

    [SkippableFact]
    public async Task A_real_server_renders_img2img_from_the_init_image_at_its_size()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var provider = Provider();

        // the source is rendered here, not committed: an img2img the server cannot decode fails the call
        var source = await provider.GenerateAsync(Ask("a red square on a white background", "256x256"));
        Assert.True(source.IsOk, source.Detail);
        var clock = Stopwatch.StartNew();

        var edited = await provider.GenerateAsync(Ask("the same square, blue", "256x256", source.Artifacts[0].Data));

        Assert.True(edited.IsOk, edited.Detail);
        var artifact = Assert.Single(edited.Artifacts);
        Assert.Equal((256, 256), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"img2img 256x256 in {clock.Elapsed.TotalSeconds:F1} s");
    }
}
