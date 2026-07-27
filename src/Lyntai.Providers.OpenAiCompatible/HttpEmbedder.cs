using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Embeddings;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>
/// HttpClient-based <see cref="IEmbedder"/> for OpenAI-compatible embedding endpoints. Posts
/// <c>{model, input[]}</c> — batched — and extracts vectors tolerantly from either the OpenAI/LM-Studio
/// <c>data[].embedding</c> shape or Ollama's <c>embeddings[[…]]</c> shape. Endpoint + flavor come from the
/// same <see cref="ProviderDetect"/> the chat provider uses (Ollama → native <c>/api/embed</c>; a bare
/// Azure resource → <c>/openai/v1/embeddings</c>; everything else → <c>/v1/embeddings</c>). The per-call
/// deadline is <see cref="LyntaiOptions.ProviderTimeout"/>. Failures THROW (an embedding call has no
/// verdict/fallback) — <see cref="Lyntai.Memory.ISemanticMemory.RecallAsync"/> is fail-open and swallows them,
/// while <c>RememberAsync</c> surfaces them by design.
/// </summary>
public sealed class HttpEmbedder(
    string id,
    OpenAiCompatibleEmbedderOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger<HttpEmbedder>? logger = null,
    bool disposeHttpClient = true) : IEmbedder
{
    private readonly ILogger _logger = logger ?? NullLogger<HttpEmbedder>.Instance;
    private readonly string _flavor = config.Flavor ?? ProviderDetect.Detect(config.BaseUrl);

    /// <summary>Get the per-call HttpClient. Lyntai-created clients are disposed after each call; an
    /// APP-supplied (BYO) client is NEVER disposed — the app owns its lifetime.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed

        var batchSize = config.BatchSize;
        if (batchSize <= 0 || texts.Count <= batchSize)
            return await EmbedBatchAsync(texts, http, ct).ConfigureAwait(false);

        // input list larger than the endpoint's per-request cap → split, concatenate in input order
        var result = new List<float[]>(texts.Count);
        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var slice = texts.Skip(i).Take(batchSize).ToList();
            result.AddRange(await EmbedBatchAsync(slice, http, ct).ConfigureAwait(false));
        }
        return result;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> batch, HttpClient http, CancellationToken ct)
    {
        var timeout = options.ProviderTimeout;
        string body;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            using var response = await http.SendAsync(BuildRequest(batch), timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                throw new HttpRequestException($"{id}: embeddings HTTP {(int)response.StatusCode} {Head(errorBody)}");
            }
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"{id}: no embeddings response within {timeout}");
        }

        var vectors = TryExtractVectors(body)
            ?? throw new InvalidOperationException($"{id}: malformed or empty embeddings response");
        if (vectors.Count != batch.Count)
            throw new InvalidOperationException($"{id}: expected {batch.Count} embeddings, got {vectors.Count}");
        _logger.LogDebug("{Id}: embedded {Count} texts ({Dim}-dim)", id, vectors.Count, vectors[0].Length);
        return vectors;
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<string> texts)
    {
        // {model, input[]} — accepted verbatim by both the OpenAI /v1/embeddings and the Ollama /api/embed
        // shapes, so one body serves every flavor; only the endpoint + response key differ.
        var payload = new JsonObject
        {
            ["model"] = config.Model ?? "",
            ["input"] = new JsonArray([.. texts.Select(t => (JsonNode)JsonValue.Create(t))]),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Authorization = new("Bearer", config.ApiKey);
            // Azure key auth conventionally travels in the api-key header (mirrors the chat provider)
            if (_flavor == ProviderDetect.AzureOpenAi)
                request.Headers.TryAddWithoutValidation("api-key", config.ApiKey);
        }
        return request;
    }

    internal Uri Endpoint()
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = _flavor switch
        {
            // Ollama's native batched embeddings endpoint (parallel to the chat provider's /api/chat)
            ProviderDetect.Ollama => "/api/embed",
            // Azure's OpenAI-COMPATIBLE (v1) surface lives under /openai/v1 on the resource host — a bare
            // resource URL would otherwise compose /v1/… and 404. A base already carrying /openai(…/v1)
            // falls through to the generic suffix logic below.
            ProviderDetect.AzureOpenAi when !baseUrl.Contains("/openai", StringComparison.OrdinalIgnoreCase)
                => "/openai/v1/embeddings",
            _ => baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? "/embeddings"
                : "/v1/embeddings",
        };
        return new Uri(baseUrl + path);
    }

    /// <summary>Tolerant extraction covering the two response shapes: OpenAI/LM-Studio
    /// <c>data[].embedding</c> (ordered by the authoritative <c>index</c>) and Ollama <c>embeddings[[…]]</c>
    /// (input order). Also accepts Ollama's legacy single <c>embedding[]</c> shape. Returns null on a
    /// malformed body or a shape carrying no vectors.</summary>
    internal static IReadOnlyList<float[]>? TryExtractVectors(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // OpenAI / LM Studio: { data: [ { index, embedding: [...] }, ... ] }
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var items = new List<(int Index, float[] Vector)>();
                var i = 0;
                foreach (var el in data.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object &&
                        el.TryGetProperty("embedding", out var emb) && emb.ValueKind == JsonValueKind.Array)
                    {
                        var index = el.TryGetProperty("index", out var ix) && ix.ValueKind == JsonValueKind.Number
                            ? ix.GetInt32() : i;
                        items.Add((index, ToFloats(emb)));
                    }
                    i++;
                }
                return items.Count > 0 ? [.. items.OrderBy(t => t.Index).Select(t => t.Vector)] : null;
            }

            // Ollama /api/embed: { embeddings: [ [...], [...] ] } (already in input order)
            if (root.TryGetProperty("embeddings", out var embeddings) && embeddings.ValueKind == JsonValueKind.Array)
            {
                var list = new List<float[]>();
                foreach (var el in embeddings.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Array) list.Add(ToFloats(el));
                return list.Count > 0 ? list : null;
            }

            // Ollama legacy single /api/embeddings: { embedding: [...] }
            if (root.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array)
                return [ToFloats(single)];

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static float[] ToFloats(JsonElement array)
    {
        var vector = new float[array.GetArrayLength()];
        var i = 0;
        foreach (var n in array.EnumerateArray())
            vector[i++] = n.ValueKind == JsonValueKind.Number ? (float)n.GetDouble() : 0f;
        return vector;
    }

    private static async Task<string> SafeRead(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return ""; }
    }

    private static string Head(string text, int max = 300)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
