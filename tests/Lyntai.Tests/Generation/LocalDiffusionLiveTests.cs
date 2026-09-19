using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;

namespace Lyntai.Tests.Generation;

/// <summary>The local diffusion backend against a REAL <c>sd-cli</c>, which nothing in
/// <see cref="LocalDiffusionProviderTests"/> can stand in for: those pin the argv this backend BUILDS, and
/// only a real engine can say whether that argv is the one it ACCEPTS — the two diverged once (the retired
/// <c>img2img</c> mode value), which is exactly the divergence this test exists to catch.
///
/// <para>Skipped without <c>LYNTAI_SD_CLI</c> (the full path to <c>sd-cli.exe</c> — NEVER a prefix match,
/// the same release ships <c>sd-server.exe</c> beside it, which starts and waits forever) and
/// <c>LYNTAI_SD_MODEL</c> (any SD 1.5 checkpoint). A CPU render is minutes, not seconds — this suite is a
/// measurement, not a regression gate.</para></summary>
public class LocalDiffusionLiveTests
{
    private static string? Binary => Environment.GetEnvironmentVariable("LYNTAI_SD_CLI");
    private static string? Model => Environment.GetEnvironmentVariable("LYNTAI_SD_MODEL");

    [SkippableFact]
    public async Task A_real_engine_renders_txt2img_then_img2img_and_honours_the_size_clamp()
    {
        Skip.If(string.IsNullOrWhiteSpace(Binary) || string.IsNullOrWhiteSpace(Model),
            "set LYNTAI_SD_CLI to a real sd-cli.exe and LYNTAI_SD_MODEL to an SD 1.5 checkpoint");

        var options = new LocalDiffusionOptions
        {
            BinaryPath = Binary,
            ModelPath = Model,
            Steps = 4,   // measuring argv acceptance and dimensions, not image quality
        };
        var provider = new LocalDiffusionProvider(options, new ProcessRunner());

        // 250x250 exercises the clamp on the way in — rounded to the engine's multiple of 64 (256) and held
        // at its floor — and the engine closes the loop by accepting the argv and returning those dimensions.
        var rendered = await provider.GenerateAsync(Ask("a red square on a white background", png: null));

        Assert.True(rendered.IsOk, rendered.Detail);
        var png = rendered.Artifacts[0].Data!;
        Assert.Equal((256, 256), PngDimensions(png));

        // img2img is the half the unit tests could not defend: the engine reads the init flag's PRESENCE as
        // the switch, and the explicit mode pair the argv used to carry is an argv ERROR on this build.
        var edited = await provider.GenerateAsync(Ask("the same square, blue", png));

        Assert.True(edited.IsOk, edited.Detail);
        Assert.Equal((256, 256), PngDimensions(edited.Artifacts[0].Data!));
    }

    private static MediaRequest Ask(string prompt, byte[]? png) => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = prompt,
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "250x250" },
        Inputs = png is null ? [] : [new MediaInput("image/png", Data: png, Role: MediaInputRoles.Init)],
    };

    /// <summary>IHDR width/height — big-endian at offsets 16 and 20 — is enough to assert dimensions
    /// without an image library.</summary>
    private static (int Width, int Height) PngDimensions(byte[] png)
    {
        Assert.True(png.Length > 24 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "not a PNG");
        return (ReadBigEndian(png, 16), ReadBigEndian(png, 20));
    }

    private static int ReadBigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
