using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Generation.Providers;

/// <summary>Configuration for <see cref="ComfyUiProvider"/>.
///
/// Every endpoint path is settable for one specific reason: **this backend's surface was not measured**
/// against a running server (none was available on the machine where it was written), so the defaults are
/// documented-surface rather than observed. If a path or field turns out to differ, a host retargets it here
/// instead of waiting for a Lyntai release.</summary>
public sealed class ComfyUiOptions
{
    /// <summary>Where ComfyUI is listening (<c>http://127.0.0.1:8188</c>). Blank = not configured.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; set; } = "comfyui";

    /// <summary>Media kinds this install can serve. Both by default: which one a run produces is decided by
    /// the WORKFLOW, not by the endpoint — so the host declares what its graphs cover.</summary>
    public IReadOnlyList<string> Kinds { get; set; } = [GenerationKinds.Image, GenerationKinds.Video];

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

    /// <summary>The option key holding the workflow graph JSON.</summary>
    public string WorkflowOption { get; set; } = "workflow";

    /// <summary>Response field carrying the accepted run's id, read from the submit reply.</summary>
    /// <remarks><b>The response FIELD names are settable for the same reason the endpoint paths are</b> — this
    /// backend's surface is documented, not measured — and they fail more quietly. A wrong path is a 404 on
    /// the first call; a wrong field name means a submitted render is never recognised as accepted, or a
    /// finished one is polled forever. The host who first runs this against a real ComfyUI is who finds out,
    /// and they must be able to correct it in `appsettings.json` rather than wait for a release.</remarks>
    public string PromptIdField { get; set; } = "prompt_id";

    /// <summary>History-entry field holding a run's produced files, keyed by node. Its PRESENCE is also the
    /// fallback completion signal when <see cref="StatusField"/> is absent or shaped unexpectedly.</summary>
    public string OutputsField { get; set; } = "outputs";

    /// <summary>History-entry field holding the run's status block.</summary>
    public string StatusField { get; set; } = "status";

    /// <summary>Boolean inside <see cref="StatusField"/> that says a run has finished.</summary>
    public string CompletedField { get; set; } = "completed";

    /// <summary>The option key holding a dotted path to the node input that receives
    /// <see cref="GenerationRequest.Prompt"/> (e.g. <c>"6.inputs.text"</c>).</summary>
    public string PromptPathOption { get; set; } = "prompt-path";

    /// <summary>Ceiling for ONE HTTP call to the server — a submit, a history read, an interrupt, a probe.
    ///
    /// <para><b>It does not bound the render.</b> This backend is submit → poll → fetch, and the run outlives
    /// any single call: <c>GenerationRenderJobHandler</c> polls it across job re-dispatches and process
    /// restarts, so poll and fetch arrive with no memory of when the submit happened and no request in hand. A
    /// whole-operation deadline could only live where the operation does — in the job's own retry budget. What
    /// this bounds is the thing that was genuinely unbounded: a server that accepts a connection and never
    /// answers.</para>
    ///
    /// <para>Shorter than the inline backends' default because these calls are queue operations rather than
    /// renders — none of them should take minutes. On <c>SubmitAsync</c> a request's
    /// <see cref="GenerationRequest.TimeoutSeconds"/> still overrides it (it is the most specific thing that
    /// caller can say about that call). <see cref="Timeout.InfiniteTimeSpan"/> removes THIS deadline, but a
    /// submit whose request carries its own <see cref="GenerationRequest.TimeoutSeconds"/> still has
    /// one.</para></summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// An <see cref="IGenerationProvider"/> + <see cref="IGenerationJobProvider"/> over a locally-run **ComfyUI**.
/// Three things make it unlike the other HTTP backends:
///
/// <list type="number">
/// <item><b>Graph-shaped.</b> ComfyUI runs a WORKFLOW, not a prompt, so the caller supplies the graph
///   (<c>Options["workflow"]</c>) and optionally where the prompt belongs in it
///   (<c>Options["prompt-path"]</c>). <see cref="GenerationRequest.Prompt"/> may be null, and no default
///   graph is invented — guessing one would silently produce something nobody asked for.</item>
/// <item><b>Asynchronous, locally.</b> <see cref="GenerationDelivery.Job"/> delivery on a machine you own,
///   composing with <c>Lyntai.Jobs</c> exactly like a hosted render.</item>
/// <item><b>No content policy in the path</b>, which makes it the candidate to place after a hosted backend
///   when a refusal should be picked up locally (<see cref="Routing.GenerationRoutingPolicy"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>UNVERIFIED SURFACE.</b> No ComfyUI instance was available to measure when this was written, so
/// endpoint paths and response field names are documented-surface, not observed. Every path is therefore an
/// option (<see cref="ComfyUiOptions"/>), and the parsing is defensive: an unrecognised history shape reports
/// "not finished" rather than inventing an artifact. Confirm against a live server before relying on it, and
/// prefer fixing an option over patching this class.</para>
/// <para>Produced files are returned as <b>view URIs</b>, not bytes — the same rule as a hosted backend's
/// signed URL. A local video is easily 100 MB, and downloading it uninvited would be the platform spending
/// the caller's memory.</para>
/// </remarks>
/// <param name="options">Endpoint paths, declared kinds and option keys.</param>
/// <param name="httpFactory">Supplies the <see cref="HttpClient"/> — BYO (design §7).</param>
/// <param name="disposeHttpClient">Whether this provider disposes what <paramref name="httpFactory"/> returns.
/// Default true, for the usual factory that MAKES a client per call. Pass false when the factory hands back a
/// client the HOST owns — disposing that leaves the second call throwing
/// <see cref="ObjectDisposedException"/>. <c>AddComfyUiProvider</c> sets this for you.</param>
public sealed class ComfyUiProvider(
    ComfyUiOptions options, Func<HttpClient> httpFactory, bool disposeHttpClient = true)
    : IGenerationProvider, IGenerationJobProvider
{
    /// <inheritdoc/>
    public string Id => options.Id;

    /// <inheritdoc/>
    public GenerationCapabilities Capabilities { get; } = new()
    {
        Kinds = options.Kinds,
        Deliveries = [GenerationDelivery.Job],
        SupportsInputs = true,   // a workflow can take an init image; the graph decides how
    };

    /// <summary>Reads server info — free, and it answers "is it up, and which build?" without generating.
    /// Bounded by <see cref="ComfyUiOptions.Timeout"/>.</summary>
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
            using var response = await http.GetAsync(Url(options.SystemStatsPath), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new GenerationProbeResult(false, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            return new GenerationProbeResult(true, "ComfyUI answered", Version: ComfyVersion(body));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationProbeResult(false, $"probe failed: {ex.Message}");
        }
    }

    /// <summary>Inline delivery is not this backend's mode — say so rather than hiding a poll loop inside one
    /// call (which would lose progress, cancellation and restart-survival).</summary>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported,
            "ComfyUI generates asynchronously: use submit → poll → fetch (IGenerationJobProvider)"));

    /// <inheritdoc/>
    /// <remarks>Bounded by the request's <see cref="GenerationRequest.TimeoutSeconds"/> if it carries one, else
    /// <see cref="ComfyUiOptions.Timeout"/> — the QUEUEING call only, not the run it starts.</remarks>
    public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(
            GenerationDeadline.Resolve(request.TimeoutSeconds, options.Timeout), ct,
            token => SubmitCoreAsync(request, token),
            // A submit that timed out may still have been queued — say so rather than implying nothing
            // happened, and mark it inconclusive so the router surfaces it instead of queueing the same
            // workflow at the next backend (a local GPU is not free either).
            reason => Failed($"the submit {reason}; the workflow may still have been accepted") with
            {
                Inconclusive = true,
            });

    private async Task<GenerationOperation> SubmitCoreAsync(GenerationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return Failed("no BaseUrl configured");

        if (request.Option(options.WorkflowOption) is not { Length: > 0 } workflowJson)
            return Failed($"ComfyUI needs a workflow graph in Options[\"{options.WorkflowOption}\"] — " +
                "there is no sensible default graph to invent on your behalf");

        JsonNode? graph;
        try { graph = JsonNode.Parse(workflowJson); }
        catch (JsonException ex) { return Failed($"the workflow in Options[\"{options.WorkflowOption}\"] is not valid JSON: {ex.Message}"); }
        if (graph is null) return Failed("the workflow parsed to nothing");

        if (request.Prompt is { Length: > 0 } prompt &&
            request.Option(options.PromptPathOption) is { Length: > 0 } path)
            Substitute(graph, path, prompt);

        // JsonObject over an anonymous type — keeps the package's trim/AOT claim honest
        var payload = new JsonObject { ["prompt"] = graph }.ToJsonString();

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Url(options.SubmitPath), content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed($"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            var id = Field(body, options.PromptIdField);
            return id is null
                ? Failed($"no {options.PromptIdField} in the response: {HttpArtifacts.FailureDetail(body, 200)}")
                : new GenerationOperation(id, GenerationOperationStatus.Queued);
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
    public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => PollCoreAsync(operationId, token),
            reason => new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: $"the status call {reason} — the run is still assumed to be going"));

    private async Task<GenerationOperation> PollCoreAsync(string operationId, CancellationToken ct)
    {
        var (body, failure, transport) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
        if (failure is not null)
            return new GenerationOperation(operationId,
                transport ? GenerationOperationStatus.Running : GenerationOperationStatus.Failed, Detail: failure);

        return Entry(body!, operationId) is { } entry && Completed(entry)
            ? new GenerationOperation(operationId, GenerationOperationStatus.Succeeded, Progress: 1)
            : new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: "not in history yet — still queued or running");
    }

    /// <inheritdoc/>
    /// <remarks>Bounded by <see cref="ComfyUiOptions.Timeout"/>; a fired deadline is a
    /// <see cref="GenerationVerdict.Timeout"/> result, and the operation can simply be fetched again.</remarks>
    public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => FetchCoreAsync(operationId, token),
            reason => GenerationResult.Failure(GenerationVerdict.Timeout, $"the result fetch {reason}"));

    private async Task<GenerationResult> FetchCoreAsync(string operationId, CancellationToken ct)
    {
        // a fetch is asked for a finished render's bytes, so an unanswered read is a failed fetch (the caller
        // simply fetches again) rather than the "still going" the poll reports
        var (body, failure, _) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
        if (failure is not null) return GenerationResult.Failure(GenerationVerdict.Failed, failure);

        if (Entry(body!, operationId) is not { } entry || !Completed(entry))
            return GenerationResult.Failure(GenerationVerdict.Failed,
                $"operation {operationId} is not finished — poll until Succeeded before fetching");

        var artifacts = OutputArtifacts(entry);
        return artifacts.Count > 0
            ? GenerationResult.Success(artifacts, new GenerationUsage(Count: artifacts.Count))
            : GenerationResult.Failure(GenerationVerdict.Failed,
                $"operation {operationId} completed with no recognised outputs");
    }

    /// <summary>Interrupts the RUNNING job. ComfyUI's interrupt is server-wide rather than per-id, so this
    /// stops whatever is executing — documented here because it matters if a host queues several. Bounded by
    /// <see cref="ComfyUiOptions.Timeout"/>; an interrupt that timed out may or may not have landed, so the run
    /// is reported as still going rather than assumed stopped.</summary>
    public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        GenerationDeadline.GuardAsync(options.Timeout, ct,
            token => CancelCoreAsync(operationId, token),
            reason => new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: $"the interrupt {reason}"));

    private async Task<GenerationOperation> CancelCoreAsync(string operationId, CancellationToken ct)
    {
        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Url(options.InterruptPath), content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new GenerationOperation(operationId, GenerationOperationStatus.Cancelled,
                    Detail: "interrupt sent (ComfyUI interrupts the RUNNING job, not this id specifically)")
                : new GenerationOperation(operationId, GenerationOperationStatus.Running,
                    Detail: $"interrupt rejected: {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GenerationOperation(operationId, GenerationOperationStatus.Running, Detail: ex.Message);
        }
    }

    private string Root => options.BaseUrl.TrimEnd('/');

    private string Url(string path) => $"{Root}/{path.TrimStart('/')}";

    private GenerationOperation Failed(string detail) =>
        new("", GenerationOperationStatus.Failed, Detail: detail);

    /// <summary>Reads the history document. <c>Transport</c> distinguishes "the server did not answer" — an
    /// unreachable host or a 5xx — from a failure that is about THIS operation: an unconfigured BaseUrl, or a
    /// 4xx saying the id (or the guessed <see cref="ComfyUiOptions.HistoryPath"/>) is wrong. Only the poll cares,
    /// and it is the difference between waiting and giving up.</summary>
    private async Task<(string? Body, string? Failure, bool Transport)> HistoryAsync(
        string operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) return (null, "no BaseUrl configured", false);

        using var lease = HttpClientLease.From(httpFactory, disposeHttpClient);
        var http = lease.Client;
        try
        {
            using var response = await http.GetAsync($"{Url(options.HistoryPath)}/{operationId}", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null, false)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}", (int)response.StatusCode >= 500);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return (null, $"ComfyUI at {Root} is not reachable: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
        }
    }

    /// <summary>Substitute the prompt at a dotted path (<c>"6.inputs.text"</c>). A path that doesn't exist is
    /// left alone rather than created: inventing nodes in someone's graph is worse than sending it unchanged
    /// with the placeholder they wrote.</summary>
    private static void Substitute(JsonNode graph, string dottedPath, string value)
    {
        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return;

        var node = graph;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            node = node is JsonObject obj && obj.TryGetPropertyValue(segments[i], out var next) ? next : null;
            if (node is null) return;
        }
        if (node is JsonObject target && target.ContainsKey(segments[^1])) target[segments[^1]] = value;
    }

    private static JsonElement? Entry(string body, string operationId)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(operationId, out var entry))
                return entry.Clone();
            // some builds key history by their own id; a single-entry object is unambiguous enough to use
            foreach (var property in doc.RootElement.EnumerateObject())
                return property.Value.Clone();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
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
    /// view URI. Collection names vary by node pack, so ANY array of objects carrying a
    /// <c>filename</c> counts — that tolerance is deliberate given the surface is unverified.</summary>
    private IReadOnlyList<GenerationArtifact> OutputArtifacts(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(options.OutputsField, out var outputs) || outputs.ValueKind != JsonValueKind.Object)
            return [];

        var artifacts = new List<GenerationArtifact>();
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
                    artifacts.Add(new GenerationArtifact(MediaTypeOf(filename), Uri: uri,
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
    private static string MediaTypeOf(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".flac" => "audio/flac",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        _ => "application/octet-stream",
    };

    private static string? Field(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return HttpArtifacts.Str(doc.RootElement, name);
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
