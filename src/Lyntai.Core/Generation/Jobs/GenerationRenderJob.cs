using System.Text.Json;

namespace Lyntai.Generation.Jobs;

/// <summary>The payload of a durable generation job: which candidates to route across, and the request to
/// generate. Serialized to the job's <c>Payload</c> string.</summary>
/// <remarks>JSON is built and parsed BY HAND (<see cref="Utf8JsonWriter"/> / <see cref="JsonDocument"/>) rather
/// than reflection-serialized, matching <c>MemoryPruneJobHandler</c>: Core claims trim/AOT compatibility, and a
/// reflection serializer here would quietly make that claim false.</remarks>
/// <param name="Candidates">Candidate specs in routing order — <c>"provider"</c> or <c>"provider:model"</c>,
/// the same shape <c>UseDefaultGenerationCandidates</c> accepts.</param>
/// <param name="Request">What to generate.</param>
public sealed record GenerationRenderJob(IReadOnlyList<string> Candidates, GenerationRequest Request)
{
    /// <summary>Serialize for <c>JobSpec.Payload</c>.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("candidates");
            foreach (var candidate in Candidates) writer.WriteStringValue(candidate);
            writer.WriteEndArray();

            writer.WriteString("kind", Request.Kind);
            // the consumer tag rides along so a render RESUMED in another process still bills to whoever
            // asked for it — a durable job outlives the request that created it
            if (Request.Consumer != "default") writer.WriteString("consumer", Request.Consumer);
            if (Request.Prompt is { } prompt) writer.WriteString("prompt", prompt);
            if (Request.Model is { } model) writer.WriteString("model", model);
            if (Request.TimeoutSeconds is { } timeout) writer.WriteNumber("timeoutSeconds", timeout);

            if (Request.Options.Count > 0)
            {
                writer.WriteStartObject("options");
                foreach (var (key, value) in Request.Options) writer.WriteString(key, value);
                writer.WriteEndObject();
            }

            if (Request.Inputs.Count > 0)
            {
                writer.WriteStartArray("inputs");
                foreach (var input in Request.Inputs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("mediaType", input.MediaType);
                    // base64 — a first-frame image or voice sample must survive the queue, and the job store
                    // holds text
                    if (input.Data is { Length: > 0 } data) writer.WriteString("data", Convert.ToBase64String(data));
                    if (input.Uri is { } uri) writer.WriteString("uri", uri);
                    if (input.Role is { } role) writer.WriteString("role", role);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Read a payload back, or null when it isn't one. Null rather than throwing: a handler turns an
    /// unreadable payload into a FAILED job with a reason, which is more useful than an exception in a queue.</summary>
    public static GenerationRenderJob? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (GenerationJson.Str(root, "kind") is not { } kind) return null;

            var candidates = new List<string>();
            if (root.TryGetProperty("candidates", out var candidateArray) &&
                candidateArray.ValueKind == JsonValueKind.Array)
                foreach (var candidate in candidateArray.EnumerateArray())
                    if (candidate.ValueKind == JsonValueKind.String && candidate.GetString() is { Length: > 0 } spec)
                        candidates.Add(spec);

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("options", out var optionObject) && optionObject.ValueKind == JsonValueKind.Object)
                foreach (var option in optionObject.EnumerateObject())
                    if (option.Value.ValueKind == JsonValueKind.String)
                        options[option.Name] = option.Value.GetString() ?? "";

            var inputs = new List<GenerationInput>();
            if (root.TryGetProperty("inputs", out var inputArray) && inputArray.ValueKind == JsonValueKind.Array)
                foreach (var input in inputArray.EnumerateArray())
                {
                    if (input.ValueKind != JsonValueKind.Object) continue;
                    if (GenerationJson.Str(input, "mediaType") is not { } mediaType) continue;
                    byte[]? data = null;
                    if (GenerationJson.Str(input, "data") is { } base64)
                        try { data = Convert.FromBase64String(base64); } catch (FormatException) { }
                    inputs.Add(new GenerationInput(mediaType, data,
                        GenerationJson.Str(input, "uri"), GenerationJson.Str(input, "role")));
                }

            return new GenerationRenderJob(candidates, new GenerationRequest
            {
                Kind = kind,
                Consumer = GenerationJson.Str(root, "consumer") ?? "default",
                Prompt = GenerationJson.Str(root, "prompt"),
                Model = GenerationJson.Str(root, "model"),
                Options = options,
                Inputs = inputs,
                TimeoutSeconds = root.TryGetProperty("timeoutSeconds", out var t) &&
                    t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var seconds) ? seconds : null,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
