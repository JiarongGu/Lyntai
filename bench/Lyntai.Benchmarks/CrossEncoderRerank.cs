using System.Net.Http.Json;
using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Memory.Verification;
using Lyntai.Providers.Onnx;
using Lyntai.Text;

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
    /// a <c>llama-server</c> serves ONE model: the vector backend and the reranker are two processes on two
    /// ports, not two models behind one.</summary>
    internal const string UrlVariable = "LYNTAI_LIVE_RERANK_URL";

    /// <summary>Name sent as the <c>model</c> field. A single-model server ignores it (measured — it echoes
    /// back whatever it is sent), so it matters only on a router.</summary>
    internal const string ModelVariable = "LYNTAI_LIVE_RERANK_MODEL";

    /// <summary>A directory holding an ONNX cross-encoder export. Set it and the arm scores IN PROCESS
    /// instead of over HTTP — no server, no port — which is the only way to reach a model llama.cpp cannot
    /// convert correctly at all (<c>docs/memory-measurements.md</c> §5).</summary>
    internal const string OnnxDirectoryVariable = "LYNTAI_ONNX_RERANK_MODEL_DIR";

    /// <summary>Which graph inside that directory. Unset probes <c>onnx/model.onnx</c>, the fp32 export —
    /// name the int8 sibling to measure the size class this study is actually about.</summary>
    internal const string OnnxFileVariable = "LYNTAI_ONNX_RERANK_MODEL_FILE";

    internal static string BaseUrl =>
        Environment.GetEnvironmentVariable(UrlVariable) ?? "http://localhost:8081";

    internal static string Model =>
        Environment.GetEnvironmentVariable(ModelVariable) ?? "bge-reranker-v2-m3";

    internal static string? OnnxDirectory => Environment.GetEnvironmentVariable(OnnxDirectoryVariable);

    private int _calls;
    private int _scored;
    private int _truncated;
    private OnnxCrossEncoder? _local;
    private WordPieceTokenizer? _tokenizer;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<double, byte> _values = new();

    /// <summary>Score in process from an ONNX export rather than over HTTP, when
    /// <see cref="OnnxDirectoryVariable"/> names one. Everything downstream is unchanged — the same
    /// <see cref="CrossEncoderVerifier"/>, the same audit, the same arm — so the only difference between
    /// this row and a published one is which runtime produced the numbers, which is the comparison.</summary>
    /// <returns>What the run should PRINT about the backend, or null when no directory was named.</returns>
    internal string? UseLocalOnnx()
    {
        if (OnnxDirectory is not { Length: > 0 } directory) return null;

        var file = Environment.GetEnvironmentVariable(OnnxFileVariable);
        _local = OnnxCrossEncoder.FromDirectory(directory,
            new OnnxCrossEncoderOptions { ModelFile = string.IsNullOrWhiteSpace(file) ? null : file });

        // The library's own tokenizer, for the TRUNCATION control below — the same vocabulary the session
        // will use, so the count is exact rather than a character-length proxy.
        _tokenizer = WordPieceTokenizer.FromModelDirectory(directory);

        var graph = Path.Combine(directory, file ?? Path.Combine("onnx", "model.onnx"));
        return $"in process from {Path.GetFileName(graph)} ({Bytes(graph):N0} B, "
            + $"{_local.MaxTokens}-token window)";
    }

    /// <summary>Calls made, pairs scored, and DISTINCT scores seen — the control that separates "the
    /// reranker did not help" from "the reranker never discriminated". A model returning one value for
    /// every candidate reorders nothing, and under rank competition that reads as a clean flat result
    /// rather than as the broken instrument it is (<c>SweepDoubles.CountingSaliencePolicy</c> carries the
    /// same reasoning for salience).</summary>
    internal (int Calls, int Scored, int DistinctScores) Audit => (_calls, _scored, _values.Count);

    /// <summary>Pairs whose DOCUMENT did not fit the model's window, counted only on the in-process path
    /// where the window is known. <b>It separates "this model is weaker" from "this model never saw the
    /// evidence"</b> — every published arm here has an 8192-token window and the candidate has 512, so a
    /// deficit is unreadable without it.</summary>
    internal int Truncated => _truncated;

    /// <summary>Zeroes every counter and clears every distinct score seen. Called once, in
    /// <see cref="MemoryContentionSweep.BuildRigAsync"/> immediately after <see cref="ReachableAsync"/>
    /// succeeds, so that probe's own two scored pairs do not count toward the MEASURED region's
    /// <c>DistinctRerankScores</c> control — the same reason <c>CountingAnnotation.Reset()</c> exists.
    /// </summary>
    internal void Reset()
    {
        Interlocked.Exchange(ref _calls, 0);
        Interlocked.Exchange(ref _scored, 0);
        Interlocked.Exchange(ref _truncated, 0);
        _values.Clear();
    }

    /// <summary>Scores every document against the query, best-first. Null when the endpoint did not answer
    /// with a usable result — never a fabricated ordering, which would be indistinguishable from a real one
    /// in the table.</summary>
    internal async Task<IReadOnlyList<(int Index, double Score)>?> RankAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];
        if (_local is not null) return LocalRank(query, documents, ct);
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

    /// <summary>A model file's size on disk, FOLLOWING a symbolic link.
    ///
    /// <para><b>Windows reports a reparse point's own length, which is 0</b> — and every model in a
    /// HuggingFace cache is a symlink into <c>blobs/</c>, so the naive read prints <c>0 B</c> for a file
    /// that is plainly there. This repository's standing rule is to state EXACT BYTES whenever a size
    /// decides anything, and a zero in that column is how the rule gets quietly broken
    /// (<c>.claude/knowledge/pitfalls.md</c>).</para></summary>
    private static long Bytes(string path)
    {
        if (!File.Exists(path)) return 0;
        var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
        return target is not null ? new FileInfo(target.FullName).Length : new FileInfo(path).Length;
    }

    /// <summary>The in-process path: the same scores through the same audit, plus the truncation count only
    /// this side can know. Synchronous under an async signature because the session runs on the calling
    /// thread — stated rather than hidden behind a <c>Task.Run</c> that would misreport the cost.</summary>
    private IReadOnlyList<(int Index, double Score)> LocalRank(
        string query, IReadOnlyList<string> documents, CancellationToken ct)
    {
        var scores = _local!.CallAsync(new ScoreRequest(query, documents), ct)
            .GetAwaiter().GetResult().Scores;

        var budget = _local.MaxTokens - 3;                        // [CLS] q [SEP] d [SEP]
        var asked = _tokenizer!.EncodeToIds(query).Count;
        var scored = new List<(int, double)>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            if (_tokenizer.EncodeToIds(documents[i]).Count > budget - asked) Interlocked.Increment(ref _truncated);
            _values.TryAdd(Math.Round(scores[i], 6), 0);
            scored.Add((i, scores[i]));
        }

        Interlocked.Increment(ref _calls);
        Interlocked.Add(ref _scored, scored.Count);
        return scored;
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
