using System.Text.Json;
using Lyntai.Inference;

namespace Lyntai.Generation;

/// <summary>The domain's shared JSON readers and writers — used by the durable job payloads and by the agent
/// tools, which is why they live here rather than in any one of them. Hand-walked (<see cref="JsonDocument"/>,
/// <see cref="Utf8JsonWriter"/>) rather than reflection-serialized: Core claims trim/AOT compatibility, and a
/// reflection serializer would quietly make that claim false.</summary>
internal static class GenerationJson
{
    /// <summary>Read a string member, or null when it is absent, not a string, or empty. The object-kind test is
    /// part of the contract, not a formality: <c>TryGetProperty</c> THROWS on an element that isn't an object,
    /// so a copy of this reader without it is one careless caller away from an exception.</summary>
    public static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>Write <paramref name="candidates"/> as the <c>candidates</c> array of the object being written.</summary>
    public static void WriteCandidates(Utf8JsonWriter writer, IReadOnlyList<string> candidates)
    {
        writer.WriteStartArray("candidates");
        foreach (var candidate in candidates) writer.WriteStringValue(candidate);
        writer.WriteEndArray();
    }

    /// <summary>The non-empty strings of the <c>candidates</c> array, in order; empty when there is none.</summary>
    public static List<string> ReadCandidates(JsonElement element)
    {
        var candidates = new List<string>();
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("candidates", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var candidate in array.EnumerateArray())
                if (candidate.ValueKind == JsonValueKind.String && candidate.GetString() is { Length: > 0 } spec)
                    candidates.Add(spec);
        return candidates;
    }

    /// <summary>Write <paramref name="request"/>'s fields as members of the object being written.</summary>
    public static void WriteRequest(Utf8JsonWriter writer, MediaRequest request)
    {
        writer.WriteString("kind", request.Kind);
        // the consumer tag rides along so a job RESUMED in another process still bills to whoever asked for
        // it — a durable job outlives the request that created it
        if (request.Consumer != "default") writer.WriteString("consumer", request.Consumer);
        if (request.Prompt is { } prompt) writer.WriteString("prompt", prompt);
        if (request.Model is { } model) writer.WriteString("model", model);
        if (request.TimeoutSeconds is { } timeout) writer.WriteNumber("timeoutSeconds", timeout);

        if (request.Options.Count > 0)
        {
            writer.WriteStartObject("options");
            foreach (var (key, value) in request.Options) writer.WriteString(key, value);
            writer.WriteEndObject();
        }

        if (request.Inputs.Count > 0)
        {
            writer.WriteStartArray("inputs");
            foreach (var input in request.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("mediaType", input.MediaType);
                // base64 — a first-frame image or voice sample must survive the queue, and the job store holds text
                if (input.Data is { Length: > 0 } data) writer.WriteString("data", Convert.ToBase64String(data));
                if (input.Uri is { } uri) writer.WriteString("uri", uri);
                if (input.Role is { } role) writer.WriteString("role", role);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>Read a request <see cref="WriteRequest"/> wrote, or null when the element is not an object or
    /// names no <c>kind</c>.</summary>
    public static MediaRequest? ReadRequest(JsonElement element)
    {
        if (Str(element, "kind") is not { } kind) return null;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("options", out var optionObject) && optionObject.ValueKind == JsonValueKind.Object)
            foreach (var option in optionObject.EnumerateObject())
                if (option.Value.ValueKind == JsonValueKind.String)
                    options[option.Name] = option.Value.GetString() ?? "";

        var inputs = new List<MediaInput>();
        if (element.TryGetProperty("inputs", out var inputArray) && inputArray.ValueKind == JsonValueKind.Array)
            foreach (var input in inputArray.EnumerateArray())
            {
                if (Str(input, "mediaType") is not { } mediaType) continue;
                inputs.Add(new MediaInput(mediaType, Bytes(input), Str(input, "uri"), Str(input, "role")));
            }

        return new MediaRequest
        {
            Kind = kind,
            Consumer = Str(element, "consumer") ?? "default",
            Prompt = Str(element, "prompt"),
            Model = Str(element, "model"),
            Options = options,
            Inputs = inputs,
            TimeoutSeconds = element.TryGetProperty("timeoutSeconds", out var t) &&
                t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var seconds) ? seconds : null,
        };
    }

    /// <summary>The base64 <c>data</c> member decoded, or null when it is absent or not base64.</summary>
    public static byte[]? Bytes(JsonElement element)
    {
        if (Str(element, "data") is not { } base64) return null;
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
    }
}
