using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>
/// A cross-encoder reranker in the memory VERIFICATION seam, over an OpenAI/Cohere-shaped
/// <c>/v1/rerank</c> endpoint.
///
/// <para><b>Why this seam rather than the ranking one.</b> <c>IMemoryRankingPolicy.Rank</c> is synchronous
/// and takes its context by <c>in</c>, so no model-backed policy fits it at all. Verification is async, sees
/// the pool at <c>GraphMemoryOptions.VerificationDepth</c>, and promotes before the cut — so endorsing the
/// reranker's own top-<see cref="CrossEncoderVerificationOptions.EndorseCount"/> expresses "rerank the pool
/// and take the top k" through a seam that already ships.</para>
///
/// <para><b>It scores pairs and never generates</b>, which is what makes it cheap: a sub-500 MB
/// cross-encoder is a supported alternative to a multi-gigabyte instruct model here, and this repository's
/// own figures put the small model AHEAD in this role. Those figures are ladder rungs rather than shipped
/// defaults — <c>docs/memory-measurements.md</c> §5 owns them, and nothing is registered unless you register
/// it.</para>
///
/// <para><b>Fail-open, like every model-backed memory seam.</b> An unreachable endpoint, a non-2xx, a
/// malformed body or a per-request timeout all report <c>NoOpinion</c>, which leaves the ranking untouched
/// and is distinct from an EMPTY endorsement meaning "none of these answered". The caller's own cancellation
/// is re-thrown instead — tested as <c>ct.IsCancellationRequested</c>, never by the exception's type,
/// because this client's own timeout arrives as the same exception type.</para>
/// </summary>
public sealed class CrossEncoderVerificationPolicy(
    CrossEncoderVerificationOptions config,
    Func<HttpClient> httpFactory,
    LyntaiOptions options,
    ILogger<CrossEncoderVerificationPolicy>? logger = null,
    bool disposeHttpClient = true) : IMemoryVerificationPolicy
{
    private readonly ILogger _logger = logger ?? NullLogger<CrossEncoderVerificationPolicy>.Instance;

    /// <summary>Lyntai-created clients are disposed after each call; an APP-supplied client is NEVER
    /// disposed — the app owns its lifetime.</summary>
    private HttpClient? OwnedClient() => disposeHttpClient ? httpFactory() : null;

    /// <summary>Endorses the reranker's best <see cref="CrossEncoderVerificationOptions.EndorseCount"/> of
    /// whatever the engine showed it, or <c>NoOpinion</c> when nothing usable answered.</summary>
    /// <exception cref="OperationCanceledException">The CALLER's <paramref name="ct"/> was cancelled.</exception>
    public async Task<MemoryVerification> VerifyAsync(
        MemoryVerificationRequest request, CancellationToken ct = default)
    {
        if (request.Candidates.Count == 0) return MemoryVerification.NoOpinion;

        // CONTENT, falling back to the headline only when the engine supplied none. A headline is a
        // truncation and scoring a fragment cost this arm 13 points (D108).
        var documents = request.Candidates.Select(c => c.Content ?? c.Headline).ToList();

        var scored = await RankAsync(request.Query, documents, ct).ConfigureAwait(false);
        if (scored is null) return MemoryVerification.NoOpinion;

        var ids = scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Max(0, config.EndorseCount))
            .Where(s => s.Index >= 0 && s.Index < request.Candidates.Count)   // never invent an id
            .Select(s => request.Candidates[s.Index].Id)
            .ToList();

        // EVERY scored candidate, not just the endorsed ones: the rejected scores are what a caller needs
        // to read a margin, and this policy had been computing and discarding them. Same index guard as
        // above, and last-wins on a duplicated index rather than throwing — a backend that repeats one is
        // malformed, and losing the verdict over it would be the fail-open seam turning into a hard failure.
        var byId = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (index, score) in scored)
            if (index >= 0 && index < request.Candidates.Count)
                byId[request.Candidates[index].Id] = score;

        return new MemoryVerification(ids) { Scores = byId };
    }

    /// <summary>Scores every document against the query. Null — never a fabricated ordering — when the
    /// endpoint did not answer usably, because an invented order is indistinguishable from a real one.</summary>
    private async Task<List<(int Index, double Score)>?> RankAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct)
    {
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
                _logger.LogDebug("cross-encoder rerank HTTP {Status}; reporting NoOpinion", (int)response.StatusCode);
                return null;
            }
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("cross-encoder rerank did not answer within {Timeout}; reporting NoOpinion", timeout);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "cross-encoder rerank unreachable; reporting NoOpinion");
            return null;
        }

        return TryExtractScores(body, documents.Count);
    }

    private HttpRequestMessage BuildRequest(string query, IReadOnlyList<string> documents)
    {
        var payload = new JsonObject
        {
            ["model"] = config.Model ?? "",
            ["query"] = query,
            ["documents"] = new JsonArray([.. documents.Select(d => (JsonNode)JsonValue.Create(d))]),
            ["top_n"] = documents.Count,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/v1/rerank")
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        return request;
    }

    /// <summary>Reads <c>results[].index</c> plus <c>relevance_score</c> (Cohere/llama.cpp) or <c>score</c>.
    /// Returns null on any malformed element rather than a partial ordering.</summary>
    private static List<(int Index, double Score)>? TryExtractScores(string body, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array) return null;

            var scored = new List<(int, double)>(results.GetArrayLength());
            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) return null;
                if (!r.TryGetProperty("index", out var i) || i.ValueKind != JsonValueKind.Number) return null;
                if ((!r.TryGetProperty("relevance_score", out var s) || s.ValueKind != JsonValueKind.Number)
                    && (!r.TryGetProperty("score", out s) || s.ValueKind != JsonValueKind.Number)) return null;
                var value = s.GetDouble();
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                scored.Add((i.GetInt32(), value));
            }
            return scored.Count == 0 && count > 0 ? null : scored;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
