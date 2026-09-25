using Lyntai.Inference;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="ComfyUiProvider"/>.
///
/// Every endpoint path is settable because the server is a third-party install whose surface can move
/// between releases; the defaults are MEASURED (ComfyUI 0.36.0 answered every one as shipped), and a host
/// whose build differs retargets a path or field here instead of waiting for a Lyntai release.</summary>
public sealed class ComfyUiOptions
{
    /// <summary>Where ComfyUI is listening (<c>http://127.0.0.1:8188</c>). Blank = not configured.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "comfyui";

    /// <summary>Media kinds this install can serve. All three by default: which one a run produces is decided
    /// by the WORKFLOW, not by the endpoint — so the host declares what its graphs cover.</summary>
    public IReadOnlyList<string> Produces { get; set; } =
        [ProviderKinds.Image, ProviderKinds.Video, ProviderKinds.Model3d];

    /// <summary>Queue a workflow (returns a prompt id).</summary>
    public string SubmitPath { get; set; } = "prompt";

    /// <summary>Completed-run history, keyed by prompt id.</summary>
    public string HistoryPath { get; set; } = "history";

    /// <summary>Serves a produced file by filename/subfolder/type.</summary>
    public string ViewPath { get; set; } = "view";

    /// <summary>Interrupts the running job.</summary>
    public string InterruptPath { get; set; } = "interrupt";

    /// <summary>Server/version info — the free probe.</summary>
    public string SystemStatsPath { get; set; } = "system_stats";

    /// <summary>Stores an input file in the server's input folder and answers the name it was stored under
    /// (<c>name</c>, <c>subfolder</c>). Every <see cref="MediaRequest.Inputs"/> entry is sent here before the
    /// workflow is queued.</summary>
    public string UploadPath { get; set; } = "upload/image";

    /// <summary>The input subfolder a mesh (a <c>model/*</c> media type) is uploaded into — where ComfyUI's 3D
    /// loaders look. Any other input goes to the input folder's root.</summary>
    public string MeshSubfolder { get; set; } = "3d";

    /// <summary>The most bytes one URI input may be fetched as — 128 MiB by default, above any image or mesh
    /// and a short clip. A declared length over it is refused before reading, and a body is cut off once it
    /// passes it; either way that input is not uploaded and the workflow is not queued (an earlier input of
    /// the same request may already be). Inline bytes are not counted: they are already in the caller's
    /// memory.</summary>
    public long MaxFetchBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>Ceiling for fetching a URI input from ANOTHER origin, per request including each redirect hop —
    /// two minutes by default. It holds whatever the submit's own deadline is, so a stalled third-party server
    /// cannot hold a submit open when <see cref="Timeout"/> is infinite. Past it the input is not uploaded and
    /// the workflow is not queued. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> removes it.</summary>
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The option key holding the workflow graph JSON.</summary>
    public string WorkflowOption { get; set; } = "workflow";

    /// <summary>Response field carrying the accepted run's id, read from the submit reply.</summary>
    /// <remarks><b>The response FIELD names are settable for the same reason the endpoint paths are</b> —
    /// upstream can rename them — and they fail more quietly. A wrong path is a 404 on
    /// the first call; a wrong field name means a submitted render is never recognised as accepted, or a
    /// finished one is polled forever. All four shipped names are measured (ComfyUI 0.36.0), so a host only
    /// touches these when a build diverges — in `appsettings.json`, not by waiting for a release.</remarks>
    public string PromptIdField { get; set; } = "prompt_id";

    /// <summary>History-entry field holding a run's produced files, keyed by node. Its PRESENCE is also the
    /// fallback completion signal when <see cref="StatusField"/> is absent or shaped unexpectedly.</summary>
    public string OutputsField { get; set; } = "outputs";

    /// <summary>History-entry field holding the run's status block.</summary>
    public string StatusField { get; set; } = "status";

    /// <summary>Boolean inside <see cref="StatusField"/> that says a run has finished.</summary>
    public string CompletedField { get; set; } = "completed";

    /// <summary>Text inside <see cref="StatusField"/> naming how a run ended (<c>"success"</c>, <c>"error"</c>).
    /// Read only to recognise <see cref="FailedStatusText"/>.</summary>
    public string StatusTextField { get; set; } = "status_str";

    /// <summary>The <see cref="StatusTextField"/> value of a run that failed while executing. Such a run polls
    /// as FAILED; without this it read as still running, because its completed flag stays false.</summary>
    public string FailedStatusText { get; set; } = "error";

    /// <summary>Array inside <see cref="StatusField"/> holding the run's events, each <c>[name, data]</c>.</summary>
    public string MessagesField { get; set; } = "messages";

    /// <summary>The event in <see cref="MessagesField"/> that says which node failed and why. The failure's
    /// detail is its node and <c>exception_message</c> only — never its traceback or inputs, which carry the
    /// server's file paths.</summary>
    public string ExecutionErrorEvent { get; set; } = "execution_error";

    /// <summary>The option key holding a dotted path to the node input that receives
    /// <see cref="MediaRequest.Prompt"/> (e.g. <c>"6.inputs.text"</c>).</summary>
    public string PromptPathOption { get; set; } = "prompt-path";

    /// <summary>The option key holding a dotted path to the node input that receives an uploaded input's
    /// stored name (e.g. <c>"1.inputs.model_file"</c>). An input with no role binds through this key; an input
    /// with a role binds through <c>"&lt;key&gt;:&lt;role&gt;"</c> (<c>"input-path:init"</c>) and never falls
    /// back to the roleless one. An input whose key is unset, or whose path is not a field of the workflow, is
    /// REFUSED before anything is uploaded — never dropped.
    /// <para>An input given as a URI is FETCHED from wherever it points — any absolute http(s) URI, bounded
    /// by <see cref="MaxFetchBytes"/> — so a host that lets a model supply inputs (an agent tool's
    /// <c>imageUrl</c>) validates those URIs before they reach this provider.</para></summary>
    public string InputPathOption { get; set; } = "input-path";

    /// <summary>Ceiling for ONE call of this provider — a probe, a history read, an interrupt, or a submit,
    /// which covers fetching and uploading every input as well as the queue call itself.
    ///
    /// <para><b>It does not bound the render.</b> This backend is submit → poll → fetch, and the run outlives
    /// any single call: <c>GenerationRenderJobHandler</c> polls it across job re-dispatches and process
    /// restarts, so poll and fetch arrive with no memory of when the submit happened and no request in hand. A
    /// whole-operation deadline could only live where the operation does — in the job's own retry budget. What
    /// this bounds is the thing that was genuinely unbounded: a server that accepts a connection and never
    /// answers.</para>
    ///
    /// <para>Shorter than the inline backends' default because these calls are queue operations rather than
    /// renders — none of them should take minutes, though a submit carrying large inputs may need more. On
    /// <c>SubmitAsync</c> a request's
    /// <see cref="MediaRequest.TimeoutSeconds"/> still overrides it (it is the most specific thing that
    /// caller can say about that call). <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline, but a
    /// submit whose request carries its own <see cref="MediaRequest.TimeoutSeconds"/> still has
    /// one.</para></summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// An <see cref="IModelProvider"/> + <see cref="IMediaJobProvider"/> over a locally-run **ComfyUI**.
/// Three things make it unlike the other HTTP backends:
///
/// <list type="number">
/// <item><b>Graph-shaped.</b> ComfyUI runs a WORKFLOW, not a prompt, so the caller supplies the graph
///   (<c>Options["workflow"]</c>) and optionally where the prompt belongs in it
///   (<c>Options["prompt-path"]</c>). <see cref="MediaRequest.Prompt"/> may be null, and no default
///   graph is invented — guessing one would silently produce something nobody asked for. Each
///   <see cref="MediaRequest.Inputs"/> entry is uploaded and its stored name written where the caller says
///   (<c>Options["input-path"]</c>, or <c>Options["input-path:&lt;role&gt;"]</c>): the loader node is
///   the caller's, so an input with nowhere to go is refused.</item>
/// <item><b>Asynchronous, locally.</b> <see cref="ProviderOperation.Queued"/> delivery on a machine you own,
///   composing with <c>Lyntai.Jobs</c> exactly like a hosted render.</item>
/// <item><b>No content policy in the path</b>, which makes it the candidate to place after a hosted backend
///   when a refusal should be picked up locally (<see cref="MediaRoutingPolicy"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>MEASURED against a live server</b> (ComfyUI 0.36.0) over image, video and mesh workflows:
/// probe, submit, poll, fetch, interrupt and upload all answered on the documented paths, every response
/// field name was confirmed as shipped, and the view URI served what its history entry named — a rendered
/// PNG, an MP4 a video-producing workflow filed under the collection called <c>images</c>, and a GLB filed
/// under <c>3d</c> that chained, uploaded again, into a graph that rendered it
/// (<c>ComfyUiLiveTests</c> is the measurement). Every path stays an option
/// (<see cref="ComfyUiOptions"/>) because upstream can rename between releases, and the parsing stays
/// defensive: an unrecognised history shape reports "not finished" rather than inventing an artifact.</para>
/// <para>Produced files are returned as <b>view URIs</b>, not bytes — the same rule as a hosted backend's
/// signed URL. A local video is easily 100 MB, and downloading it uninvited would be the platform spending
/// the caller's memory.</para>
/// <para><b>An input given as a URI is the one thing this backend downloads</b>, because a loader node reads
/// the server's input folder, not a URL. It is fetched from any http(s) server, capped by
/// <see cref="ComfyUiOptions.MaxFetchBytes"/>, with the ComfyUI client — and whatever the host configured on
/// it — used on ComfyUI's own origin only; every other origin gets a client carrying no credentials. A
/// redirect's target is fetched the same way as long as the ComfyUI client does not follow redirects itself:
/// <c>AddComfyUiProvider</c>'s does not, but a client the <paramref name="httpFactory"/> returns may. So a
/// host that lets a MODEL name inputs validates those URIs first.</para>
/// </remarks>
/// <param name="options">Endpoint paths, declared kinds and option keys.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7). It should NOT follow
/// redirects: it carries what you configured for ComfyUI, and a redirect it follows by itself takes that to
/// wherever the redirect points before this provider can see where it went (an answer from off ComfyUI's
/// origin is then refused, but the request has already been sent).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddComfyUiProvider</c> sets this for you.</param>
public sealed class ComfyUiProvider(
    ComfyUiOptions options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IModelProvider, IMediaJobProvider
{
    // no credentials, cookies or redirects: what a URI on any other origin is fetched through. Static, so a
    // fetch never costs a connection pool of its own.
    private static readonly HttpClient SharedForeignClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,   // the submit's own deadline bounds it
    };

    private readonly HttpClient _foreign = SharedForeignClient;

    /// <summary>The same provider with the credential-less client's handler replaced, for tests.</summary>
    internal ComfyUiProvider(ComfyUiOptions options, Func<HttpClient> httpFactory, HttpMessageHandler foreignHandler)
        : this(options, httpFactory) =>
        _foreign = new HttpClient(foreignHandler, disposeHandler: false);

    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    /// <remarks>Derived per access, so a host that changes <see cref="ComfyUiOptions.Produces"/> after
    /// registration is routed by the new value.</remarks>
    public ProviderCapabilities Capabilities => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = options.Produces,
        Operations = [ProviderOperation.Queued],
        // an admission promise the submit path keeps: each input is uploaded and bound at the graph field the
        // CALLER names (ComfyUiOptions.InputPathOption), and one with nowhere to go is refused, never dropped
        SupportsInputs = true,
    };

    /// <summary>Reads server info — free, and it answers "is it up, and which build?" without generating.
    /// Bounded by <see cref="ComfyUiOptions.Timeout"/>.</summary>
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
            using var response = await http.GetAsync(Url(options.SystemStatsPath), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ProviderProbeResult(false, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            return new ProviderProbeResult(true, "ComfyUI answered", Version: ComfyVersion(body));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProviderProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <summary>Inline delivery is not this backend's mode — say so rather than hiding a poll loop inside one
    /// call (which would lose progress, cancellation and restart-survival).</summary>
    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported,
            "ComfyUI generates asynchronously: use submit → poll → fetch (IMediaJobProvider)"));

    /// <inheritdoc/>
    /// <remarks>Bounded by the request's <see cref="MediaRequest.TimeoutSeconds"/> if it carries one, else
    /// <see cref="ComfyUiOptions.Timeout"/> — every input fetch and upload and the QUEUEING call together, not
    /// the run it starts. A failed fetch or upload fails the submit and queues nothing.</remarks>
    public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default)
    {
        var stage = new SubmitStage();
        return GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => SubmitCoreAsync(request, stage, token),
            // Only once the queue call is out may a submit that timed out have been accepted: then it is
            // inconclusive, so the router surfaces it instead of queueing the same workflow at the next backend
            // (a local GPU is not free either). Before that, nothing was queued — and a deadline spent waiting
            // on another server is the input's fault, not this host's.
            reason => stage.Queueing
                ? Failed($"the submit {reason}; the workflow may still have been accepted") with { Inconclusive = true }
                : stage.FetchingFrom is { } origin
                    ? Unsupported($"the submit {reason} while fetching an input from {origin}, so nothing was queued")
                    : Failed($"the submit {reason} before the workflow was sent, so nothing was queued"));
    }

    /// <summary>Where a submit had got to, read by its timeout handler.</summary>
    private sealed class SubmitStage
    {
        public bool Queueing;
        public string? FetchingFrom;
    }

    private async Task<QueuedOperation> SubmitCoreAsync(MediaRequest request, SubmitStage stage, CancellationToken ct)
    {
        // never set up is not a fault of this host, so routing advances without a strike (D31)
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return Failed("no BaseUrl configured") with { Verdict = ProviderVerdict.NotConfigured };

        if (request.Option(options.WorkflowOption) is not { Length: > 0 } workflowJson)
            return Unsupported($"ComfyUI needs a workflow graph in Options[\"{options.WorkflowOption}\"] — " +
                "there is no sensible default graph to invent on your behalf");

        JsonNode? graph;
        try { graph = JsonNode.Parse(workflowJson); }
        catch (JsonException ex) { return Unsupported($"the workflow in Options[\"{options.WorkflowOption}\"] is not valid JSON: {ex.Message}"); }
        if (graph is null) return Unsupported("the workflow parsed to nothing");

        // a prompt path that misses leaves the graph as the caller wrote it, placeholder and all
        GraphField? promptField = null;
        if (request.Prompt is { Length: > 0 } prompt &&
            request.Option(options.PromptPathOption) is { Length: > 0 } path &&
            Resolve(graph, path) is { } field)
        {
            field.Owner[field.Name] = prompt;
            promptField = field;
        }

        // every input is bound BEFORE anything is fetched or uploaded, so a refusal spends nothing
        var (bindings, refusal) = BindInputs(graph, request, promptField);
        if (refusal is not null) return Unsupported(refusal);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            foreach (var binding in bindings)
            {
                var (stored, failure, verdict) = await UploadAsync(http, binding.Input, stage, ct).ConfigureAwait(false);
                if (failure is not null)
                    return Failed($"{binding.Describe} was not uploaded, so the workflow was not submitted: {failure}")
                        with { Verdict = verdict };
                binding.Field.Owner[binding.Field.Name] = stored;
            }

            // JsonObject over an anonymous type — keeps the package's trim/AOT claim honest
            var payload = new JsonObject { ["prompt"] = graph }.ToJsonString();
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            stage.Queueing = true;   // from here, an answer that never comes may hide a queued run
            using var response = await http.PostAsync(Url(options.SubmitPath), content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed($"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}")
                    with { Verdict = RequestRefusal(response.StatusCode, body) };

            var id = Field(body, options.PromptIdField);
            return id is null
                ? Failed($"no {options.PromptIdField} in the response: {HttpArtifacts.FailureDetail(body, 200)}")
                : new QueuedOperation(id, QueuedOperationStatus.Queued);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return Failed($"ComfyUI at {Root} is not reachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }

    /// <summary>Reads the run's history entry. An EMPTY history means "not landed yet" — reporting that as a
    /// failure would fail every run that simply hasn't finished. A history read that TIMES OUT, or that cannot
    /// reach the server at all, is treated the same way, and for the same reason: no answer is not a failed
    /// render, and reading it as terminal would abandon a run that is merely still going. A 4xx or an
    /// unconfigured BaseUrl IS terminal — that id will never resolve, and polling it forever strands the
    /// job.</summary>
    public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => PollCoreAsync(operationId, token),
            reason => new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: $"the status call {reason} — the run is still assumed to be going"));

    private async Task<QueuedOperation> PollCoreAsync(string operationId, CancellationToken ct)
    {
        var (body, failure, transport, _) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
        if (failure is not null)
            return new QueuedOperation(operationId,
                transport ? QueuedOperationStatus.Running : QueuedOperationStatus.Failed, Detail: failure);

        var entry = Entry(body!, operationId);
        if (ExecutionFailure(entry) is { } failed)
            return new QueuedOperation(operationId, QueuedOperationStatus.Failed, Detail: failed);

        return Completed(entry)
            ? new QueuedOperation(operationId, QueuedOperationStatus.Succeeded, Progress: 1)
            : new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: "not in history yet — still queued or running");
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="ComfyUiOptions.Timeout"/>; a fired deadline is a
    /// <see cref="ProviderVerdict.Timeout"/> result, and the operation can simply be fetched again.</remarks>
    public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => FetchCoreAsync(operationId, token),
            reason => MediaResponse.Failure(ProviderVerdict.Timeout, $"the result fetch {reason}"));

    private async Task<MediaResponse> FetchCoreAsync(string operationId, CancellationToken ct)
    {
        // a fetch is asked for a finished render's bytes, so an unanswered read is a failed fetch (the caller
        // simply fetches again) rather than the "still going" the poll reports
        var (body, failure, _, status) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
        // CLASSIFY rather than flatten. This hardcoded Failed for every failed read, so ComfyUI behind an
        // authenticating proxy told a host "the render failed" — which is both wrong and unactionable, where
        // NotConfigured says to go and set the credential up. hasCredentials is FALSE because this backend
        // has no credential surface at all: a 401 here can only mean something in front of it wants one.
        if (failure is not null)
            return MediaResponse.Failure(
                status is { } code
                    ? ProviderVerdictClassifier.FromHttpFailure(code, failure, hasCredentials: false)
                    : ProviderVerdictClassifier.FromErrorText(failure),
                failure);

        var entry = Entry(body!, operationId);
        if (ExecutionFailure(entry) is { } failed)
            return MediaResponse.Failure(ProviderVerdict.Failed, failed);
        if (entry is null || !Completed(entry))
            return MediaResponse.Failure(ProviderVerdict.Failed,
                $"operation {operationId} is not finished — poll until Succeeded before fetching");

        var artifacts = OutputArtifacts(entry);
        return artifacts.Count > 0
            ? MediaResponse.Success(artifacts, new MediaUsage(Count: artifacts.Count))
            : MediaResponse.Failure(ProviderVerdict.Failed,
                $"operation {operationId} completed with no recognised outputs");
    }

    /// <summary>Interrupts the RUNNING job. ComfyUI's interrupt is server-wide rather than per-id, so this
    /// stops whatever is executing — documented here because it matters if a host queues several. Bounded by
    /// <see cref="ComfyUiOptions.Timeout"/>; an interrupt that timed out may or may not have landed, so the run
    /// is reported as still going rather than assumed stopped.</summary>
    public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => CancelCoreAsync(operationId, token),
            reason => new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: $"the interrupt {reason}"));

    private async Task<QueuedOperation> CancelCoreAsync(string operationId, CancellationToken ct)
    {
        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Url(options.InterruptPath), content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new QueuedOperation(operationId, QueuedOperationStatus.Cancelled,
                    Detail: "interrupt sent (ComfyUI interrupts the RUNNING job, not this id specifically)")
                : new QueuedOperation(operationId, QueuedOperationStatus.Running,
                    Detail: $"interrupt rejected: {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new QueuedOperation(operationId, QueuedOperationStatus.Running, Detail: ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    private string Url(string path) => $"{Root}/{path.TrimStart('/')}";

    private QueuedOperation Failed(string detail) =>
        new("", QueuedOperationStatus.Failed, Detail: detail);

    /// <summary>A refusal of the REQUEST as posed, not a fault of this host: the router advances past it without
    /// counting it against the backend, since another candidate may serve the same request.</summary>
    private QueuedOperation Unsupported(string detail) =>
        Failed(detail) with { Verdict = ProviderVerdict.Unsupported };

    /// <summary>The verdict of a refused call to this server. A 4xx other than access or a rate limit refuses
    /// THIS request — a graph failing validation (a missing model or node, a bad value), a stale view URI — so
    /// it is <see cref="ProviderVerdict.Unsupported"/>; access and rate are classified as a fetch's are; a 5xx
    /// is left to the router's reading of the detail, as a host fault.</summary>
    private static ProviderVerdict? RequestRefusal(HttpStatusCode status, string? body) => (int)status switch
    {
        401 or 403 or 429 => ProviderVerdictClassifier.FromHttpFailure(status, body, hasCredentials: false),
        >= 400 and < 500 => ProviderVerdict.Unsupported,
        _ => null,
    };

    /// <summary>Reads the history document. <c>Transport</c> distinguishes "the server did not answer" — an
    /// unreachable host or a 5xx — from a failure that is about THIS operation: an unconfigured BaseUrl, or a
    /// 4xx saying the id (or the guessed <see cref="ComfyUiOptions.HistoryPath"/>) is wrong. Only the poll cares,
    /// and it is the difference between waiting and giving up.
    /// <para><c>Status</c> carries the TYPED status back rather than only its rendering inside the failure
    /// text: <c>ProviderVerdictClassifier</c> documents that a typed status wins over body text, and a
    /// caller holding only the string cannot reach the better entry point.</para></summary>
    private async Task<(string? Body, string? Failure, bool Transport, HttpStatusCode? Status)> HistoryAsync(
        string operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) return (null, "no BaseUrl configured", false, null);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var response = await http.GetAsync($"{Url(options.HistoryPath)}/{operationId}", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null, false, response.StatusCode)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}",
                   (int)response.StatusCode >= 500, response.StatusCode);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return (null, $"ComfyUI at {Root} is not reachable: {ex.Message}", true, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false, null);
        }
    }

    /// <summary>The EXISTING field a dotted path (<c>"6.inputs.text"</c>) names, or null. Never created:
    /// inventing nodes in someone's graph is worse than any answer a miss leads to.</summary>
    private static GraphField? Resolve(JsonNode graph, string dottedPath)
    {
        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        var node = graph;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            node = node is JsonObject obj && obj.TryGetPropertyValue(segments[i], out var next) ? next : null;
            if (node is null) return null;
        }
        return node is JsonObject owner && owner.ContainsKey(segments[^1]) ? new GraphField(owner, segments[^1]) : null;
    }

    private sealed record GraphField(JsonObject Owner, string Name)
    {
        public bool Is(GraphField other) => ReferenceEquals(Owner, other.Owner) && Name == other.Name;
    }

    private sealed record InputBinding(MediaInput Input, GraphField Field, string Describe);

    /// <summary>Where each input goes: the path under <see cref="ComfyUiOptions.InputPathOption"/>, or under
    /// <c>"&lt;key&gt;:&lt;role&gt;"</c> for an input with a role. The first input with nowhere to go — no
    /// path, a path the graph lacks, a field the prompt or another input already took, nothing to send, or a
    /// URI that is not absolute http(s) — is the refusal.</summary>
    private (IReadOnlyList<InputBinding> Bindings, string? Refusal) BindInputs(
        JsonNode graph, MediaRequest request, GraphField? promptField)
    {
        var bindings = new List<InputBinding>();
        for (var i = 0; i < request.Inputs.Count; i++)
        {
            var input = request.Inputs[i];
            var key = input.Role is { Length: > 0 } role ? $"{options.InputPathOption}:{role}" : options.InputPathOption;
            var describe = $"input {i + 1} ({input.MediaType}{(input.Role is { Length: > 0 } r ? $", role {r}" : "")})";

            if (request.Option(key) is not { Length: > 0 } path)
                return ([], $"{describe} has nowhere to go: set Options[\"{key}\"] to the dotted path of the "
                    + "workflow field that loads it (e.g. \"1.inputs.model_file\")");
            if (Resolve(graph, path) is not { } field)
                return ([], $"{describe}: Options[\"{key}\"] is \"{path}\", which is not a field of the workflow");
            if (promptField is not null && field.Is(promptField))
                return ([], $"{describe}: \"{path}\" is where the prompt was written, which it would erase");
            if (bindings.Any(b => b.Field.Is(field)))
                return ([], $"{describe}: \"{path}\" is already bound to another input, which it would erase");
            if (input.Data is not { Length: > 0 })
            {
                if (string.IsNullOrWhiteSpace(input.Uri))
                    return ([], $"{describe} carries neither bytes nor a URI");
                if (!Uri.TryCreate(input.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    return ([], $"{describe}: fetching {input.Uri} is refused — only an absolute http or https URI is fetched");
            }

            bindings.Add(new InputBinding(input, field, describe));
        }
        return (bindings, null);
    }

    /// <summary>Store one input in the server's input folder and answer the name a loader reads it by —
    /// <c>"&lt;subfolder&gt;/&lt;name&gt;"</c> AS THE SERVER REPORTED IT, since the server may rename.
    /// A failure carries the verdict routing should act on, or null to leave it to the router.</summary>
    private async Task<(string? Stored, string? Failure, ProviderVerdict? Verdict)> UploadAsync(
        HttpClient http, MediaInput input, SubmitStage stage, CancellationToken ct)
    {
        var bytes = input.Data;
        if (bytes is not { Length: > 0 })
        {
            var (fetched, failure, verdict) = await DownloadAsync(http, new Uri(input.Uri!), stage, ct).ConfigureAwait(false);
            stage.FetchingFrom = null;   // left set only when a deadline escapes the fetch, for the handler to read
            if (fetched is null) return (null, $"fetching {input.Uri} failed: {failure}", verdict);
            bytes = fetched;
        }

        // a fresh name per upload: a shared one would let two concurrent jobs load each other's file
        var name = $"lyntai-{Guid.NewGuid():N}{HttpArtifacts.ExtensionForMediaType(input.MediaType)}";
        var file = new ByteArrayContent(bytes);
        if (MediaTypeHeaderValue.TryParse(input.MediaType, out var contentType))
            file.Headers.ContentType = contentType;

        using var form = new MultipartFormDataContent { { file, "image", name }, { new StringContent("input"), "type" } };
        if (input.MediaType.StartsWith("model/", StringComparison.OrdinalIgnoreCase) &&
            options.MeshSubfolder is { Length: > 0 } subfolder)
            form.Add(new StringContent(subfolder), "subfolder");

        using var response = await http.PostAsync(Url(options.UploadPath), form, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (null, $"the upload answered {(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}",
                RequestRefusal(response.StatusCode, body));

        return StoredName(body) is { } stored
            ? (stored, null, null)
            : (null, $"the upload answered no name: {HttpArtifacts.FailureDetail(body, 200)}", null);
    }

    private const int MaxRedirects = 5;

    /// <summary>GET a URI input. ComfyUI's client carries what the host configured FOR ComfyUI, so it is
    /// used on ComfyUI's own origin only; any other origin gets the credential-less client, bounded by
    /// <see cref="ComfyUiOptions.FetchTimeout"/>. Redirects are followed HERE, so that choice is made again at
    /// every hop. Anything another origin does is the input's fault, never this host's.</summary>
    private async Task<(byte[]? Bytes, string? Failure, ProviderVerdict? Verdict)> DownloadAsync(
        HttpClient comfy, Uri uri, SubmitStage stage, CancellationToken ct)
    {
        var at = uri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var own = OnComfyUi(at);
            ProviderVerdict? Elsewhere() => own ? null : ProviderVerdict.Unsupported;
            using var bound = own || options.FetchTimeout <= TimeSpan.Zero ? null : new CancellationTokenSource(options.FetchTimeout);
            using var linked = bound is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, bound.Token);
            stage.FetchingFrom = own ? null : at.GetLeftPart(UriPartial.Authority);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, at);
                using var response = await (own ? comfy : _foreign)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked?.Token ?? ct).ConfigureAwait(false);

                // a client the host supplies may follow a redirect by itself; an answer from elsewhere is not used
                if (response.RequestMessage?.RequestUri is { } answered && !SameOrigin(answered, at))
                    return (null, $"the client followed a redirect to {answered.GetLeftPart(UriPartial.Authority)}, "
                        + "whose answer is not used", null);

                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    at = location.IsAbsoluteUri ? location : new Uri(at, location);
                    if (at.Scheme is not ("http" or "https"))
                        return (null, $"it redirected to {at}, which is not http or https", ProviderVerdict.Unsupported);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    return (null, $"it answered {(int)response.StatusCode}",
                        own ? RequestRefusal(response.StatusCode, null) : ProviderVerdict.Unsupported);
                if (response.Content.Headers.ContentLength > options.MaxFetchBytes ||
                    await ReadCappedAsync(response.Content, linked?.Token ?? ct).ConfigureAwait(false) is not { } bytes)
                    return (null, $"it is over ComfyUiOptions.MaxFetchBytes ({options.MaxFetchBytes} bytes)",
                        ProviderVerdict.Unsupported);
                return (bytes, null, null);
            }
            catch (OperationCanceledException) when (bound is { IsCancellationRequested: true } && !ct.IsCancellationRequested)
            {
                return (null, $"it did not answer within ComfyUiOptions.FetchTimeout ({options.FetchTimeout})",
                    ProviderVerdict.Unsupported);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (null, ex.Message, Elsewhere());
            }
        }
        return (null, $"it redirected more than {MaxRedirects} times", ProviderVerdict.Unsupported);
    }

    /// <summary>The body, or null once it passes <see cref="ComfyUiOptions.MaxFetchBytes"/> — counted while
    /// reading, because a declared length can be absent or wrong.</summary>
    private async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var body = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var kept = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (kept.Length + read > options.MaxFetchBytes) return null;
            kept.Write(chunk, 0, read);
        }
        return kept.ToArray();
    }

    private bool OnComfyUi(Uri uri) => Uri.TryCreate(Root, UriKind.Absolute, out var root) && SameOrigin(uri, root);

    private static bool SameOrigin(Uri a, Uri b) =>
        a.IsAbsoluteUri && b.IsAbsoluteUri && a.Port == b.Port &&
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase);

    private static string? StoredName(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (HttpArtifacts.Str(doc.RootElement, "name") is not { } name) return null;
            return HttpArtifacts.Str(doc.RootElement, "subfolder") is { } subfolder ? $"{subfolder}/{name}" : name;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Entry(string body, string operationId)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(operationId, out var entry))
                return entry.Clone();

            // Some builds key history by their own id, so a SINGLE-entry object is unambiguous and is used.
            // The count check is the whole guard: `foreach { return }` took the first property of an object
            // of ANY size, so an un-keyed listing — which `HistoryPath` being a host option makes reachable —
            // silently returned ANOTHER RENDER's status and artifacts, and the job handler completed the job
            // with them. Wrong data, no failure anywhere.
            if (doc.RootElement.EnumerateObject().Count() != 1) return null;
            foreach (var property in doc.RootElement.EnumerateObject())
                return property.Value.Clone();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What a run that failed WHILE EXECUTING says, or null for any other run. Its completed flag stays
    /// false, so without this it polled as running until the caller gave up. The detail is the failing node
    /// and its exception message only: the event's traceback and inputs name paths on the SERVER.</summary>
    private string? ExecutionFailure(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(options.StatusField, out var status) || status.ValueKind != JsonValueKind.Object ||
            HttpArtifacts.Str(status, options.StatusTextField) is not { } text ||
            !string.Equals(text, options.FailedStatusText, StringComparison.OrdinalIgnoreCase))
            return null;

        if (status.TryGetProperty(options.MessagesField, out var messages) && messages.ValueKind == JsonValueKind.Array)
            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 2) continue;
                if (message[0].ValueKind != JsonValueKind.String ||
                    message[0].GetString() != options.ExecutionErrorEvent) continue;

                var error = message[1];
                var node = string.Join(" ", new[]
                {
                    HttpArtifacts.Scalar(error, "node_id") is { } id ? $"node {id}" : null,
                    HttpArtifacts.Str(error, "node_type") is { } type ? $"({type})" : null,
                }.OfType<string>());
                var said = HttpArtifacts.Str(error, "exception_message") is { } m
                    ? HttpArtifacts.FailureDetail(m) : "no message";
                return $"the run failed{(node.Length > 0 ? $" at {node}" : "")}: {said}";
            }

        return $"the run failed: its history says \"{text}\" but carries no {options.ExecutionErrorEvent} message";
    }

    /// <summary>Treats a run as done when it says so, and — defensively — when it has outputs but no status
    /// block, since an unrecognised status shape shouldn't strand a finished render.</summary>
    private bool Completed(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value) return false;
        if (value.TryGetProperty(options.StatusField, out var status) && status.ValueKind == JsonValueKind.Object &&
            status.TryGetProperty(options.CompletedField, out var completed) &&
            completed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return completed.GetBoolean();
        return value.TryGetProperty(options.OutputsField, out var outputs) && outputs.ValueKind == JsonValueKind.Object;
    }

    /// <summary>Walk <c>outputs.&lt;node&gt;.&lt;images|gifs|…&gt;[]</c> and turn each file reference into a
    /// view URI. Collection names vary by node pack, so ANY array of objects carrying a <c>filename</c>
    /// counts — a tolerance MEASURED to be load-bearing on 0.36.0: the core <c>SaveVideo</c> node files an
    /// MP4 under the collection named <c>images</c>, beside an <c>animated</c> array of bare booleans. A
    /// name-keyed walk would misread every video as absent, and one assuming file objects would choke on
    /// the flags — the <c>filename</c> test is the only reliable signal, on measurement and not merely on
    /// caution.</summary>
    private IReadOnlyList<MediaArtifact> OutputArtifacts(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(options.OutputsField, out var outputs) || outputs.ValueKind != JsonValueKind.Object)
            return [];

        var artifacts = new List<MediaArtifact>();
        foreach (var node in outputs.EnumerateObject())
        {
            if (node.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var collection in node.Value.EnumerateObject())
            {
                if (collection.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var file in collection.Value.EnumerateArray())
                {
                    if (file.ValueKind != JsonValueKind.Object) continue;
                    if (HttpArtifacts.Str(file, "filename") is not { } filename) continue;

                    var subfolder = HttpArtifacts.Str(file, "subfolder") ?? "";
                    var type = HttpArtifacts.Str(file, "type") ?? "output";
                    var uri = $"{Url(options.ViewPath)}?filename={Uri.EscapeDataString(filename)}" +
                        $"&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
                    artifacts.Add(new MediaArtifact(MediaTypeOf(filename), Uri: uri,
                        Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["node"] = node.Name,
                            ["filename"] = filename,
                        }));
                }
            }
        }
        return artifacts;
    }

    /// <summary>MIME from the produced file's extension — the only signal ComfyUI gives about what a workflow
    /// actually made (the same graph can emit a PNG or an MP4 depending on its nodes).</summary>
    private static string MediaTypeOf(string filename) =>
        HttpArtifacts.MediaTypeForExtension(Path.GetExtension(filename));

    private static string? Field(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return HttpArtifacts.Scalar(doc.RootElement, name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ComfyVersion(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("system", out var system))
                return HttpArtifacts.Str(system, "comfyui_version");
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
