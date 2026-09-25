using Lyntai.Inference;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Http;

/// <summary>The <c>/v1/rerank</c> WIRE SHAPE — the route Cohere defined and llama.cpp's <c>--reranking</c>
/// mode, Jina, TEI and vLLM serve. Composed by <see cref="HttpModelProvider"/> when a registration declares
/// <see cref="Lyntai.Inference.ProviderKinds.Score"/>, exactly as
/// <see cref="HttpVectorTransport"/> is for vectors. A transport, not a backend: it has no id and no
/// capabilities, because the provider that owns it is the backend.
///
/// <para><b>It reports a non-Ok VERDICT rather than a degraded answer</b>, which is the rule
/// <see cref="ScoreResponse"/> states — there is no score meaning "I could not", and a zero ranks as
/// confidently as any other number, so failure gets its own axis. A caller that wants to fail open reads
/// the verdict; <c>ScoringVerificationPolicy</c> does exactly that.</para></summary>
internal sealed class HttpRerankTransport(
    string id,
    HttpRerankTransport.Settings config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger? logger = null,
    bool disposeHttpClient = true)
{
    /// <summary>What the owning provider decides: the absolute endpoint and the auth convention, plus the
    /// pair window — the same split <see cref="HttpVectorTransport.Settings"/> makes.</summary>
    /// <param name="Endpoint">The absolute rerank endpoint this registration POSTs to.</param>
    /// <param name="ApiKey">Bearer token; null for a keyless local endpoint.</param>
    /// <param name="AzureConventions">Whether key auth also travels in Azure's <c>api-key</c> header.</param>
    /// <param name="Model">The model named on the wire, when the endpoint selects by name.</param>
    /// <param name="MaxInputChars">The PAIR window, in characters after NFKC normalisation; null sends every
    /// pair whole.</param>
    /// <param name="Segmentation">Segment or truncate past the window; null segments at the record's
    /// defaults.</param>
    internal sealed record Settings(
        Uri Endpoint,
        string? ApiKey,
        bool AzureConventions,
        string? Model,
        int? MaxInputChars = null,
        InputSegmentation? Segmentation = null);

    // a bound with no record segments at the record's defaults
    private static readonly InputSegmentation Defaults = new();

    private readonly ILogger _logger = logger ?? NullLogger<HttpRerankTransport>.Instance;

    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <summary>Score every document against the query, one score per document IN INPUT ORDER.
    /// <para>Failure is a <see cref="ScoreResponse"/> verdict, classified like the chat and vector paths, so
    /// a 429 cools this host and a 401 answered to a call with no key is NotConfigured (D153).</para>
    /// <para><see cref="HttpModelOptions.MaxInputChars"/> is the PAIR window: the query keeps at most its
    /// share, cut once for every document, and a document longer than the rest is sent as pieces, in the same
    /// request as everything else, and scores as its best piece — or, truncating, is sent cut.</para></summary>
    /// <exception cref="OperationCanceledException">The caller's <paramref name="ct"/> was cancelled.</exception>
    public async Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Query;
        if (request.Documents.Count == 0) return new ScoreResponse(ProviderVerdict.Ok, []);
        SegmentPlan? plan = null;
        var documents = request.Documents;
        if (config.MaxInputChars is { } window)
        {
            var segmentation = config.Segmentation ?? Defaults;
            query = InputSegmenter.QueryWithin(query, window, segmentation.MinDocumentShare);
            var budget = window - InputSegmenter.Measure(query);
            if (segmentation.Overflow == InputOverflow.Truncate)
                documents = [.. request.Documents.Select(d => InputSegmenter.Truncate(d, budget))];
            else
            {
                plan = InputSegmenter.Segment(
                    request.Documents, budget, segmentation.Overlap, segmentation.MaxPiecesPerInput);
                documents = plan.Pieces;
            }
        }

        // the same resolution ladder the text shape has always had — explicit seconds (clamped), the
        // consumer's TimeoutByConsumer tier, the default tier, the global timeout (D162/D163)
        var timeout = options.ResolveTimeout(request.TimeoutSeconds, request.Consumer);
        HttpJsonResponse reply;
        using (var owned = OwnedClient())       // disposed only when Lyntai owns it
        {
            var http = owned ?? httpFactory();  // BYO client: fetched, not disposed
            reply = await HttpJsonCall.SendAsync(http, BuildRequest(query, documents), timeout,
                HttpEndpoint.HasCredentials(config.ApiKey), id, "rerank", ct).ConfigureAwait(false);
        }
        if (reply.Body is not { } body) return ScoreResponse.Failure(reply.Verdict, reply.Detail);

        var scores = TryExtractScores(body, documents.Count);
        if (scores is null)
            return ScoreResponse.Failure(
                ProviderVerdict.Failed, $"{id}: malformed or incomplete rerank response");
        if (plan is { Segmented: > 0 })
        {
            _logger.LogDebug("{Id}: segmented {Segmented} of {Count} documents into {Pieces} pieces",
                id, plan.Segmented, plan.Inputs.Count, plan.Pieces.Count);
            scores = BestPiece(plan, scores);
        }
        _logger.LogDebug("{Id}: scored {Count} documents", id, scores.Length);
        return ScoreResponse.Success(scores);
    }

    /// <summary>A document is as relevant as its most relevant passage: the maximum over its pieces.</summary>
    private static double[] BestPiece(SegmentPlan plan, double[] pieceScores) =>
        [.. Enumerable.Range(0, plan.Inputs.Count)
            .Select(i => pieceScores[plan.First[i]..plan.First[i + 1]].Max())];

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
        return HttpJsonCall.Post(config.Endpoint, payload, config.ApiKey, config.AzureConventions);
    }

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
            if (WireJson.Array(doc.RootElement, "results") is not { } results) return null;

            var scores = new double[count];
            var seen = new bool[count];
            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) return null;
                if (WireJson.Int32(r, "index") is not { } index) return null;
                if ((!r.TryGetProperty("relevance_score", out var s) || s.ValueKind != JsonValueKind.Number)
                    && (!r.TryGetProperty("score", out s) || s.ValueKind != JsonValueKind.Number)) return null;
                var value = s.GetDouble();
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                if (index < 0 || index >= count) return null;   // an index we did not send is unusable
                scores[index] = value;
                seen[index] = true;
            }
            return Array.TrueForAll(seen, x => x) ? scores : null;
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return null;
        }
    }
}
