using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Processes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="PiperProvider"/>. The engine and its voice are the HOST's to
/// provide — Lyntai drives what is already on disk and never downloads either (<c>docs/DECISIONS.md</c>
/// D20).</summary>
public sealed class PiperOptions
{
    /// <summary>Path to the <c>piper</c> executable. Absent → the backend reports
    /// <see cref="ProviderVerdict.NotConfigured"/>. A full path, deliberately — no PATH probe.</summary>
    public string? BinaryPath { get; set; }

    /// <summary>Path to the voice model (a piper <c>.onnx</c>). Its own config is expected BESIDE it as
    /// <c>&lt;ModelPath&gt;.json</c> — piper's shipping convention — and is where the sample rate is read
    /// from unless <see cref="SampleRate"/> overrides it.</summary>
    public string? ModelPath { get; set; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "piper";

    /// <summary>Output sample rate in Hz, for the media type the chunks carry. <c>null</c> — the default —
    /// reads the voice's own declaration (<c>audio.sample_rate</c> in the voice config); a value set here
    /// wins. When neither exists the chunks still flow, typed without a rate parameter — unstated because
    /// unstatable, never invented.</summary>
    public int? SampleRate { get; set; }

    /// <summary>Absolute ceiling for one synthesis. Generous — a long text is minutes of CPU — and present
    /// so a wedged engine cannot hang a caller forever.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the engine may go SILENT before it is treated as wedged. Shorter than
    /// <see cref="Timeout"/> on purpose: a healthy synthesis streams continuously, so silence — not elapsed
    /// time — is what distinguishes a dead engine from a slow one.</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The argv flag tokens, keyed by what each one MEANS rather than by its spelling. A key
    /// absent here falls back to <see cref="DefaultArgvFlags"/>, so a host overrides one flag without
    /// restating the rest.</summary>
    /// <remarks><b>Settable because the engine renames things between releases</b> — the same seam that
    /// absorbed <c>sd-cli</c>'s retired mode value as a configuration edit. Recognised keys: <c>model</c>,
    /// <c>output-raw</c>.</remarks>
    public IDictionary<string, string> ArgvFlags { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The argv spellings used for any key <see cref="ArgvFlags"/> does not override.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultArgvFlags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = "--model",
            ["output-raw"] = "--output-raw",
        };

    /// <summary>Extra arguments appended verbatim to every synthesis — a speaker id, a length scale,
    /// whatever this build accepts that the platform has no opinion about. Empty by default; the same
    /// escape hatch <see cref="LocalDiffusionOptions.ExtraArgs"/> is, for the same reason.</summary>
    public IList<string> ExtraArgs { get; set; } = [];
}

/// <summary>
/// An <see cref="IModelProvider"/> over a locally-installed <b>piper</b> TTS engine: speech entirely on the
/// host's machine — no key, no network, no content policy in the path. STREAMING is the native mode (the
/// engine emits raw PCM to stdout as it synthesises, read through
/// <see cref="IProcessRunner.StreamBytesAsync"/>); <see cref="GenerateAsync"/> is the same stream,
/// buffered.
/// </summary>
/// <remarks>
/// <para>The chunks carry <c>audio/pcm;rate=…;bits=16;channels=1;endian=little</c>. <b><c>audio/L16</c> is
/// deliberately NOT claimed</b>: RFC 2586's L16 is big-endian, and the engine emits little-endian samples —
/// a chunk labelled L16 would decode as noise in a strict reader. The rate comes from the VOICE's own
/// config (<c>&lt;model&gt;.json</c>, piper's shipping convention) unless
/// <see cref="PiperOptions.SampleRate"/> overrides it.</para>
/// <para>The prompt travels over STDIN, never argv — the family's spawn hygiene (design §6): text carries
/// newlines and metacharacters, and piper reads lines from stdin as its input contract anyway. The clocks
/// are the RUNNER's (inactivity + absolute backstop), arriving as <see cref="ProcessTimeoutException"/> —
/// this provider passes no clock of its own, exactly as the CLI providers do.</para>
/// </remarks>
/// <param name="options">Engine paths and synthesis defaults.</param>
/// <param name="runner">Process execution — BYO to sandbox or audit the spawn. A BYO runner must implement
/// <see cref="IProcessRunner.StreamBytesAsync"/> (binary cannot be served through the string-typed buffered
/// path); the default runner does.</param>
public sealed class PiperProvider(PiperOptions options, IProcessRunner runner) : IModelProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Audio],
        Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
    };

    /// <summary>Presence of the engine and its voice on disk — free, exact, and never synthesises to
    /// answer a setup question.</summary>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(Missing() is { } missing
            ? new ProviderProbeResult(false, missing)
            : new ProviderProbeResult(true,
                $"piper at '{options.BinaryPath}' with voice '{Path.GetFileName(options.ModelPath)}'"));

    private string? Missing() => LocalEngine.Missing(options.BinaryPath, options.ModelPath, "piper", "voice");

    /// <inheritdoc/>
    /// <remarks>The buffered mode: the SAME stream, collected into one artifact, so the two modes cannot
    /// disagree about argv, media type or failure classification.</remarks>
    public async Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
    {
        var bytes = new List<byte>();
        string? mediaType = null;
        await foreach (var chunk in StreamAsync(request, ct).ConfigureAwait(false))
        {
            if (chunk.Error is { } verdict)
                return MediaResponse.Failure(verdict, chunk.Detail);
            if (chunk.Data is { Length: > 0 } data)
            {
                bytes.AddRange(data);
                mediaType ??= chunk.MediaType;
            }
            if (chunk.Final)
                return MediaResponse.Success(
                    [new MediaArtifact(mediaType ?? PcmMediaType(null), Data: [.. bytes])],
                    chunk.Usage ?? new MediaUsage(Count: 1));
        }
        return MediaResponse.Failure(ProviderVerdict.Failed, "the stream ended without a terminal chunk");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MediaChunk> StreamAsync(MediaRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Missing() is { } missing)
        {
            yield return MediaChunk.Failure(ProviderVerdict.NotConfigured, missing);
            yield break;
        }
        var (binary, model) = (options.BinaryPath!, options.ModelPath!);

        if (request.Prompt is not { Length: > 0 } prompt || string.IsNullOrWhiteSpace(prompt))
        {
            yield return MediaChunk.Failure(ProviderVerdict.Unsupported,
                "text-to-speech needs text: supply MediaRequest.Prompt");
            yield break;
        }

        var rate = ResolveSampleRate(model);
        var mediaType = PcmMediaType(rate);
        var argv = BuildArgs(model);

        var maxDuration = options.Timeout;
        var inactivity = LocalEngine.Inactivity(maxDuration, options.InactivityTimeout);

        long total = 0;
        await using var enumerator = runner.StreamBytesAsync(binary, argv, stdin: prompt,
            inactivityTimeout: inactivity, maxDuration: maxDuration, ct: ct).GetAsyncEnumerator(ct);
        while (true)
        {
            byte[]? data;
            MediaChunk? failure = null;
            try
            {
                data = await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
            }
            catch (OperationCanceledException) { throw; }
            catch (ProcessTimeoutException ex)
            {
                (data, failure) = (null, MediaChunk.Failure(ProviderVerdict.Timeout, ex.Message));
            }
            catch (ProcessRunException ex)
            {
                (data, failure) = (null,
                    MediaChunk.Failure(ProviderVerdictClassifier.FromErrorText(ex.Message), ex.Message));
            }
            catch (NotSupportedException ex)
            {
                // a BYO runner without the binary member: a wiring gap, not an engine failure
                (data, failure) = (null, MediaChunk.Failure(ProviderVerdict.NotConfigured, ex.Message));
            }
            catch (Exception ex)
            {
                (data, failure) = (null, MediaChunk.Failure(ProviderVerdict.Failed, $"spawn failed: {ex.Message}"));
            }

            if (failure is not null)
            {
                yield return failure;
                yield break;
            }
            if (data is null) break;
            if (data.Length == 0) continue;
            total += data.Length;
            yield return MediaChunk.Content(data, mediaType);
        }

        // Seconds is DERIVED from the bytes actually streamed and the voice's declared rate — measured,
        // never estimated — and omitted when the rate is unknown rather than computed from a guess.
        yield return MediaChunk.Completed(new MediaUsage(
            Count: 1,
            Seconds: rate is { } r && total > 0 ? total / (r * 2.0) : null));
    }

    /// <summary>The engine's argument list: the voice, the raw-PCM switch, then
    /// <see cref="PiperOptions.ExtraArgs"/> verbatim and LAST. The text is never here — it travels over
    /// stdin, which is both the spawn hygiene rule and piper's own input contract.</summary>
    internal List<string> BuildArgs(string model)
    {
        var flag = (string name) => LocalEngine.Flag(options.ArgvFlags, PiperOptions.DefaultArgvFlags, name);

        List<string> args = [flag("model"), model, flag("output-raw")];
        args.AddRange(options.ExtraArgs);
        return args;
    }

    /// <summary>The voice's declared sample rate: <see cref="PiperOptions.SampleRate"/> when set, else
    /// <c>audio.sample_rate</c> from the config beside the voice (<c>&lt;model&gt;.json</c>), else null —
    /// a missing declaration is reported as absence, never replaced with a guess. Only a POSITIVE integer is a
    /// rate: anything else in either place reads as unset.</summary>
    internal int? ResolveSampleRate(string model)
    {
        if (options.SampleRate is > 0 and var explicitRate) return explicitRate;
        try
        {
            var configPath = model + ".json";
            if (!File.Exists(configPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            // the Number check is load-bearing: TryGetInt32 THROWS on a string, past the catch below
            if (doc.RootElement.TryGetProperty("audio", out var audio) &&
                audio.ValueKind == JsonValueKind.Object &&
                audio.TryGetProperty("sample_rate", out var rate) && rate.ValueKind == JsonValueKind.Number &&
                rate.TryGetInt32(out var hz) && hz > 0)
                return hz;
            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The chunk media type. <c>audio/L16</c> is deliberately not used: RFC 2586 L16 is
    /// big-endian and the engine emits little-endian, so the honest type spells the layout out.</summary>
    internal static string PcmMediaType(int? rate) =>
        rate is { } r
            ? $"audio/pcm;rate={r};bits=16;channels=1;endian=little"
            : "audio/pcm;bits=16;channels=1;endian=little";
}
