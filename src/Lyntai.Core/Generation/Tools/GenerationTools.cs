using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Generation.Jobs;
using Lyntai.Generation.Routing;

namespace Lyntai.Generation.Tools;

/// <summary>
/// The generation domain exposed as <see cref="ITool"/>s, so an agent can create media without either domain
/// referencing the other's concrete types. This is the whole coupling story: the LLM side already knows
/// <see cref="ITool"/>, so these work in the in-process tool loop AND — via
/// <c>Lyntai.Tools.Mcp.Hosting</c> — for a CLI agent that runs its own loop over MCP
/// (<c>docs/DECISIONS.md</c> D24).
/// </summary>
/// <remarks>Five tools rather than one do-everything call, because an agent needs to DISCOVER what is possible
/// before choosing: what backends exist and what they serve (<c>generate_backends</c>), the inline path
/// (<c>generate</c>), and the asynchronous path a video render requires (<c>generate_submit</c> →
/// <c>generate_status</c> → <c>generate_fetch</c>). Each is its own class registered into the tool collection —
/// adding one is a registration, never an edit to a switch.</remarks>
internal static class GenerationToolJson
{
    /// <summary>Parse a tool's arguments object; an empty/invalid payload yields an empty object rather than
    /// throwing, so a model that sends nothing gets a validation message instead of a stack trace.</summary>
    public static JsonDocument Parse(string? argumentsJson)
    {
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try { return JsonDocument.Parse(argumentsJson); }
            catch (JsonException) { /* fall through to an empty object */ }
        }
        return JsonDocument.Parse("{}");
    }

    /// <summary>A machine-readable error observation. Returned rather than thrown: the model should be able to
    /// read what it got wrong and retry, which a stack trace doesn't help with.</summary>
    public static string Error(string message) => Write(writer =>
    {
        writer.WriteBoolean("ok", false);
        writer.WriteString("error", message);
    });

    /// <summary>Build a JSON observation.</summary>
    public static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Describe artifacts for a model: what they are and where, never the bytes. A base64 image in a
    /// tool observation would blow the context window for no benefit — the app's sink is where bytes go.</summary>
    public static void WriteArtifacts(Utf8JsonWriter writer, IReadOnlyList<GenerationArtifact> artifacts, bool delivered)
    {
        writer.WriteBoolean("delivered", delivered);
        writer.WriteStartArray("artifacts");
        foreach (var artifact in artifacts)
        {
            writer.WriteStartObject();
            writer.WriteString("mediaType", artifact.MediaType);
            if (artifact.Uri is { } uri) writer.WriteString("uri", uri);
            if (artifact.Data is { } data) writer.WriteNumber("bytes", data.Length);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>Turn the request fields a model may supply into a <see cref="GenerationRequest"/>. Unknown
    /// members become pass-through <see cref="GenerationRequest.Options"/>, so a model can use a backend's own
    /// knobs (duration, aspect, voice) without Lyntai enumerating them.</summary>
    public static GenerationRequest ReadRequest(
        JsonElement root, string consumer, out IReadOnlyList<string> candidates)
    {
        var candidateList = new List<string>();
        if (root.TryGetProperty("backends", out var backends) && backends.ValueKind == JsonValueKind.Array)
            foreach (var backend in backends.EnumerateArray())
                if (backend.ValueKind == JsonValueKind.String && backend.GetString() is { Length: > 0 } id)
                    candidateList.Add(id);
        candidates = candidateList;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            // "consumer" is reserved and IGNORED, not read: the billing/cap tag is the host's to set, and a
            // model that could name it could route around a per-consumer cap
            { "kind", "prompt", "model", "backends", "imageUrl", "consumer" };
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var property in root.EnumerateObject())
                if (!reserved.Contains(property.Name) && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    options[property.Name] = property.Value.ToString();

        var inputs = new List<GenerationInput>();
        if (GenerationJson.Str(root, "imageUrl") is { } imageUrl)
            inputs.Add(new GenerationInput("image/*", Uri: imageUrl, Role: GenerationInputRoles.Init));

        return new GenerationRequest
        {
            Kind = GenerationJson.Str(root, "kind") ?? GenerationKinds.Image,
            Consumer = consumer,
            Prompt = GenerationJson.Str(root, "prompt"),
            Model = GenerationJson.Str(root, "model"),
            Options = options,
            Inputs = inputs,
        };
    }

    /// <summary>Candidates the model named, or the host's configured default order. A model naming a backend it
    /// invented gets an "unsupported" answer from routing rather than a crash.</summary>
    public static IReadOnlyList<GenerationCandidate> Candidates(
        IReadOnlyList<string> named, IReadOnlyList<GenerationCandidate> defaults)
    {
        if (named.Count == 0) return defaults;
        return [.. named.Select(GenerationCandidateSpec.Parse)];
    }

    /// <summary>The arguments the two operation-handle tools take — the backend id and operation id
    /// <c>generate_submit</c> returned. ONE literal for both: a model that saw the same pair described two ways
    /// would have reason to think they were different pairs.</summary>
    public const string OperationSchema = """
        {"type":"object","properties":{
          "backend":{"type":"string","description":"The backend id generate_submit returned."},
          "operationId":{"type":"string","description":"The operation id generate_submit returned."}},
         "required":["backend","operationId"]}
        """;

    /// <summary>Read that pair and resolve the backend holding the operation. False = <paramref name="error"/>
    /// carries the observation to return, so <c>generate_status</c> and <c>generate_fetch</c> answer a missing
    /// argument or an unknown backend in exactly the same words.</summary>
    public static bool TryReadOperation(
        JsonDocument args,
        IEnumerable<IGenerationProvider> providers,
        [NotNullWhen(true)] out IGenerationJobProvider? backend,
        out string backendId,
        out string operationId,
        [NotNullWhen(false)] out string? error)
    {
        backend = null;
        backendId = "";
        operationId = "";
        error = null;

        if (GenerationJson.Str(args.RootElement, "backend") is not { } id ||
            GenerationJson.Str(args.RootElement, "operationId") is not { } operation)
        {
            error = Error("both 'backend' and 'operationId' are required");
            return false;
        }

        if (GenerationToolRegistry.JobBackend(providers, id) is not { } resolved)
        {
            error = Error($"'{id}' is not a registered asynchronous backend");
            return false;
        }

        backend = resolved;
        backendId = id;
        operationId = operation;
        return true;
    }
}

/// <summary>Lists the generation backends and what each can actually do — the tool an agent calls FIRST, so it
/// picks a backend that exists and supports the medium instead of guessing a name.</summary>
public sealed class GenerationBackendsTool(IEnumerable<IGenerationProvider> providers) : ITool
{
    /// <inheritdoc/>
    public string Name => "generate_backends";

    /// <inheritdoc/>
    public string? Description =>
        "List the available generation backends and what each supports: media kinds (image/video/audio/3d), " +
        "delivery (inline = one call; job = submit then poll), whether it takes an input image, and whether it " +
        "is usable right now. Call this before generating so you name a backend that exists and can do the job.";

    /// <inheritdoc/>
    public string? ParametersJsonSchema => """{"type":"object","properties":{},"additionalProperties":false}""";

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
    {
        var list = providers.ToList();
        var probes = new List<(IGenerationProvider Provider, GenerationProbeResult Probe)>();
        foreach (var provider in list)
            probes.Add((provider, await provider.ProbeAsync(ct).ConfigureAwait(false)));

        return GenerationToolJson.Write(writer =>
        {
            writer.WriteBoolean("ok", true);
            writer.WriteStartArray("backends");
            foreach (var (provider, probe) in probes)
            {
                writer.WriteStartObject();
                writer.WriteString("id", provider.Id);
                writer.WriteBoolean("usable", probe.Available);
                if (probe.Detail is { } detail) writer.WriteString("detail", detail);

                writer.WriteStartArray("kinds");
                foreach (var kind in provider.Capabilities.Kinds) writer.WriteStringValue(kind);
                writer.WriteEndArray();

                writer.WriteStartArray("delivery");
                foreach (var delivery in provider.Capabilities.Deliveries)
                    writer.WriteStringValue(delivery.ToString().ToLowerInvariant());
                writer.WriteEndArray();

                writer.WriteBoolean("takesInputImage", provider.Capabilities.SupportsInputs);
                if (provider.Capabilities.Models.Count > 0)
                {
                    writer.WriteStartArray("models");
                    foreach (var model in provider.Capabilities.Models) writer.WriteStringValue(model);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });
    }
}

/// <summary>Generates INLINE — one call, artifacts back. For a medium whose backends are asynchronous (video,
/// batch music) an agent uses <see cref="GenerationSubmitTool"/> instead; this reports that rather than
/// blocking.</summary>
public sealed class GenerationInlineTool(
    IGenerationRouter router,
    GenerationOptions options,
    IGenerationArtifactSink? sink = null,
    string consumer = "agent") : ITool
{
    /// <summary>The spend/rate-limit tag renders from this tool bill to — <c>"agent"</c> by default, NOT the
    /// platform's <c>"default"</c>. A tool loop is the runaway-spend case (a model retrying a render in a
    /// loop), so it is capped separately out of the box: set <c>Budget.PerConsumer["agent"]</c> and it binds
    /// agent-driven renders without touching what a user pressing a button may spend.</summary>
    public string Consumer { get; } = consumer;

    /// <inheritdoc/>
    public string Name => "generate";

    /// <inheritdoc/>
    public string? Description =>
        "Generate media in one call and return where it landed. Best for images. Use generate_backends first to " +
        "see what is available; if the backend for your medium is asynchronous (video usually is), use " +
        "generate_submit instead. Bytes are handed to the host application, not returned here.";

    /// <inheritdoc/>
    public string? ParametersJsonSchema => """
        {"type":"object","properties":{
          "kind":{"type":"string","description":"image | video | audio | 3d (default: image)"},
          "prompt":{"type":"string","description":"What to generate."},
          "model":{"type":"string","description":"Optional model/endpoint id at the chosen backend."},
          "backends":{"type":"array","items":{"type":"string"},"description":"Backend ids in preference order; omit to use the host's default order."},
          "imageUrl":{"type":"string","description":"Optional source image URL for an edit/img2img."},
          "size":{"type":"string","description":"Optional pixel size, e.g. 1024x1024."}},
         "required":["prompt"]}
        """;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var args = GenerationToolJson.Parse(argumentsJson);
        var request = GenerationToolJson.ReadRequest(args.RootElement, Consumer, out var named);
        if (request.Prompt is null && request.Inputs.Count == 0)
            return GenerationToolJson.Error("a prompt (or an imageUrl to edit) is required");

        var result = await router.GenerateAsync(
            GenerationToolJson.Candidates(named, options.DefaultCandidates), request, ct).ConfigureAwait(false);

        if (!result.IsOk)
            return GenerationToolJson.Error($"{result.Verdict}: {result.Detail}");

        var delivered = false;
        if (sink is not null)
        {
            await sink.ReceiveAsync(new GenerationArtifactDelivery(
                Guid.NewGuid(), "inline", "", result.Artifacts, result.Usage), ct).ConfigureAwait(false);
            delivered = true;
        }

        return GenerationToolJson.Write(writer =>
        {
            writer.WriteBoolean("ok", true);
            GenerationToolJson.WriteArtifacts(writer, result.Artifacts, delivered);
        });
    }
}

/// <summary>Submits an ASYNCHRONOUS generation and returns the handle to poll. The shape a video render
/// actually has — an agent that tried to wait inline would block for minutes.</summary>
public sealed class GenerationSubmitTool(
    IGenerationRouter router, GenerationOptions options, string consumer = "agent") : ITool
{
    /// <summary>The spend/rate-limit tag submissions from this tool bill to — see
    /// <see cref="GenerationInlineTool.Consumer"/>.</summary>
    public string Consumer { get; } = consumer;

    /// <inheritdoc/>
    public string Name => "generate_submit";

    /// <inheritdoc/>
    public string? Description =>
        "Start a long-running generation (video, music) and get back a backend id + operation id. Poll it with " +
        "generate_status, then collect it with generate_fetch. Renders commonly take minutes, so do other work " +
        "in between rather than polling in a tight loop.";

    /// <inheritdoc/>
    public string? ParametersJsonSchema => """
        {"type":"object","properties":{
          "kind":{"type":"string","description":"video | audio | image | 3d (default: image)"},
          "prompt":{"type":"string","description":"What to generate."},
          "model":{"type":"string","description":"Optional model/endpoint id at the chosen backend."},
          "backends":{"type":"array","items":{"type":"string"},"description":"Backend ids in preference order."},
          "imageUrl":{"type":"string","description":"Optional first-frame / reference image URL."},
          "duration":{"type":"string","description":"Optional clip length in seconds, if the backend takes one."}},
         "required":["prompt"]}
        """;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var args = GenerationToolJson.Parse(argumentsJson);
        var request = GenerationToolJson.ReadRequest(args.RootElement, Consumer, out var named);
        if (request.Prompt is null && request.Inputs.Count == 0)
            return GenerationToolJson.Error("a prompt (or an imageUrl) is required");

        var submission = await router.SubmitAsync(
            GenerationToolJson.Candidates(named, options.DefaultCandidates), request, ct).ConfigureAwait(false);

        if (submission.Operation.Status == GenerationOperationStatus.Failed)
            // An INCONCLUSIVE submission is the one failure a model must not react to in its usual way. Its
            // default move after a tool error is to call the tool again — and here that re-submits work a
            // backend may already be running and billing for, walking straight around the router's refusal to
            // try a second candidate. So this observation INSTRUCTS rather than informs: it names the backend
            // (the id the router deliberately kept), forbids the retry, and points at the tool that can settle
            // the question instead. The machine-readable flag rides alongside so a host can branch on it
            // without parsing prose.
            return submission.Operation.Inconclusive
                ? GenerationToolJson.Write(writer =>
                {
                    writer.WriteBoolean("ok", false);
                    writer.WriteBoolean("inconclusive", true);
                    writer.WriteString("backend", submission.ProviderId);
                    writer.WriteString("error",
                        $"The submission to '{submission.ProviderId}' got no answer, so that backend MAY " +
                        $"ALREADY be running this generation and billing for it. {submission.Operation.Detail} " +
                        "DO NOT retry this submission: calling generate_submit again for this request buys the " +
                        "same generation twice. Tell the user it may be running at " +
                        $"'{submission.ProviderId}' so they can check, and use generate_status instead if you " +
                        "already hold an operationId for it.");
                })
                : GenerationToolJson.Error(submission.Operation.Detail ?? "no backend accepted the request");

        return GenerationToolJson.Write(writer =>
        {
            writer.WriteBoolean("ok", true);
            writer.WriteString("backend", submission.ProviderId);
            writer.WriteString("operationId", submission.Operation.Id);
            writer.WriteString("status", submission.Operation.Status.ToString().ToLowerInvariant());
            writer.WriteString("next", "poll generate_status with this backend + operationId");
        });
    }
}

/// <summary>Reports where a submitted generation is.</summary>
public sealed class GenerationStatusTool(IEnumerable<IGenerationProvider> providers) : ITool
{
    /// <inheritdoc/>
    public string Name => "generate_status";

    /// <inheritdoc/>
    public string? Description =>
        "Check a submitted generation: returns queued | running | succeeded | failed | cancelled, with progress " +
        "when the backend reports it. When it says succeeded, call generate_fetch.";

    /// <inheritdoc/>
    public string? ParametersJsonSchema => GenerationToolJson.OperationSchema;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var args = GenerationToolJson.Parse(argumentsJson);
        if (!GenerationToolJson.TryReadOperation(
                args, providers, out var backend, out _, out var operationId, out var error))
            return error;

        var operation = await backend.PollAsync(operationId, ct).ConfigureAwait(false);
        return GenerationToolJson.Write(writer =>
        {
            writer.WriteBoolean("ok", true);
            writer.WriteString("status", operation.Status.ToString().ToLowerInvariant());
            if (operation.Progress is { } progress) writer.WriteNumber("progress", progress);
            if (operation.Detail is { } detail) writer.WriteString("detail", detail);
            if (operation.Status == GenerationOperationStatus.Succeeded)
                writer.WriteString("next", "call generate_fetch to collect it");
        });
    }
}

/// <summary>Collects a finished generation's artifacts.</summary>
public sealed class GenerationFetchTool(
    IEnumerable<IGenerationProvider> providers,
    IGenerationArtifactSink? sink = null) : ITool
{
    /// <inheritdoc/>
    public string Name => "generate_fetch";

    /// <inheritdoc/>
    public string? Description =>
        "Collect a finished generation (after generate_status reports succeeded) and return where it landed. " +
        "Bytes go to the host application, not into this observation.";

    /// <inheritdoc/>
    public string? ParametersJsonSchema => GenerationToolJson.OperationSchema;

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var args = GenerationToolJson.Parse(argumentsJson);
        if (!GenerationToolJson.TryReadOperation(
                args, providers, out var backend, out var backendId, out var operationId, out var error))
            return error;

        var result = await backend.FetchAsync(operationId, ct).ConfigureAwait(false);
        if (!result.IsOk) return GenerationToolJson.Error($"{result.Verdict}: {result.Detail}");

        var delivered = false;
        if (sink is not null)
        {
            await sink.ReceiveAsync(new GenerationArtifactDelivery(
                Guid.NewGuid(), backendId, operationId, result.Artifacts, result.Usage), ct).ConfigureAwait(false);
            delivered = true;
        }

        return GenerationToolJson.Write(writer =>
        {
            writer.WriteBoolean("ok", true);
            GenerationToolJson.WriteArtifacts(writer, result.Artifacts, delivered);
        });
    }
}

/// <summary>Shared backend lookup for the status/fetch tools.</summary>
internal static class GenerationToolRegistry
{
    /// <summary>The registered backend with this id, IF it is asynchronous. Null covers both "no such backend"
    /// and "that one is inline-only" — a model gets one clear message either way.</summary>
    public static IGenerationJobProvider? JobBackend(IEnumerable<IGenerationProvider> providers, string id) =>
        providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            as IGenerationJobProvider;
}
