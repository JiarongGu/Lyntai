using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="FalQueueProvider"/>.</summary>
/// <remarks>Every URL segment is settable because this backend's surface is <b>documented, not measured</b> —
/// see the remarks on <see cref="FalQueueProvider"/>. A host that finds a path has moved can retarget it here
/// instead of waiting for a Lyntai release.</remarks>
public sealed record FalQueueOptions
{
    /// <summary>The queue API root. Blank = not configured.</summary>
    public string BaseUrl { get; init; } = "https://queue.fal.run";

    /// <summary>The API key, sent as <c>Authorization: Key &lt;key&gt;</c>. Passed in by the host and never
    /// stored (D26/D30).</summary>
    public string? ApiKey { get; init; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; init; } = "fal";

    /// <summary>Model/endpoint id used when a request doesn't name one (e.g. <c>fal-ai/wan-t2v</c>). An
    /// aggregator serves hundreds, so most callers name one per request instead.</summary>
    public string? Model { get; init; }

    /// <summary>Media kinds this account's models cover. Declared rather than discovered — the catalogue is
    /// large and changes without us, so the host states what it actually uses.</summary>
    public IReadOnlyList<string> Kinds { get; init; } =
        [GenerationKinds.Video, GenerationKinds.Image, GenerationKinds.Audio];

    /// <summary>Path segment for a request's status, appended as
    /// <c>{BaseUrl}/{model}/requests/{id}/{StatusSegment}</c>.</summary>
    public string StatusSegment { get; init; } = "status";

    /// <summary>Path segment collection for requests: <c>{BaseUrl}/{model}/{RequestsSegment}/{id}</c>.</summary>
    public string RequestsSegment { get; init; } = "requests";

    /// <summary>Query parameter used to hand the backend a webhook URL the APP hosts. Lyntai never hosts one
    /// (D30) — supply the URL via <c>GenerationRequest.Options["webhook"]</c> and call
    /// <see cref="FalQueueProvider.FetchAsync"/> when it fires.</summary>
    public string WebhookQueryParameter { get; init; } = "fal_webhook";

    /// <summary>Ceiling for ONE HTTP call to the queue — a submit, a status read, a result fetch, a cancel.
    ///
    /// <para><b>It does not bound the render.</b> The queue is asynchronous by design: a video render outlives
    /// every individual call, and <c>GenerationRenderJobHandler</c> polls it across job re-dispatches and
    /// process restarts — so poll and fetch arrive with no memory of when the submit happened and no request in
    /// hand. A whole-operation deadline could only live where the operation does, in the job's own retry
    /// budget. What this bounds is the thing that was genuinely unbounded: a queue that accepts a connection
    /// and never answers, against a client the <c>Add*</c> shim gives an infinite
    /// <see cref="HttpClient"/> timeout.</para>
    ///
    /// <para>Shorter than the inline backends' default because these are queue operations rather than renders.
    /// On <see cref="FalQueueProvider.SubmitAsync"/> a request's
    /// <see cref="GenerationRequest.TimeoutSeconds"/> still overrides it (the most specific thing that caller
    /// can say about that call); <see cref="Timeout.InfiniteTimeSpan"/> opts out entirely.</para></summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// An <see cref="IGenerationProvider"/> + <see cref="IGenerationJobProvider"/> over <b>fal.ai's queue API</b> —
/// the aggregator chosen as the first remote backend because one integration reaches the Wan/Kling/Veo-class
/// models behind a single queue shape (submit → poll → fetch, or a webhook the app owns).
///
/// Pairs with <see cref="Jobs.GenerationRenderJobHandler"/>: submit once, checkpoint the operation id, poll to
/// completion across restarts. That is the combination that stops a crash from paying for a render twice.
/// </summary>
/// <remarks>
/// <para><b>UNVERIFIED SURFACE.</b> Written from documented shapes without an API key to call, exactly as the
/// ComfyUI backend was. Paths and field names are therefore options, response parsing is tolerant (an
/// unrecognised payload reports "no artifacts" rather than inventing one), and this should be confirmed against
/// a live account before being relied on. Do not let its tests reassure you about the vendor's wire format —
/// they pin OUR behaviour given a shape, not that the shape is right.</para>
/// <para><b>The operation id carries its model</b> (<c>"{model}#{requestId}"</c>). The queue's status and result
/// URLs both need the model id, but a resumed job only hands back an operation id — so the model travels inside
/// it rather than forcing every caller to persist a second field. Documented because it is visible in
/// checkpoints and logs.</para>
/// <para><b>Cost:</b> fal prices per model AND per resolution, so the headline rate often buys a lower tier.
/// Whatever the response reports lands in <see cref="GenerationUsage.CostUsd"/> — it is never inferred from a
/// rate card.</para>
/// </remarks>
/// <param name="options">Endpoint, credential and declared kinds.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddFalProvider</c> sets this for you.</param>
public sealed class FalQueueProvider(
    FalQueueOptions options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IGenerationProvider, IGenerationJobProvider
{
    /// <summary>Separates the model id from the queue's request id inside an operation id.</summary>
    private const char ModelSeparator = '#';

    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public GenerationCapabilities Capabilities { get; } = new()
    {
        Kinds = options.Kinds,
        Deliveries = [GenerationDelivery.Job],
        SupportsInputs = true,          // image→video, reference→video: the model decides
        // Models deliberately NOT enumerated: hundreds, changing without us, and an empty list means
        // "unknown" rather than "serves nothing" (GenerationCapabilities.Models).
    };

    /// <summary>Credential presence only. The queue has no free "is this key good?" endpoint that doesn't
    /// enqueue work, and a probe must never spend a generation — so this reports what it can honestly know and
    /// says so.</summary>
    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey)
            ? new GenerationProbeResult(false, "not configured: BaseUrl and ApiKey are both required")
            : new GenerationProbeResult(true,
                "configured (credential presence only — the queue has no free validation endpoint, and a probe " +
                "must not enqueue a billable request)"));

    /// <summary>Inline delivery is not this backend's mode — the queue is asynchronous by design.</summary>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported,
            "fal's queue is asynchronous: use submit → poll → fetch (IGenerationJobProvider), or the durable " +
            "GenerationRenderJobHandler"));

    /// <inheritdoc/>
    /// <remarks>Bounded by the request's <see cref="GenerationRequest.TimeoutSeconds"/> if it carries one, else
    /// <see cref="FalQueueOptions.Timeout"/> — the ENQUEUEING call only, not the render it starts.</remarks>
    public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => SubmitCoreAsync(request, token),
            // a submit that timed out may still have been ACCEPTED — and a queued render is billable, so this
            // must not read as "nothing happened"
            reason => Failed($"the submit {reason}; the request may still have been enqueued"));

    private async Task<GenerationOperation> SubmitCoreAsync(GenerationRequest request, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return Failed(missing);
        if (Model(request) is not { Length: > 0 } model)
            return Failed("no model: name one on the request, the candidate (\"fal:model-id\") or FalQueueOptions.Model");

        var url = $"{Root}/{model.Trim('/')}";
        if (request.Option("webhook") is { Length: > 0 } webhook)
            url += $"?{options.WebhookQueryParameter}={Uri.EscapeDataString(webhook)}";

        var http = httpFactory();
        using var owned = disposeHttpClient ? http : null;   // a BYO client is the host's to dispose, not ours
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(BuildInput(request), Encoding.UTF8, "application/json"),
            };
            Authorize(message);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Failed($"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            return Field(body, "request_id") is { } requestId
                ? new GenerationOperation($"{model}{ModelSeparator}{requestId}", GenerationOperationStatus.Queued)
                : Failed($"no request_id in the response: {HttpArtifacts.FailureDetail(body, 200)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="FalQueueOptions.Timeout"/>. A status call that times out reports the
    /// operation as still RUNNING — the same treatment a transport failure already gets here, and for the same
    /// reason: no answer is not a failed render, and reading it as terminal would abandon a submitted (and
    /// billed) generation that is merely still going.</remarks>
    public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => PollCoreAsync(operationId, token),
            reason => new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: $"the status call {reason} — the render is still assumed to be running"));

    private async Task<GenerationOperation> PollCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return new GenerationOperation(operationId, GenerationOperationStatus.Failed,
                Detail: $"malformed operation id '{operationId}' — expected \"model{ModelSeparator}requestId\"");

        var (body, failure) = await GetAsync(
            $"{Root}/{model}/{options.RequestsSegment}/{requestId}/{options.StatusSegment}", ct).ConfigureAwait(false);
        if (failure is not null)
            return new GenerationOperation(operationId, GenerationOperationStatus.Running, Detail: failure);

        var status = Field(body!, "status");
        return status?.ToUpperInvariant() switch
        {
            "IN_QUEUE" => new GenerationOperation(operationId, GenerationOperationStatus.Queued, Detail: QueueDetail(body!)),
            "IN_PROGRESS" => new GenerationOperation(operationId, GenerationOperationStatus.Running),
            "COMPLETED" => new GenerationOperation(operationId, GenerationOperationStatus.Succeeded, Progress: 1),
            // an unknown status is NOT a failure: treating "something new" as terminal would abandon a render
            // that is merely in a state this build hasn't heard of
            _ => new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: $"unrecognised status '{status}'"),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="FalQueueOptions.Timeout"/>; a fired deadline is a
    /// <see cref="GenerationVerdict.Timeout"/> result, and the operation can simply be fetched again.</remarks>
    public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => FetchCoreAsync(operationId, token),
            reason => GenerationResult.Failure(GenerationVerdict.Timeout, $"the result fetch {reason}"));

    private async Task<GenerationResult> FetchCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return GenerationResult.Failure(GenerationVerdict.Failed, $"malformed operation id '{operationId}'");

        var (body, failure) = await GetAsync(
            $"{Root}/{model}/{options.RequestsSegment}/{requestId}", ct).ConfigureAwait(false);
        if (failure is not null) return GenerationResult.Failure(GenerationVerdictClassifier.FromErrorText(failure), failure);

        var artifacts = ReadArtifacts(body!);
        return artifacts.Count > 0
            ? GenerationResult.Success(artifacts, new GenerationUsage(Count: artifacts.Count, CostUsd: Cost(body!)))
            : GenerationResult.Failure(GenerationVerdict.Failed,
                $"no artifacts in the result: {HttpArtifacts.FailureDetail(body!, 200)}");
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="FalQueueOptions.Timeout"/>; a cancel that timed out may or may not have
    /// landed, so the render is reported as still running rather than assumed stopped.</remarks>
    public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => CancelCoreAsync(operationId, token),
            reason => new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: $"the cancel {reason}"));

    private async Task<GenerationOperation> CancelCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return new GenerationOperation(operationId, GenerationOperationStatus.Failed, Detail: "malformed operation id");

        var http = httpFactory();
        using var owned = disposeHttpClient ? http : null;   // a BYO client is the host's to dispose, not ours
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Put,
                $"{Root}/{model}/{options.RequestsSegment}/{requestId}/cancel");
            Authorize(message);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new GenerationOperation(operationId, GenerationOperationStatus.Cancelled)
                // a render already running may not be cancellable — report it, don't pretend
                : new GenerationOperation(operationId, GenerationOperationStatus.Running,
                    Detail: $"cancel rejected: {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationOperation(operationId, GenerationOperationStatus.Running, Detail: ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    private string? Unconfigured() =>
        string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey)
            ? "not configured: BaseUrl and ApiKey are both required"
            : null;

    private string? Model(GenerationRequest request) =>
        request.Model is { Length: > 0 } model ? model : options.Model;

    private void Authorize(HttpRequestMessage message)
    {
        if (options.ApiKey is { Length: > 0 } key)
            message.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
    }

    private static GenerationOperation Failed(string detail) =>
        new("", GenerationOperationStatus.Failed, Detail: detail);

    /// <summary>Split <c>"model#requestId"</c>. The model may itself contain slashes (<c>fal-ai/wan-t2v</c>),
    /// which is why the separator is a character the ids don't use rather than the last path segment.</summary>
    private static (string Model, string? RequestId) Split(string operationId)
    {
        var at = operationId.LastIndexOf(ModelSeparator);
        return at <= 0 || at == operationId.Length - 1
            ? (operationId, null)
            : (operationId[..at], operationId[(at + 1)..]);
    }

    private async Task<(string? Body, string? Failure)> GetAsync(string url, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return (null, missing);

        var http = httpFactory();
        using var owned = disposeHttpClient ? http : null;   // a BYO client is the host's to dispose, not ours
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            Authorize(message);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>The model's input object. Common fields are mapped; everything else in
    /// <see cref="GenerationRequest.Options"/> is passed through verbatim, because each model on an aggregator
    /// takes its own parameters and typing them would need a release per model.</summary>
    internal static string BuildInput(GenerationRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (request.Prompt is { Length: > 0 } prompt) writer.WriteString("prompt", prompt);

            // a first-frame / reference image travels as a URL when the caller has one; fal takes URLs, so
            // inline bytes are refused rather than silently uploaded somewhere on the caller's behalf
            if (request.Inputs.FirstOrDefault(i => i.Uri is { Length: > 0 }) is { } input)
                writer.WriteString(input.Role == GenerationInputRoles.FirstFrame ? "image_url" : "input_image_url", input.Uri!);

            foreach (var (key, value) in request.Options)
            {
                if (string.Equals(key, "webhook", StringComparison.OrdinalIgnoreCase)) continue;  // a URL, not an input
                writer.WriteString(key, value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Pull artifacts out of a completed result. Tolerant by design: fal's models return their output
    /// under model-specific names (<c>video</c>, <c>images</c>, <c>audio</c>), so ANY object carrying a
    /// <c>url</c> counts — and nothing recognised means "no artifacts", never an invented one.</summary>
    internal static IReadOnlyList<GenerationArtifact> ReadArtifacts(string body)
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            var artifacts = new List<GenerationArtifact>();
            Walk(doc.RootElement, artifacts, depth: 0);
            return artifacts;
        }
    }

    private static void Walk(JsonElement element, List<GenerationArtifact> artifacts, int depth)
    {
        if (depth > 4 || artifacts.Count >= 32) return;   // a result document, not an arbitrary graph

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (Str(element, "url") is { } url)
                {
                    var contentType = Str(element, "content_type") ?? MediaTypeOf(url);
                    artifacts.Add(new GenerationArtifact(contentType, Uri: url));
                    return;   // this object IS the artifact — don't also walk its siblings
                }
                foreach (var property in element.EnumerateObject()) Walk(property.Value, artifacts, depth + 1);
                return;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, artifacts, depth + 1);
                return;
        }
    }

    private static double? Cost(string body)
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return null;
        using (doc)
        {
            foreach (var name in (string[])["cost", "cost_usd", "price"])
                if (doc.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var cost))
                    return cost;
            return null;
        }
    }

    private static string? QueueDetail(string body) =>
        Field(body, "queue_position") is { } position ? $"queue position {position}" : null;

    private static string MediaTypeOf(string url) => Path.GetExtension(new Uri(url, UriKind.RelativeOrAbsolute)
        .ToString().Split('?')[0]).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        _ => "application/octet-stream",
    };

    private static string? Field(string body, string name)
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return null;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(name, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            };
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
