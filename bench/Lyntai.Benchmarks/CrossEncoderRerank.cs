using System.Net.Http.Json;
using System.Text.Json;
using Lyntai.Memory.Verification;

namespace Lyntai.Benchmarks;

/// <summary>
/// A purpose-built cross-encoder reranker, over any OpenAI/Cohere-shaped <c>/v1/rerank</c> endpoint.
///
/// <para><b>Why this arm exists.</b> <c>docs/memory-measurements.md</c> §5 records the retrieval gap as ranking rather
/// than retrieval — the oracle endorses evidence the engine already holds and gains <b>+9.5</b> points,
/// while a real 4B LLM judge on the same arm SPENDS 10.5. The reading filed there is that the seam's shape
/// (score <c>(query, candidate)</c> pairs, reorder, never generate) is what rerankers exist for and an LLM
/// judge is a general tool doing a specialised job. That was a design lead read out of the literature and
/// measured on nothing. This measures it.</para>
///
/// <para><b>It needs no library change, which is the point of running it before proposing one.</b>
/// <c>IMemoryRankingPolicy.Rank</c> is synchronous and takes its context by <c>in</c>, so a model-backed
/// ranking policy cannot be plugged into that seam at all — which is why the blocker was recorded as
/// wanting an in-process ONNX package. The VERIFICATION seam is async and already sees the whole candidate
/// pool at <c>VerificationDepth</c>, and under the shipped <c>Partition</c> an endorsed set is promoted
/// ahead of everything unendorsed and then cut at the caller's limit. So endorsing exactly the reranker's
/// top-<c>limit</c> makes the returned page BE the reranker's choice of the pool. That is "rerank the pool
/// and take the top k" expressed through a seam that already ships.</para>
/// </summary>
internal sealed class CrossEncoderReranker(HttpClient http, string baseUrl, string model)
{
    /// <summary>Endpoint of the rerank server. Separate from <see cref="SweepDoubles.UrlVariable"/> because
    /// a <c>llama-server</c> serves ONE model: the embedder and the reranker are two processes on two
    /// ports, not two models behind one.</summary>
    internal const string UrlVariable = "LYNTAI_LIVE_RERANK_URL";

    /// <summary>Name sent as the <c>model</c> field. A single-model server ignores it (measured — it echoes
    /// back whatever it is sent), so it matters only on a router.</summary>
    internal const string ModelVariable = "LYNTAI_LIVE_RERANK_MODEL";

    internal static string BaseUrl =>
        Environment.GetEnvironmentVariable(UrlVariable) ?? "http://localhost:8081";

    internal static string Model =>
        Environment.GetEnvironmentVariable(ModelVariable) ?? "bge-reranker-v2-m3";

    private int _calls;
    private int _scored;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<double, byte> _values = new();

    /// <summary>Calls made, pairs scored, and DISTINCT scores seen — the control that separates "the
    /// reranker did not help" from "the reranker never discriminated". A model returning one value for
    /// every candidate reorders nothing, and under rank competition that reads as a clean flat result
    /// rather than as the broken instrument it is (<c>SweepDoubles.CountingSaliencePolicy</c> carries the
    /// same reasoning for salience).</summary>
    internal (int Calls, int Scored, int DistinctScores) Audit => (_calls, _scored, _values.Count);

    /// <summary>Scores every document against the query, best-first. Null when the endpoint did not answer
    /// with a usable result — never a fabricated ordering, which would be indistinguishable from a real one
    /// in the table.</summary>
    internal async Task<IReadOnlyList<(int Index, double Score)>?> RankAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];
        try
        {
            using var response = await http.PostAsJsonAsync($"{baseUrl}/v1/rerank",
                new { model, query, documents, top_n = documents.Count }, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array) return null;

            var scored = new List<(int, double)>(results.GetArrayLength());
            foreach (var r in results.EnumerateArray())
            {
                if (!r.TryGetProperty("index", out var i)) return null;
                if (!r.TryGetProperty("relevance_score", out var s) &&
                    !r.TryGetProperty("score", out s)) return null;
                var value = s.GetDouble();
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                scored.Add((i.GetInt32(), value));
                _values.TryAdd(Math.Round(value, 6), 0);
            }

            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _scored, scored.Count);
            return scored;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>One probe against the live endpoint, scoring a pair whose answer is not in doubt. Returns
    /// false when nothing usable answers, so an arm can print a SKIPPED line rather than quietly reporting
    /// the unjudged base as though a reranker had run.</summary>
    internal async Task<bool> ReachableAsync()
    {
        var probe = await RankAsync("what colour is the sky", ["the sky is blue", "bread is baked"]);
        return probe is { Count: 2 };
    }
}

/// <summary>Endorses the reranker's own top-<paramref name="top"/> of whatever the engine showed it.
///
/// <para><b>Deliberately not a judge.</b> A judge decides IF each candidate answered and the engine
/// promotes all of them; the size of that set is the judge's to choose, and choosing it badly is what cost
/// the 4B model 10.5 points (29.1 endorsements per recall out of 80 shown). This endorses a FIXED count, so
/// the promoted set can never be larger than the page and promotion refines the ranking instead of
/// replacing it. What is being measured is the ORDER a cross-encoder puts the pool in, nothing else.</para>
///
/// <para><b>An unreachable endpoint reports NoOpinion</b>, which the engine treats as "no verdict" and is
/// distinct from an empty endorsement meaning "none of these answered" — collapsing the two is the defect
/// <c>MemoryVerification</c>'s own docs warn about.</para></summary>
internal sealed class CrossEncoderVerifier(CrossEncoderReranker reranker, int top) : IMemoryVerificationPolicy
{
    public async Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
        CancellationToken ct = default)
    {
        if (request.Candidates.Count == 0) return MemoryVerification.NoOpinion;

        // CONTENT, falling back to the headline only when nobody supplied one. The headline is a truncation
        // and scoring a fragment cost this arm 13 points; the engine now passes the whole entry, so the
        // `+hl512` arms exist as the historical control rather than as the way to get the text.
        var scored = await reranker.RankAsync(
            request.Query, [.. request.Candidates.Select(c => c.Content ?? c.Headline)], ct);
        if (scored is null) return MemoryVerification.NoOpinion;

        var ids = scored
            .OrderByDescending(s => s.Score)
            .Take(top)
            .Where(s => s.Index >= 0 && s.Index < request.Candidates.Count)  // never invent an id
            .Select(s => request.Candidates[s.Index].Id)
            .ToList();
        return new MemoryVerification(ids);
    }
}
