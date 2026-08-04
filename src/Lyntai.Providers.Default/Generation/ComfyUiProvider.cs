using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Generation.Http;

/// <summary>Configuration for <see cref="ComfyUiProvider"/>.
///
/// Every endpoint path is settable for one specific reason: **this backend's surface was not measured**
/// against a running server (none was available on the machine where it was written), so the defaults are
/// documented-surface rather than observed. If a path or field turns out to differ, a host retargets it here
/// instead of waiting for a Lyntai release.</summary>
public sealed record ComfyUiOptions
{
    /// <summary>Where ComfyUI is listening (<c>http://127.0.0.1:8188</c>). Blank = not configured.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>The candidate id this backend registers under.</summary>
    public string Id { get; init; } = "comfyui";

    /// <summary>Media kinds this install can serve. Both by default: which one a run produces is decided by
    /// the WORKFLOW, not by the endpoint — so the host declares what its graphs cover.</summary>
    public IReadOnlyList<string> Kinds { get; init; } = [GenerationKinds.Image, GenerationKinds.Video];

    /// <summary>Queue a workflow (returns a prompt id).</summary>
    public string SubmitPath { get; init; } = "prompt";

    /// <summary>Completed-run history, keyed by prompt id.</summary>
    public string HistoryPath { get; init; } = "history";

    /// <summary>Serves a produced file by filename/subfolder/type.</summary>
    public string ViewPath { get; init; } = "view";

    /// <summary>Interrupts the running job.</summary>
    public string InterruptPath { get; init; } = "interrupt";

    /// <summary>Server/version info — the free probe.</summary>
    public string SystemStatsPath { get; init; } = "system_stats";

    /// <summary>The option key holding the workflow graph JSON.</summary>
    public string WorkflowOption { get; init; } = "workflow";

    /// <summary>The option key holding a dotted path to the node input that receives
    /// <see cref="GenerationRequest.Prompt"/> (e.g. <c>"6.inputs.text"</c>).</summary>
    public string PromptPathOption { get; init; } = "prompt-path";
}

/// <summary>
/// An <see cref="IGenerationProvider"/> + <see cref="IGenerationJobProvider"/> over a locally-run **ComfyUI**.
/// Three things make it unlike the other HTTP backends, and all three were reasons to build it:
///
/// <list type="number">
/// <item><b>Graph-shaped.</b> ComfyUI runs a WORKFLOW, not a prompt — so the caller supplies the graph
///   (<c>Options["workflow"]</c>) and, optionally, where the prompt text belongs inside it
///   (<c>Options["prompt-path"]</c>, e.g. <c>"6.inputs.text"</c>). <see cref="GenerationRequest.Prompt"/> may
///   be null. No default graph is invented: guessing one would silently produce something nobody asked for.</item>
/// <item><b>Asynchronous, locally.</b> Submit returns a prompt id, the run lands in history later, and the
///   files are then served by the view endpoint — so this is <see cref="GenerationDelivery.Job"/> delivery on
///   a machine you own, and it composes with <c>Lyntai.Jobs</c> exactly like a hosted render.</item>
/// <item><b>No content policy in the path.</b> The server is the host's, which is what makes it the candidate
///   to place after a hosted backend when a refusal should be picked up locally
///   (<see cref="Routing.GenerationRoutingPolicy"/>).</item>
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
public sealed class ComfyUiProvider(ComfyUiOptions options, Func<HttpClient> httpFactory)
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

    /// <summary>Reads server info — free, and it answers "is it up, and which build?" without generating.</summary>
    public async Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new GenerationProbeResult(false, "not configured: no BaseUrl");

        using var http = httpFactory();
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
    public async Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default)
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

        var payload = JsonSerializer.Serialize(new { prompt = graph });

        using var http = httpFactory();
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(Url(options.SubmitPath), content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed($"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");

            var id = Field(body, "prompt_id");
            return id is null
                ? Failed($"no prompt_id in the response: {HttpArtifacts.FailureDetail(body, 200)}")
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
    /// failure would fail every run that simply hasn't finished.</summary>
    public async Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default)
    {
        var (body, failure) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
        if (failure is not null) return new GenerationOperation(operationId, GenerationOperationStatus.Failed, Detail: failure);

        return Entry(body!, operationId) is { } entry && Completed(entry)
            ? new GenerationOperation(operationId, GenerationOperationStatus.Succeeded, Progress: 1)
            : new GenerationOperation(operationId, GenerationOperationStatus.Running,
                Detail: "not in history yet — still queued or running");
    }

    /// <inheritdoc/>
    public async Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default)
    {
        var (body, failure) = await HistoryAsync(operationId, ct).ConfigureAwait(false);
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
    /// stops whatever is executing — documented here because it matters if a host queues several.</summary>
    public async Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default)
    {
        using var http = httpFactory();
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

    private async Task<(string? Body, string? Failure)> HistoryAsync(string operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) return (null, "no BaseUrl configured");

        using var http = httpFactory();
        try
        {
            using var response = await http.GetAsync($"{Url(options.HistoryPath)}/{operationId}", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return (null, $"ComfyUI at {Root} is not reachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
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
    private static bool Completed(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value) return false;
        if (value.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object &&
            status.TryGetProperty("completed", out var completed) &&
            completed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return completed.GetBoolean();
        return value.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Object;
    }

    /// <summary>Walk <c>outputs.&lt;node&gt;.&lt;images|gifs|…&gt;[]</c> and turn each file reference into a
    /// view URI. Collection names vary by node pack, so ANY array of objects carrying a
    /// <c>filename</c> counts — that tolerance is deliberate given the surface is unverified.</summary>
    private IReadOnlyList<GenerationArtifact> OutputArtifacts(JsonElement? entry)
    {
        if (entry is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
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
                    if (Str(file, "filename") is not { } filename) continue;

                    var subfolder = Str(file, "subfolder") ?? "";
                    var type = Str(file, "type") ?? "output";
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
            return Str(doc.RootElement, name);
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
                return Str(system, "comfyui_version");
            return null;
        }
        catch (JsonException)
        {
            return null;
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
