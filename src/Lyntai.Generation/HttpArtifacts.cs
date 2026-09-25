using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Text;

namespace Lyntai.Generation.Providers;

/// <summary>Shared reading of what an HTTP generation backend sends back. Lives once because the HTTP backends
/// in this package face the same two questions — "did it fail, and what does that mean?" and "where are the
/// bytes?" — and only the JSON path differs. (The local engines, `sd-cli` and piper, are not HTTP.)</summary>
internal static class HttpArtifacts
{
    /// <summary>Parse a wire body as ONE JSON object, strictly: a vendor's reply is JSON or it is not, so none of
    /// the tolerances for reading a model's reply apply — least of all finding "the first balanced object" inside
    /// an HTML error page. False, with no document, for anything else. The caller disposes the document.</summary>
    public static bool TryParseObject(string? body, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object)
            {
                doc = parsed;
                return true;
            }
            parsed.Dispose();
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

    /// <summary>Read the OpenAI-shaped images envelope: <c>{ data: [ { b64_json | url } ] }</c>.
    /// A URL is returned AS a URI artifact rather than downloaded — the platform never spends the caller's
    /// bandwidth (or guesses at auth for someone else's host) uninvited.</summary>
    public static IReadOnlyList<MediaArtifact> FromOpenAiEnvelope(string body)
    {
        if (!TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var artifacts = new List<MediaArtifact>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (JsonExtract.StringProperty(item, "b64_json") is { } b64 && DecodeBase64(b64) is { } bytes)
                    artifacts.Add(new MediaArtifact("image/png", Data: bytes, Metadata: RevisedPrompt(item)));
                else if (JsonExtract.StringProperty(item, "url") is { } url)
                    artifacts.Add(new MediaArtifact("image/png", Uri: url, Metadata: RevisedPrompt(item)));
            }
            return artifacts;
        }
    }

    /// <summary>Read the Stable Diffusion WebUI envelope: <c>{ images: [ "&lt;base64&gt;" ] }</c>.</summary>
    public static IReadOnlyList<MediaArtifact> FromWebUiEnvelope(string body)
    {
        if (!TryParseObject(body, out var doc)) return [];
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
                return [];

            var artifacts = new List<MediaArtifact>();
            foreach (var item in images.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && DecodeBase64(item.GetString()) is { } bytes)
                    artifacts.Add(new MediaArtifact("image/png", Data: bytes));
            return artifacts;
        }
    }

    /// <summary>The backend's own words about a failure, trimmed to something loggable. Prefers a nested
    /// <c>error.message</c> (the shape every OpenAI-shaped service uses) and falls back to the raw body,
    /// because the raw body is what a diagnostics pane actually needs when the shape is unfamiliar.</summary>
    public static string FailureDetail(string body, int max = 500)
    {
        var message = body;
        if (TryParseObject(body, out var doc))
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("error", out var error))
                    message = error.ValueKind == JsonValueKind.String
                        ? error.GetString() ?? body
                        : JsonExtract.StringProperty(error, "message") ?? body;
                else if (JsonExtract.StringProperty(doc.RootElement, "message") is { } flat)
                    message = flat;
            }

        message = message.Trim();
        return message.Length <= max ? message : message[..max];
    }

    /// <summary>MIME type for a produced file's extension — the only signal these backends give about what a
    /// job actually made, since the same request can yield an image or a video depending on the model.
    /// <para>ONE table, because two had already drifted: fal's copy was missing <c>.gif</c> and
    /// <c>.flac</c>, so the same extension became <c>audio/flac</c> from one backend and
    /// <c>application/octet-stream</c> from the other — and that media type is what a consumer's
    /// <c>IGenerationArtifactSink</c> switches on and what <c>MediaArtifact.ToInput</c> carries into
    /// the next stage of a chain.</para>
    /// <para>Extension EXTRACTION stays per-backend: ComfyUI reports a filename, fal a URL that may carry a
    /// query string. Those are genuinely different inputs; the mapping is not.</para>
    /// <para>A mesh's type comes from here too: ComfyUI's <c>view</c> serves a GLB as
    /// <c>application/octet-stream</c>, so the header says nothing.</para></summary>
    /// <param name="extension">The file extension, with or without its leading dot; case-insensitive.</param>
    public static string MediaTypeForExtension(string? extension)
    {
        var wanted = (extension ?? string.Empty).ToLowerInvariant().TrimStart('.');
        foreach (var (ext, mediaType) in Extensions)
            if (ext == wanted) return mediaType;
        return "application/octet-stream";
    }

    /// <summary>The same table read the other way: the extension, with its dot, to name a stored file of
    /// this media type — or empty when the table does not know it. A loader may pick its parser by the
    /// extension, so an uploaded input needs the right one.</summary>
    /// <param name="mediaType">The MIME type; parameters after <c>;</c> are ignored, case-insensitive.</param>
    public static string ExtensionForMediaType(string? mediaType)
    {
        var wanted = (mediaType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        foreach (var (ext, type) in Extensions)
            if (type == wanted) return "." + ext;
        return "";
    }

    // the FIRST extension listed for a type is the one a stored file is named with
    private static readonly (string Extension, string MediaType)[] Extensions =
    [
        ("png", "image/png"),
        ("jpg", "image/jpeg"),
        ("jpeg", "image/jpeg"),
        ("webp", "image/webp"),
        ("gif", "image/gif"),
        ("mp4", "video/mp4"),
        ("webm", "video/webm"),
        ("flac", "audio/flac"),
        ("wav", "audio/wav"),
        ("mp3", "audio/mpeg"),
        ("glb", "model/gltf-binary"),
        ("gltf", "model/gltf+json"),
        ("obj", "model/obj"),
        ("stl", "model/stl"),
    ];

    private static IReadOnlyDictionary<string, string>? RevisedPrompt(JsonElement item) =>
        JsonExtract.StringProperty(item, "revised_prompt") is { } revised
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["revised_prompt"] = revised }
            : null;
}
