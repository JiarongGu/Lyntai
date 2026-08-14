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
    public double Strength { get; set; } = 0.5;

    /// <summary>Absolute ceiling for one render. CPU diffusion legitimately runs for many minutes, so this is
    /// generous; it exists so a wedged engine cannot hang a caller forever.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long the engine may go SILENT before it is treated as wedged. Shorter than
    /// <see cref="Timeout"/> on purpose: the engine prints progress, so silence — not elapsed time — is what
    /// distinguishes a dead render from a slow one.</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);
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
/// <para>Two details that look incidental and are not. The spawn's working directory is the BINARY's directory,
/// because the engine loads <c>ggml*.dll</c> from beside itself and fails at load time otherwise — no longer a
/// ported guess: a consuming app confirmed it against a real release, whose zip ships those libraries next to
/// the executable. And sizes are clamped to multiples of 64 within 256–768, because the engine requires the
/// former and a CPU render above that is minutes of pointless waiting (this half is still ported, not
/// measured).</para>
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
    public GenerationCapabilities Capabilities { get; } = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,   // img2img
        Limits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["max-width"] = "768",
            ["max-height"] = "768",
            ["size-multiple-of"] = "64",
        },
    };

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

            var (width, height) = ClampSize(request.Option("size"));
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
    internal List<string> BuildArgs(string model, string prompt, string output, int width, int height, string? initPath)
    {
        List<string> args =
        [
            "-m", model,
            "-p", prompt,
            "-o", output,
            "-W", width.ToString(),
            "-H", height.ToString(),
            "--steps", options.Steps.ToString(),
            "--cfg-scale", options.CfgScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];
        if (initPath is not null)
        {
            args.Add("-M");
            args.Add("img2img");
            args.Add("-i");
            args.Add(initPath);
            args.Add("--strength");
            args.Add(options.Strength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return args;
    }

    /// <summary>Parse <c>"WxH"</c> and clamp to what the engine can actually do on a CPU: keep the aspect ratio,
    /// cap the longer side at 768, floor at 256, and round each side to a multiple of 64 (an engine
    /// requirement). An unparseable hint falls back to 512×512 rather than failing — a bad size is not worth
    /// losing a render over.</summary>
    internal static (int Width, int Height) ClampSize(string? size)
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

        var longest = Math.Max(width, height);
        if (longest > 768)
        {
            var factor = 768.0 / longest;
            width = (int)Math.Round(width * factor);
            height = (int)Math.Round(height * factor);
        }
        return (Round64(width), Round64(height));
    }

    private static int Round64(int value) => Math.Clamp((int)Math.Round(value / 64.0) * 64, 256, 768);

    private static string Tail(string text, int max = 400)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
    }
}
