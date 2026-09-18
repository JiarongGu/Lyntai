using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>
/// The <c>/embeddings</c> WIRE SHAPE, composed by <see cref="HttpModelProvider"/> when a host
/// declares that route. It is a transport rather than a backend — it has no id and no capabilities,
/// because the provider that owns it is the backend (<c>docs/DECISIONS.md</c> D132). Posts
/// <c>{model, input[]}</c> — batched — and extracts vectors tolerantly from either the OpenAI/LM-Studio
/// <c>data[].embedding</c> shape or Ollama's <c>embeddings[[…]]</c> shape. Endpoint + dialect come from the
/// same <see cref="ProviderDetect"/> the chat provider uses (Ollama → native <c>/api/embed</c>; a bare
/// Azure resource → <c>/openai/v1/embeddings</c>; everything else → <c>/v1/embeddings</c>).
/// <see cref="LyntaiOptions.ProviderTimeout"/> is the deadline for one HTTP REQUEST, so a call that
/// <see cref="HttpModelOptions.BatchSize"/> splits is bounded by batches × that value rather
/// than by it once.
/// <para><b>Failures come back as a <see cref="VectorResponse"/> verdict</b> (<c>docs/DECISIONS.md</c>
/// <b>D153</b>), classified through the same
/// <see cref="ProviderVerdictClassifier.FromHttpFailure(System.Net.HttpStatusCode,string,bool)"/> the
/// chat path uses — so a 429 cools this host and a 401 answered to a call carrying NO key is
/// <see cref="ProviderVerdict.NotConfigured"/> rather than a blamed <see cref="ProviderVerdict.AuthFailed"/>.
/// This class previously threw and said that distinction in the MESSAGE, because an embed call had no
/// verdict to put it in.</para>
/// </summary>
internal sealed class HttpVectorTransport(
    string id,
    HttpModelOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger? logger = null,
    bool disposeHttpClient = true)
{
    private readonly ILogger _logger = logger ?? NullLogger<HttpVectorTransport>.Instance;

    private readonly HttpDialect _dialect = HttpEndpoint.ResolveDialect(config.Dialect, config.BaseUrl);

    /// <summary>Get the per-call HttpClient. Lyntai-created clients are disposed after each call; an
    /// APP-supplied (BYO) client is NEVER disposed — the app owns its lifetime.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <summary>Embed a request's texts, one vector per input in the SAME order (batched per
    /// <see cref="HttpModelOptions.BatchSize"/> and concatenated), applying the role's configured prefix
    /// (<see cref="HttpModelOptions.DocumentPrefix"/> / <see cref="HttpModelOptions.QueryPrefix"/>) first.
    /// With neither prefix set — the default, and every symmetric model — it forwards without allocating.
    ///
    /// <para><b>A failed BATCH fails the whole call.</b> Returning the batches that happened to succeed
    /// would hand the caller fewer vectors than texts, silently mis-pairing every one after the gap.</para>
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's <paramref name="ct"/> was cancelled — the
    /// one failure that is not a verdict, because it belongs to the caller.</exception>
    public async Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prefix = request.Role == EmbeddingRole.Query ? config.QueryPrefix : config.DocumentPrefix;
        IReadOnlyList<string> texts = string.IsNullOrEmpty(prefix)
            ? request.Texts
            : [.. request.Texts.Select(t => prefix + t)];

        // nothing asked for is nothing owed, and no HTTP call. Constructed directly rather than through
        // Success, whose empty-list guard is about an Ok that answered a real request with nothing.
        if (texts.Count == 0) return new VectorResponse(ProviderVerdict.Ok, []);

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
            var batch = await EmbedBatchAsync(slice, http, ct).ConfigureAwait(false);
            if (!batch.IsOk) return batch;
            result.AddRange(batch.Vectors);
        }
        return VectorResponse.Success(result);
    }

    private async Task<VectorResponse> EmbedBatchAsync(IReadOnlyList<string> batch, HttpClient http, CancellationToken ct)
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
                var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                // the THREE-argument overload: a 401/403 answered to a call that carried no credentials is
                // NotConfigured, which advances blamelessly, where AuthFailed benches the host
                return VectorResponse.Failure(
                    ProviderVerdictClassifier.FromHttpFailure(
                        response.StatusCode, errorBody, hasCredentials: !string.IsNullOrWhiteSpace(config.ApiKey)),
                    $"{id}: embeddings HTTP {(int)response.StatusCode} {HttpBody.Head(errorBody)}");
            }
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return VectorResponse.Failure(
                ProviderVerdict.Timeout, $"{id}: no embeddings response within {timeout}");
        }
        // A transport-level throw (socket reset, DNS, TLS) is deliberately NOT caught here: the classifier's
        // thrown-exception arm is internal to Core, and `ProviderRouter` already applies it to anything that
        // escapes a backend. Catching it here would mean a second, weaker copy of that taxonomy.

        var vectors = TryExtractVectors(body);
        if (vectors is null)
            return VectorResponse.Failure(ProviderVerdict.Failed, $"{id}: malformed or empty embeddings response");
        if (vectors.Count != batch.Count)
            return VectorResponse.Failure(
                ProviderVerdict.Failed, $"{id}: expected {batch.Count} embeddings, got {vectors.Count}");
        _logger.LogDebug("{Id}: embedded {Count} texts ({Dim}-dim)", id, vectors.Count, vectors[0].Length);
        return VectorResponse.Success(vectors);
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<string> texts)
    {
        // {model, input[]} — accepted verbatim by both the OpenAI /v1/embeddings and the Ollama /api/embed
        // shapes, so one body serves every dialect; only the endpoint + response key differ.
        var payload = new JsonObject
        {
            ["model"] = config.Model ?? "",
            ["input"] = new JsonArray([.. texts.Select(t => (JsonNode)JsonValue.Create(t))]),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        HttpEndpoint.ApplyAuth(request, config.ApiKey, _dialect);
        return request;
    }

    /// <summary>The embeddings endpoint — Ollama's native batched <c>/api/embed</c> (parallel to the chat
    /// provider's <c>/api/chat</c>), otherwise the OpenAI-shaped <c>embeddings</c> route.</summary>
    private Uri Endpoint() =>
        HttpEndpoint.Build(config.BaseUrl, _dialect, ollamaNativePath: "/api/embed", openAiRoute: "embeddings");

    /// <summary>Tolerant extraction covering the two response shapes: OpenAI/LM-Studio
    /// <c>data[].embedding</c> (ordered by the authoritative <c>index</c>) and Ollama <c>embeddings[[…]]</c>
    /// (input order). Also accepts Ollama's legacy single <c>embedding[]</c> shape. Returns null on a
    /// malformed body or a shape carrying no vectors.</summary>
    private static IReadOnlyList<float[]>? TryExtractVectors(string body)
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
                        // a bad element fails the WHOLE response: dropping just this vector would trip the
                        // count check with a message about arity, pointing away from the real defect
                        if (TryToFloats(emb) is not { } vector) return null;
                        var index = el.TryGetProperty("index", out var ix) && ix.ValueKind == JsonValueKind.Number
                            ? ix.GetInt32() : i;
                        items.Add((index, vector));
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
                {
                    if (el.ValueKind != JsonValueKind.Array) continue;
                    if (TryToFloats(el) is not { } vector) return null;
                    list.Add(vector);
                }
                return list.Count > 0 ? list : null;
            }

            // Ollama legacy single /api/embeddings: { embedding: [...] }
            if (root.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array)
                return TryToFloats(single) is { } vector ? [vector] : null;

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The vector, or NULL when any element is not a finite number — which makes the whole response
    /// malformed rather than yielding a vector with a hole in it.
    ///
    /// <para><b>A bad element used to become <c>0f</c>.</b> That is indistinguishable from a legitimate zero
    /// component, so a null, a stringified number or an overflowing magnitude produced a plausible vector
    /// that was then stored and compared by cosine with nothing reporting it. Failing the response is the
    /// behaviour this type's own doc already promises, and the caller already turns null into that
    /// exception.</para></summary>
    private static float[]? TryToFloats(JsonElement array)
    {
        var vector = new float[array.GetArrayLength()];
        var i = 0;
        foreach (var n in array.EnumerateArray())
        {
            if (n.ValueKind != JsonValueKind.Number) return null;
            var value = (float)n.GetDouble();
            if (!float.IsFinite(value)) return null;   // an Infinity poisons every cosine it touches
            vector[i++] = value;
        }
        return vector;
    }

    /// <summary>" (not configured: no ApiKey)" when the server demanded credentials this call never carried,
    /// otherwise empty. A vector backend has no verdict and no fallback — it THROWS — so unlike a chat provider it
    /// has no <c>NotConfigured</c> outcome to report and nothing to route around; the wording is the only
    /// thing a host can act on. Without it a 401 sends someone to check a key they never supplied, when the
    /// answer is to set one. Same distinction as the provider side, expressed the only way this seam allows.
    /// A local embeddings server (LM Studio, Ollama) needs no key, so the hint is tied to the server actually
    /// answering 401/403 rather than to the key being absent.</summary>
    private string NotConfiguredHint(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        && string.IsNullOrWhiteSpace(config.ApiKey)
            ? " (not configured: no ApiKey)"
            : "";
}
