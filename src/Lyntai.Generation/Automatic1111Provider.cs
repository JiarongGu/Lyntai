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

    /// <summary>The <see cref="MediaParameters"/> the server answering at <see cref="BaseUrl"/> honours — what this
    /// backend declares, and so what a request may name before it is refused. The default is what was measured
    /// to take effect on stable-diffusion.cpp's <c>sd-server</c>, which loads ONE checkpoint and so cannot switch it
    /// per request; a Stable Diffusion WebUI also takes a checkpoint (<c>override_settings</c>) and LoRA tags, and a
    /// host running one adds <see cref="MediaParameters.Checkpoint"/> and <see cref="MediaParameters.Loras"/>. Nothing
    /// here is probed: only the host knows which server it runs.</summary>
    public IList<string> HonouredParameters { get; set; } =
    [
        MediaParameters.NegativePrompt, MediaParameters.Seed, MediaParameters.Steps, MediaParameters.CfgScale,
        MediaParameters.Sampler, MediaParameters.Scheduler,
    ];

    /// <summary>Where a request's LoRAs go. Null — the default — writes each into the prompt as the WebUI's
    /// <c>&lt;lora:name:weight&gt;</c> tag, with the name's model extension dropped. A name here sends them instead as
    /// that JSON array field of the payload, each entry <c>{"path": name, "multiplier": weight, "is_high_noise":
    /// false}</c> — what stable-diffusion.cpp's <c>sd-server</c> reads (set it to <c>"lora"</c>), since it ignores
    /// tags and refuses a path its <c>/sdapi/v1/loras</c> does not list. The request's name is sent as given, so there
    /// it is the file name that endpoint reports (<c>"lcm-lora-sdv1-5.safetensors"</c>).</summary>
    public string? LoraRequestField { get; set; }

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
/// <remarks>Run against stable-diffusion.cpp's <c>sd-server</c>, which implements this API — not against the
/// WebUI itself, whose behaviour beyond those shapes is unmeasured. A server that isn't running
/// reports <see cref="ProviderVerdict.Failed"/> — a down host, as for every other HTTP backend — so routing
/// advances and, past the dead-host threshold, stops asking until the cooldown ends. No <c>BaseUrl</c> is
/// <see cref="ProviderVerdict.NotConfigured"/>.</remarks>
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
    /// <remarks>Derived per access, so a host that changes <see cref="Automatic1111Options.HonouredParameters"/>
    /// after registration is routed by the new value.</remarks>
    public ProviderCapabilities Capabilities => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
        SupportsInputs = true,          // img2img
        Parameters = [.. options.HonouredParameters],
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

        if (ParameterRefusal.For(Capabilities, request, loraTags: true, checkpointFile: false) is { } unhonoured)
            return MediaResponse.Failure(ProviderVerdict.Unsupported, unhonoured);

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
            ["prompt"] = WithLoraTags(request),
            ["steps"] = request.Steps ?? options.Steps,
            ["width"] = width,
            ["height"] = height,
            ["cfg_scale"] = request.CfgScale ?? options.CfgScale,
        };
        // a field only when the request names it: the server's own default stands otherwise
        if (request.NegativePrompt is { Length: > 0 } negative) payloadBody["negative_prompt"] = negative;
        if (request.Seed is { } seed) payloadBody["seed"] = seed;
        if (request.Sampler is { Length: > 0 } sampler) payloadBody["sampler_name"] = sampler;
        if (request.Scheduler is { Length: > 0 } scheduler) payloadBody["scheduler"] = scheduler;
        if (options.LoraRequestField is { Length: > 0 } loraField && request.Loras.Count > 0)
            payloadBody[loraField] = new JsonArray([.. request.Loras.Select(l => (JsonNode)new JsonObject
            {
                ["path"] = l.Name,
                ["multiplier"] = l.Weight,
                ["is_high_noise"] = false,
            })]);
        if (request.Checkpoint is { Length: > 0 } checkpoint)
            payloadBody["override_settings"] = new JsonObject { ["sd_model_checkpoint"] = checkpoint };
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
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            // nothing listening is a DOWN host (D31), as ComfyUI reports it — only the detail differs from the
            // fall-through, which a render dropped mid-response takes
            return MediaResponse.Failure(ProviderVerdict.Failed,
                $"the WebUI at {Root} is not reachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return MediaResponse.Failure(ProviderVerdictClassifier.FromThrown(ex), ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    /// <summary>The prompt with each LoRA written in as the WebUI's <c>&lt;lora:name:weight&gt;</c> tag, which the
    /// server resolves against its own LoRA folder — the host's to configure there, not here. The tag names the LoRA
    /// without its model extension, as the WebUI lists it.</summary>
    private string WithLoraTags(MediaRequest request) =>
        options.LoraRequestField is { Length: > 0 }
            ? request.Prompt ?? ""
            : (request.Prompt ?? "") + string.Concat(request.Loras.Select(l =>
                $" <lora:{StripModelExtension(l.Name)}:{l.Weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}>"));

    private static string StripModelExtension(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".safetensors" or ".ckpt" or ".pt" or ".gguf"
            ? Path.GetFileNameWithoutExtension(name) : name;

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
