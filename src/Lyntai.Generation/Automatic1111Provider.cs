using Lyntai.Inference;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="Automatic1111Provider"/>.</summary>
public sealed class Automatic1111Options
{
    /// <summary>Where the WebUI is listening (<c>http://127.0.0.1:7860</c>). Blank = not configured.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:7860";

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "a1111";

    /// <summary>Sampling steps when the request doesn't override them.</summary>
    public int Steps { get; set; } = 25;

    /// <summary>Classifier-free guidance scale when the request doesn't override it.</summary>
    public double CfgScale { get; set; } = 7;

    /// <summary>How much of the source image an img2img edit may change (0..1).</summary>
    public double DenoisingStrength { get; set; } = 0.45;

    /// <summary>Pixel size when the request doesn't ask for one — modest by default, because this backend
    /// runs on the host's own GPU and a large default makes a first call look broken rather than slow.</summary>
    public int DefaultWidth { get; set; } = 512;

    /// <summary>See <see cref="DefaultWidth"/>.</summary>
    public int DefaultHeight { get; set; } = 512;

    /// <summary>Ceiling for ONE call to this backend — the render, and the probe. Generous because a render on
    /// the host's own GPU legitimately runs for minutes (which is why <c>AddAutomatic1111Provider</c> gives its
    /// client an infinite <see cref="HttpClient"/> timeout rather than the 100-second default), but bounded: a
    /// WebUI wedged mid-render answers nothing at all, and without a deadline that hangs a background render
    /// forever. A request's own <see cref="MediaRequest.TimeoutSeconds"/> overrides it.
    /// <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline — a request that carries its own
    /// <see cref="MediaRequest.TimeoutSeconds"/> still imposes one, since the more specific instruction
    /// wins either way.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// An <see cref="IModelProvider"/> over a locally-run Stable Diffusion WebUI (Automatic1111):
/// <c>POST /sdapi/v1/txt2img</c>, or <c>/sdapi/v1/img2img</c> when the request carries an input image.
/// Responses are <c>{ images: [ "&lt;base64&gt;" ] }</c>, sometimes with a <c>data:</c> prefix — both decode.
///
/// No API key and no content policy sit in this path: the server is the host's own. That makes it the
/// candidate a host puts AFTER a hosted backend when it wants a refusal to be picked up locally
/// (<see cref="MediaRoutingPolicy"/>).
///
/// <b>The loaded checkpoint decides the model, not the request.</b> The payload carries prompt, size, steps and
/// CFG only, so <see cref="MediaRequest.Model"/> is NOT honoured — including a candidate's
/// <c>"a1111:some-checkpoint"</c> pin, which the router applies to the request and this backend then ignores.
/// The render uses whatever checkpoint the WebUI currently holds (<see cref="ProbeAsync"/> reports which), and a
/// host that needs a specific one switches it in the WebUI. Every local backend answers "what decides the
/// model?" differently: ComfyUI's is the workflow, the local engine's its model path, this one's the server.
/// </summary>
/// <remarks>Wire shapes ported from a sibling app's production implementation. A server that simply isn't
/// running reports <see cref="ProviderVerdict.NotConfigured"/> rather than a failure — on a fresh machine
/// that is the normal state, and routing should skip it without penalising it.</remarks>
/// <param name="options">Endpoint and sampling defaults.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddAutomatic1111Provider</c> sets this for you.</param>
public sealed class Automatic1111Provider(
    Automatic1111Options options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IModelProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
        SupportsInputs = true,          // img2img
    };

    /// <summary>Asks which checkpoints are loaded (<c>GET /sdapi/v1/sd-models</c>). Free, and a better answer
    /// than "the port is open": a WebUI with no checkpoint is up but cannot generate, so that reports
    /// unavailable. Bounded by <see cref="Automatic1111Options.Timeout"/> — a WebUI that accepts the connection
    /// while it loads a checkpoint can otherwise stall the probe indefinitely.</summary>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct, ProbeCoreAsync,
            reason => new ProviderProbeResult(false, $"probe {reason}"));

    private async Task<ProviderProbeResult> ProbeCoreAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new ProviderProbeResult(false, "not configured: no BaseUrl");

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var response = await http.GetAsync($"{Root}/sdapi/v1/sd-models", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ProviderProbeResult(false, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            var first = FirstCheckpoint(body);
            return first is null
                ? new ProviderProbeResult(false, "the WebUI answered but has no checkpoint loaded")
                : new ProviderProbeResult(true, $"checkpoint: {first}", Version: first);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProviderProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    /// <remarks>Runs under a deadline: the request's <see cref="MediaRequest.TimeoutSeconds"/> if it
    /// carries one, else <see cref="Automatic1111Options.Timeout"/>. A fired deadline is a
    /// <see cref="ProviderVerdict.Timeout"/> result; <paramref name="ct"/> keeps its own meaning and still
    /// propagates as cancellation.</remarks>
    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => GenerateCoreAsync(request, token),
            reason => MediaResponse.Failure(ProviderVerdict.Timeout, $"the render {reason}"));

    private async Task<MediaResponse> GenerateCoreAsync(MediaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return MediaResponse.Failure(ProviderVerdict.NotConfigured, "no BaseUrl configured");

        var source = SingleInitInput.Read(request, "img2img", out var refusal);
        if (refusal is not null) return MediaResponse.Failure(ProviderVerdict.Unsupported, refusal);
        if (source is not null && source.Data is not { Length: > 0 })
            return MediaResponse.Failure(ProviderVerdict.Unsupported,
                "img2img needs the source BYTES; supply MediaInput.Data rather than a URI");

        var (width, height) = Size(request);
        // JsonObject, not an anonymous type: reflection serialization would break this package's
        // trim/AOT claim (IL2026/IL3050) — same reason as Payloads/OpenAiPayload
        var payloadBody = new JsonObject
        {
            ["prompt"] = request.Prompt ?? "",
            ["steps"] = options.Steps,
            ["width"] = width,
            ["height"] = height,
            ["cfg_scale"] = options.CfgScale,
        };
        if (source is not null)
        {
            payloadBody["init_images"] = new JsonArray(Convert.ToBase64String(source.Data!));
            payloadBody["denoising_strength"] = options.DenoisingStrength;
        }
        var payload = payloadBody.ToJsonString();
        var endpoint = source is null ? "txt2img" : "img2img";

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{Root}/sdapi/v1/{endpoint}", content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // no credential surface, so the 2-arg overload: a 401 from a proxy in front of the WebUI is
            // AuthFailed, never a NotConfigured asking for a key this backend has no way to send
            if (!response.IsSuccessStatusCode)
                return MediaResponse.Failure(
                    ProviderVerdictClassifier.FromHttpFailure(response.StatusCode, body),
                    HttpArtifacts.FailureDetail(body));

            var artifacts = HttpArtifacts.FromWebUiEnvelope(body);
            return artifacts.Count > 0
                ? MediaResponse.Success(artifacts, new MediaUsage(Count: artifacts.Count))
                : MediaResponse.Failure(ProviderVerdict.Failed,
                    $"no image in the response: {HttpArtifacts.FailureDetail(body, 200)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            // a local server that isn't running is NOT a fault to penalise — it's an unconfigured candidate
            return MediaResponse.Failure(ProviderVerdict.NotConfigured,
                $"the WebUI at {Root} is not reachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return MediaResponse.Failure(ProviderVerdictClassifier.FromException(ex), ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    /// <summary><c>"768x512"</c> → (768, 512); anything unparseable — or numeric but non-positive, such as
    /// <c>"0x0"</c> — falls back to the configured default rather than failing the call, because a bad size hint
    /// is not worth losing a generation over.</summary>
    private (int Width, int Height) Size(MediaRequest request) =>
        SizeHint.TryParse(request.Option("size"), out var width, out var height)
            ? (width, height)
            : (options.DefaultWidth, options.DefaultHeight);

    /// <summary>The first checkpoint's name from <c>[{ "title": …, "model_name": … }]</c>, or null when the
    /// list is empty/unreadable.</summary>
    private static string? FirstCheckpoint(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                foreach (var name in (string[])["model_name", "title"])
                    if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { Length: > 0 } text)
                        return text;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
