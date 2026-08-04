using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lyntai.Generation.Http;

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
public sealed class OpenAiImageProvider(OpenAiImageOptions options, Func<HttpClient> httpFactory) : IGenerationProvider
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
    /// nothing, instead of the generate-and-discard test this replaces.</summary>
    public async Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new GenerationProbeResult(false, "not configured: no BaseUrl");

        using var http = httpFactory();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/models");
            Authorize(request);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new GenerationProbeResult(true, "models endpoint answered")
                : new GenerationProbeResult(false, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return GenerationResult.Failure(GenerationVerdict.NotConfigured, "no BaseUrl configured");

        var edit = request.Inputs.FirstOrDefault();
        if (edit is not null && edit.Data is not { Length: > 0 })
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                "this endpoint edits BYTES; supply GenerationInput.Data (a URI-only input would mean the " +
                "platform downloading it for you, and guessing at auth for that host)");

        using var http = httpFactory();
        try
        {
            using var message = edit is null ? Generation(request) : Edit(request, edit);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return GenerationResult.Failure(
                    GenerationVerdictClassifier.FromHttpFailure(response.StatusCode, body),
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

    private string Size(GenerationRequest request) =>
        request.Option("size") is { Length: > 0 } size ? size : options.DefaultSize;

    private string? Model(GenerationRequest request) =>
        request.Model is { Length: > 0 } model ? model : options.Model;

    private HttpRequestMessage Generation(GenerationRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = Model(request),
            prompt = request.Prompt ?? "",
            n = 1,
            size = Size(request),
            response_format = "b64_json",
        });
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
