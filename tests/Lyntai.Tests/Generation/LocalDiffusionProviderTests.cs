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
        // perfectly good install — measured against a real release by a consuming app, not merely ported
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

    // ---- the ported argv is CORRECTABLE without a library release ------------------------------------
    //
    // The class header says this argv is PORTED from a working implementation, not measured here. The
    // engine is a third-party binary whose flags can be renamed upstream between releases, so a host that
    // hits that must be able to edit configuration rather than wait for a Lyntai release.

    private static LocalDiffusionProvider Configured(Action<LocalDiffusionOptions> configure)
    {
        var options = new LocalDiffusionOptions { BinaryPath = "sd", ModelPath = "m" };
        configure(options);
        return new LocalDiffusionProvider(options, new FakeProcessRunner());
    }

    [Fact]
    public void A_host_can_rename_one_flag_without_restating_the_others()
    {
        var provider = Configured(o => o.ArgvFlags["cfg-scale"] = "--cfg");

        var args = provider.BuildArgs("model.gguf", "a cat", "out.png", 512, 512, initPath: null);

        Assert.Contains("--cfg", args);
        Assert.DoesNotContain("--cfg-scale", args);
        Assert.Equal("7", args[args.IndexOf("--cfg") + 1]);
        // everything NOT overridden keeps its ported spelling — a partial override is the common case
        Assert.Contains("--steps", args);
        Assert.Contains("-W", args);
    }

    [Fact]
    public void A_host_can_rename_the_img2img_flags_and_the_mode_VALUE()
    {
        var provider = Configured(o =>
        {
            o.ArgvFlags["init"] = "--init-img";
            o.ArgvFlags["mode"] = "--mode";
            o.Img2ImgMode = "i2i";
        });

        var args = provider.BuildArgs("model.gguf", "a cat", "out.png", 512, 512, initPath: "init.png");

        Assert.Equal("i2i", args[args.IndexOf("--mode") + 1]);
        Assert.Equal("init.png", args[args.IndexOf("--init-img") + 1]);
    }

    [Fact]
    public void Extra_args_are_appended_verbatim_and_LAST()
    {
        // Safe for THIS engine because its argv is options all the way down — no trailing positional the way
        // codex's `-` stdin marker is, where anything after it is swallowed as prompt text (D65).
        var provider = Configured(o => o.ExtraArgs = ["--seed", "42", "--vae", "vae.safetensors"]);

        var args = provider.BuildArgs("model.gguf", "a cat", "out.png", 512, 512, initPath: null);

        Assert.Equal(["--seed", "42", "--vae", "vae.safetensors"], args[^4..]);
    }

    [Fact]
    public void The_unconfigured_argv_is_byte_identical_to_the_ported_one()
    {
        // The flags became a lookup rather than literals; an unconfigured host must not be able to tell.
        var ported = Configured(_ => { }).BuildArgs("model.gguf", "a cat", "out.png", 512, 512, "init.png");

        Assert.Equal(
            ["-m", "model.gguf", "-p", "a cat", "-o", "out.png", "-W", "512", "-H", "512",
             "--steps", "20", "--cfg-scale", "7", "-M", "img2img", "-i", "init.png", "--strength", "0.5"],
            ported);
    }

    // ---- the ceiling is the HOST's, and it has to reach both halves of the clamp ----------------------

    [Fact]
    public void A_raised_ceiling_moves_the_ROUNDING_too_not_just_the_scale()
    {
        // THE DEFECT THIS PINS. The clamp scaled against one hard-coded 768 and then rounded through a
        // SECOND, so a raised ceiling moved the scale and left every result pinned at 768 by the rounder —
        // a knob that appears to work and cannot exceed its old value. Both halves now take the same cap,
        // and 1024 is chosen here precisely because it is above the old constant.
        Assert.Equal((1024, 1024), LocalDiffusionProvider.ClampSize("1024x1024", 1024));
        Assert.Equal((1024, 576), LocalDiffusionProvider.ClampSize("1280x720", 1024));
    }

    [Fact]
    public void No_ceiling_leaves_a_large_request_alone_apart_from_the_engines_rounding()
    {
        // What a GPU host gets by default: the multiple-of-64 requirement still applies (it is the engine's,
        // not a policy), and nothing else is imposed.
        Assert.Equal((1024, 1792), LocalDiffusionProvider.ClampSize("1024x1792", null));
        Assert.Equal((1024, 1792), LocalDiffusionProvider.ClampSize("1020x1790", null));   // rounded to 64
    }

    [Fact]
    public void The_engines_floor_outranks_a_ceiling_set_below_it()
    {
        // A cap under the floor asks for something the engine cannot render. Honouring it would turn a
        // policy into a failed render, so the floor wins.
        Assert.Equal((256, 256), LocalDiffusionProvider.ClampSize("512x512", 64));
    }

    [Fact]
    public void A_cap_that_is_not_a_multiple_of_64_cannot_defeat_the_engines_rounding()
    {
        // The multiple-of-64 rule is the ENGINE's; the cap is the host's policy, and nothing validates it.
        // A bare clamp against a 1000 cap returns 1000 (1000 % 64 = 40) straight into sd-cli's argv — so the
        // cap itself is floored to a multiple of 64 before it may win.
        Assert.Equal((960, 960), LocalDiffusionProvider.ClampSize("4096x4096", 1000));
        Assert.Equal((256, 256), LocalDiffusionProvider.ClampSize("512x512", 300));

        // ...and a cap that already honours the rule is untouched by the flooring.
        Assert.Equal((960, 960), LocalDiffusionProvider.ClampSize("4096x4096", 960));
    }

    [Theory]
    [InlineData(DiffusionAccelerator.Cpu, 768)]
    [InlineData(DiffusionAccelerator.Gpu, null)]
    public void The_declared_accelerator_derives_the_default_ceiling(DiffusionAccelerator accelerator, int? expected)
    {
        // A DECLARATION, never a probe: which build of the engine is installed is something the host knows
        // and this library cannot. Gpu derives NO ceiling rather than an invented number — the reason for
        // the CPU cap is that each step is expensive, and that premise does not survive acceleration.
        var options = new LocalDiffusionOptions { Accelerator = accelerator };
        Assert.Equal(expected, options.EffectiveMaxDimension);
    }

    [Fact]
    public void An_explicit_ceiling_outranks_the_one_the_accelerator_derives()
    {
        var capped = new LocalDiffusionOptions { Accelerator = DiffusionAccelerator.Gpu, MaxDimension = 1536 };
        var lowered = new LocalDiffusionOptions { Accelerator = DiffusionAccelerator.Cpu, MaxDimension = 512 };

        Assert.Equal(1536, capped.EffectiveMaxDimension);
        Assert.Equal(512, lowered.EffectiveMaxDimension);
    }

    [Fact]
    public void The_ADVERTISED_ceiling_follows_the_configured_one_rather_than_a_constant()
    {
        // The third site the cap had to reach, and the easiest to miss: Limits is informational, so a stale
        // number here fails nothing — it just tells a consumer a ceiling the backend does not have.
        static LocalDiffusionProvider Provider(Action<LocalDiffusionOptions> configure)
        {
            var options = new LocalDiffusionOptions { BinaryPath = "sd", ModelPath = "m" };
            configure(options);
            return new LocalDiffusionProvider(options, new FakeProcessRunner());
        }

        var cpu = Provider(_ => { });
        var gpu = Provider(o => o.Accelerator = DiffusionAccelerator.Gpu);
        var explicitCap = Provider(o => o.MaxDimension = 1536);

        Assert.Equal("768", cpu.Capabilities.Limits["max-width"]);
        Assert.Equal("1536", explicitCap.Capabilities.Limits["max-height"]);

        // No ceiling → the keys are OMITTED, which reads as "not enumerated". Advertising a large number
        // instead would be inventing the ceiling this profile deliberately refuses to invent.
        Assert.False(gpu.Capabilities.Limits.ContainsKey("max-width"));
        Assert.False(gpu.Capabilities.Limits.ContainsKey("max-height"));

        // The engine's own requirement holds on every profile — it is not a policy.
        foreach (var provider in new[] { cpu, gpu, explicitCap })
            Assert.Equal("64", provider.Capabilities.Limits["size-multiple-of"]);
    }

    [Fact]
    public void The_ADVERTISED_ceiling_follows_a_LATE_provisioned_option_change()
    {
        // The registration doc advertises late provisioning — "the next render reads the current values" —
        // and enforcement (ClampSize) does read the options live. An advertisement captured once at
        // construction is the D68 stale-limit defect reintroduced through the mutable-options path: the
        // enforced ceiling moves and Capabilities.Limits keeps reporting the old one forever.
        var options = new LocalDiffusionOptions { BinaryPath = "sd", ModelPath = "m" };
        var provider = new LocalDiffusionProvider(options, new FakeProcessRunner());

        Assert.Equal("768", provider.Capabilities.Limits["max-width"]);

        options.Accelerator = DiffusionAccelerator.Gpu;
        Assert.False(provider.Capabilities.Limits.ContainsKey("max-width"));

        options.MaxDimension = 1536;
        Assert.Equal("1536", provider.Capabilities.Limits["max-height"]);
    }

    [Fact]
    public void The_default_options_render_exactly_as_they_did_before_the_ceiling_became_configurable()
    {
        // The shipped default is byte-identical to the hard-coded behaviour it replaced. A knob that changes
        // what an unconfigured host gets is a silent behaviour change wearing a feature's clothes.
        var options = new LocalDiffusionOptions();
        foreach (var size in new[] { "512x512", "1024x1024", "1280x720", "64x64", null })
            Assert.Equal(
                LocalDiffusionProvider.ClampSize(size),
                LocalDiffusionProvider.ClampSize(size, options.EffectiveMaxDimension));
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
        // the binary and weights are the HOST's to download (D20) — absent is a normal state to report, not a
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
