using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>
/// The batched-embeddings WIRE SHAPE, composed by whichever provider owns the registration. It is a
/// transport rather than a backend — it has no id and no capabilities, because the provider that owns it is
/// the backend (<c>docs/DECISIONS.md</c> D132). Posts <c>{model, input[]}</c> — a body BOTH the OpenAI
/// <c>/v1/embeddings</c> and the Ollama <c>/api/embed</c> routes accept verbatim, which is why one
/// transport serves both providers: the OWNER decides the endpoint (<see cref="Settings.Endpoint"/>), and
/// the response extraction tolerates either answer shape by its JSON structure.
/// <see cref="LyntaiOptions.ProviderTimeout"/> is the deadline for one HTTP REQUEST, so a call that
/// <see cref="Settings.BatchSize"/> splits is bounded by batches × that value rather than by it once.
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
    HttpVectorTransport.Settings config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger? logger = null,
    bool disposeHttpClient = true)
{
    /// <summary>What the owning provider decides: the absolute endpoint and the auth convention, plus the
    /// embed knobs every HTTP embeddings surface shares.</summary>
    /// <param name="Endpoint">The absolute embeddings endpoint this registration POSTs to.</param>
    /// <param name="ApiKey">Bearer token; null for a keyless local endpoint.</param>
    /// <param name="AzureConventions">Whether key auth also travels in Azure's <c>api-key</c> header.</param>
    /// <param name="Model">The model named on the wire, when the endpoint selects by name.</param>
    /// <param name="BatchSize">Max inputs per request; <c>0</c> sends the whole batch at once.</param>
    /// <param name="DocumentPrefix">Prepended verbatim to <see cref="EmbeddingRole.Document"/> text.</param>
    /// <param name="QueryPrefix">Prepended verbatim to <see cref="EmbeddingRole.Query"/> text.</param>
    /// <param name="MaxInputChars">The most characters one sent input may carry, prefix included; a longer
    /// input is segmented and its pieces' vectors pooled. Null sends every input whole.</param>
    internal sealed record Settings(
        Uri Endpoint,
        string? ApiKey,
        bool AzureConventions,
        string? Model,
        int BatchSize,
        string? DocumentPrefix,
        string? QueryPrefix,
        int? MaxInputChars = null);

    private readonly ILogger _logger = logger ?? NullLogger<HttpVectorTransport>.Instance;

    /// <summary>Get the per-call HttpClient. Lyntai-created clients are disposed after each call; an
    /// APP-supplied (BYO) client is NEVER disposed — the app owns its lifetime.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <summary>Embed a request's texts, one vector per input in the SAME order (batched per
    /// <see cref="Settings.BatchSize"/> and concatenated), applying the role's configured prefix
    /// (<see cref="Settings.DocumentPrefix"/> / <see cref="Settings.QueryPrefix"/>) first.
    /// With neither prefix set — the default, and every symmetric model — it forwards without allocating.
    ///
    /// <para><b>A failed BATCH fails the whole call.</b> Returning the batches that happened to succeed
    /// would hand the caller fewer vectors than texts, silently mis-pairing every one after the gap.</para>
    ///
    /// <para>An input that, with its prefix, is longer than <see cref="Settings.MaxInputChars"/> is segmented,
    /// each piece carrying the prefix, and answered with its pieces' vectors pooled; every other input's vector
    /// is returned exactly as the endpoint sent it.</para>
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's <paramref name="ct"/> was cancelled — the
    /// one failure that is not a verdict, because it belongs to the caller.</exception>
    public async Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prefix = request.Role == EmbeddingRole.Query ? config.QueryPrefix : config.DocumentPrefix;
        var plan = config.MaxInputChars is { } max
            ? InputSegmenter.Segment(request.Texts, max - (prefix?.Length ?? 0))
            : null;
        var pieces = plan?.Pieces ?? request.Texts;
        IReadOnlyList<string> texts = string.IsNullOrEmpty(prefix)
            ? pieces
            : [.. pieces.Select(t => prefix + t)];

        // nothing asked for is nothing owed, and no HTTP call. Constructed directly rather than through
        // Success, whose empty-list guard is about an Ok that answered a real request with nothing.
        if (texts.Count == 0) return new VectorResponse(ProviderVerdict.Ok, []);

        var response = await EmbedAllAsync(texts, request, ct).ConfigureAwait(false);
        if (plan is null || plan.Segmented == 0 || !response.IsOk) return response;
        _logger.LogDebug("{Id}: segmented {Segmented} of {Count} inputs into {Pieces} pieces",
            id, plan.Segmented, request.Texts.Count, plan.Pieces.Count);
        return Pool(plan, response);
    }

    /// <summary>One vector per input: a segmented input's pieces pooled, any other input's vector as sent.</summary>
    private VectorResponse Pool(Segmentation plan, VectorResponse response)
    {
        var vectors = new float[plan.Inputs.Count][];
        for (var i = 0; i < vectors.Length; i++)
        {
            var (first, end) = (plan.First[i], plan.First[i + 1]);
            if (!plan.IsSegmented(i))
            {
                vectors[i] = response.Vectors[first];
                continue;
            }
            if (PooledUnitVector(response.Vectors, plan.Pieces, first, end) is not { } pooled)
                return VectorResponse.Failure(ProviderVerdict.Failed,
                    $"{id}: the pieces of input {i} came back in different dimensions");
            vectors[i] = pooled;
        }
        return VectorResponse.Success(vectors, usage: response.Usage);
    }

    /// <summary>The length-weighted mean of the pieces' unit vectors, re-normalised; the longest piece's
    /// unit vector where they cancel out. Null when the pieces disagree on dimension.</summary>
    private static float[]? PooledUnitVector(IReadOnlyList<float[]> vectors, IReadOnlyList<string> pieces,
        int first, int end)
    {
        var dim = vectors[first].Length;
        var sum = new double[dim];
        var longest = first;
        for (var j = first; j < end; j++)
        {
            if (vectors[j].Length != dim) return null;
            if (pieces[j].Length > pieces[longest].Length) longest = j;
            var norm = Norm(vectors[j]);
            if (norm == 0) continue;
            // the weighted mean's divisor is dropped: re-normalising removes it anyway
            for (var k = 0; k < dim; k++) sum[k] += pieces[j].Length * vectors[j][k] / norm;
        }
        var total = Math.Sqrt(sum.Sum(x => x * x));
        if (total == 0)
        {
            var fallback = vectors[longest];
            var norm = Norm(fallback);
            return norm == 0 ? fallback : [.. fallback.Select(x => (float)(x / norm))];
        }
        return [.. sum.Select(x => (float)(x / total))];
    }

    private static double Norm(float[] v) => Math.Sqrt(v.Sum(x => (double)x * x));

    /// <summary>Embed every text as sent, split per <see cref="Settings.BatchSize"/> and concatenated in
    /// order.</summary>
    private async Task<VectorResponse> EmbedAllAsync(IReadOnlyList<string> texts, VectorRequest request,
        CancellationToken ct)
    {
        using var owned = OwnedClient();       // disposed only when Lyntai owns it
        var http = owned ?? httpFactory();     // BYO client: fetched, not disposed

        // the same resolution ladder the text shape has always had — explicit seconds (clamped), the
        // consumer's TimeoutByConsumer tier, the default tier, the global timeout (D162/D163)
        var timeout = options.ResolveTimeout(request.TimeoutSeconds, request.Consumer);

        var batchSize = config.BatchSize;
        if (batchSize <= 0 || texts.Count <= batchSize)
            return await EmbedBatchAsync(texts, http, timeout, ct).ConfigureAwait(false);

        // input list larger than the endpoint's per-request cap → split, concatenate in input order
        var result = new List<float[]>(texts.Count);
        ProviderUsage? usage = null;
        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var slice = texts.Skip(i).Take(batchSize).ToList();
            var batch = await EmbedBatchAsync(slice, http, timeout, ct).ConfigureAwait(false);
            if (!batch.IsOk) return batch;
            result.AddRange(batch.Vectors);
            if (batch.Usage is { } u)
                usage = new ProviderUsage(
                    (usage?.InputTokens ?? 0) + u.InputTokens,
                    (usage?.OutputTokens ?? 0) + u.OutputTokens,
                    usage?.CostUsd is { } c ? c + (u.CostUsd ?? 0) : u.CostUsd);
        }
        return VectorResponse.Success(result, usage: usage);
    }

    private async Task<VectorResponse> EmbedBatchAsync(IReadOnlyList<string> batch, HttpClient http,
        TimeSpan timeout, CancellationToken ct)
    {
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
        return VectorResponse.Success(vectors, usage: TryExtractUsage(body));
    }

    /// <summary>What the wire said this batch spent — OpenAI's <c>usage.prompt_tokens</c>, or Ollama's
    /// <c>prompt_eval_count</c> on <c>/api/embed</c>. Null where the endpoint reported nothing, which is a
    /// fact worth preserving rather than a zero (D162).</summary>
    private static ProviderUsage? TryExtractUsage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                return new ProviderUsage(Lyntai.Providers.Basic.WireJson.Long(u, "prompt_tokens"));
            if (root.TryGetProperty("prompt_eval_count", out _))
                return new ProviderUsage(Lyntai.Providers.Basic.WireJson.Long(root, "prompt_eval_count"));
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<string> texts)
    {
        // {model, input[]} — accepted verbatim by both the OpenAI /v1/embeddings and the Ollama /api/embed
        // routes, so one body serves both owners; only the endpoint + response key differ.
        var payload = new JsonObject
        {
            ["model"] = config.Model ?? "",
            ["input"] = new JsonArray([.. texts.Select(t => (JsonNode)JsonValue.Create(t))]),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        HttpEndpoint.ApplyAuth(request, config.ApiKey, config.AzureConventions);
        return request;
    }

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
