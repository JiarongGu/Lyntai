using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Generation.Http;

/// <summary>Shared reading of what an HTTP generation backend sends back. Lives once because all three
/// backends in this package face the same two questions — "did it fail, and what does that mean?" and "where
/// are the bytes?" — and only the JSON path differs.</summary>
internal static class HttpArtifacts
{
    /// <summary>Base64 that may arrive as a bare payload or as a <c>data:image/png;base64,…</c> URL. Both
    /// occur in practice from the same backend family, so both are handled in one place.</summary>
    public static byte[]? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value;
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = raw.IndexOf(',');
            if (comma < 0) return null;
            raw = raw[(comma + 1)..];
        }
        try { return Convert.FromBase64String(raw); }
        catch (FormatException) { return null; }
    }

    /// <summary>Read the OpenAI-compatible images envelope: <c>{ data: [ { b64_json | url } ] }</c>.
    /// A URL is returned AS a URI artifact rather than downloaded — the platform never spends the caller's
    /// bandwidth (or guesses at auth for someone else's host) uninvited.</summary>
    public static IReadOnlyList<GenerationArtifact> FromOpenAiEnvelope(string body, string mediaType = "image/png")
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var artifacts = new List<GenerationArtifact>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (Str(item, "b64_json") is { } b64 && DecodeBase64(b64) is { } bytes)
                    artifacts.Add(new GenerationArtifact(mediaType, Data: bytes, Metadata: RevisedPrompt(item)));
                else if (Str(item, "url") is { } url)
                    artifacts.Add(new GenerationArtifact(mediaType, Uri: url, Metadata: RevisedPrompt(item)));
            }
            return artifacts;
        }
    }

    /// <summary>Read the Stable Diffusion WebUI envelope: <c>{ images: [ "&lt;base64&gt;" ] }</c>.</summary>
    public static IReadOnlyList<GenerationArtifact> FromWebUiEnvelope(string body, string mediaType = "image/png")
    {
        if (!JsonExtract.TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
                return [];

            var artifacts = new List<GenerationArtifact>();
            foreach (var item in images.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && DecodeBase64(item.GetString()) is { } bytes)
                    artifacts.Add(new GenerationArtifact(mediaType, Data: bytes));
            return artifacts;
        }
    }

    /// <summary>The backend's own words about a failure, trimmed to something loggable. Prefers a nested
    /// <c>error.message</c> (the shape every OpenAI-compatible service uses) and falls back to the raw body,
    /// because the raw body is what a diagnostics pane actually needs when the shape is unfamiliar.</summary>
    public static string FailureDetail(string body, int max = 500)
    {
        var message = body;
        if (JsonExtract.TryParseObject(body, out var doc))
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("error", out var error))
                    message = error.ValueKind == JsonValueKind.String
                        ? error.GetString() ?? body
                        : Str(error, "message") ?? body;
                else if (Str(doc.RootElement, "message") is { } flat)
                    message = flat;
            }

        message = message.Trim();
        return message.Length <= max ? message : message[..max];
    }

    private static IReadOnlyDictionary<string, string>? RevisedPrompt(JsonElement item) =>
        Str(item, "revised_prompt") is { } revised
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["revised_prompt"] = revised }
            : null;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
