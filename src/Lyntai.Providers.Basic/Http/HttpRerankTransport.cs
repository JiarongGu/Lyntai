using Lyntai.Lifecycle;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>The <c>/v1/rerank</c> WIRE SHAPE — the route Cohere defined and llama.cpp's <c>--reranking</c>
/// mode, Jina, TEI and vLLM serve. Composed by <see cref="HttpModelProvider"/> when a registration declares
/// <see cref="Lyntai.Lifecycle.ProviderKinds.Score"/>, exactly as
/// <see cref="HttpVectorTransport"/> is for vectors. A transport, not a backend: it has no id and no
/// capabilities, because the provider that owns it is the backend.
///
/// <para><b>It THROWS rather than reporting a degraded answer</b>, which is the rule
/// <see cref="Lyntai.Lifecycle.IModelProvider.ScoreAsync"/> states — there is no score meaning "I could
/// not", and a zero ranks as confidently as any other number. A caller that wants to fail open catches;
/// <c>ScoringVerificationPolicy</c> does exactly that.</para></summary>
internal sealed class HttpRerankTransport(
    string id,
    HttpModelOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger? logger = null,
    bool disposeHttpClient = true)
{
    private readonly ILogger _logger = logger ?? NullLogger<HttpRerankTransport>.Instance;
    private readonly HttpDialect _dialect = HttpEndpoint.ResolveDialect(config.Dialect, config.BaseUrl);

    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <summary>Score every document against the query, returning one score per document IN INPUT ORDER.</summary>
    /// <exception cref="HttpRequestException">The endpoint returned a non-2xx status or was unreachable.</exception>
    /// <exception cref="TimeoutException">No response within <see cref="LyntaiOptions.ProviderTimeout"/>.</exception>
    /// <exception cref="InvalidOperationException">The response was malformed, scored a document the caller
    /// did not send, or left one unscored.</exception>
    /// <exception cref="OperationCanceledException">The caller's <paramref name="ct"/> was cancelled.</exception>
    public async Task<IReadOnlyList<double>> ScoreAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];

        var timeout = options.ProviderTimeout;
        string body;
        try
        {
            using var owned = OwnedClient();       // disposed only when Lyntai owns it
            var http = owned ?? httpFactory();     // BYO client: fetched, not disposed
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            using var response = await http.SendAsync(BuildRequest(query, documents), timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"{id}: rerank HTTP {(int)response.StatusCode} {HttpBody.Head(errorBody)}");
            }
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"{id}: no rerank response within {timeout}");
        }

        var scores = TryExtractScores(body, documents.Count)
            ?? throw new InvalidOperationException($"{id}: malformed or incomplete rerank response");
        _logger.LogDebug("{Id}: scored {Count} documents", id, scores.Length);
        return scores;
    }

    private HttpRequestMessage BuildRequest(string query, IReadOnlyList<string> documents)
    {
        var payload = new JsonObject
        {
            ["model"] = config.Model ?? "",
            ["query"] = query,
            ["documents"] = new JsonArray([.. documents.Select(d => (JsonNode)JsonValue.Create(d))]),
            // every document, because the caller wants a score per document rather than a shortlist
            ["top_n"] = documents.Count,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        HttpEndpoint.ApplyAuth(request, config.ApiKey, _dialect);
        return request;
    }

    /// <summary>Ollama serves no native rerank route, so every dialect composes the same
    /// OpenAI/Cohere-shaped one.</summary>
    private Uri Endpoint() =>
        HttpEndpoint.Build(config.BaseUrl, _dialect, ollamaNativePath: "/v1/rerank", openAiRoute: "rerank");

    /// <summary>Reads <c>results[].index</c> plus <c>relevance_score</c> (Cohere/llama.cpp) or <c>score</c>,
    /// and puts them back in INPUT order — the endpoint answers SORTED, and an index is only meaningful to
    /// the caller that supplied the documents.
    ///
    /// <para>Null on any malformed element, on an index outside the batch, or on a document left unscored,
    /// rather than a partial ordering: an unfilled slot is a zero, and a zero ranks as confidently as a real
    /// score.</para></summary>
    private static double[]? TryExtractScores(string body, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array) return null;

            var scores = new double[count];
            var seen = new bool[count];
            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) return null;
                if (!r.TryGetProperty("index", out var i) || i.ValueKind != JsonValueKind.Number) return null;
                if ((!r.TryGetProperty("relevance_score", out var s) || s.ValueKind != JsonValueKind.Number)
                    && (!r.TryGetProperty("score", out s) || s.ValueKind != JsonValueKind.Number)) return null;
                var value = s.GetDouble();
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                var index = i.GetInt32();
                if (index < 0 || index >= count) return null;   // an index we did not send is unusable
                scores[index] = value;
                seen[index] = true;
            }
            return Array.TrueForAll(seen, x => x) ? scores : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
