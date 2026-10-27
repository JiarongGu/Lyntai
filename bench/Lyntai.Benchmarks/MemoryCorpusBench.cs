using System.Diagnostics;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// Recall over a REAL record set far larger than this repository's own: numbered decisions, a fix log and a task
/// archive, read from a directory that stays on the machine (<c>--dir</c>, or <c>LYNTAI_CORPUS_DIR</c>, default
/// <c>devtools/_corpus</c>, gitignored). <b>Nothing here carries the records' text</b>: the questions are built
/// from the records' structure at run time and the output is numbers.
///
/// <para><b>What a question is.</b> Each is made from one part of a record and answered by another, with the
/// quoted part held out of the index — a fix's symptom against its root cause, a decision's title against the
/// section naming what it rejected, an amendment's one-line claim against the paragraph that makes it, an archived
/// task's title against its body. <see cref="QuestionKind"/> lists the six. That is the "answer in other words"
/// shape without a model writing paraphrases: the query and the answer are different text by construction, and
/// the lexical overlap between them is measured per question so recall can be read against it.</para>
///
/// <para><b>What is measured per engine.</b> Ingestion time, bytes on disk, recall latency, and per question
/// kind hit rate at 1/5/10, reciprocal rank, precision at 10 and recall at 10. Then the two cuts that explain a
/// miss: by lexical overlap of the question with its answer, and by the answer's position in the ingest order
/// (a decaying engine buries what is old). The amendment kinds report whether the amending text or the text it
/// replaced came first.</para>
///
/// <para>Every recall is paired against the engine's own state: a recall on shipped defaults reinforces what it
/// returns, so <c>graph</c> and <c>graph-ro</c> (reinforcement, co-activation and the review log off) are both
/// run, and the questions are asked in one fixed shuffled order.</para>
/// </summary>
internal static class MemoryCorpusBench
{
    private const string Task = "corpus";
    private const string Scope = "records";
    private const int Limit = 10;

    private sealed record Arm(string Name, bool Embeds, Func<ArmEnv, IMemoryEngine> Build);

    private sealed class ArmEnv(MemoryPolicySweep.SweepDb db, SweepDoubles.CachingVectorProvider? vectorProvider)
    {
        public MemoryPolicySweep.SweepDb Db { get; } = db;

        public SweepDoubles.CachingVectorProvider VectorProvider => vectorProvider!;
    }

    private sealed record Result(int Question, QuestionKind Kind, QuestionOutcome Outcome, double LatencyMs, bool Empty,
        double Overlap, int GoldRecords, int Quintile);

    private sealed record ArmSummary(string Arm, string Tier, int Writes, double IngestSeconds, long Bytes,
        double RecallP50, double RecallP95, double EmptyRate, IReadOnlyList<Result> Results);

    public static async Task<int> RunAsync(string[] args)
    {
        var dir = Value(args, "--dir") ?? Environment.GetEnvironmentVariable("LYNTAI_CORPUS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_corpus"));
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"memory-corpus: no corpus directory at '{dir}'.");
            Console.Error.WriteLine("  It holds decisions/D*.md, FIX-LOG.md and task-archive.md (see RecordParser for the shapes).");
            Console.Error.WriteLine("  Point --dir or LYNTAI_CORPUS_DIR at one; the text stays out of the tree.");
            return 1;
        }

        var perKind = int.Parse(Value(args, "--per-kind") ?? "150");
        var maxChunks = int.Parse(Value(args, "--chunks") ?? int.MaxValue.ToString());
        var records = Load(dir);
        var chunks = RecordChunker.Chunk(records).Take(maxChunks).ToList();
        var present = chunks.Select(c => c.RecordKey).ToHashSet();
        var questions = QuestionBuilder.Build([.. records.Where(r => present.Contains(r.Key))], perKind, seed: 1);
        var byKey = records.ToDictionary(r => r.Key);
        PrintCorpus(records, chunks, questions);
        if (args.Contains("--inspect")) return 0;

        var texts = new Dictionary<string, List<string>>();
        foreach (var g in chunks.GroupBy(c => c.RecordKey)) texts[g.Key] = [.. g.Select(c => c.Text)];
        var overlap = questions.ToDictionary(q => q.Id, q => RecallScoring.Overlap(q.Text,
            q.Gold.Where(g => g.Role == q.Primary).Select(g => g.RecordKey).Distinct().SelectMany(k => texts.GetValueOrDefault(k) ?? [])));
        var firstChunk = new Dictionary<string, int>();
        for (var i = 0; i < chunks.Count; i++) firstChunk.TryAdd(chunks[i].RecordKey, i);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        SweepDoubles.CachingVectorProvider? embedder = null;
        var tier = "none";
        if (!args.Contains("--model-free"))
        {
            embedder = await SweepDoubles.TryRealVectorProviderAsync(http, "memory-corpus");
            if (embedder is not null) tier = $"{SweepDoubles.ServedOrRequestedModel} @ {SweepDoubles.BaseUrl}";
        }

        var embedNote = "";
        if (embedder is not null) embedNote = await WarmEmbeddingsAsync(embedder, chunks, questions);

        var arms = Arms(embedder is not null).Where(a => Selected(args, a.Name)).ToList();
        var summaries = new List<ArmSummary>();
        foreach (var arm in arms)
        {
            Console.WriteLine($"-- {arm.Name}: ingesting {chunks.Count} chunk(s)…");
            summaries.Add(await RunArmAsync(arm, embedder, chunks, questions, overlap, firstChunk, tier));
        }

        PrintResults(summaries, questions, byKey, tier, embedNote);
        WriteJson(summaries, tier, chunks.Count);
        return 0;
    }

    private static List<CorpusRecord> Load(string dir)
    {
        var records = new List<CorpusRecord>();
        var decisions = Path.Combine(dir, "decisions");
        if (Directory.Exists(decisions))
            foreach (var file in Directory.GetFiles(decisions, "D*.md"))
                if (RecordParser.Decision(File.ReadAllText(file)) is { } d) records.Add(d);

        if (File.Exists(Path.Combine(dir, "FIX-LOG.md")))
            records.AddRange(RecordParser.Fixes(File.ReadAllText(Path.Combine(dir, "FIX-LOG.md"))));
        if (File.Exists(Path.Combine(dir, "task-archive.md")))
            records.AddRange(RecordParser.Archive(File.ReadAllText(Path.Combine(dir, "task-archive.md"))));
        return records;
    }

    private static IEnumerable<Arm> Arms(bool embedder)
    {
        // The keyword store caps a scope at 500 entries by DEFAULT (FIFO), which is documented and is the first
        // thing a large corpus meets; the uncapped arm is the store as a corpus-sized caller must configure it.
        yield return new Arm("lexical-default-cap", false, e => new LexicalMemoryEngine(Task,
            new SqliteMemoryStore(e.Db.Factory, new LyntaiOptions())));
        yield return new Arm("lexical", false, e => new LexicalMemoryEngine(Task,
            new SqliteMemoryStore(e.Db.Factory, new LyntaiOptions { MemoryEviction = MemoryEvictionPolicy.CountCap(int.MaxValue) })));
        yield return new Arm("graph", false, e => new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(e.Db.Factory)));
        yield return new Arm("graph-ro", false, e => new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(e.Db.Factory),
            options: ReadOnly));
        if (!embedder) yield break;

        yield return new Arm("semantic", true, e => new SemanticMemoryEngine(Task,
            new SemanticMemory([e.VectorProvider], new InMemoryVectorStore()), Limit));
        yield return new Arm("semantic-sqlite", true, e => new SemanticMemoryEngine(Task,
            new SemanticMemory([e.VectorProvider], new SqliteVectorStore(e.Db.Factory)), Limit));
        yield return new Arm("graph-ro+seed20", true, e => Seeded(e, ReadOnly, 20));
        yield return new Arm("graph-ro+seed20-nolink", true, e => Seeded(e, ReadOnly with { MinSimilarity = 1.01 }, 20));
        yield return new Arm("graph+seed20", true, e => Seeded(e, new GraphMemoryOptions(), 20));
    }

    private static GraphMemoryOptions ReadOnly => new()
    {
        ReinforceOn = MemoryReinforcementActs.None,
        CoActivationCap = 0,
        LogReviews = false,
    };

    private static GraphMemoryEngine Seeded(ArmEnv e, GraphMemoryOptions options, int k)
    {
        var vectors = new InMemoryVectorStore();
        return new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(e.Db.Factory), options: options, seams: new GraphMemorySeams
        {
            Providers = [e.VectorProvider],
            Vectors = vectors,
            SeedSources = [new LexicalSeedSource(), new SubjectSeedSource(),
                new SemanticSeedSource([e.VectorProvider], vectors, new SemanticSeedOptions { K = k })],
        });
    }

    private static bool Selected(string[] args, string name) =>
        Value(args, "--arms") is not { } list || list.Split(',').Contains(name);

    private static async Task<string> WarmEmbeddingsAsync(SweepDoubles.CachingVectorProvider embedder,
        IReadOnlyList<CorpusChunk> chunks, IReadOnlyList<CorpusQuestion> questions)
    {
        var sw = Stopwatch.StartNew();
        var dim = (await embedder.EmbedAsync(["probe"]))[0].Length;
        await Parallel.ForEachAsync(chunks.Select(c => c.Text).Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (text, ct) => await embedder.EmbedAsync([text], ct));
        var corpusSeconds = sw.Elapsed.TotalSeconds;

        var each = new List<double>();
        foreach (var q in questions)
        {
            var t = Stopwatch.StartNew();
            await embedder.EmbedAsync([q.Text]);
            each.Add(t.Elapsed.TotalMilliseconds);
        }

        each.Sort();
        return $"{dim}-dim vectors; {chunks.Select(c => c.Text).Distinct().Count()} chunk(s) embedded in {corpusSeconds:F1}s "
            + $"(4 concurrent); a query embeds in p50 {Pct(each, 0.5):F1} ms / p95 {Pct(each, 0.95):F1} ms";
    }

    private static async Task<ArmSummary> RunArmAsync(Arm arm, SweepDoubles.CachingVectorProvider? embedder,
        IReadOnlyList<CorpusChunk> chunks, IReadOnlyList<CorpusQuestion> questions, Dictionary<int, double> overlap,
        Dictionary<string, int> firstChunk, string tier)
    {
        using var db = new MemoryPolicySweep.SweepDb();
        var engine = arm.Build(new ArmEnv(db, embedder));
        var chunkOf = new Dictionary<string, int>(StringComparer.Ordinal);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < chunks.Count; i++)
        {
            var written = await engine.RememberAsync(new MemoryWrite(Task, Scope, chunks[i].Text));
            chunkOf.TryAdd(written.Reference.Id, i);
            if ((i + 1) % 2000 == 0) Console.WriteLine($"   {i + 1}/{chunks.Count} in {sw.Elapsed.TotalSeconds:F0}s");
        }

        var ingest = sw.Elapsed.TotalSeconds;
        var results = new List<Result>(questions.Count);
        var latencies = new List<double>(questions.Count);
        foreach (var q in questions)
        {
            sw.Restart();
            var recall = await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit));
            var ms = sw.Elapsed.TotalMilliseconds;
            latencies.Add(ms);
            var ranked = recall.Items.Where(i => chunkOf.ContainsKey(i.Reference.Id))
                .Select(i => chunks[chunkOf[i.Reference.Id]]).ToList();
            var gold = q.Gold.Where(g => g.Role == q.Primary).Select(g => g.RecordKey).Distinct().ToList();
            var quintile = gold.Where(firstChunk.ContainsKey).Select(g => firstChunk[g] * 5 / Math.Max(1, chunks.Count)).DefaultIfEmpty(0).Min();
            results.Add(new Result(q.Id, q.Kind, RecallScoring.Score(q, ranked), ms, recall.Items.Count == 0,
                overlap[q.Id], gold.Count, quintile));
        }

        latencies.Sort();
        return new ArmSummary(arm.Name, arm.Embeds ? tier : "none", chunks.Count, ingest, StorageBytes(db), Pct(latencies, 0.5),
            Pct(latencies, 0.95), results.Count(r => r.Empty) / (double)results.Count, results);
    }

    private static long StorageBytes(MemoryPolicySweep.SweepDb db)
    {
        db.Checkpoint();
        return new[] { db.DbPath, db.DbPath + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    }

    // ---- reporting -------------------------------------------------------------------------------------------

    private static void PrintCorpus(IReadOnlyList<CorpusRecord> records, IReadOnlyList<CorpusChunk> chunks,
        IReadOnlyList<CorpusQuestion> questions)
    {
        Console.WriteLine("memory-corpus — recall over a real record set (numbers only; the text stays on the machine)\n");
        foreach (var kind in Enum.GetValues<RecordKind>())
        {
            var set = records.Where(r => r.Kind == kind).ToList();
            var words = set.Sum(r => r.Sections.Sum(s => Words(s.Body) + Words(s.Tag)));
            Console.WriteLine($"  {kind,-9} {set.Count,5} record(s)  {words,9:N0} words");
        }

        var size = chunks.Select(c => c.Text.Length).OrderBy(x => x).ToList();
        Console.WriteLine($"  chunks    {chunks.Count,5}  chars p50 {size[size.Count / 2]} / max {size[^1]}; "
            + $"held out of the index: titles, fix symptoms, amendment headlines");
        Console.WriteLine("  questions " + string.Join("  ", questions.GroupBy(q => q.Kind).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()}")));
        Console.WriteLine();
    }

    private static void PrintResults(IReadOnlyList<ArmSummary> arms, IReadOnlyList<CorpusQuestion> questions,
        Dictionary<string, CorpusRecord> byKey, string tier, string embedNote)
    {
        Console.WriteLine($"Embedding tier: {tier}" + (embedNote.Length > 0 ? $"\n  {embedNote}" : "") + "\n");
        Console.WriteLine($"{"arm",-24}{"writes",7}{"ingest s",9}{"MiB",8}{"p50 ms",8}{"p95 ms",8}{"empty",7}");
        foreach (var a in arms)
            Console.WriteLine($"{a.Arm,-24}{a.Writes,7}{a.IngestSeconds,9:F1}{a.Bytes / 1048576.0,8:F1}{a.RecallP50,8:F1}{a.RecallP95,8:F1}{a.EmptyRate,7:P0}");

        Console.WriteLine("\nPrimary role per question kind — hit@1 / hit@5 / hit@10 · MRR · P@10 · R@10 (R: distinct gold records reached)");
        foreach (var kind in Enum.GetValues<QuestionKind>())
        {
            Console.WriteLine($"\n  {kind} ({questions.Count(q => q.Kind == kind)} question(s))");
            foreach (var a in arms)
            {
                var rs = a.Results.Where(r => r.Kind == kind).ToList();
                if (rs.Count == 0) continue;
                Console.WriteLine($"    {a.Arm,-24}{Rate(rs, 1)} /{Rate(rs, 5)} /{Rate(rs, 10)}   MRR {rs.Average(r => r.Outcome.Reciprocal):F3}"
                    + $"   P@10 {rs.Average(r => r.Outcome.Precision(10)):F3}   R@10 {rs.Average(r => r.Outcome.Recall(10, r.GoldRecords)):F3}");
            }
        }

        PrintAmendments(arms);
        PrintBy(arms, "lexical overlap of the question with its answer (hit@10)",
            [("<1/3", r => r.Overlap < 1 / 3.0), ("1/3-2/3", r => r.Overlap is >= 1 / 3.0 and < 2 / 3.0), (">=2/3", r => r.Overlap >= 2 / 3.0)]);
        PrintBy(arms, "age of the answer in the ingest order, oldest fifth first (hit@10)",
            [.. Enumerable.Range(0, 5).Select<int, (string, Func<Result, bool>)>(i => ($"q{i + 1}", r => r.Quintile == i))]);
    }

    private static void PrintAmendments(IReadOnlyList<ArmSummary> arms)
    {
        Console.WriteLine("\nAmendments and supersession");
        Console.WriteLine("  AmendmentChange — the amending paragraph vs the text it amends: found@10 / amendment ranked above the original / only the original returned");
        foreach (var a in arms)
        {
            var rs = a.Results.Where(r => r.Kind == QuestionKind.AmendmentChange).ToList();
            if (rs.Count == 0) continue;
            var found = rs.Count(r => r.Outcome.Hit(10));
            var above = rs.Count(r => r.Outcome.FirstRank["amendment"] > 0
                && (r.Outcome.FirstRank["stale"] == 0 || r.Outcome.FirstRank["amendment"] < r.Outcome.FirstRank["stale"]));
            var staleOnly = rs.Count(r => r.Outcome.FirstRank["amendment"] == 0 && r.Outcome.FirstRank["stale"] > 0);
            Console.WriteLine($"    {a.Arm,-24}{found / (double)rs.Count,6:P0} /{above / (double)rs.Count,6:P0} /{staleOnly / (double)rs.Count,6:P0}");
        }

        Console.WriteLine("  AmenderOfDecision — 'current rule on X': amender found@10 / original found@10 / both / amender ranked above original");
        foreach (var a in arms)
        {
            var rs = a.Results.Where(r => r.Kind == QuestionKind.AmenderOfDecision).ToList();
            if (rs.Count == 0) continue;
            double P(Func<Result, bool> f) => rs.Count(f) / (double)rs.Count;
            Console.WriteLine($"    {a.Arm,-24}{P(r => r.Outcome.FirstRank["amender"] is > 0 and <= 10),6:P0} /{P(r => r.Outcome.FirstRank["original"] is > 0 and <= 10),6:P0} /"
                + $"{P(r => r.Outcome.FirstRank["amender"] is > 0 and <= 10 && r.Outcome.FirstRank["original"] is > 0 and <= 10),6:P0} /"
                + $"{P(r => r.Outcome.FirstRank["amender"] > 0 && (r.Outcome.FirstRank["original"] == 0 || r.Outcome.FirstRank["amender"] < r.Outcome.FirstRank["original"])),6:P0}");
        }
    }

    private static void PrintBy(IReadOnlyList<ArmSummary> arms, string title, (string Label, Func<Result, bool> In)[] buckets)
    {
        Console.WriteLine($"\nBy {title}");
        Console.WriteLine($"  {"arm",-24}" + string.Concat(buckets.Select(b => $"{b.Label,16}")));
        foreach (var a in arms)
            Console.WriteLine($"  {a.Arm,-24}" + string.Concat(buckets.Select(b =>
            {
                var rs = a.Results.Where(b.In).ToList();
                return rs.Count == 0 ? $"{"-",16}" : $"{rs.Count(r => r.Outcome.Hit(10)) / (double)rs.Count,8:P0} (n={rs.Count,4})";
            })));
    }

    private static void WriteJson(IReadOnlyList<ArmSummary> arms, string tier, int chunks)
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_corpus-results"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            tier, chunks,
            arms = arms.Select(a => new
            {
                a.Arm, a.Tier, a.Writes, a.IngestSeconds, a.Bytes, a.RecallP50, a.RecallP95, a.EmptyRate,
                kinds = a.Results.GroupBy(r => r.Kind).Select(g => new
                {
                    kind = g.Key.ToString(), n = g.Count(),
                    hit1 = g.Count(r => r.Outcome.Hit(1)) / (double)g.Count(),
                    hit5 = g.Count(r => r.Outcome.Hit(5)) / (double)g.Count(),
                    hit10 = g.Count(r => r.Outcome.Hit(10)) / (double)g.Count(),
                    mrr = g.Average(r => r.Outcome.Reciprocal),
                }),
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nresults → {path}");
    }

    private static string Rate(IReadOnlyList<Result> rs, int k) => $"{rs.Count(r => r.Outcome.Hit(k)) / (double)rs.Count,6:P0}";

    private static double Pct(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
