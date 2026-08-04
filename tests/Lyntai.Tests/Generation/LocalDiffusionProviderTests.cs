using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The local stable-diffusion.cpp backend — generation that runs entirely on the host's machine: no
/// key, no network, no content policy. Driven through the BYO <see cref="IProcessRunner"/>, so no binary is
/// spawned here.
///
/// The argv and the size-clamping rules are PORTED from a sibling app's working implementation (the engine
/// isn't installed on this dev machine, so they are production-proven rather than measured here). That is why
/// they are pinned by exact-argv assertions: if a future edit drifts from the shape a real `sd-cli` accepts,
/// these fail rather than a user's render failing.</summary>
public class LocalDiffusionProviderTests
{
    private static (LocalDiffusionProvider Provider, FakeProcessRunner Runner, string Dir) Provider(
        Action<LocalDiffusionOptions>? configure = null, bool createFiles = true)
    {
        var dir = Path.Combine(TestPaths.TestScratchDir, $"sd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "sd-cli.exe");
        var model = Path.Combine(dir, "sd15.gguf");
        if (createFiles)
        {
            File.WriteAllText(exe, "");
            File.WriteAllText(model, "");
        }

        var options = new LocalDiffusionOptions { BinaryPath = exe, ModelPath = model, WorkDirectory = dir };
        configure?.Invoke(options);
        var runner = new FakeProcessRunner();
        return (new LocalDiffusionProvider(options, runner), runner, dir);
    }

    /// <summary>Behave like the real engine: write the PNG to whatever path came after <c>-o</c>.</summary>
    private static Func<string, IReadOnlyList<string>, ProcessResult> ProducesImage(byte[]? bytes = null) =>
        (_, args) =>
        {
            var at = args.ToList().IndexOf("-o");
            if (at >= 0 && at + 1 < args.Count) File.WriteAllBytes(args[at + 1], bytes ?? [0x89, 0x50]);
            return new ProcessResult(0, "done", "");
        };

    private static GenerationRequest Ask(string prompt = "a red square", string? size = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (size is not null) options["size"] = size;
        return new GenerationRequest { Kind = GenerationKinds.Image, Prompt = prompt, Options = options };
    }

    [Fact]
    public void It_declares_a_local_inline_image_backend()
    {
        var (provider, _, _) = Provider();

        Assert.Equal("local-diffusion", provider.Id);
        Assert.Equal([GenerationKinds.Image], provider.Capabilities.Kinds);
        Assert.Equal([GenerationDelivery.Inline], provider.Capabilities.Deliveries);
        Assert.True(provider.Capabilities.SupportsInputs);
        Assert.IsNotAssignableFrom<IGenerationJobProvider>(provider);
    }

    [Fact]
    public async Task A_text_to_image_run_uses_the_ported_argv()
    {
        var (provider, runner, dir) = Provider();
        runner.RunHandler = ProducesImage();

        var result = await provider.GenerateAsync(Ask("a red square", "512x512"));

        Assert.True(result.IsOk, result.Detail);
        var args = runner.LastArgs!.ToList();
        Assert.Equal(Path.Combine(dir, "sd15.gguf"), args[args.IndexOf("-m") + 1]);
        Assert.Equal("a red square", args[args.IndexOf("-p") + 1]);
        Assert.Equal("512", args[args.IndexOf("-W") + 1]);
        Assert.Equal("512", args[args.IndexOf("-H") + 1]);
        Assert.Equal("20", args[args.IndexOf("--steps") + 1]);
        Assert.Equal("7", args[args.IndexOf("--cfg-scale") + 1]);
        Assert.DoesNotContain("img2img", args);
    }

    [Fact]
    public async Task It_spawns_from_the_BINARY_directory_so_the_engines_native_libraries_resolve()
    {
        // sd-cli loads ggml*.dll from beside itself; spawning from anywhere else fails at load time on a
        // perfectly good install (ported detail, and the reason this assertion exists)
        var (provider, runner, dir) = Provider();
        runner.RunHandler = ProducesImage();

        await provider.GenerateAsync(Ask());

        Assert.Equal(dir, runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task An_init_image_switches_to_img2img_and_is_written_for_the_engine_to_read()
    {
        var (provider, runner, _) = Provider();
        string? initPath = null;
        runner.RunHandler = (cmd, args) =>
        {
            var list = args.ToList();
            var at = list.IndexOf("-i");
            if (at >= 0) initPath = list[at + 1];
            return ProducesImage()(cmd, args);
        };

        var result = await provider.GenerateAsync(Ask("brighten it") with
        {
            Inputs = [new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.Init)],
        });

        Assert.True(result.IsOk, result.Detail);
        var args = runner.LastArgs!.ToList();
        Assert.Equal("img2img", args[args.IndexOf("-M") + 1]);
        Assert.Equal("0.5", args[args.IndexOf("--strength") + 1]);
        Assert.NotNull(initPath);
        // the engine reads the source from disk, so the bytes must have been staged before the spawn
        Assert.False(File.Exists(initPath), "the work directory should be cleaned up after the call");
    }

    [Theory]
    [InlineData("512x512", 512, 512)]
    [InlineData("1024x1024", 768, 768)]     // capped at SD-1.5's CPU-friendly ceiling
    [InlineData("1280x720", 768, 448)]      // aspect kept, each side rounded to a multiple of 64
    [InlineData("64x64", 256, 256)]         // floored
    [InlineData("enormous", 512, 512)]      // unparseable falls back rather than failing the render
    [InlineData(null, 512, 512)]
    public void The_size_clamp_matches_the_engines_constraints(string? size, int width, int height)
    {
        // sd.cpp requires multiples of 64; a CPU render at 1024+ is minutes of pointless waiting
        Assert.Equal((width, height), LocalDiffusionProvider.ClampSize(size));
    }

    [Fact]
    public async Task A_produced_image_comes_back_as_bytes()
    {
        var (provider, runner, _) = Provider();
        runner.RunHandler = ProducesImage([9, 9, 9, 9]);

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal("image/png", result.Artifacts[0].MediaType);
        Assert.Equal([9, 9, 9, 9], result.Artifacts[0].Data);
    }

    [Fact]
    public async Task An_unprovisioned_engine_is_NOT_CONFIGURED_and_never_spawns()
    {
        // the binary and weights are the HOST's to download (D26) — absent is a normal state to report, not a
        // failure to blame the backend for
        var (provider, runner, _) = Provider(createFiles: false);

        var result = await provider.GenerateAsync(Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.False(probe.Available);
        Assert.Contains("not configured", probe.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_probe_is_free_and_checks_both_the_binary_and_the_weights()
    {
        var (provider, runner, _) = Provider();

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Empty(runner.Calls);          // presence on disk answers it — nothing is generated
    }

    [Fact]
    public async Task A_model_file_missing_beside_a_present_binary_still_reports_unavailable()
    {
        var (provider, _, dir) = Provider();
        File.Delete(Path.Combine(dir, "sd15.gguf"));

        var probe = await provider.ProbeAsync();

        Assert.False(probe.Available);
        Assert.Contains("model", probe.Detail);
    }

    [Fact]
    public async Task A_failed_run_reports_the_engines_stderr()
    {
        var (provider, runner, _) = Provider();
        runner.RunHandler = (_, _) => new ProcessResult(1, "", "failed to load model: out of memory");

        var result = await provider.GenerateAsync(Ask());

        Assert.False(result.IsOk);
        Assert.Contains("out of memory", result.Detail);
    }

    [Fact]
    public async Task A_clean_exit_that_produced_no_file_is_a_failure_not_an_empty_success()
    {
        var (provider, runner, _) = Provider();
        runner.RunHandler = (_, _) => new ProcessResult(0, "finished", "");   // exits 0, writes nothing

        var result = await provider.GenerateAsync(Ask());

        Assert.False(result.IsOk);
        Assert.Contains("no image", result.Detail);
    }

    [Fact]
    public async Task A_stalled_engine_is_a_TIMEOUT_verdict()
    {
        var (provider, runner, _) = Provider();
        runner.RunResult = new ProcessResult(0, "", "", ProcessTimeoutKind.Inactivity);

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Timeout, result.Verdict);
    }

    [Fact]
    public async Task The_render_gets_an_inactivity_clock_with_an_absolute_backstop_not_one_wall_clock()
    {
        // CPU diffusion is slow but chatty: a single wall clock kills a healthy render, which is exactly the
        // trap pitfalls.md documents for the LLM side
        var (provider, runner, _) = Provider(o => o.Timeout = TimeSpan.FromMinutes(30));
        runner.RunHandler = ProducesImage();

        await provider.GenerateAsync(Ask());

        Assert.Equal(TimeSpan.FromMinutes(30), runner.LastMaxDuration);
        Assert.True(runner.LastInactivityTimeout < runner.LastMaxDuration,
            "the inactivity window must be shorter than the absolute backstop");
    }

    [Fact]
    public async Task A_URI_only_input_is_refused_rather_than_fetched()
    {
        var (provider, runner, _) = Provider();

        var result = await provider.GenerateAsync(Ask() with
        {
            Inputs = [new GenerationInput("image/png", Uri: "https://example.invalid/in.png")],
        });

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task The_work_directory_is_cleaned_up_even_when_the_run_fails()
    {
        var (provider, runner, dir) = Provider();
        runner.RunHandler = (_, _) => throw new InvalidOperationException("spawn exploded");

        var result = await provider.GenerateAsync(Ask());

        Assert.False(result.IsOk);
        Assert.Empty(Directory.GetDirectories(dir));   // no per-call scratch left behind
    }
}
