using System.Diagnostics;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>LongMemEval's knowledge-update class — the benchmark where forgetting is supposed to HELP.</b>
///
/// <para>LoCoMo distributes its questions uniformly over months of history, so it rewards a perfect archive
/// and penalises decay by construction (<c>docs/memory.md</c> §5). This is the opposite shape. A
/// knowledge-update question carries exactly TWO dated sessions: an earlier one stating a fact and a later
/// one REVISING it. The turns that carry each are flagged, so the question this asks is not "can you find
/// it" but <b>"do you prefer the CURRENT value over the superseded one"</b> — which is the claim a decay
/// model actually makes, and one a flat cosine index has no mechanism to make at all.</para>
///
/// <para><b>The headline metric is preference, not recall.</b> Both facts are textually similar and both sit
/// in the store; an archive returns whichever the embedder likes. Scoring "did you retrieve the answer"
/// would hide that. So the arms are scored on whether the CURRENT turn outranks the STALE one, with plain
/// hit-rates beside it so a preference win cannot be manufactured by retrieving neither.</para>
///
/// <para>Model-free throughout, for the reason the LoCoMo harness gives: no reader and no judge, so neither
/// can be credited or blamed.</para>
/// </summary>
internal static class MemoryLongMemEvalBench
{
    internal const string DataVariable = "LYNTAI_LME_PATH";
    private const int RecallLimit = 10;
    private const string Task = "lme";
    private const string Scope = "session";

    private sealed record Turn(int Session, int Index, string Role, string Content, bool HasAnswer)
    {
        /// <summary>The marker rides in the CONTENT so a hit is checkable with no model, exactly as the
        /// LoCoMo harness carries a dialogue id. Every arm reads the same text, so none is advantaged.</summary>
        public string Tag => $"(s{Session}t{Index})";

        public string Text => $"[{Role}] {Content}";
    }

    private sealed record Question(string Id, string Text, string Type, IReadOnlyList<Turn> Turns,
        IReadOnlyList<Turn> Current, IReadOnlyList<Turn> Stale, IReadOnlyList<Turn> Evidence);

    public static async Task<int> RunAsync(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(DataVariable)
            ?? Path.Combine("devtools", "_bench-data", "lme_oracle.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-longmemeval: dataset not found at '{path}'.");
            Console.Error.WriteLine("  Download it and re-run:");
            Console.Error.WriteLine("    curl -sSL -o devtools/_bench-data/lme_oracle.json \\");
            Console.Error.WriteLine("      https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned/"
                + "resolve/main/longmemeval_oracle.json");
            Console.Error.WriteLine($"  Or point {DataVariable} at a copy.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-longmemeval");
        if (embedder is null) return 1;

        // `--temporal` is the COUNTER-TEST to the knowledge-update class, not more of the same. Temporal
        // reasoning asks things like "what was the FIRST issue after the service", so the answer is often
        // the EARLIER fact and preferring the current one would be wrong. It needs BOTH facts present, which
        // is exactly what the suppression that wins knowledge-update should cost. Different question,
        // different metric: all-evidence recall rather than preference.
        var temporal = args.Contains("--temporal");
        var questions = Load(path, temporal ? "temporal-reasoning" : "knowledge-update", temporal);
        if (questions.Count == 0)
        {
            Console.Error.WriteLine("memory-longmemeval: no question survived loading — a knowledge-update "
                + "needs two dated sessions with flagged answer turns; a temporal one needs any.");
            return 1;
        }

        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : questions.Count;
        var sampled = questions.Take(take).ToList();

        if (temporal)
        {
            Console.WriteLine("=== LongMemEval temporal-reasoning: the COST side of the same bet ===");
            Console.WriteLine();
            Console.WriteLine("Knowledge-update rewards suppressing a superseded fact. This class does not:");
            Console.WriteLine("'what was the FIRST issue after the service' wants the EARLIER fact, and most");
            Console.WriteLine("questions need BOTH. So the metric is all-evidence recall, and the suppression");
            Console.WriteLine("that won the other class is expected to COST here. That is the trade, measured.");
            Console.WriteLine();
            Console.WriteLine($"Questions: {sampled.Count} temporal-reasoning   k = {RecallLimit}   "
                + $"embedder {SweepDoubles.Model}   model-free");
            Console.WriteLine();
            await RunTemporalAsync(sampled, embedder);
            return 0;
        }

        Console.WriteLine("=== LongMemEval knowledge-update: does the memory PREFER the current fact? ===");
        Console.WriteLine();
        Console.WriteLine("Each question carries an earlier session stating a fact and a later one REVISING");
        Console.WriteLine("it. Both sit in the store and both are textually similar, so retrieving 'the");
        Console.WriteLine("answer' is not the test - preferring the CURRENT one over the superseded one is.");
        Console.WriteLine("That is the claim a decay model makes and a flat index cannot.");
        Console.WriteLine();
        Console.WriteLine($"Questions: {sampled.Count} knowledge-update   k = {RecallLimit}   "
            + $"embedder {SweepDoubles.Model}   model-free");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();
        string[] arms = ["lyntai", "vector"];
        var currentHit = new Dictionary<string, int>();
        var staleHit = new Dictionary<string, int>();
        var prefers = new Dictionary<string, int>();
        var decidable = new Dictionary<string, int>();

        foreach (var q in sampled)
        {
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store, embedder: embedder,
                vectors: new InMemoryVectorStore());

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await embedder.EmbedAsync(content)));
            }

            var lyntai = (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                .Items.Select(i => i.Content ?? i.Headline).ToList();
            var vector = (await TopKAsync(embedder, index, q.Text, RecallLimit)).ToList();

            foreach (var (arm, got) in new[] { ("lyntai", lyntai), ("vector", vector) })
            {
                var cur = FirstIndexOf(got, q.Current);
                var sta = FirstIndexOf(got, q.Stale);
                if (cur >= 0) currentHit[arm] = currentHit.GetValueOrDefault(arm) + 1;
                if (sta >= 0) staleHit[arm] = staleHit.GetValueOrDefault(arm) + 1;

                // DECIDABLE means the arm returned at least one of the two, so a preference exists to score.
                // Without this an arm that retrieves NEITHER would score a vacuous 100%.
                if (cur < 0 && sta < 0) continue;
                decidable[arm] = decidable.GetValueOrDefault(arm) + 1;
                if (cur >= 0 && (sta < 0 || cur < sta)) prefers[arm] = prefers.GetValueOrDefault(arm) + 1;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"prefers current",18} {"current@k",12} {"stale@k",12} {"decidable",11}");
        Console.WriteLine(new string('-', 66));
        foreach (var arm in arms)
        {
            var d = decidable.GetValueOrDefault(arm);
            var p = prefers.GetValueOrDefault(arm);
            Console.WriteLine($"{arm,-10} {(d == 0 ? "-" : $"{(double)p / d:P1} ({p}/{d})"),18} "
                + $"{$"{(double)currentHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} "
                + $"{$"{(double)staleHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} {d,11}");
        }

        Console.WriteLine();
        Console.WriteLine("  'prefers current' is scored only over questions where the arm returned at least");
        Console.WriteLine("  one of the two facts — otherwise retrieving NEITHER would score a vacuous 100%.");
        Console.WriteLine("  'stale@k' is not a failure on its own: returning both is fine if the current one");
        Console.WriteLine("  ranks first. It is here so a preference win cannot hide a recall collapse.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>All-evidence recall. A temporal question usually needs EVERY flagged turn — "the first issue
    /// after the service" is unanswerable from the later fact alone — so partial recall is a miss, and the
    /// any-evidence column beside it shows how much of the gap is partial rather than total.</summary>
    private static async Task RunTemporalAsync(List<Question> sampled, SweepDoubles.CachingEmbedder embedder)
    {
        var stopwatch = Stopwatch.StartNew();
        string[] arms = ["lyntai", "vector"];
        var all = new Dictionary<string, int>();
        var any = new Dictionary<string, int>();
        var evidenceTotal = 0;
        var evidenceFound = new Dictionary<string, int>();

        foreach (var q in sampled)
        {
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store, embedder: embedder,
                vectors: new InMemoryVectorStore());

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await embedder.EmbedAsync(content)));
            }

            var lyntai = (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                .Items.Select(i => i.Content ?? i.Headline).ToList();
            var vector = (await TopKAsync(embedder, index, q.Text, RecallLimit)).ToList();
            evidenceTotal += q.Evidence.Count;

            foreach (var (arm, got) in new[] { ("lyntai", lyntai), ("vector", vector) })
            {
                var hits = q.Evidence.Count(t => got.Any(g => g.Contains(t.Tag, StringComparison.Ordinal)));
                evidenceFound[arm] = evidenceFound.GetValueOrDefault(arm) + hits;
                if (hits == q.Evidence.Count) all[arm] = all.GetValueOrDefault(arm) + 1;
                if (hits > 0) any[arm] = any.GetValueOrDefault(arm) + 1;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"all evidence@k",16} {"any evidence@k",16} {"evidence turns",16}");
        Console.WriteLine(new string('-', 62));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-10} "
                + $"{$"{(double)all.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)any.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)evidenceFound.GetValueOrDefault(arm) / evidenceTotal:P1}",16}");

        Console.WriteLine();
        Console.WriteLine("  'all evidence@k' is the one that matters: a temporal question usually needs every");
        Console.WriteLine("  flagged turn, so retrieving one of two answers nothing. 'evidence turns' is the");
        Console.WriteLine("  per-turn rate, which separates 'missed one question badly' from 'missed a little");
        Console.WriteLine("  everywhere'.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
    }

    private static int FirstIndexOf(List<string> got, IReadOnlyList<Turn> wanted)
    {
        for (var i = 0; i < got.Count; i++)
            if (wanted.Any(t => got[i].Contains(t.Tag, StringComparison.Ordinal)))
                return i;
        return -1;
    }

    private static async Task<IEnumerable<string>> TopKAsync(
        IEmbedder embedder, List<(string Text, float[] Vector)> index, string query, int k)
    {
        var q = await embedder.EmbedAsync(query);
        return index.Select(e => (e.Text, Score: Cosine(q, e.Vector)))
            .OrderByDescending(e => e.Score).Take(k).Select(e => e.Text);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Knowledge-update questions only, and only those whose two sessions BOTH carry a flagged
    /// answer turn — the discriminating shape. The later session by date holds the current value.</summary>
    private static List<Question> Load(string path, string wantType, bool temporal)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<Question>();

        foreach (var q in doc.RootElement.EnumerateArray())
        {
            if (q.GetProperty("question_type").GetString() != wantType) continue;

            var sessions = q.GetProperty("haystack_sessions");
            var dates = q.GetProperty("haystack_dates").EnumerateArray().Select(d => d.GetString() ?? "").ToList();
            if (sessions.GetArrayLength() < (temporal ? 1 : 2)) continue;

            // Order by the session's own date string. LongMemEval's format sorts lexicographically because
            // it is yyyy/MM/dd — which is why this needs no date parsing and no culture.
            var ordered = sessions.EnumerateArray()
                .Select((s, i) => (Session: s, Date: i < dates.Count ? dates[i] : "", Index: i))
                .OrderBy(x => x.Date, StringComparer.Ordinal)
                .ToList();

            var turns = new List<Turn>();
            foreach (var (session, _, si) in ordered)
                turns.AddRange(session.EnumerateArray().Select((t, ti) => new Turn(si, ti,
                    t.GetProperty("role").GetString() ?? "",
                    t.GetProperty("content").GetString() ?? "",
                    t.TryGetProperty("has_answer", out var h) && h.ValueKind == JsonValueKind.True)));

            var lastSession = ordered[^1].Index;
            var current = turns.Where(t => t.HasAnswer && t.Session == lastSession).ToList();
            var stale = turns.Where(t => t.HasAnswer && t.Session != lastSession).ToList();
            var evidence = turns.Where(t => t.HasAnswer).ToList();
            if (temporal ? evidence.Count == 0 : current.Count == 0 || stale.Count == 0) continue;

            result.Add(new Question(q.GetProperty("question_id").GetString() ?? "",
                q.GetProperty("question").GetString() ?? "", wantType, turns, current, stale, evidence));
        }
        return result;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
