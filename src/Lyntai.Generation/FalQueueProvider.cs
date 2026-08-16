using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="FalQueueProvider"/>.</summary>
/// <remarks>The queue's path segments — <see cref="RequestsSegment"/>, <see cref="StatusSegment"/> and
/// <see cref="CancelSegment"/> — are settable because this backend's surface is <b>documented, not measured</b>:
/// see the remarks on <see cref="FalQueueProvider"/>. A host that finds one has moved can retarget it here
/// instead of waiting for a Lyntai release. <b>Every</b> segment the provider builds a URL from is one of these;
/// a hardcoded literal among them would be the one path a host could not repair without a Lyntai
/// release.</remarks>
public sealed class FalQueueOptions
{
    /// <summary>The queue API root. Blank = not configured.</summary>
    public string BaseUrl { get; set; } = "https://queue.fal.run";

    /// <summary>The API key, sent as <c>Authorization: Key &lt;key&gt;</c>. Passed in by the host and never
    /// stored (D20/D24).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "fal";

    /// <summary>Model/endpoint id used when a request doesn't name one (e.g. <c>fal-ai/wan-t2v</c>). An
    /// aggregator serves hundreds, so most callers name one per request instead.</summary>
    public string? Model { get; set; }

    /// <summary>Media kinds this account's models cover. Declared rather than discovered — the catalogue is
    /// large and changes without us, so the host states what it actually uses.</summary>
    public IReadOnlyList<string> Kinds { get; set; } =
        [GenerationKinds.Video, GenerationKinds.Image, GenerationKinds.Audio];

    /// <summary>Path segment for a request's status, appended as
    /// <c>{BaseUrl}/{model}/{RequestsSegment}/{id}/{StatusSegment}</c>.</summary>
    public string StatusSegment { get; set; } = "status";

    /// <summary>Path segment collection for requests: <c>{BaseUrl}/{model}/{RequestsSegment}/{id}</c>.</summary>
    public string RequestsSegment { get; set; } = "requests";

    /// <summary>Path segment that abandons a request, appended as
    /// <c>{BaseUrl}/{model}/{RequestsSegment}/{id}/{CancelSegment}</c> — the sibling of
    /// <see cref="StatusSegment"/> on the same request path, and settable for the same reason.</summary>
    public string CancelSegment { get; set; } = "cancel";

    /// <summary>How the queue's own status strings map onto <see cref="GenerationOperationStatus"/>.
    /// Case-insensitive. A status NOT in this map is treated as still running — never as a failure, because
    /// declaring an unknown state terminal abandons a render that is merely in a state this build has not
    /// heard of.</summary>
    /// <remarks><b>Settable for the same reason the path segments are, and it matters more.</b> A wrong PATH
    /// fails loudly on the first call; a wrong status STRING fails quietly — the render is polled forever, or
    /// is reported finished when it is not. The shipped three are a reading of vendor documentation that
    /// nobody here has been able to call, so the host who first runs this against the real queue is the one
    /// who finds out, and they must be able to fix it in `appsettings.json` rather than wait for a release.
    /// That is also why this is a dictionary rather than a delegate: configuration binding can reach it.
    /// <para>Replacing the map replaces it wholesale — add to it to extend the vocabulary, assign to it to
    /// redefine one.</para></remarks>
    public IDictionary<string, GenerationOperationStatus> StatusVocabulary { get; set; } =
        new Dictionary<string, GenerationOperationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["IN_QUEUE"] = GenerationOperationStatus.Queued,
            ["IN_PROGRESS"] = GenerationOperationStatus.Running,
            ["COMPLETED"] = GenerationOperationStatus.Succeeded,
        };

    /// <summary>Result fields read, in order, as the render's cost — the first numeric one wins. Settable
    /// for the same reason as <see cref="StatusVocabulary"/>: what this queue calls the field, and whether
    /// it reports one at all, is documented rather than measured. An empty list disables cost reporting,
    /// which is honest when a deployment knows the number is wrong.</summary>
    public IList<string> CostFields { get; set; } = ["cost", "cost_usd", "price"];

    /// <summary>Query parameter used to hand the backend a webhook URL the APP hosts. Lyntai never hosts one
    /// (D24) — supply the URL via <c>GenerationRequest.Options["webhook"]</c> and call
    /// <see cref="FalQueueProvider.FetchAsync"/> when it fires.</summary>
    public string WebhookQueryParameter { get; set; } = "fal_webhook";

    /// <summary>Ceiling for ONE HTTP call to the queue — a submit, a status read, a result fetch, a cancel.
    ///
    /// <para><b>It does not bound the render.</b> The queue is asynchronous by design: a video render outlives
    /// every individual call, and <see cref="Jobs.GenerationRenderJobHandler"/> polls it across job re-dispatches and
    /// process restarts — so poll and fetch arrive with no memory of when the submit happened and no request in
    /// hand. A whole-operation deadline could only live where the operation does, in the job's own retry
    /// budget. What this bounds is the thing that was genuinely unbounded: a queue that accepts a connection
    /// and never answers, against a client the <c>Add*</c> shim gives an infinite
    /// <see cref="HttpClient"/> timeout.</para>
    ///
    /// <para>Shorter than the inline backends' default because these are queue operations rather than renders.
    /// On <see cref="FalQueueProvider.SubmitAsync"/> a request's
    /// <see cref="GenerationRequest.TimeoutSeconds"/> still overrides it (the most specific thing that caller
    /// can say about that call). <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline, but a submit
    /// whose request carries its own <see cref="GenerationRequest.TimeoutSeconds"/> still has
    /// one.</para></summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
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
/// <para><b>UNVERIFIED SURFACE.</b> Written from documented shapes without an API key to call. Paths and
/// field names are therefore options, response parsing is tolerant (an unrecognised payload reports "no
/// artifacts" rather than inventing one), and this wants confirming against a live account. Its tests pin
/// OUR behaviour given a shape, never that the shape is right.</para>
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
            // A submit that timed out may still have been ACCEPTED — and a queued render is billable, so this
            // must not read as "nothing happened". Inconclusive is what stops the router trying the NEXT
            // backend and paying for the same generation twice.
            reason => Failed($"the submit {reason}; the request may still have been enqueued") with
            {
                Inconclusive = true,
            });

    private async Task<GenerationOperation> SubmitCoreAsync(GenerationRequest request, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return Failed(missing);
        if (Model(request) is not { Length: > 0 } model)
            return Failed("no model: name one on the request, the candidate (\"fal:model-id\") or FalQueueOptions.Model");

        // fal reads input media from a URL it can fetch, so a bytes-only input has nowhere to go. Refusing is
        // the only honest answer: dropping it (which is what BuildInput used to do) submitted — and billed — a
        // text-to-video render against a caller who asked for image→video, and the result looked plausible.
        if (request.Inputs.Count > 0 && !request.Inputs.Any(i => i.Uri is { Length: > 0 }))
            return Failed("fal takes input media as a URL; supply GenerationInput.Uri rather than Data — the " +
                "platform will not upload your bytes on your behalf");

        var url = $"{Root}/{model.Trim('/')}";
        if (request.Option("webhook") is { Length: > 0 } webhook)
            url += $"?{options.WebhookQueryParameter}={Uri.EscapeDataString(webhook)}";

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
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

        var (body, failure, transport) = await GetAsync(
            $"{Root}/{model}/{options.RequestsSegment}/{requestId}/{options.StatusSegment}", ct).ConfigureAwait(false);
        // A failure that never reached the queue leaves the render alive; one the queue ANSWERED — a 4xx for
        // an id it does not know, or a backend with no key — is terminal. Reporting every failure as Running
        // stranded the job: GenerationRenderJobHandler re-checkpoints and polls again, forever, so the render
        // was never dead-lettered, never failed and never completed, with the reason sitting in Detail where
        // nothing acts on it.
        if (failure is not null)
            return new GenerationOperation(operationId,
                transport ? GenerationOperationStatus.Running : GenerationOperationStatus.Failed,
                Detail: failure);

        var status = Field(body!, "status");

        // The vocabulary is CONFIGURED, not compiled in. This backend's wire format is documented, not
        // measured, so the shipped three are a best reading of vendor docs — and the host who first runs it
        // for real is the one who finds out. Correcting a wrong string must not require a library release,
        // and `StatusVocabulary` is a plain dictionary precisely so it can be fixed in `appsettings.json`.
        if (status is not null && options.StatusVocabulary.TryGetValue(status, out var mapped))
            return new GenerationOperation(operationId, mapped,
                Progress: mapped == GenerationOperationStatus.Succeeded ? 1 : null,
                Detail: mapped == GenerationOperationStatus.Queued ? QueueDetail(body!) : null);

        // an unknown status is NOT a failure: treating "something new" as terminal would abandon a render
        // that is merely in a state this build hasn't heard of
        return new GenerationOperation(operationId, GenerationOperationStatus.Running,
            Detail: $"unrecognised status '{status}'");
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

        // the fetch path classifies the failure rather than branching on transport: a fetch that cannot be
        // completed is a result the caller acts on now, where a poll is a question that can be asked again
        var (body, failure, _) = await GetAsync(
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

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Put,
                $"{Root}/{model}/{options.RequestsSegment}/{requestId}/{options.CancelSegment}");
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

    /// <summary><c>Transport</c> distinguishes a failure that says NOTHING about the render from one that
    /// says this id will never resolve. Only <see cref="PollAsync"/> cares, and it is the difference between
    /// waiting and abandoning something already paid for.
    ///
    /// <para><b>The bar for terminal is high, and deliberately higher than ComfyUI's.</b> Only a <c>404</c>
    /// — the queue saying it does not know this id — and the unconfigured pre-check are terminal.
    /// <c>429</c>, <c>401</c>, <c>403</c>, <c>408</c> and every 5xx keep polling. This rule was first
    /// written as "terminal unless 5xx", copied from <c>ComfyUiProvider.HistoryAsync</c>, and that copy does
    /// not transfer: ComfyUI is a loopback server that never rate-limits, while fal is a hosted, paid,
    /// rate-limiting API — so a single <c>429</c> would have dead-lettered a render that was still running
    /// and already billed. A render abandoned is money gone; a render polled a few times too many is not.</para>
    ///
    /// <para>Nothing polls forever regardless: such a job ends by cancellation, a deadline, or the handler
    /// returning <c>Fail</c>. The terminal arm exists for the one case where none of those would ever fire
    /// because the id is simply unknown.</para></summary>
    private async Task<(string? Body, string? Failure, bool Transport)> GetAsync(string url, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return (null, missing, false);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            Authorize(message);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null, false)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}",
                   // transport = "keep waiting". Everything EXCEPT a 404 qualifies: a rate limit, an auth
                   // blip during a key rotation, a gateway challenge and a 5xx all say nothing about whether
                   // the render is still running, and it is already paid for.
                   response.StatusCode != System.Net.HttpStatusCode.NotFound);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message, true);   // never reached the queue — says nothing about the render
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
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

            // a first-frame / reference image travels as a URL, because fal takes URLs only. An input carrying
            // nothing but bytes is refused in SubmitCoreAsync rather than skipped here — skipping it is what
            // billed a text-to-video render against a caller who asked for image→video.
            if (request.Inputs.FirstOrDefault(i => i.Uri is { Length: > 0 }) is { } input)
                writer.WriteString(
                    string.Equals(input.Role, GenerationInputRoles.FirstFrame, StringComparison.OrdinalIgnoreCase)
                        ? "image_url"
                        : "input_image_url",
                    input.Uri!);

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
                if (HttpArtifacts.Str(element, "url") is { } url)
                {
                    var contentType = HttpArtifacts.Str(element, "content_type") ?? MediaTypeOf(url);
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

    private double? Cost(string body)
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return null;
        using (doc)
        {
            foreach (var name in options.CostFields)
                if (doc.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var cost))
                    return cost;
            return null;
        }
    }

    private static string? QueueDetail(string body) =>
        Field(body, "queue_position") is { } position ? $"queue position {position}" : null;

    private static string MediaTypeOf(string url) =>
        HttpArtifacts.MediaTypeForExtension(Path.GetExtension(
            new Uri(url, UriKind.RelativeOrAbsolute).ToString().Split('?')[0]));

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
}
