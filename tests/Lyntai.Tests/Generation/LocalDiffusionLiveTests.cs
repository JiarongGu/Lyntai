using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The local diffusion backend against a REAL <c>sd-cli</c>, which nothing in
/// <see cref="LocalDiffusionProviderTests"/> can stand in for: those pin the argv this backend BUILDS, and
/// only a real engine can say whether that argv is the one it ACCEPTS — an upstream retirement such as the
/// <c>img2img</c> mode value is exactly the divergence this test exists to catch.
///
/// <para>Skipped without <c>LYNTAI_SD_CLI</c> (the full path to <c>sd-cli.exe</c> — NEVER a prefix match,
/// the same release ships <c>sd-server.exe</c> beside it, which starts and waits forever) and
/// <c>LYNTAI_SD_MODEL</c> (any SD 1.5 checkpoint). A CPU render is minutes, not seconds — this suite is a
/// measurement, not a regression gate.</para>
///
/// <para>The checkpoint and LoRA journeys need the model folders too: <c>LYNTAI_SD_CHECKPOINT_DIR</c> and a second
/// checkpoint's file name in <c>LYNTAI_SD_SECOND_CHECKPOINT</c> (SDXL base), and <c>LYNTAI_SD_LORA_DIR</c> with the LCM
/// LoRA's name in <c>LYNTAI_SD_LORA</c>. Each skips naming what is missing. <c>LYNTAI_LIVE_OUTPUT_DIR</c> keeps the
/// renders for a person to look at.</para></summary>
public class LocalDiffusionLiveTests(ITestOutputHelper output)
{
    private static string? Binary => Environment.GetEnvironmentVariable("LYNTAI_SD_CLI");
    private static string? Model => Environment.GetEnvironmentVariable("LYNTAI_SD_MODEL");
    private static string? CheckpointDir => Environment.GetEnvironmentVariable("LYNTAI_SD_CHECKPOINT_DIR");
    private static string? SecondCheckpoint => Environment.GetEnvironmentVariable("LYNTAI_SD_SECOND_CHECKPOINT");
    private static string? LoraDir => Environment.GetEnvironmentVariable("LYNTAI_SD_LORA_DIR");
    private static string? Lora => Environment.GetEnvironmentVariable("LYNTAI_SD_LORA");

    /// <summary>Keeps each run's engine log, so a test can say which model the engine said it loaded.</summary>
    private sealed class LoggingRunner(IProcessRunner inner) : IProcessRunner
    {
        public string Log { get; private set; } = "";

        public async Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default)
        {
            var result = await inner.RunAsync(command, args, stdin, inactivityTimeout, maxDuration,
                workingDirectory, environment, ct).ConfigureAwait(false);
            Log = result.StdOut + result.StdErr;
            return result;
        }

        public IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) =>
            inner.StreamLinesAsync(command, args, stdin, inactivityTimeout, maxDuration, workingDirectory, environment, ct);
    }

    private LocalDiffusionProvider Engine(LoggingRunner runner, Action<LocalDiffusionOptions> configure)
    {
        var options = new LocalDiffusionOptions
        {
            BinaryPath = Binary,
            ModelPath = Model,
            Accelerator = DiffusionAccelerator.Gpu,
        };
        configure(options);
        return new LocalDiffusionProvider(options, runner);
    }

    private async Task<(byte[] Png, double Seconds)> Render(LocalDiffusionProvider engine, MediaRequest request)
    {
        var clock = Stopwatch.StartNew();
        var rendered = await engine.GenerateAsync(request);
        Assert.True(rendered.IsOk, rendered.Detail);
        return (rendered.Artifacts[0].Data!, clock.Elapsed.TotalSeconds);
    }

    /// <summary>The engine's own account of one generation, which leaves out process start and model load.</summary>
    private static string EngineSeconds(string log) =>
        System.Text.RegularExpressions.Regex.Match(log, @"generate_image completed in ([\d.]+)s") is { Success: true } m
            ? $"{m.Groups[1].Value} s" : "n/a";

    [SkippableFact]
    public async Task Two_checkpoints_switch_between_consecutive_requests_by_name()
    {
        Skip.If(string.IsNullOrWhiteSpace(Binary) || string.IsNullOrWhiteSpace(Model) ||
                string.IsNullOrWhiteSpace(CheckpointDir) || string.IsNullOrWhiteSpace(SecondCheckpoint),
            "set LYNTAI_SD_CLI, LYNTAI_SD_MODEL, LYNTAI_SD_CHECKPOINT_DIR and LYNTAI_SD_SECOND_CHECKPOINT (SDXL base)");
        var runner = new LoggingRunner(new ProcessRunner());
        var engine = Engine(runner, o => { o.CheckpointDirectory = CheckpointDir; o.Steps = 4; });
        var first = Path.GetFileName(Model!);

        // the same words and seed on two checkpoints, SD 1.5 and back again: the engine's own log names the file
        // it loaded, so it is the checkpoint that changed and not the image by chance
        var requests = new[] { first, SecondCheckpoint!, first }.Select(name => Ask("a red apple on a wooden table", png: null) with
        {
            Checkpoint = name,
            Seed = 7,
            CfgScale = 1,
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "512x512" },
        }).ToList();

        var renders = new List<byte[]>();
        foreach (var request in requests)
        {
            var (png, seconds) = await Render(engine, request);
            Assert.Equal((512, 512), LivePng.Dimensions(png));
            Assert.Contains(request.Checkpoint!, runner.Log);
            output.WriteLine($"checkpoint {request.Checkpoint}: 512x512, 4 steps, seed 7 in {seconds:F1} s");
            LivePng.Keep($"sd-cli-{renders.Count + 1}-{Path.GetFileNameWithoutExtension(request.Checkpoint)}.png", png);
            renders.Add(png);
        }

        Assert.NotEqual(renders[0], renders[1]);   // another model, another picture
        Assert.Equal(renders[0], renders[2]);      // and back: the first checkpoint again, bit for bit
    }

    [SkippableFact]
    public async Task The_LCM_LoRA_makes_a_four_step_render_at_CFG_one_a_recognisable_image()
    {
        Skip.If(string.IsNullOrWhiteSpace(Binary) || string.IsNullOrWhiteSpace(Model) ||
                string.IsNullOrWhiteSpace(LoraDir) || string.IsNullOrWhiteSpace(Lora),
            "set LYNTAI_SD_CLI, LYNTAI_SD_MODEL (SD 1.5), LYNTAI_SD_LORA_DIR and LYNTAI_SD_LORA (the LCM LoRA's name)");
        var runner = new LoggingRunner(new ProcessRunner());
        var engine = Engine(runner, o => o.LoraDirectory = LoraDir);
        var request = Ask("a red apple on a wooden table", png: null) with
        {
            Steps = 4,
            CfgScale = 1,
            Seed = 7,
            Sampler = "lcm",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = "512x512" },
        };

        await Render(engine, request);   // compiles the shaders and warms the file cache, so neither timing below carries it
        var (without, withoutSeconds) = await Render(engine, request);
        var withoutEngine = EngineSeconds(runner.Log);
        var (with, withSeconds) = await Render(engine, request with { Loras = [new MediaLora(Lora!, 1.0)] });
        Assert.Contains("(834 / 834) LoRA tensors have been applied", runner.Log);   // the engine applied all of it

        var (detailWithout, detailWith) = (LivePng.Detail(without), LivePng.Detail(with));
        output.WriteLine($"512x512, 4 steps, CFG 1, seed 7, sampler lcm: without the LoRA {withoutSeconds:F1} s per call, " +
                         $"{withoutEngine} in the engine (detail {detailWithout:F2}); with {Lora} at 1.0 {withSeconds:F1} s " +
                         $"per call, {EngineSeconds(runner.Log)} in the engine (detail {detailWith:F2})");
        LivePng.Keep("sd-cli-4steps-without-lora.png", without);
        LivePng.Keep("sd-cli-4steps-with-lora.png", with);

        Assert.NotEqual(without, with);
        Assert.True(detailWith > 1.5 * detailWithout,
            $"the LoRA should turn a four-step smear into a picture; detail {detailWithout:F2} -> {detailWith:F2}");
    }

    [SkippableFact]
    public async Task A_seed_and_a_negative_prompt_reach_the_engine()
    {
        Skip.If(string.IsNullOrWhiteSpace(Binary) || string.IsNullOrWhiteSpace(Model),
            "set LYNTAI_SD_CLI to a real sd-cli.exe and LYNTAI_SD_MODEL to an SD 1.5 checkpoint");
        var engine = Engine(new LoggingRunner(new ProcessRunner()), o => o.Steps = 4);
        var request = Ask("a red apple on a wooden table", png: null) with { Seed = 11 };

        var (a, _) = await Render(engine, request);
        var (b, _) = await Render(engine, request);
        var (otherSeed, _) = await Render(engine, request with { Seed = 12 });
        var (negated, _) = await Render(engine, request with { NegativePrompt = "red" });

        Assert.Equal(a, b);               // one seed, one image
        Assert.NotEqual(a, otherSeed);    // the seed is what decides it
        Assert.NotEqual(a, negated);      // and a negative prompt moves it
    }

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

        // img2img is the half the unit tests cannot defend: the engine reads the init flag's PRESENCE as the
        // switch, and an explicit img2img mode pair is an argv ERROR on this build.
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
