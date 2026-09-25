using Lyntai.Inference;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="FalProvider"/>.</summary>
/// <remarks>Every URL segment, the status vocabulary, the error and cost fields, the auth scheme and the extra
/// query parameters are settable because this backend has never been called against the real service (see
/// <see cref="FalProvider"/>): a host that finds the wire differs corrects it here, in configuration, rather
/// than waiting for a Lyntai release.</remarks>
public sealed class FalOptions
{
    /// <summary>The queue API root. Blank = not configured.</summary>
    public string BaseUrl { get; set; } = "https://queue.fal.run";

    /// <summary>The API key, sent as <c>Authorization: {AuthScheme} &lt;key&gt;</c>. Passed in by the host and
    /// never stored (D20/D24).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The scheme <see cref="ApiKey"/> is sent under. fal's own queue takes <c>Key</c>, the default; a
    /// gateway proxying fal's wire may take <c>Bearer</c> (see <see cref="QueryParameters"/>).</summary>
    public string AuthScheme { get; set; } = "Key";

    /// <summary>Query parameters added to EVERY call — submit, status, result and cancel — before the
    /// webhook's. Empty by default.
    /// <para>They make a gateway that proxies fal's own wire a configuration rather than a class. Hugging
    /// Face's router reaches fal with <c>BaseUrl = "https://router.huggingface.co/fal-ai"</c>,
    /// <c>AuthScheme = "Bearer"</c>, a Hugging Face token as <see cref="ApiKey"/> and
    /// <c>QueryParameters["_subdomain"] = "queue"</c> — the same wire, so the same parsing.</para></summary>
    public IDictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "fal";

    /// <summary>Model/endpoint id used when a request doesn't name one (e.g. <c>fal-ai/wan-t2v</c>). An
    /// aggregator serves hundreds, so most callers name one per request instead.</summary>
    public string? Model { get; set; }

    /// <summary>Media kinds this account's models cover. Declared rather than discovered — the catalogue is
    /// large and changes without us, so the host states what it actually uses.</summary>
    public IReadOnlyList<string> Produces { get; set; } =
        [ProviderKinds.Video, ProviderKinds.Image, ProviderKinds.Audio];

    /// <summary>Path segment for a request's status, appended as
    /// <c>{BaseUrl}/{model}/{RequestsSegment}/{id}/{StatusSegment}</c>.</summary>
    public string StatusSegment { get; set; } = "status";

    /// <summary>Path segment collection for requests: <c>{BaseUrl}/{model}/{RequestsSegment}/{id}</c>.</summary>
    public string RequestsSegment { get; set; } = "requests";

    /// <summary>Path segment that abandons a request, appended as
    /// <c>{BaseUrl}/{model}/{RequestsSegment}/{id}/{CancelSegment}</c> — the sibling of
    /// <see cref="StatusSegment"/> on the same request path, and settable for the same reason.</summary>
    public string CancelSegment { get; set; } = "cancel";

    /// <summary>How the queue's own status strings map onto <see cref="QueuedOperationStatus"/>.
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
    public IDictionary<string, QueuedOperationStatus> StatusVocabulary { get; set; } =
        new Dictionary<string, QueuedOperationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["IN_QUEUE"] = QueuedOperationStatus.Queued,
            ["IN_PROGRESS"] = QueuedOperationStatus.Running,
            ["COMPLETED"] = QueuedOperationStatus.Succeeded,
        };

    /// <summary>Result fields read, in order, as the render's cost — the first numeric one wins. Settable
    /// for the same reason as <see cref="StatusVocabulary"/>: what this queue calls the field, and whether
    /// it reports one at all, is documented rather than measured. An empty list disables cost reporting,
    /// which is honest when a deployment knows the number is wrong.</summary>
    public IList<string> CostFields { get; set; } = ["cost", "cost_usd", "price"];

    /// <summary>The status-document field that marks a FAILED request. fal documents a failed request as
    /// <c>COMPLETED</c> carrying this field, so a status that maps to
    /// <see cref="QueuedOperationStatus.Succeeded"/> and carries a non-empty value here polls as
    /// <see cref="QueuedOperationStatus.Failed"/>, the value in the detail — never as a success whose fetch
    /// then fails. Empty disables the check.</summary>
    public string ErrorField { get; set; } = "error";

    /// <summary>The field beside <see cref="ErrorField"/> naming the failure's kind
    /// (<c>content_policy_violation</c>), carried into the failed operation's detail. Empty leaves it
    /// out.</summary>
    public string ErrorTypeField { get; set; } = "error_type";

    /// <summary>Query parameter used to hand the backend a webhook URL the APP hosts. Lyntai never hosts one
    /// (D24) — supply the URL via <c>MediaRequest.Options["webhook"]</c> and call
    /// <see cref="FalProvider.FetchAsync"/> when it fires.</summary>
    public string WebhookQueryParameter { get; set; } = "fal_webhook";

    /// <summary>Ceiling for ONE HTTP call to the queue — a submit, a status read, a result fetch, a cancel.
    ///
    /// <para><b>It does not bound the render.</b> The queue is asynchronous by design: a video render outlives
    /// every individual call, and a durable job polls it across re-dispatches and process restarts — so poll and
    /// fetch arrive with no memory of when the submit happened and no request in hand. A whole-operation
    /// deadline could only live where the operation does, in the job's own retry budget. What this bounds is
    /// the thing that was genuinely unbounded: a queue that accepts a connection and never answers, against a
    /// client the <c>Add*</c> shim gives an infinite <see cref="HttpClient"/> timeout.</para>
    ///
    /// <para>Shorter than the inline backends' default because these are queue operations rather than renders.
    /// On <see cref="FalProvider.SubmitAsync"/> a request's
    /// <see cref="MediaRequest.TimeoutSeconds"/> still overrides it (the most specific thing that caller
    /// can say about that call). <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline, but a submit
    /// whose request carries its own <see cref="MediaRequest.TimeoutSeconds"/> still has
    /// one.</para></summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// An <see cref="IModelProvider"/> + <see cref="IMediaJobProvider"/> over <b>fal.ai's queue API</b> — one
/// integration reaching the Wan/Kling/Veo-class models behind a single queue shape (submit → poll → fetch, or a
/// webhook the app owns). A durable job (<see cref="Jobs.GenerationPipelineJobHandler"/>) submits once,
/// checkpoints the operation id and polls to completion across restarts, so a crash never pays for a render twice.
/// </summary>
/// <remarks>
/// <para><b>Never called against fal.ai.</b> Written from fal's public queue documentation; nobody maintaining
/// Lyntai holds a fal account, so no request from it has reached the real service. Its tests pin this library's
/// behaviour for each documented shape, never that fal answers in it — treat the first real run as the
/// verification. A host corrects in configuration the URL segments, the status vocabulary, the error, cost and
/// auth settings, extra query parameters (<see cref="FalOptions"/>), and any request field, which
/// <see cref="MediaRequest.Options"/> sends verbatim. It cannot correct the response fields read by name:
/// <c>request_id</c>, <c>status</c>, <c>queue_position</c>, <c>url</c> and <c>content_type</c>.</para>
/// <para><b>Open against the docs:</b> whether a model with a sub-path (<c>fal-ai/flux/dev</c>) takes its full
/// path in the status and result URLs, which is what this sends; and a cancel is a REQUEST (202), so
/// <see cref="CancelAsync"/> reports the render still running and only a poll says how it ended.</para>
/// <para><b>Cost:</b> fal's documented results carry NO cost field, so unless a
/// <see cref="FalOptions.CostFields"/> name matches, <see cref="MediaUsage.CostUsd"/> is null and a spend cap
/// never sees these renders. A reported cost is used as given, never inferred from a rate card.</para>
/// <para><b>The operation id carries its model</b> (<c>"{model}#{requestId}"</c>): the status and result URLs
/// need the model, and a resumed job hands back only an operation id.</para>
/// </remarks>
/// <param name="options">Endpoint, credential and declared kinds.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddFalProvider</c> sets this for you.</param>
public sealed class FalProvider(
    FalOptions options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IModelProvider, IMediaJobProvider
{
    /// <summary>Separates the model id from the queue's request id inside an operation id.</summary>
    private const char ModelSeparator = '#';

    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    /// <remarks>Derived per access, so a host that changes <see cref="FalOptions.Produces"/> after
    /// registration is routed by the new value.</remarks>
    public ProviderCapabilities Capabilities => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = options.Produces,
        Operations = [ProviderOperation.Queued],
        SupportsInputs = true,          // ONE input, by URL, in a role fal has a field for; any other is refused
        // Models deliberately NOT enumerated: hundreds, changing without us, and an empty list means
        // "unknown" rather than "serves nothing" (ProviderCapabilities.Models).
    };

    /// <summary>Credential presence only. The queue has no free "is this key good?" endpoint that doesn't
    /// enqueue work, and a probe must never spend a generation — so this reports what it can honestly know and
    /// says so.</summary>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey)
            ? new ProviderProbeResult(false, "not configured: BaseUrl and ApiKey are both required")
            : new ProviderProbeResult(true,
                "configured (credential presence only — the queue has no free validation endpoint, and a probe " +
                "must not enqueue a billable request)"));

    /// <summary>Inline delivery is not this backend's mode — the queue is asynchronous by design.</summary>
    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported,
            "fal's queue is asynchronous: use submit → poll → fetch (IMediaJobProvider), or a durable " +
            "generation job"));

    /// <inheritdoc/>
    /// <remarks>Bounded by the request's <see cref="MediaRequest.TimeoutSeconds"/> if it carries one, else
    /// <see cref="FalOptions.Timeout"/> — the ENQUEUEING call only, not the render it starts.</remarks>
    public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => SubmitCoreAsync(request, token),
            // a submit that timed out may still have been ACCEPTED, and a queued render is billable: Inconclusive
            // stops the router buying the same generation from the next backend
            reason => QueuedOperation.Failure($"the submit {reason}; the request may still have been enqueued") with
            {
                Inconclusive = true,
            });

    private async Task<QueuedOperation> SubmitCoreAsync(MediaRequest request, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return QueuedOperation.Failure(missing, ProviderVerdict.NotConfigured);
        if (Model(request) is not { Length: > 0 } model)
            return QueuedOperation.Failure(
                "no model: name one on the request, the candidate (\"fal:model-id\") or FalOptions.Model");

        if (InputRefusal(request) is { } refusal)
            return QueuedOperation.Failure(refusal, ProviderVerdict.Unsupported);

        var url = Url(model, request.Option("webhook") is { Length: > 0 } webhook ? webhook : null);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        var sent = false;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(BuildInput(request), Encoding.UTF8, "application/json"),
            };
            Authorize(message);
            sent = true;   // from here the queue may hold a render, whatever reaches this process
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return QueuedOperation.Failure($"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            return Field(body, "request_id") is { } requestId
                ? new QueuedOperation($"{model}{ModelSeparator}{requestId}", QueuedOperationStatus.Queued)
                : QueuedOperation.Failure("no request_id in a 2xx answer, so the request may still have been " +
                                          $"enqueued: {HttpArtifacts.FailureDetail(body, 200)}") with { Inconclusive = true };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return QueuedOperation.FromThrownSubmit(ex, sent);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="FalOptions.Timeout"/>. A status call that times out, or never reaches the
    /// queue, reports the render still RUNNING — no answer is not a failed render. Only a <c>404</c> and an
    /// unconfigured backend are terminal: a 429, 401, 403, 408 or 5xx says nothing about a render already paid
    /// for.</remarks>
    public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        QueueCalls.PollGuard(options.Timeout, operationId, ct, token => PollCoreAsync(operationId, token));

    private async Task<QueuedOperation> PollCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return new QueuedOperation(operationId, QueuedOperationStatus.Failed,
                Detail: $"malformed operation id '{operationId}' — expected \"model{ModelSeparator}requestId\"");

        var (body, failure, transport, _) = await GetAsync(
            Url($"{model}/{options.RequestsSegment}/{requestId}/{options.StatusSegment}"), ct).ConfigureAwait(false);
        if (failure is not null) return QueueCalls.PollFailure(operationId, failure, transport);

        var status = Field(body!, "status");
        if (status is not null && options.StatusVocabulary.TryGetValue(status, out var mapped))
        {
            // fal reports a FAILED request as COMPLETED plus an error field
            if (mapped == QueuedOperationStatus.Succeeded && ReportedError(body!) is { } error)
                return new QueuedOperation(operationId, QueuedOperationStatus.Failed, Detail: error);
            return new QueuedOperation(operationId, mapped,
                Progress: mapped == QueuedOperationStatus.Succeeded ? 1 : null,
                Detail: mapped == QueuedOperationStatus.Queued ? QueueDetail(body!) : null);
        }

        // an unknown status is NOT a failure: treating "something new" as terminal would abandon a render
        // that is merely in a state this build hasn't heard of
        return new QueuedOperation(operationId, QueuedOperationStatus.Running,
            Detail: $"unrecognised status '{status}'");
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="FalOptions.Timeout"/>; a fired deadline is a
    /// <see cref="ProviderVerdict.Timeout"/> result, and the operation can simply be fetched again.</remarks>
    public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default) =>
        QueueCalls.FetchGuard(options.Timeout, ct, token => FetchCoreAsync(operationId, token));

    private async Task<MediaResponse> FetchCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return MediaResponse.Failure(ProviderVerdict.Failed, $"malformed operation id '{operationId}'");

        // a fetch that cannot complete is a result the caller acts on now, so it is classified rather than
        // read as transport the way a poll — a question that can be asked again — is
        var (body, failure, _, status) = await GetAsync(
            Url($"{model}/{options.RequestsSegment}/{requestId}"), ct).ConfigureAwait(false);
        if (failure is not null) return QueueCalls.FetchFailure(status, failure, hasCredentials: true);

        var artifacts = ReadArtifacts(body!);
        return artifacts.Count > 0
            ? MediaResponse.Success(artifacts, new MediaUsage(Count: artifacts.Count, CostUsd: Cost(body!)))
            : MediaResponse.Failure(ProviderVerdict.Failed,
                $"no artifacts in the result: {HttpArtifacts.FailureDetail(body!, 200)}");
    }

    /// <inheritdoc/>
    /// <remarks>fal documents its cancel as a REQUEST — <c>202 {"status":"CANCELLATION_REQUESTED"}</c> — so a
    /// 202 reports the render still <see cref="QueuedOperationStatus.Running"/>: it may yet finish and be billed,
    /// and only a poll says how it ended. Any other 2xx is <see cref="QueuedOperationStatus.Cancelled"/>. Bounded
    /// by <see cref="FalOptions.Timeout"/>; a cancel that timed out may or may not have landed, so it too reports
    /// the render still running.</remarks>
    public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        QueueCalls.CancelGuard(options.Timeout, operationId, ct, token => CancelCoreAsync(operationId, token));

    private async Task<QueuedOperation> CancelCoreAsync(string operationId, CancellationToken ct)
    {
        var (model, requestId) = Split(operationId);
        if (requestId is null)
            return new QueuedOperation(operationId, QueuedOperationStatus.Failed, Detail: "malformed operation id");

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Put,
                Url($"{model}/{options.RequestsSegment}/{requestId}/{options.CancelSegment}"));
            Authorize(message);
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Accepted)
                return QueueCalls.CancelAnswered(operationId, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: $"cancellation requested ({Field(body, "status") ?? "202"}): the render may still " +
                        "finish and be billed — poll to see how it ended");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return QueueCalls.CancelUnanswered(operationId, ex);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    /// <summary><c>{Root}/{path}</c> with <see cref="FalOptions.QueryParameters"/> and, on a submit, the
    /// webhook.</summary>
    private string Url(string path, string? webhook = null)
    {
        var query = options.QueryParameters
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
            .ToList();
        if (webhook is not null) query.Add($"{options.WebhookQueryParameter}={Uri.EscapeDataString(webhook)}");
        return query.Count == 0 ? $"{Root}/{path}" : $"{Root}/{path}?{string.Join('&', query)}";
    }

    /// <summary>The failure a status document reports under <see cref="FalOptions.ErrorField"/>, with its
    /// <see cref="FalOptions.ErrorTypeField"/>, or null when it reports none.</summary>
    private string? ReportedError(string body)
    {
        if (options.ErrorField is not { Length: > 0 } field || !HttpArtifacts.TryParseObject(body, out var doc))
            return null;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(field, out var error)) return null;
            var said = error.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => error.GetString() is { Length: > 0 } text ? text : null,
                _ => error.GetRawText(),
            };
            if (said is null) return null;
            var type = options.ErrorTypeField is { Length: > 0 } typeField
                ? HttpArtifacts.Scalar(doc.RootElement, typeField)
                : null;
            return $"the render failed{(type is null ? "" : $" ({type})")}: {HttpArtifacts.FailureDetail(said)}";
        }
    }

    private string? Unconfigured() =>
        string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey)
            ? "not configured: BaseUrl and ApiKey are both required"
            : null;

    // trimmed ONCE, here: the submit URL and the operation id every later call builds from must agree
    private string? Model(MediaRequest request) =>
        (request.Model is { Length: > 0 } model ? model : options.Model)?.Trim('/');

    /// <summary>Why fal cannot take this request's inputs, or null. fal maps ONE input, given as a URL it
    /// fetches itself, onto one field — so a second input, a bytes-only one, or a role it has no field for is
    /// refused before anything is sent: dropping it bills a render the caller did not ask for.</summary>
    private static string? InputRefusal(MediaRequest request)
    {
        if (request.Inputs.Count == 0) return null;
        if (request.Inputs.Count > 1)
            return $"fal maps one input onto one field and this request carries {request.Inputs.Count}; send " +
                "one, and pass any further model field through MediaRequest.Options";
        var input = request.Inputs[0];
        if (input.Uri is not { Length: > 0 })
            return "fal takes input media as a URL; supply MediaInput.Uri rather than Data — the platform " +
                "will not upload your bytes on your behalf";
        return InputField(input.Role) is null
            ? $"fal has no field for an input in the role '{input.Role}' — it takes an init, first-frame or " +
              "reference image"
            : null;
    }

    /// <summary>The request field an input in <paramref name="role"/> is sent as, or null for a role fal
    /// has no field for.</summary>
    private static string? InputField(string? role) =>
        string.IsNullOrEmpty(role) || Is(role, MediaInputRoles.Init) || Is(role, MediaInputRoles.Reference)
            ? "input_image_url"
            : Is(role, MediaInputRoles.FirstFrame) ? "image_url" : null;

    private static bool Is(string role, string expected) =>
        string.Equals(role, expected, StringComparison.OrdinalIgnoreCase);

    private void Authorize(HttpRequestMessage message)
    {
        if (options.ApiKey is { Length: > 0 } key)
            message.Headers.Authorization = new AuthenticationHeaderValue(options.AuthScheme, key);
    }

    /// <summary>Split <c>"model#requestId"</c>. The model may itself contain slashes (<c>fal-ai/wan-t2v</c>),
    /// which is why the separator is a character the ids don't use rather than the last path segment.</summary>
    private static (string Model, string? RequestId) Split(string operationId)
    {
        var at = operationId.LastIndexOf(ModelSeparator);
        return at <= 0 || at == operationId.Length - 1
            ? (operationId, null)
            : (operationId[..at], operationId[(at + 1)..]);
    }

    /// <summary>GET <paramref name="url"/>. The bar for TRANSPORT is deliberately higher than ComfyUI's: every
    /// status but a <c>404</c> keeps polling, because fal is a hosted, paid, rate-limiting API where a render
    /// abandoned is money gone and one polled a few times too many is not.</summary>
    private async Task<(string? Body, string? Failure, bool Transport, HttpStatusCode? Status)> GetAsync(
        string url, CancellationToken ct)
    {
        if (Unconfigured() is { } missing) return (null, missing, false, null);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(message);
        return await QueueCalls.SendAsync(lease.Client, message,
            isTransport: status => status != HttpStatusCode.NotFound,
            unreachable: ex => ex.Message, ct).ConfigureAwait(false);
    }

    /// <summary>The model's input object. Common fields are mapped; everything else in
    /// <see cref="MediaRequest.Options"/> is passed through verbatim, because each model on an aggregator
    /// takes its own parameters and typing them would need a release per model.</summary>
    internal static string BuildInput(MediaRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (request.Prompt is { Length: > 0 } prompt) writer.WriteString("prompt", prompt);

            // any other input shape was refused before this is built (InputRefusal)
            if (request.Inputs is [{ Uri: { Length: > 0 } uri } input] && InputField(input.Role) is { } field)
                writer.WriteString(field, uri);

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
    internal static IReadOnlyList<MediaArtifact> ReadArtifacts(string body)
    {
        if (!HttpArtifacts.TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            var artifacts = new List<MediaArtifact>();
            Walk(doc.RootElement, artifacts, depth: 0);
            return artifacts;
        }
    }

    private static void Walk(JsonElement element, List<MediaArtifact> artifacts, int depth)
    {
        if (depth > 4 || artifacts.Count >= 32) return;   // a result document, not an arbitrary graph

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (HttpArtifacts.Str(element, "url") is { } url)
                {
                    var contentType = HttpArtifacts.Str(element, "content_type") ?? MediaTypeOf(url);
                    artifacts.Add(new MediaArtifact(contentType, Uri: url));
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
        if (!HttpArtifacts.TryParseObject(body, out var doc)) return null;
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
        HttpArtifacts.MediaTypeForExtension(Path.GetExtension(url.Split('?', '#')[0]));

    /// <summary>A top-level string-or-number field of a wire body (<see cref="HttpArtifacts.Scalar"/>), or null.</summary>
    private static string? Field(string body, string name)
    {
        if (!HttpArtifacts.TryParseObject(body, out var doc)) return null;
        using (doc) return HttpArtifacts.Scalar(doc.RootElement, name);
    }
}
