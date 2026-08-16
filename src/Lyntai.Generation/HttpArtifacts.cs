using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Generation.Providers;

/// <summary>Shared reading of what an HTTP generation backend sends back. Lives once because the HTTP backends
/// in this package face the same two questions — "did it fail, and what does that mean?" and "where are the
/// bytes?" — and only the JSON path differs. (The local engine is the one backend that is not HTTP.)</summary>
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

    /// <summary>A scalar identifier field as text, accepting a JSON <b>string OR number</b>.
    /// <para>Separate from <see cref="Str"/> deliberately, rather than widening it: <c>Str</c> reads
    /// <c>url</c>, <c>b64_json</c> and error messages, where a number is meaningless and answering null is
    /// the honest result. An ID is the one field a backend may legitimately send either way.</para>
    /// <para>Shared because the two spellings had already DIVERGED — fal accepted a numeric id and the
    /// ComfyUI reader did not, so a build returning <c>{"prompt_id": 12345}</c> had an accepted workflow
    /// reported as rejected, which is the exact failure the reader's own doc says it guards against.</para></summary>
    /// <param name="element">The element to read from; anything but an object answers null.</param>
    /// <param name="name">The property name.</param>
    public static string? Scalar(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : null,
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    /// <summary>MIME type for a produced file's extension — the only signal these backends give about what a
    /// job actually made, since the same request can yield an image or a video depending on the model.
    /// <para>ONE table, because two had already drifted: fal's copy was missing <c>.gif</c> and
    /// <c>.flac</c>, so the same extension became <c>audio/flac</c> from one backend and
    /// <c>application/octet-stream</c> from the other — and that media type is what a consumer's
    /// <c>IGenerationArtifactSink</c> switches on and what <c>GenerationArtifact.ToInput</c> carries into
    /// the next stage of a chain.</para>
    /// <para>Extension EXTRACTION stays per-backend: ComfyUI reports a filename, fal a URL that may carry a
    /// query string. Those are genuinely different inputs; the mapping is not.</para></summary>
    /// <param name="extension">The file extension, with or without its leading dot; case-insensitive.</param>
    public static string MediaTypeForExtension(string? extension) =>
        (extension ?? string.Empty).ToLowerInvariant().TrimStart('.') switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "mp3" => "audio/mpeg",
            _ => "application/octet-stream",
        };

    /// <summary>A non-empty string property of a JSON object, or null. Shared with the queue backends, which
    /// read their own envelopes the same way — the object-kind guard is the part a copied reader loses, and
    /// <c>JsonElement.TryGetProperty</c> throws rather than answering false when the element is not an
    /// object.</summary>
    /// <param name="element">The element to read from; anything but an object answers null.</param>
    /// <param name="name">The property name.</param>
    public static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static IReadOnlyDictionary<string, string>? RevisedPrompt(JsonElement item) =>
        Str(item, "revised_prompt") is { } revised
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["revised_prompt"] = revised }
            : null;
}
