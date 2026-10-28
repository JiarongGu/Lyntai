using Lyntai.Inference;
using Lyntai.Processes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="LocalDiffusionProvider"/>. The engine and its weights are the HOST's
/// to provide — Lyntai drives what is already on disk and never downloads either (<c>docs/DECISIONS.md</c>
/// D20).</summary>
public sealed class LocalDiffusionOptions
{
    /// <summary>Path to the <c>sd-cli</c> executable. Absent → the backend reports
    /// <see cref="ProviderVerdict.NotConfigured"/>.</summary>
    public string? BinaryPath { get; set; }

    /// <summary>Path to the model weights (a GGUF/safetensors file the engine accepts) — the checkpoint a request
    /// that names none renders with.</summary>
    public string? ModelPath { get; set; }

    /// <summary>The folder a request's <see cref="MediaRequest.Checkpoint"/> is looked up in, by file name (the name
    /// as given, else with <c>.safetensors</c>, <c>.gguf</c> or <c>.ckpt</c> added). Null — the default — means this
    /// backend does not switch checkpoints and says so in its capabilities. A name carrying a folder is refused, so a
    /// request cannot reach outside it.</summary>
    public string? CheckpointDirectory { get; set; }

    /// <summary>The folder a request's <see cref="MediaRequest.Loras"/> are looked up in, passed to the engine as its
    /// LoRA directory with each LoRA written into the prompt as <c>&lt;lora:name:weight&gt;</c>. Null — the default —
    /// means this backend takes no LoRAs and says so in its capabilities.</summary>
    public string? LoraDirectory { get; set; }

    /// <summary>Where per-call scratch (the output PNG, a staged init image) is written. Each call gets its own
    /// subdirectory, deleted afterwards. Defaults to the OS temp directory.</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "local-diffusion";

    /// <summary>Sampling steps. 20 matches the engine's own default — CPU diffusion makes each step expensive.</summary>
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
    /// particular GPU ceiling. A host that wants a ceiling sets one.</para>
    /// <para>The cap SCALES rather than crops: the longer side is brought to the cap and the other side
    /// moves with it, so the aspect ratio a caller asked for survives. A per-axis clamp would silently
    /// reshape a portrait request into something squarer.</para></remarks>
    public int? MaxDimension { get; set; }

    /// <summary>The argv flag tokens, keyed by what each one MEANS rather than by its spelling. A key absent
    /// here falls back to <see cref="DefaultArgvFlags"/>, so a host overrides one flag without restating ten.</summary>
    /// <remarks><b>Settable because the engine renames things between releases, and it HAS</b>: upstream
    /// retired the <c>img2img</c> mode value, and the correction this seam was built for arrived as a default
    /// change here and remains a configuration edit for any host whose engine drifts next — never a wait for
    /// a Lyntai release. Recognised keys: <c>model</c>, <c>prompt</c>, <c>output</c>,
    /// <c>width</c>, <c>height</c>, <c>steps</c>, <c>cfg-scale</c>, <c>mode</c>, <c>init</c>,
    /// <c>strength</c>, <c>negative-prompt</c>, <c>seed</c>, <c>sampler</c>, <c>scheduler</c>, <c>lora-dir</c>.</remarks>
    public IDictionary<string, string> ArgvFlags { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The measured argv spellings (<c>master-874-656a135</c>), used for any key
    /// <see cref="ArgvFlags"/> does not override.</summary>
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
            ["negative-prompt"] = "-n",
            ["seed"] = "-s",
            ["sampler"] = "--sampling-method",
            ["scheduler"] = "--scheduler",
            ["lora-dir"] = "--lora-model-dir",
        };

    /// <summary>The value passed after the <c>mode</c> flag for an image-to-image run — or <c>null</c>, the
    /// default, to pass NO mode flag at all. The current engine (measured: <c>master-874-656a135</c>) selects
    /// img2img by the PRESENCE of the init flag and REJECTS the retired <c>img2img</c> mode value as an argv
    /// error; an older build that required the explicit pair gets it back by setting <c>"img2img"</c> here.</summary>
    public string? Img2ImgMode { get; set; }

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
/// An <see cref="IModelProvider"/> over a locally-installed <b>stable-diffusion.cpp</b> (<c>sd-cli</c>):
/// image generation entirely on the host's machine — no key, no network, no content policy in the path. INLINE
/// delivery, because a local render blocks until the file exists; there is no operation id to resume.
/// </summary>
/// <remarks>
/// <para>The argv and the size-clamping rules are <b>MEASURED against a real engine</b>
/// (<c>master-874-656a135</c>): one txt2img and one img2img render ran end to end through this class
/// (<c>LocalDiffusionLiveTests</c>), and the exact-argv tests pin what that engine accepted. The measurement
/// corrected one ported detail — img2img is selected by the init flag's PRESENCE, and the old explicit mode
/// pair is an argv error (<see cref="LocalDiffusionOptions.Img2ImgMode"/> restores it for an older build).</para>
/// <para>Two details that look incidental and are not. The spawn's working directory is the BINARY's
/// directory, because the engine loads <c>ggml*.dll</c> from beside itself and fails at load time otherwise.
/// And the size clamp — multiples of 64, floored at 256, fitted to
/// <see cref="LocalDiffusionOptions.MaxDimension"/> — exists because the engine ROUNDS silently rather than
/// failing (a measured 500x500 request came back 512x512 with no warning), so without it the size a caller
/// asked for and the size delivered diverge with nobody deciding that. 768 on the default CPU profile, where
/// a larger render is minutes of pointless waiting; unbounded on <c>Gpu</c>.</para>
/// <para>The executable is supplied by the host as a full path — deliberately no PATH probe and no name
/// matching, because a real release ships <c>sd-cli.exe</c> AND <c>sd-server.exe</c> side by side, and a loose
/// <c>sd</c>-prefix match would launch the server, which starts and then waits forever: a hang, not an
/// error.</para>
/// <para>The spawn goes through <see cref="IProcessRunner"/> — so it inherits the BYO-runner seam,
/// kill-the-tree cancellation, and a per-chunk INACTIVITY clock with an absolute backstop rather than one wall
/// clock that would kill a healthy slow render.</para>
/// </remarks>
/// <param name="options">Engine paths and sampling defaults.</param>
/// <param name="runner">Process execution — BYO to sandbox or audit the spawn.</param>
public sealed class LocalDiffusionProvider(LocalDiffusionOptions options, IProcessRunner runner) : IModelProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    /// <remarks>The advertised ceiling is the CONFIGURED one (<see cref="LocalDiffusionOptions.EffectiveMaxDimension"/>),
    /// never a constant: <see cref="ProviderCapabilities.Limits"/> is informational, so a stale number fails
    /// nothing and is simply false to a consumer planning against it. Derived PER ACCESS, so a host that changes
    /// the options after registration advertises the new values.</remarks>
    public ProviderCapabilities Capabilities => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
        SupportsInputs = true,   // img2img
        Parameters = Honoured(),
        Limits = SizeLimits(options.EffectiveMaxDimension),
    };

    /// <summary>What the engine's argv carries for a request — and, for the two that need a folder, only when the
    /// host named one: a checkpoint or a LoRA this backend has no folder to resolve is refused, not guessed.</summary>
    private List<string> Honoured()
    {
        List<string> honoured =
            [MediaParameters.NegativePrompt, MediaParameters.Seed, MediaParameters.Steps, MediaParameters.CfgScale,
             MediaParameters.Sampler, MediaParameters.Scheduler];
        if (!string.IsNullOrWhiteSpace(options.CheckpointDirectory)) honoured.Add(MediaParameters.Checkpoint);
        if (!string.IsNullOrWhiteSpace(options.LoraDirectory)) honoured.Add(MediaParameters.Loras);
        return honoured;
    }

    /// <summary>The size limits to advertise. With no ceiling the dimension keys are OMITTED rather than set
    /// to some large number: <see cref="ProviderCapabilities"/> reads an absent key as "not enumerated",
    /// which is exactly true here, while any number would be a ceiling nobody measured.</summary>
    private static IReadOnlyDictionary<string, string> SizeLimits(int? maxDimension)
    {
        var limits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["size-multiple-of"] = "64",   // the engine delivers only multiples of 64, on every accelerator
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
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(Missing() is { } missing
            ? new ProviderProbeResult(false, missing)
            : new ProviderProbeResult(true,
                $"sd-cli at '{options.BinaryPath}' with model '{Path.GetFileName(options.ModelPath)}'"));

    private string? Missing() => LocalEngine.Missing(options.BinaryPath, options.ModelPath, "sd-cli", "model file");

    /// <inheritdoc/>
    public async Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
    {
        // a refusal of the request as posed costs nothing: no spawn, and only the folder reads it needs
        if (ParameterRefusal.For(Capabilities, request, loraTags: true, checkpointFile: true) is { } unhonoured)
            return MediaResponse.Failure(ProviderVerdict.Unsupported, unhonoured);
        var (checkpoint, loras, notFound) = ResolveModels(request);
        if (notFound is not null) return MediaResponse.Failure(ProviderVerdict.Unsupported, notFound);

        var model = checkpoint ?? options.ModelPath;
        if (LocalEngine.Missing(options.BinaryPath, model, "sd-cli", "model file") is { } missing)
            return MediaResponse.Failure(ProviderVerdict.NotConfigured, missing);
        var binary = options.BinaryPath!;

        var source = SingleInitInput.Read(request, "the engine", out var refusal);
        if (refusal is not null) return MediaResponse.Failure(ProviderVerdict.Unsupported, refusal);
        if (source is not null && source.Data is not { Length: > 0 })
            return MediaResponse.Failure(ProviderVerdict.Unsupported,
                "the engine reads its source image from DISK; supply MediaInput.Data rather than a URI");

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
            var argv = BuildArgs(model!, WithLoraTags(request.Prompt ?? "", loras), output, width, height, initPath,
                request, loras.Count > 0 ? options.LoraDirectory : null);

            var maxDuration = options.Timeout;
            var inactivity = LocalEngine.Inactivity(maxDuration, options.InactivityTimeout);

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
                return MediaResponse.Failure(ProviderVerdict.Failed, $"spawn failed: {ex.Message}");
            }

            if (result.TimedOut)
                return MediaResponse.Failure(ProviderVerdict.Timeout,
                    result.TimeoutKind == ProcessTimeoutKind.MaxDuration
                        ? $"the render exceeded {maxDuration}"
                        : $"the engine went silent for {inactivity}");

            if (!File.Exists(output))
            {
                var stderr = result.StdErrTail(400);
                return MediaResponse.Failure(ProviderVerdict.Failed,
                    result.ExitCode != 0
                        ? $"exit {result.ExitCode}: {stderr}"
                        : $"no image produced{(stderr.Length > 0 ? $": {stderr}" : "")}");
            }

            var bytes = await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
            return MediaResponse.Success(
                [new MediaArtifact("image/png", Data: bytes)], new MediaUsage(Count: 1));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return MediaResponse.Failure(ProviderVerdict.Failed, ex.Message);
        }
        finally
        {
            // a per-call scratch dir must never survive the call, on any path
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>The engine's argument list, MEASURED against a real engine — txt2img by default; a staged
    /// source image adds the init flag, whose presence IS the img2img switch.</summary>
    /// <remarks>Every FLAG is looked up in <see cref="LocalDiffusionOptions.ArgvFlags"/> rather than written
    /// here, because the engine is a third-party binary that renames things between releases — the retired
    /// <c>img2img</c> mode value is the measured example — so an upstream rename is a configuration edit, not
    /// a Lyntai release. The ORDER and the pairing stay this backend's — only the
    /// tokens are the host's — because which flags may legally sit where is the engine's grammar
    /// (`docs/DECISIONS.md` D65 records what happens when a caller places argv it does not own).</remarks>
    internal List<string> BuildArgs(
        string model, string prompt, string output, int width, int height, string? initPath,
        MediaRequest? request = null, string? loraDirectory = null)
    {
        var flag = (string name) => LocalEngine.Flag(options.ArgvFlags, LocalDiffusionOptions.DefaultArgvFlags, name);

        List<string> args =
        [
            flag("model"), model,
            flag("prompt"), prompt,
            flag("output"), output,
            flag("width"), width.ToString(),
            flag("height"), height.ToString(),
            flag("steps"), (request?.Steps ?? options.Steps).ToString(Invariant),
            flag("cfg-scale"), (request?.CfgScale ?? options.CfgScale).ToString(Invariant),
        ];
        if (request?.NegativePrompt is { Length: > 0 } negative) { args.Add(flag("negative-prompt")); args.Add(negative); }
        if (request?.Seed is { } seed) { args.Add(flag("seed")); args.Add(seed.ToString(Invariant)); }
        if (request?.Sampler is { Length: > 0 } sampler) { args.Add(flag("sampler")); args.Add(sampler); }
        if (request?.Scheduler is { Length: > 0 } scheduler) { args.Add(flag("scheduler")); args.Add(scheduler); }
        if (loraDirectory is { Length: > 0 }) { args.Add(flag("lora-dir")); args.Add(loraDirectory); }
        if (initPath is not null)
        {
            // MEASURED: the engine reads the init flag's PRESENCE as the img2img switch. The mode pair is
            // emitted only for a host that set Img2ImgMode — an older build that still requires it.
            if (options.Img2ImgMode is { Length: > 0 } mode)
            {
                args.Add(flag("mode"));
                args.Add(mode);
            }
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

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    private static readonly string[] ModelExtensions = [".safetensors", ".gguf", ".ckpt"];

    /// <summary>The checkpoint file and the LoRAs a request names, each looked up under the host's folder — or the
    /// sentence saying which is not there. A LoRA comes back as the name its tag is written with: the file's name
    /// without its model extension, which is how the engine looks it up in its LoRA directory.</summary>
    private (string? Checkpoint, IReadOnlyList<(string Tag, double Weight)> Loras, string? NotFound) ResolveModels(
        MediaRequest request)
    {
        string? checkpoint = null;
        if (request.Checkpoint is { Length: > 0 } name)
        {
            checkpoint = Find(options.CheckpointDirectory, name);
            if (checkpoint is null)
                return (null, [], $"the checkpoint '{name}' is not in CheckpointDirectory, so nothing was spawned");
        }

        var loras = new List<(string Tag, double Weight)>();
        foreach (var lora in request.Loras)
        {
            var file = Find(options.LoraDirectory, lora.Name);
            if (file is null)
                return (null, [], $"the LoRA '{lora.Name}' is not in LoraDirectory, so nothing was spawned");
            var stem = ModelExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(file) : Path.GetFileName(file);
            loras.Add((stem, lora.Weight));
        }
        return (checkpoint, loras, null);
    }

    /// <summary>The file <paramref name="name"/> names directly under <paramref name="directory"/>, as given or with
    /// a model extension added, or null. The name is already a plain file name here.</summary>
    private static string? Find(string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        foreach (var candidate in new[] { name }.Concat(ModelExtensions.Select(e => name + e)))
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string WithLoraTags(string prompt, IReadOnlyList<(string Tag, double Weight)> loras) =>
        loras.Count == 0 ? prompt
            : prompt + string.Concat(loras.Select(l => $" <lora:{l.Tag}:{l.Weight.ToString("R", Invariant)}>"));

    /// <summary>Parse <c>"WxH"</c> and fit it to what the configured engine can actually do: keep the aspect
    /// ratio, bring the longer side down to <paramref name="maxDimension"/> when one applies, floor at 256,
    /// and round each side to a multiple of 64 — the engine delivers only such sizes, and rounds a
    /// non-multiple SILENTLY, so clamping here keeps the declared size and the delivered size the same.
    /// An unparseable hint falls back to 512×512 rather than failing — a bad size is not worth losing a
    /// render over.</summary>
    /// <param name="size">The caller's <c>"WxH"</c> hint, or null.</param>
    /// <param name="maxDimension">Longest permitted side, or null for no ceiling —
    /// <see cref="LocalDiffusionOptions.EffectiveMaxDimension"/>, which derives it from the declared
    /// accelerator unless the host set one. No default: the options are the one place the cap lives.</param>
    /// <remarks>The cap reaches the ROUNDING as well as the scale — a second copy in the rounder would pin
    /// every result at the old value however high the ceiling is raised.</remarks>
    internal static (int Width, int Height) ClampSize(string? size, int? maxDimension)
    {
        if (!SizeHint.TryParse(size, out var width, out var height)) (width, height) = (512, 512);

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
}
