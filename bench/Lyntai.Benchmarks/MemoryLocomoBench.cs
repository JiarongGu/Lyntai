using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>LoCoMo, the benchmark this repository publishes no number against.</b> Every recall figure here is
/// measured on a synthetic corpus that calls itself "a comparison instrument, not a claim about your data",
/// so nothing this library reports can be ranked against Mem0, Zep or Letta in EITHER direction.
/// <para><b>What it does and does not buy, stated first.</b> The published LoCoMo scores are produced with a
/// frontier reader model; this runs whatever <c>SweepDoubles.ChatModel</c> resolves locally. An absolute
/// score from here is therefore NOT comparable to a published one, and the run says so in its own output.
/// What IS comparable is the arm difference: every arm answers the same questions with the same reader, so
/// the only thing varying is the memory layer.</para>
/// <para><b>The ablation is the point.</b> <c>vector</c> is the same embedder and the same k with no graph,
/// no decay, no salience and no ranking policy — so <c>lyntai</c> minus <c>vector</c> is what this library
/// adds over plain similarity search. <c>full</c> puts the whole conversation in the prompt and is the
/// no-memory-needed ceiling. See <c>docs/memory.md</c> §5.</para>
/// </summary>
internal static class MemoryLocomoBench
{
    internal const string DataVariable = "LYNTAI_LOCOMO_PATH";

    /// <summary>Categories 1–4 are the 1540 questions the literature scores. 5 is LoCoMo's adversarial
    /// class and is excluded by the published protocol, so it is excluded here rather than quietly folded
    /// in — including it would make this number incomparable to the field's own for a second reason.</summary>
    private static readonly int[] ScoredCategories = [1, 2, 3, 4];

    private const int RecallLimit = 20;
    private const int Seed = 12345;

    private static readonly string[] CategoryNames =
        ["", "multi-hop", "temporal", "open-domain", "single-hop", "adversarial(excluded)"];

    private sealed record Question(string ConvId, string Text, string Gold, int Category,
        IReadOnlyList<string> Evidence);

    private sealed record Turn(string Speaker, string Text, string Date, string DiaId);

    public static async Task<int> RunAsync(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(DataVariable)
            ?? Path.Combine("devtools", "_bench-data", "locomo10.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-locomo: dataset not found at '{path}'.");
            Console.Error.WriteLine("  Download it (2.8 MB, MIT) and re-run:");
            Console.Error.WriteLine("    curl -sSL -o devtools/_bench-data/locomo10.json \\");
            Console.Error.WriteLine("      https://raw.githubusercontent.com/snap-research/locomo/main/data/locomo10.json");
            Console.Error.WriteLine($"  Or point {DataVariable} at a copy.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-locomo");
        if (embedder is null) return 1;
        // The retrieval diagnostic needs no reader at all, so it does not demand one — a machine with an
        // embedder but no chat model can still run the half that measures the memory layer.
        var needsReader = !args.Contains("--retrieval");
        var chat = needsReader ? await SweepDoubles.TryRealChatAsync(http, "memory-locomo") : null;
        if (needsReader && chat is null) return 1;

        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : 200;
        var wantFull = args.Contains("--full");
        // `--retrieval` is the MODEL-FREE diagnostic: does the recalled set contain the evidence turn
        // LoCoMo names? It separates a retrieval failure from a reading failure, which an accuracy table
        // conflates — and it does so with no reader and no judge, so neither can be blamed or credited.
        var retrievalOnly = args.Contains("--retrieval");
        var dump = args.Contains("--dump");
        string[] arms = wantFull
            ? ["lyntai", "lyntai+sem", "lyntai+rel", "vector", "full"]
            : ["lyntai", "lyntai+sem", "lyntai+rel", "vector"];

        var (conversations, questions) = Load(path);
        var sampled = Stratify(questions, take);

        PrintPreamble(chat?.Model ?? "(none - retrieval only)", embedder,
            conversations.Count, questions.Count, sampled.Count, arms, retrievalOnly);

        var stopwatch = Stopwatch.StartNew();
        var correct = new Dictionary<(string Arm, int Category), int>();
        var asked = new Dictionary<(string Arm, int Category), int>();
        var unknown = new Dictionary<string, int>();
        var returned = new Dictionary<string, int>();

        foreach (var (convId, turns) in conversations)
        {
            var mine = sampled.Where(q => q.ConvId == convId).ToList();
            if (mine.Count == 0) continue;

            // ONE PRISTINE STORE PER ARM, and this is a correctness requirement rather than tidiness.
            // A recall REINFORCES what it returns, so three arms sharing a store would each mutate the decay
            // state the next one reads. That is not hypothetical: it moved this table's own numbers between
            // runs (lyntai 10.0% -> 5.5%) when a fourth arm was added, with the seed and the data unchanged.
            // Turning reinforcement off would isolate them more cheaply, but it also measures a
            // configuration nobody ships and whose own doc calls it the worst arm for recall quality - so
            // the cost of a fresh ingestion per arm is paid instead. Embeddings are cached across arms, so
            // what is actually repeated is the SQLite and graph work.
            var configs = new (string Arm, GraphMemoryOptions? Options, IMemoryRankingPolicy? Ranking)[]
            {
                ("lyntai", null, null),
                ("lyntai+sem", new GraphMemoryOptions { SemanticSeedK = RecallLimit }, null),
                ("lyntai+rel", new GraphMemoryOptions { SemanticSeedK = RecallLimit },
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { RetrievabilityWeight = 0 })),
            };

            // The dia_id rides along in the CONTENT so an evidence hit is checkable without a model. It is
            // in the text every arm reads, so none is advantaged.
            var texts = turns.Select(t => $"[{t.Date}] ({t.DiaId}) {t.Speaker}: {t.Text}").ToList();
            var vectorIndex = new List<(string Text, float[] Vector)>();
            foreach (var text in texts) vectorIndex.Add((text, await embedder.EmbedAsync(text)));

            var recalled = new Dictionary<(string Arm, string Question), List<string>>();
            foreach (var (arm, options, ranking) in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var store = new SqliteMemoryGraphStore(db.Factory);
                var vectors = new InMemoryVectorStore();
                var engine = new GraphMemoryEngine("locomo", store, options: options,
                    embedder: embedder, vectors: vectors, ranking: ranking);

                foreach (var text in texts)
                    await engine.RememberAsync(new MemoryWrite(convId, "session", text));

                foreach (var q in mine)
                    recalled[(arm, q.Text)] = (await engine.RecallAsync(
                        new MemoryQuery(convId, "session", q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList();
            }

            if (retrievalOnly)
            {
                foreach (var q in mine.Where(q => q.Evidence.Count > 0))
                {
                    var sets = configs.Select(c => (c.Arm, Got: recalled[(c.Arm, q.Text)]))
                        .Append(("vector", (await TopKAsync(embedder, vectorIndex, q.Text, RecallLimit)).ToList()));

                    foreach (var (arm, got) in sets)
                    {
                        var key = (arm, q.Category);
                        asked[key] = asked.GetValueOrDefault(key) + 1;
                        returned[arm] = returned.GetValueOrDefault(arm) + got.Count;
                        if (q.Evidence.Any(e => got.Any(g => g.Contains($"({e})", StringComparison.Ordinal))))
                            correct[key] = correct.GetValueOrDefault(key) + 1;
                    }
                }
                Console.WriteLine($"  {convId}: {turns.Count} turns x {configs.Length} arm(s) ingested, "
                    + $"{mine.Count(q => q.Evidence.Count > 0)} question(s) probed");
                continue;
            }

            foreach (var q in mine)
            {
                foreach (var arm in arms)
                {
                    // Every `lyntai*` arm reads the set its own pristine-store engine already produced
                    // above, so the QA table and the retrieval table are scoring the SAME recalls rather
                    // than two independently-drawn ones.
                    var context = arm switch
                    {
                        "vector" => string.Join("\n", await TopKAsync(embedder, vectorIndex, q.Text, RecallLimit)),
                        "full" => string.Join("\n", texts),
                        _ => string.Join("\n", recalled[(arm, q.Text)]),
                    };

                    var hypothesis = (await chat!.AskAsync(AnswerPrompt(context, q.Text), maxTokens: 48))?.Trim() ?? "";
                    var key = (arm, q.Category);
                    asked[key] = asked.GetValueOrDefault(key) + 1;
                    if (hypothesis.Length == 0 || hypothesis.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
                        unknown[arm] = unknown.GetValueOrDefault(arm) + 1;
                    else if (await JudgeAsync(chat, q, hypothesis))
                        correct[key] = correct.GetValueOrDefault(key) + 1;
                }
            }

            Console.WriteLine($"  {convId}: {turns.Count} turns ingested, {mine.Count} question(s) asked");
        }

        PrintResults(arms, correct, asked, unknown, returned, retrievalOnly);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>Cosine top-k over the SAME embedder — the naive-RAG arm, with no graph, decay, salience or
    /// ranking policy in the path. It is the ablation the arm table rests on, so it deliberately shares the
    /// embedder instance rather than building a second one.</summary>
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

    private static string AnswerPrompt(string context, string question) =>
        $"""
         Answer the question using ONLY the conversation excerpts below.
         Reply with the answer alone - a few words, no sentence, no explanation.
         If the excerpts do not contain the answer, reply exactly: unknown

         Excerpts:
         {context}

         Question: {question}
         Answer:
         """;

    /// <summary>Grades a hypothesis against the gold answer with the SAME model that answered. That is a
    /// known weakness and is disclosed in the preamble rather than hidden: a self-grading reader can be
    /// generous to its own phrasing. It is used because the alternative on this machine is no grader at all.
    /// </summary>
    private static async Task<bool> JudgeAsync(SweepDoubles.OpenAiCompatibleChat chat, Question q, string hypothesis)
    {
        var verdict = await chat.AskAsync(
            $"""
             Grade a short answer. Reply with exactly one word: YES or NO.

             Question: {q.Text}
             Reference answer: {q.Gold}
             Candidate answer: {hypothesis}

             Does the candidate state the same fact as the reference? YES or NO:
             """, maxTokens: 3);
        return verdict?.TrimStart().StartsWith("YES", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static (List<(string Id, List<Turn> Turns)>, List<Question>) Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var conversations = new List<(string, List<Turn>)>();
        var questions = new List<Question>();

        foreach (var sample in doc.RootElement.EnumerateArray())
        {
            var id = sample.GetProperty("sample_id").GetString()!;
            var conv = sample.GetProperty("conversation");
            var turns = new List<Turn>();

            foreach (var prop in conv.EnumerateObject())
            {
                if (!prop.Name.StartsWith("session_", StringComparison.Ordinal)
                    || prop.Name.EndsWith("date_time", StringComparison.Ordinal)
                    || prop.Value.ValueKind != JsonValueKind.Array) continue;

                var date = conv.TryGetProperty($"{prop.Name}_date_time", out var d) ? d.GetString() ?? "" : "";
                foreach (var t in prop.Value.EnumerateArray())
                    turns.Add(new Turn(t.GetProperty("speaker").GetString() ?? "",
                        t.GetProperty("text").GetString() ?? "", date,
                        t.TryGetProperty("dia_id", out var did) ? did.GetString() ?? "" : ""));
            }
            conversations.Add((id, turns));

            foreach (var qa in sample.GetProperty("qa").EnumerateArray())
            {
                var category = qa.GetProperty("category").GetInt32();
                if (!ScoredCategories.Contains(category)) continue;
                if (!qa.TryGetProperty("answer", out var gold)) continue;
                var evidence = qa.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array
                    ? ev.EnumerateArray().Select(e => e.ToString()).ToList()
                    : [];
                questions.Add(new Question(id, qa.GetProperty("question").GetString() ?? "",
                    gold.ToString(), category, evidence));
            }
        }
        return (conversations, questions);
    }

    /// <summary>A seeded, category-STRATIFIED sample. Proportional rather than equal-per-category, so the
    /// mix matches the benchmark's own and the overall number means the same thing a full run's would.</summary>
    private static List<Question> Stratify(List<Question> all, int take)
    {
        if (take >= all.Count) return all;
        var rng = new Random(Seed);
        var picked = new List<Question>();
        foreach (var group in all.GroupBy(q => q.Category).OrderBy(g => g.Key))
        {
            var want = (int)Math.Round(take * (double)group.Count() / all.Count);
            picked.AddRange(group.OrderBy(_ => rng.Next()).Take(Math.Max(1, want)));
        }
        return picked;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void PrintPreamble(string model, SweepDoubles.CachingEmbedder embedder,
        int conversations, int total, int sampled, IReadOnlyList<string> arms, bool retrievalOnly)
    {
        if (retrievalOnly)
        {
            Console.WriteLine("=== LoCoMo: evidence-hit@k, the MODEL-FREE half ===");
            Console.WriteLine();
            Console.WriteLine("Does the recalled set contain the evidence turn LoCoMo names for the question?");
            Console.WriteLine("No reader and no judge, so a difference here is the MEMORY LAYER and nothing");
            Console.WriteLine("else - which is what separates a retrieval failure from a reading failure.");
            Console.WriteLine();
            Console.WriteLine($"Conversations: {conversations}   Questions: {sampled} of {total}, seeded {Seed}");
            Console.WriteLine($"Arms: {string.Join(", ", arms)}   k = {RecallLimit}   embedder {SweepDoubles.Model}");
            Console.WriteLine();
            return;
        }

        Console.WriteLine("=== LoCoMo: the benchmark this repository had no number against ===");
        Console.WriteLine();
        Console.WriteLine("*** AN ABSOLUTE SCORE HERE IS NOT COMPARABLE TO A PUBLISHED ONE. *** Published");
        Console.WriteLine($"LoCoMo results are produced with a frontier reader; this ran {model} locally, and");
        Console.WriteLine("the reader sets the ceiling far more than the memory layer does. What IS comparable");
        Console.WriteLine("is the ARM DIFFERENCE: every arm answers the same questions with the same reader.");
        Console.WriteLine();
        Console.WriteLine($"Conversations: {conversations}   Scored categories: 1-4 (the published protocol)");
        Console.WriteLine($"Questions: {sampled} sampled from {total}, seeded {Seed}, stratified by category");
        Console.WriteLine($"Arms: {string.Join(", ", arms)}   recall limit {RecallLimit}   embedder {SweepDoubles.Model}");
        Console.WriteLine();
        Console.WriteLine("The grader is the SAME model that answered, which can be generous to its own");
        Console.WriteLine("phrasing. Stated rather than hidden; a second grader is the obvious next control.");
        Console.WriteLine();
    }

    private static void PrintResults(IReadOnlyList<string> arms,
        Dictionary<(string, int), int> correct, Dictionary<(string, int), int> asked,
        Dictionary<string, int> unknown, Dictionary<string, int> returned, bool retrievalOnly)
    {
        Console.WriteLine();
        Console.WriteLine(retrievalOnly
            ? "=== evidence-hit@k by arm and category (no model in the loop) ==="
            : "=== Accuracy by arm and category (LLM-judged against the gold answer) ===");
        Console.Write($"{"Arm",-10}");
        foreach (var c in ScoredCategories) Console.Write($"  {CategoryNames[c],-14}");
        Console.WriteLine($"  {"overall",-10} {(retrievalOnly ? "items/q" : "unknown"),8}");
        Console.WriteLine(new string('-', 10 + (ScoredCategories.Length * 16) + 22));

        foreach (var arm in arms)
        {
            Console.Write($"{arm,-10}");
            int hit = 0, ask = 0;
            foreach (var c in ScoredCategories)
            {
                var a = asked.GetValueOrDefault((arm, c));
                var k = correct.GetValueOrDefault((arm, c));
                hit += k; ask += a;
                Console.Write($"  {(a == 0 ? "-" : $"{(double)k / a:P1} ({k}/{a})"),-14}");
            }
            var trailer = retrievalOnly
                ? (ask == 0 ? "-" : $"{(double)returned.GetValueOrDefault(arm) / ask:F1}")
                : unknown.GetValueOrDefault(arm).ToString();
            Console.Write($"  {(ask == 0 ? "-" : $"{(double)hit / ask:P1}"),-10} {trailer,8}");
            Console.WriteLine();
        }

        Console.WriteLine();
        if (retrievalOnly)
        {
            Console.WriteLine("  'items/q' is how many entries the arm actually RETURNED per question. An arm");
            Console.WriteLine("  well under k is not losing on ranking - it is being filtered before ranking,");
            Console.WriteLine("  which is a different defect and one this column exists to expose.");
        }
        else
        {
            Console.WriteLine("  'unknown' counts answers where the reader said the excerpts did not contain one.");
            Console.WriteLine("  A HIGH unknown count on an arm is a retrieval failure, not a reasoning failure -");
            Console.WriteLine("  which is the distinction this table exists to draw.");
        }
    }
}
