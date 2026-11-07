using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Faults the generation backends answered with a throw, a wrong path or a false success.</summary>
public class GenerationFaultTests : IDisposable
{
    private readonly List<ScratchDir> _scratch = [];

    public void Dispose() => _scratch.ForEach(s => s.Dispose());

    // ---- the deadline ----

    [Theory] // a budget past what a CancellationTokenSource takes is no deadline, not an ArgumentOutOfRange
    [InlineData(int.MaxValue)]
    [InlineData(60 * 60 * 24 * 50)]
    public async Task A_budget_past_the_clocks_range_runs_the_call_with_no_deadline(int seconds)
    {
        var budget = GenerationDeadline.Resolve(seconds, TimeSpan.FromMinutes(5));

        var result = await GenerationDeadline.GuardAsync(budget, CancellationToken.None,
            _ => Task.FromResult("ok"), reason => reason);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task TimeSpan_MaxValue_is_no_deadline_too()
    {
        var result = await GenerationDeadline.GuardAsync(TimeSpan.MaxValue, CancellationToken.None,
            _ => Task.FromResult("ok"), reason => reason);

        Assert.Equal("ok", result);
    }

    // ---- sd-cli ----

    private static string Relative(string path) => Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

    [Fact] // the engine is spawned from the binary's directory, so a relative argument would resolve there
    public async Task A_relative_model_and_work_directory_reach_the_engine_as_absolute_paths()
    {
        var scratch = new ScratchDir("sd-rel");
        _scratch.Add(scratch);
        var options = new LocalDiffusionOptions
        {
            BinaryPath = scratch.File("sd-cli.exe"),
            ModelPath = Relative(scratch.File("sd15.gguf")),
            WorkDirectory = Relative(scratch.Path),
        };
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) =>
            {
                var at = args.ToList().IndexOf("-o");
                File.WriteAllBytes(args[at + 1], [0x89, 0x50]);
                return new ProcessResult(0, "done", "");
            },
        };
        var provider = new LocalDiffusionProvider(options, runner);

        var result = await provider.GenerateAsync(
            new MediaRequest { Kind = ProviderKinds.Image, Prompt = "a red square" });

        Assert.True(result.IsOk, result.Detail);
        var args = runner.LastArgs!.ToList();
        Assert.True(Path.IsPathRooted(args[args.IndexOf("-m") + 1]));
        Assert.True(Path.IsPathRooted(args[args.IndexOf("-o") + 1]));
    }

    [Fact] // (int)Math.Round(value / 64.0) * 64 wrapped to int.MinValue and clamped to the floor
    public void A_side_near_int_MaxValue_clamps_to_the_ceiling_not_the_floor()
    {
        var (width, height) = LocalDiffusionProvider.ClampSize("2147483647x512", maxDimension: null);

        Assert.True(width > 256, $"width {width}");
        Assert.Equal(512, height);
    }

    // ---- piper ----

    [Fact] // an engine that exits 0 with no audio made nothing: the buffered mode must not report one empty artifact
    public async Task A_piper_run_that_streams_no_audio_is_a_failure()
    {
        var scratch = new ScratchDir("piper");
        _scratch.Add(scratch);
        var provider = new PiperProvider(
            new PiperOptions { BinaryPath = scratch.File("piper.exe"), ModelPath = scratch.File("voice.onnx") },
            new FakeProcessRunner { StreamBytes = [] });

        var response = await provider.GenerateAsync(
            new MediaRequest { Kind = ProviderKinds.Audio, Prompt = "..." });

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("no audio", response.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the images endpoint ----

    [Fact] // a malformed media type is the request's fault, not the host's: Unsupported, not a Failed strike
    public async Task An_init_image_with_a_malformed_media_type_is_Unsupported_not_a_host_failure()
    {
        var provider = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k", Model = "gpt-image-1" },
            () => new HttpClient(new StubHttpHandler(), disposeHandler: false));

        var response = await provider.GenerateAsync(new MediaRequest
        {
            Kind = ProviderKinds.Image,
            Prompt = "edit",
            Inputs = [new MediaInput("png", Data: [1, 2, 3], Role: MediaInputRoles.Init)],
        });

        Assert.Equal(ProviderVerdict.Unsupported, response.Verdict);
    }
}
