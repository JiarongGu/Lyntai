using Lyntai.Processes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="LocalDiffusionProvider"/>. The engine and its weights are the HOST's
/// to provide — Lyntai drives what is already on disk and never downloads either (<c>docs/DECISIONS.md</c>
/// D20).</summary>
public sealed class LocalDiffusionOptions
{
    /// <summary>Path to the <c>sd-cli</c> executable. Absent → the backend reports
    /// <see cref="GenerationVerdict.NotConfigured"/>.</summary>
    public string? BinaryPath { get; set; }

    /// <summary>Path to the model weights (a GGUF/safetensors file the engine accepts).</summary>
    public string? ModelPath { get; set; }

    /// <summary>Where per-call scratch (the output PNG, a staged init image) is written. Each call gets its own
    /// subdirectory, deleted afterwards. Defaults to the OS temp directory.</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "local-diffusion";

    /// <summary>Sampling steps. 20 is the ported default — CPU diffusion makes each step expensive.</summary>
    public int Steps { get; set; } = 20;

    /// <summary>Classifier-free guidance scale.</summary>
    public double CfgScale { get; set; } = 7;

    /// <summary>How much of the source image an img2img run may change (0..1).</summary>
    public double DenoisingStrength { get; set; } = 0.5;

    /// <summary>Absolute ceiling for one render. CPU diffusion legitimately runs for many minutes, so this is
    /// generous; it exists so a wedged engine cannot hang a caller forever.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long the engine may go SILENT before it is treated as wedged. Shorter than
    /// <see cref="Timeout"/> on purpose: the engine prints progress, so silence — not elapsed time — is what
    /// distinguishes a dead render from a slow one.</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>What the configured <see cref="BinaryPath"/> runs on. This is a DECLARATION, not a probe —
    /// the library never inspects the host's hardware, because the only thing that settles it is which build
    /// of the engine the host installed, and they know that and this code cannot.</summary>
    /// <remarks>It exists to give <see cref="MaxDimension"/> an honest default rather than one number for
    /// every machine. See that property for what each profile derives.</remarks>
    public DiffusionAccelerator Accelerator { get; set; } = DiffusionAccelerator.Cpu;

    /// <summary>Longest side a render may ask for, before the multiple-of-64 rounding the engine requires.
    /// <c>null</c> — the default — derives it from <see cref="Accelerator"/>; any value set here wins.</summary>
    /// <remarks><b>Why a cap exists at all, and why it is not one number.</b> A consuming app measured this:
    /// on a GPU-less laptop an accepted <c>1024x1792</c> means about ten minutes of grinding, which is a
    /// worse experience than a smaller image arriving promptly. Its own clamp capped the longer side at 768
    /// and that is what <see cref="DiffusionAccelerator.Cpu"/> derives.
    /// <para><b><see cref="DiffusionAccelerator.Gpu"/> derives NO cap</b>, and that is a deliberate refusal
    /// to invent a number. The reason for the CPU cap is that each step is expensive enough to turn a render
    /// into a wait; with an accelerator that premise does not hold, and no measurement here justifies any
    /// particular GPU ceiling. Picking one would be the documented-not-measured mistake <c>TASKS.md</c>
    /// GEN-VERIFY exists to correct. A host that wants a ceiling sets one.</para>
    /// <para>The cap SCALES rather than crops: the longer side is brought to the cap and the other side
    /// moves with it, so the aspect ratio a caller asked for survives. A per-axis clamp would silently
    /// reshape a portrait request into something squarer.</para></remarks>
    public int? MaxDimension { get; set; }

    /// <summary>The argv flag tokens, keyed by what each one MEANS rather than by its spelling. A key absent
    /// here falls back to <see cref="DefaultArgvFlags"/>, so a host overrides one flag without restating ten.</summary>
    /// <remarks><b>Settable because this argv is ported, not measured</b> — the same reason the ComfyUI and
    /// fal backends make their paths and field names options. The engine is a third-party binary whose flags
    /// can be renamed upstream between releases, and a host that hits that should edit configuration rather
    /// than wait for a Lyntai release. Recognised keys: <c>model</c>, <c>prompt</c>, <c>output</c>,
    /// <c>width</c>, <c>height</c>, <c>steps</c>, <c>cfg-scale</c>, <c>mode</c>, <c>init</c>,
    /// <c>strength</c>.</remarks>
    public IDictionary<string, string> ArgvFlags { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The ported argv spellings, used for any key <see cref="ArgvFlags"/> does not override.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultArgvFlags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = "-m",
            ["prompt"] = "-p",
            ["output"] = "-o",
            ["width"] = "-W",
            ["height"] = "-H",
            ["steps"] = "--steps",
            ["cfg-scale"] = "--cfg-scale",
            ["mode"] = "-M",
            ["init"] = "-i",
            ["strength"] = "--strength",
        };

    /// <summary>The value passed after the <c>mode</c> flag for an image-to-image run.</summary>
    public string Img2ImgMode { get; set; } = "img2img";

    /// <summary>Extra arguments appended verbatim to every render — a sampler, a seed, a VAE path, whatever
    /// this build accepts that the platform has no opinion about. Empty by default.</summary>
    /// <remarks>The platform deliberately models NONE of these as first-class options: each is specific to
    /// one engine build, and promoting them would put a third-party binary's flag set into a frozen public
    /// surface. This is the escape hatch that keeps that from being necessary.</remarks>
    public IList<string> ExtraArgs { get; set; } = [];

    /// <summary>The effective cap: <see cref="MaxDimension"/> when set, else what
    /// <see cref="Accelerator"/> derives, else none.</summary>
    internal int? EffectiveMaxDimension => MaxDimension ?? Accelerator switch
    {
        DiffusionAccelerator.Cpu => 768,
        _ => null,
    };
}

/// <summary>What a local diffusion engine runs on — declared by the host, never probed. It selects the
/// default size ceiling and nothing else.</summary>
public enum DiffusionAccelerator
{
    /// <summary>CPU-only, the shape the stock <c>sd-cli</c> release ships as. Derives a 768px ceiling,
    /// because each step is expensive enough that a large render becomes a wait rather than a result.</summary>
    Cpu,

    /// <summary>Hardware-accelerated. Derives NO ceiling: the reason for the CPU cap does not apply, and no
    /// measurement here justifies inventing a different number. Set <see cref="LocalDiffusionOptions.MaxDimension"/>
    /// to impose one.</summary>
    Gpu,
}

/// <summary>
/// An <see cref="IGenerationProvider"/> over a locally-installed <b>stable-diffusion.cpp</b> (<c>sd-cli</c>):
/// image generation entirely on the host's machine — no key, no network, no content policy in the path. INLINE
/// delivery, because a local render blocks until the file exists; there is no operation id to resume.
/// </summary>
/// <remarks>
/// <para>The argv and the size-clamping rules are <b>PORTED from a working implementation</b>, not measured here
/// (the engine isn't installed on the machine where this was written). They are production-proven, and pinned by
/// exact-argv tests so a later edit can't drift from the shape a real <c>sd-cli</c> accepts. Confirm against a
/// live engine before relying on them in anger.</para>
/// <para>Two details that look incidental and are not. The spawn's working directory is the BINARY's
/// directory, because the engine loads <c>ggml*.dll</c> from beside itself and fails at load time otherwise
/// — CONFIRMED against a real release. And sizes round to multiples of 64 and floor at 256 because the
/// engine requires it, then fit <see cref="LocalDiffusionOptions.MaxDimension"/> — 768 on the default CPU
/// profile, where a larger render is minutes of pointless waiting, and unbounded on <c>Gpu</c>. The 768 is
/// measured; the argv around it is still ported.</para>
/// <para>The executable is supplied by the host as a full path — deliberately no PATH probe and no name
/// matching, because a real release ships <c>sd-cli.exe</c> AND <c>sd-server.exe</c> side by side, and a loose
/// <c>sd</c>-prefix match would launch the server, which starts and then waits forever: a hang, not an
/// error.</para>
/// <para>Unlike the app implementation this was ported from, the spawn goes through
/// <see cref="IProcessRunner"/> — so it inherits the BYO-runner seam, kill-the-tree cancellation, and a
/// per-chunk INACTIVITY clock with an absolute backstop instead of one wall clock that would kill a healthy
/// slow render (the trap <c>.claude/knowledge/pitfalls.md</c> documents for the LLM side).</para>
/// </remarks>
/// <param name="options">Engine paths and sampling defaults.</param>
/// <param name="runner">Process execution — BYO to sandbox or audit the spawn.</param>
public sealed class LocalDiffusionProvider(LocalDiffusionOptions options, IProcessRunner runner) : IGenerationProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    /// <remarks>The advertised ceiling is the CONFIGURED one, not a constant. It was hard-coded at 768
    /// alongside the clamp, so once the ceiling became the host's
    /// (<see cref="LocalDiffusionOptions.Accelerator"/>) this would have gone on telling callers 768 while
    /// the backend accepted whatever a GPU host asked for — a limit a consumer reads and plans against, and
    /// the third site the cap had to reach. <see cref="GenerationCapabilities.Limits"/> is informational, so
    /// nothing would have failed; it would simply have been false.
    /// <para>Derived PER ACCESS, never captured at construction: the registration doc advertises late
    /// provisioning ("the next render reads the current values") and enforcement reads the options live, so
    /// a captured advertisement is the same stale-limit defect one mutation later.</para></remarks>
    public GenerationCapabilities Capabilities => new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,   // img2img
        Limits = SizeLimits(options.EffectiveMaxDimension),
    };

    /// <summary>The size limits to advertise. With no ceiling the dimension keys are OMITTED rather than set
    /// to some large number: <see cref="GenerationCapabilities"/> reads an absent key as "not enumerated",
    /// which is exactly true here, while any number would be a ceiling nobody measured.</summary>
    private static IReadOnlyDictionary<string, string> SizeLimits(int? maxDimension)
    {
        var limits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["size-multiple-of"] = "64",   // the engine's own requirement, true on every accelerator
        };
        if (maxDimension is { } cap)
        {
            limits["max-width"] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture);
            limits["max-height"] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return limits;
    }

    /// <summary>Presence of the engine and its weights on disk — free, exact, and the only thing worth checking
    /// before a render that costs minutes of CPU.</summary>
    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (options.BinaryPath is not { Length: > 0 } binary || !File.Exists(binary))
            return Task.FromResult(new GenerationProbeResult(false,
                $"not configured: no sd-cli binary at '{options.BinaryPath}' (the host provisions it — D20)"));

        if (options.ModelPath is not { Length: > 0 } model || !File.Exists(model))
            return Task.FromResult(new GenerationProbeResult(false,
                $"not configured: the engine is present but its model file is missing at '{options.ModelPath}'"));

        return Task.FromResult(new GenerationProbeResult(true,
            $"sd-cli at '{binary}' with model '{Path.GetFileName(model)}'"));
    }

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        if (options.BinaryPath is not { Length: > 0 } binary || !File.Exists(binary) ||
            options.ModelPath is not { Length: > 0 } model || !File.Exists(model))
            return GenerationResult.Failure(GenerationVerdict.NotConfigured,
                "the local engine or its model is not present on disk");

        var source = request.Inputs.FirstOrDefault();
        if (source is not null && source.Data is not { Length: > 0 })
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                "the engine reads its source image from DISK; supply GenerationInput.Data rather than a URI");

        var work = Path.Combine(options.WorkDirectory ?? Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var output = Path.Combine(work, "out.png");

            string? initPath = null;
            if (source is not null)
            {
                initPath = Path.Combine(work, "init.png");
                await File.WriteAllBytesAsync(initPath, source.Data!, ct).ConfigureAwait(false);
            }

            var (width, height) = ClampSize(request.Option("size"), options.EffectiveMaxDimension);
            var argv = BuildArgs(model, request.Prompt ?? "", output, width, height, initPath);

            // never below the inactivity window: a caller who shortens the absolute budget shouldn't end up
            // with a silence detector that can never fire
            var maxDuration = options.Timeout;
            var inactivity = options.InactivityTimeout < maxDuration ? options.InactivityTimeout : maxDuration;

            ProcessResult result;
            try
            {
                result = await runner.RunAsync(binary, argv, stdin: null,
                    inactivityTimeout: inactivity, maxDuration: maxDuration,
                    // the engine loads its native libraries from beside the binary
                    workingDirectory: Path.GetDirectoryName(binary), ct: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return GenerationResult.Failure(GenerationVerdict.Failed, $"spawn failed: {ex.Message}");
            }

            if (result.TimedOut)
                return GenerationResult.Failure(GenerationVerdict.Timeout,
                    result.TimeoutKind == ProcessTimeoutKind.MaxDuration
                        ? $"the render exceeded {maxDuration}"
                        : $"the engine went silent for {inactivity}");

            if (!File.Exists(output))
            {
                var stderr = Tail(result.StdErr);
                return GenerationResult.Failure(GenerationVerdict.Failed,
                    result.ExitCode != 0
                        ? $"exit {result.ExitCode}: {stderr}"
                        : $"no image produced{(stderr.Length > 0 ? $": {stderr}" : "")}");
            }

            var bytes = await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
            return GenerationResult.Success(
                [new GenerationArtifact("image/png", Data: bytes)], new GenerationUsage(Count: 1));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return GenerationResult.Failure(GenerationVerdict.Failed, ex.Message);
        }
        finally
        {
            // a per-call scratch dir must never survive the call, on any path
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>The engine's argument list. PORTED verbatim in shape from a working implementation — txt2img by
    /// default, switching to img2img when a source image is staged.</summary>
    /// <remarks>Every FLAG is looked up in <see cref="LocalDiffusionOptions.ArgvFlags"/> rather than written here,
    /// for the reason the class header gives: this argv is ported, not measured. An upstream rename is a
    /// configuration edit, not a Lyntai release. The ORDER and the pairing stay this backend's — only the
    /// tokens are the host's — because which flags may legally sit where is the engine's grammar
    /// (`docs/DECISIONS.md` D65 records what happens when a caller places argv it does not own).</remarks>
    internal List<string> BuildArgs(string model, string prompt, string output, int width, int height, string? initPath)
    {
        var flag = (string name) => options.ArgvFlags.TryGetValue(name, out var f)
            ? f
            : LocalDiffusionOptions.DefaultArgvFlags[name];

        List<string> args =
        [
            flag("model"), model,
            flag("prompt"), prompt,
            flag("output"), output,
            flag("width"), width.ToString(),
            flag("height"), height.ToString(),
            flag("steps"), options.Steps.ToString(),
            flag("cfg-scale"), options.CfgScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];
        if (initPath is not null)
        {
            args.Add(flag("mode"));
            args.Add(options.Img2ImgMode);
            args.Add(flag("init"));
            args.Add(initPath);
            args.Add(flag("strength"));
            args.Add(options.DenoisingStrength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Appended LAST and verbatim. Safe here only because sd-cli's argv is options all the way down — it
        // has no trailing positional the way codex's `-` stdin marker does, where anything after it is read
        // as prompt text and a swallowed flag is a spent turn rather than an error (`DECISIONS.md` D65).
        // That is a fact about THIS engine's grammar, which is why the append lives in this backend.
        args.AddRange(options.ExtraArgs);
        return args;
    }

    /// <summary>Parse <c>"WxH"</c> and fit it to what the configured engine can actually do: keep the aspect
    /// ratio, bring the longer side down to <paramref name="maxDimension"/> when one applies, floor at 256,
    /// and round each side to a multiple of 64 (an engine requirement). An unparseable hint falls back to
    /// 512×512 rather than failing — a bad size is not worth losing a render over.</summary>
    /// <param name="size">The caller's <c>"WxH"</c> hint, or null.</param>
    /// <param name="maxDimension">Longest permitted side, or null for no ceiling —
    /// <see cref="LocalDiffusionOptions.EffectiveMaxDimension"/>, which derives it from the declared
    /// accelerator unless the host set one.</param>
    /// <remarks><b>The cap has to reach the ROUNDING as well as the scale, and for a while it did not.</b>
    /// This method scaled against one hard-coded 768 and then rounded through a second, so raising the
    /// ceiling would have moved the scale and left every result pinned at 768 by the rounder — a knob that
    /// visibly does nothing beyond its old value. Both now take the same parameter, which is why there is
    /// only one.</remarks>
    internal static (int Width, int Height) ClampSize(string? size, int? maxDimension = 768)
    {
        int width = 512, height = 512;
        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var parsedWidth) && int.TryParse(parts[1], out var parsedHeight) &&
                parsedWidth > 0 && parsedHeight > 0)
            {
                width = parsedWidth;
                height = parsedHeight;
            }
        }

        // Scale BOTH sides by one factor rather than clamping each: a per-axis clamp turns a portrait
        // request into something squarer, which is a different picture from the one that was asked for.
        if (maxDimension is { } cap)
        {
            var longest = Math.Max(width, height);
            if (longest > cap)
            {
                var factor = (double)cap / longest;
                width = (int)Math.Round(width * factor);
                height = (int)Math.Round(height * factor);
            }
        }

        return (Round64(width, maxDimension), Round64(height, maxDimension));
    }

    /// <summary>Round to the engine's required multiple of 64, keeping the result within [256, cap]. The
    /// floor is the engine's practical minimum; the ceiling is the caller's policy and may be absent.</summary>
    private static int Round64(int value, int? maxDimension)
    {
        var rounded = (int)Math.Round(value / 64.0) * 64;
        // The cap is floored to a multiple of 64 before it may win: nothing validates the host's number, and
        // a bare clamp against a 1000 cap returns 1000 into an argv the engine rejects. A cap below the floor
        // is a host asking for something the engine cannot do; the floor wins, because returning a size the
        // engine rejects turns a policy into a failed render.
        var ceiling = maxDimension is { } cap ? Math.Max(cap / 64 * 64, MinDimension) : int.MaxValue;
        return Math.Clamp(rounded, MinDimension, ceiling);
    }

    /// <summary>The engine's practical floor. Not a policy — below this the model produces noise.</summary>
    private const int MinDimension = 256;

    private static string Tail(string text, int max = 400)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }
}
