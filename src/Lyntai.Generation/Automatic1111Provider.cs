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
    /// forever. A request's own <see cref="GenerationRequest.TimeoutSeconds"/> overrides it.
    /// <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline — a request that carries its own
    /// <see cref="GenerationRequest.TimeoutSeconds"/> still imposes one, since the more specific instruction
    /// wins either way.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// An <see cref="IGenerationProvider"/> over a locally-run Stable Diffusion WebUI (Automatic1111):
/// <c>POST /sdapi/v1/txt2img</c>, or <c>/sdapi/v1/img2img</c> when the request carries an input image.
/// Responses are <c>{ images: [ "&lt;base64&gt;" ] }</c>, sometimes with a <c>data:</c> prefix — both decode.
///
/// No API key and no content policy sit in this path: the server is the host's own. That makes it the
/// candidate a host puts AFTER a hosted backend when it wants a refusal to be picked up locally
/// (<see cref="Routing.GenerationRoutingPolicy"/>).
///
/// <b>The loaded checkpoint decides the model, not the request.</b> The payload carries prompt, size, steps and
/// CFG only, so <see cref="GenerationRequest.Model"/> is NOT honoured — including a candidate's
/// <c>"a1111:some-checkpoint"</c> pin, which the router applies to the request and this backend then ignores.
/// The render uses whatever checkpoint the WebUI currently holds (<see cref="ProbeAsync"/> reports which), and a
/// host that needs a specific one switches it in the WebUI. Every local backend answers "what decides the
/// model?" differently: ComfyUI's is the workflow, the local engine's its model path, this one's the server.
/// </summary>
/// <remarks>Wire shapes ported from a sibling app's production implementation. A server that simply isn't
/// running reports <see cref="GenerationVerdict.NotConfigured"/> rather than a failure — on a fresh machine
/// that is the normal state, and routing should skip it without penalising it.</remarks>
/// <param name="options">Endpoint and sampling defaults.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddAutomatic1111Provider</c> sets this for you.</param>
public sealed class Automatic1111Provider(
    Automatic1111Options options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IGenerationProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public GenerationCapabilities Capabilities { get; } = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,          // img2img
    };

    /// <summary>Asks which checkpoints are loaded (<c>GET /sdapi/v1/sd-models</c>). Free, and a better answer
    /// than "the port is open": a WebUI with no checkpoint is up but cannot generate, so that reports
    /// unavailable. Bounded by <see cref="Automatic1111Options.Timeout"/> — a WebUI that accepts the connection
    /// while it loads a checkpoint can otherwise stall the probe indefinitely.</summary>
    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct, ProbeCoreAsync,
            reason => new GenerationProbeResult(false, $"probe {reason}"));

    private async Task<GenerationProbeResult> ProbeCoreAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new GenerationProbeResult(false, "not configured: no BaseUrl");

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var response = await http.GetAsync($"{Root}/sdapi/v1/sd-models", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new GenerationProbeResult(false, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            var first = FirstCheckpoint(body);
            return first is null
                ? new GenerationProbeResult(false, "the WebUI answered but has no checkpoint loaded")
                : new GenerationProbeResult(true, $"checkpoint: {first}", Version: first);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    /// <remarks>Runs under a deadline: the request's <see cref="GenerationRequest.TimeoutSeconds"/> if it
    /// carries one, else <see cref="Automatic1111Options.Timeout"/>. A fired deadline is a
    /// <see cref="GenerationVerdict.Timeout"/> result; <paramref name="ct"/> keeps its own meaning and still
    /// propagates as cancellation.</remarks>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => GenerateCoreAsync(request, token),
            reason => GenerationResult.Failure(GenerationVerdict.Timeout, $"the render {reason}"));

    private async Task<GenerationResult> GenerateCoreAsync(GenerationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return GenerationResult.Failure(GenerationVerdict.NotConfigured, "no BaseUrl configured");

        var source = request.Inputs.FirstOrDefault();
        if (source is not null && source.Data is not { Length: > 0 })
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                "img2img needs the source BYTES; supply GenerationInput.Data rather than a URI");

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

            // The 2-argument form ON PURPOSE, unlike OpenAiImageProvider's `(status, body, HasCredentials)`.
            // The 3-arg overload's job is to demote AuthFailed to NotConfigured when the call carried no
            // credentials — and A1111 has NO credential concept at all here: Automatic1111Options has no key,
            // token or user/password member, and this provider never sets an Authorization header, because the
            // server is the host's own. So there is no `hasCredentials` fact to pass. Hardcoding `false` would
            // not be a tidy-up, it would be a BEHAVIOUR change: a 401 from a reverse proxy in front of the
            // WebUI would stop benching the backend and start telling the user to configure an API key that
            // does not exist. AuthFailed is the honest answer — the host's proxy rejected us. "Not set up yet"
            // already has its own two routes here: no BaseUrl, and the not-reachable arm below.
            if (!response.IsSuccessStatusCode)
                return GenerationResult.Failure(
                    GenerationVerdictClassifier.FromHttpFailure(response.StatusCode, body),
                    HttpArtifacts.FailureDetail(body));

            var artifacts = HttpArtifacts.FromWebUiEnvelope(body);
            return artifacts.Count > 0
                ? GenerationResult.Success(artifacts, new GenerationUsage(Count: artifacts.Count))
                : GenerationResult.Failure(GenerationVerdict.Failed,
                    $"no image in the response: {HttpArtifacts.FailureDetail(body, 200)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            // a local server that isn't running is NOT a fault to penalise — it's an unconfigured candidate
            return GenerationResult.Failure(GenerationVerdict.NotConfigured,
                $"the WebUI at {Root} is not reachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GenerationResult.Failure(GenerationVerdictClassifier.FromException(ex), ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    /// <summary><c>"768x512"</c> → (768, 512); anything unparseable — or numeric but non-positive, such as
    /// <c>"0x0"</c> — falls back to the configured default rather than failing the call, because a bad size hint
    /// is not worth losing a generation over.</summary>
    private (int Width, int Height) Size(GenerationRequest request)
    {
        if (request.Option("size") is { Length: > 0 } size)
        {
            var parts = size.Split('x', 'X');
            // w/h must be POSITIVE, not merely numeric: "0x0" parses, and forwarding it hands the WebUI a
            // render it can only reject — the same guard LocalDiffusionProvider.ClampSize makes
            if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) &&
                w > 0 && h > 0)
                return (w, h);
        }
        return (options.DefaultWidth, options.DefaultHeight);
    }

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
