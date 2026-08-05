using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="OpenAiImageProvider"/>.</summary>
/// <remarks>Credentials are passed IN by the host and never stored (D26/D30) — read
/// <see cref="ApiKey"/> from your own configuration or secret store.</remarks>
public sealed record OpenAiImageOptions
{
    /// <summary>The API root, including any version segment (<c>https://api.openai.com/v1</c>). Blank means
    /// "not configured", which the provider reports as <see cref="GenerationVerdict.NotConfigured"/> rather
    /// than failing.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Bearer token, where the endpoint needs one. A local OpenAI-images-shaped server usually
    /// doesn't.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Model sent when the request doesn't name one.</summary>
    public string? Model { get; init; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; init; } = "openai-images";

    /// <summary>Size used when the request doesn't ask for one.</summary>
    public string DefaultSize { get; init; } = "1024x1024";

    /// <summary>Ceiling for ONE call to this backend — the render, and the probe. Generous because an image
    /// render legitimately runs for minutes (which is why <c>AddOpenAiImageProvider</c> gives its client an
    /// infinite <see cref="HttpClient"/> timeout rather than the 100-second default), but bounded, because a
    /// backend that accepts the connection and then stalls would otherwise hang a background render forever.
    /// A request's own <see cref="GenerationRequest.TimeoutSeconds"/> overrides it.
    /// <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline — a request that carries its own
    /// <see cref="GenerationRequest.TimeoutSeconds"/> still imposes one, since the more specific instruction
    /// wins either way.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// An <see cref="IGenerationProvider"/> over an OpenAI-compatible images API — the cloud service, or any local
/// server that speaks the same shape. INLINE delivery: one request, artifacts back.
///
/// Two endpoints, chosen by whether the request carries inputs:
/// <list type="bullet">
/// <item><c>POST {BaseUrl}/images/generations</c> for text→image;</item>
/// <item><c>POST {BaseUrl}/images/edits</c> (multipart) when an input image is supplied — a prompt-guided
///   edit.</item>
/// </list>
/// Both response variants are handled: inline <c>b64_json</c>, and a <c>url</c>, which is returned AS a URI
/// artifact rather than downloaded (the platform never spends the caller's bandwidth uninvited).
/// </summary>
/// <remarks>The request/response shapes are ported from a sibling app's production implementation, which is
/// why both response variants are covered rather than only the one a first test happens to hit.</remarks>
/// <param name="options">Endpoint, credential and defaults.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO, so the host owns pooling and
/// lifetime (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns (a singleton, a Polly-decorated one): disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddOpenAiImageProvider</c> sets this for you.</param>
public sealed class OpenAiImageProvider(
    OpenAiImageOptions options, Func<HttpClient> httpFactory, bool disposeHttpClient = true) : IGenerationProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public GenerationCapabilities Capabilities { get; } = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,          // /images/edits
        // Models deliberately NOT enumerated: the catalogue is the service's, changes without us, and an
        // empty list means "unknown" rather than "serves nothing" (see GenerationCapabilities.Models).
    };

    /// <summary>Lists models (<c>GET {BaseUrl}/models</c>) — a real answer to "is this usable?" that costs
    /// nothing, instead of the generate-and-discard test this replaces. Bounded by
    /// <see cref="OpenAiImageOptions.Timeout"/>: the shim's client has no timeout of its own, so an
    /// unresponsive host would otherwise stall the probe indefinitely.</summary>
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/models");
            Authorize(request);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return new GenerationProbeResult(true, "models endpoint answered");
            // the same distinction the generate path makes, so a probe's reason matches the verdict a render
            // would get: "not configured" reads as a setup step, "rejected" reads as a wrong key
            var unconfigured = GenerationVerdictClassifier.FromHttpFailure(response.StatusCode, body, HasCredentials)
                == GenerationVerdict.NotConfigured;
            return new GenerationProbeResult(false, unconfigured
                ? $"not configured: the endpoint requires an ApiKey ({(int)response.StatusCode})"
                : $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    /// <remarks>Runs under a deadline: the request's <see cref="GenerationRequest.TimeoutSeconds"/> if it
    /// carries one, else <see cref="OpenAiImageOptions.Timeout"/>. A fired deadline is a
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

        var edit = request.Inputs.FirstOrDefault();
        if (edit is not null && edit.Data is not { Length: > 0 })
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                "this endpoint edits BYTES; supply GenerationInput.Data (a URI-only input would mean the " +
                "platform downloading it for you, and guessing at auth for that host)");

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var message = edit is null ? Generation(request) : Edit(request, edit);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                // HasCredentials: a 401 with no key supplied is NOT_CONFIGURED (skip blamelessly, offer setup),
                // not AUTH_FAILED (bench the backend for the cooldown window). An OpenAI-compatible endpoint run
                // locally needs no key at all, so only the server DEMANDING one makes "no key" a config problem.
                return GenerationResult.Failure(
                    GenerationVerdictClassifier.FromHttpFailure(response.StatusCode, body, HasCredentials),
                    HttpArtifacts.FailureDetail(body));

            var artifacts = HttpArtifacts.FromOpenAiEnvelope(body);
            return artifacts.Count > 0
                ? GenerationResult.Success(artifacts, new GenerationUsage(Count: artifacts.Count))
                : GenerationResult.Failure(GenerationVerdict.Failed,
                    $"no image in the response: {HttpArtifacts.FailureDetail(body, 200)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return GenerationResult.Failure(GenerationVerdictClassifier.FromException(ex), ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    /// <summary>Whether this backend has anything to authenticate WITH — what separates "not set up yet" from
    /// "your key was rejected" when the server answers 401/403.</summary>
    private bool HasCredentials => !string.IsNullOrWhiteSpace(options.ApiKey);

    private string Size(GenerationRequest request) =>
        request.Option("size") is { Length: > 0 } size ? size : options.DefaultSize;

    private string? Model(GenerationRequest request) =>
        request.Model is { Length: > 0 } model ? model : options.Model;

    private HttpRequestMessage Generation(GenerationRequest request)
    {
        // JsonObject over an anonymous type — keeps the package's trim/AOT claim honest
        var payload = new JsonObject
        {
            ["model"] = Model(request),
            ["prompt"] = request.Prompt ?? "",
            ["n"] = 1,
            ["size"] = Size(request),
            ["response_format"] = "b64_json",
        }.ToJsonString();
        var message = new HttpRequestMessage(HttpMethod.Post, $"{Root}/images/generations")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        Authorize(message);
        return message;
    }

    private HttpRequestMessage Edit(GenerationRequest request, GenerationInput input)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(input.Data!);
        image.Headers.ContentType = new MediaTypeHeaderValue(input.MediaType);
        form.Add(image, "image", "image.png");
        form.Add(new StringContent(request.Prompt ?? ""), "prompt");
        if (Model(request) is { Length: > 0 } model) form.Add(new StringContent(model), "model");
        form.Add(new StringContent(Size(request)), "size");
        form.Add(new StringContent("b64_json"), "response_format");

        var message = new HttpRequestMessage(HttpMethod.Post, $"{Root}/images/edits") { Content = form };
        Authorize(message);
        return message;
    }

    private void Authorize(HttpRequestMessage message)
    {
        if (options.ApiKey is { Length: > 0 } key)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }
}
