using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>memory-enrichment</c> — WHY registering an embedder costs recall quality, by varying the two
/// write-time mechanisms INDEPENDENTLY.
/// </summary>
/// <remarks>
/// <para><b>The question, and why it needed a new instrument.</b> Registering an <see cref="IEmbedder"/> and
/// an <see cref="IVectorStore"/> measurably costs recall quality on this corpus — an effect larger than
/// anything salience produces in either direction, and reproducible. Two write-time mechanisms could explain
/// it and nothing separated them: <b>(a)</b> similarity LINKING adds edges that change what traversal
/// reaches, and <b>(b)</b> NOVELTY feeds salience, which changes what is admitted and how it decays. Recall
/// has no third candidate to blame — see the note on the semantic seed below.</para>
///
/// <para><b>They separate with knobs that already ship</b>, which is why this needed no new API.
/// <c>GraphMemoryOptions.MinSimilarity</c> is the link floor and is validated only for finiteness, so a
/// value above 1 admits no cosine and NO edge is ever written while the embed and the vector search still
/// run and novelty is still computed. Salience is a DI collection, so <see cref="NeutralSaliencePolicy"/>
/// drops novelty while linking continues. Crossing the two gives a clean 2×2.</para>
///
/// <para><b>A REAL model, and this sweep fails rather than substituting a fake.</b> The arm this study
/// replaces was measured through <c>FakeEmbedder</c>, a feature-hashed bag of WORDS in which "semantic
/// similarity" IS word overlap — a double that cannot represent meaning can only ever be seen paying a cost
/// it could never be seen earning back, which is exactly why the original numbers were withdrawn. Falling
/// back to a fake here would repeat that mistake silently, so an unreachable Ollama is a hard exit.</para>
///
/// <para><b>Embeddings are CACHED by text, and that is not a shortcut.</b> The model is deterministic for
/// identical input, so caching changes nothing about what is measured — it only stops the same corpus text
/// being re-embedded once per arm. Without it a real-model sweep at this scale is not affordable; with it,
/// the four arms genuinely share one corpus AND one set of vectors, which is what makes the comparison
/// paired.</para>
///
/// <para><b>What this does NOT vary, stated rather than left implicit.</b>
/// <c>GraphMemoryOptions.SemanticSeedK</c> stays <c>0</c> throughout — its default — so no arm has a
/// query-time semantic path and the whole difference is write-time, which is the question. The seed is a
/// separate shipped feature with its own measurement; mixing it in here would reintroduce exactly the
/// confound this study exists to remove.</para>
/// </remarks>
internal static class MemoryEnrichmentSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 10;
    private const int QueryLimit = 10;

    private const string None = "none";
    private const string LinkOnly = "link-only";
    private const string NoveltyOnly = "novelty-only";
    private const string Both = "both";

    /// <summary>Above any cosine, so enrichment links nothing while still embedding and searching.</summary>
    private const double NoLinking = 1.01;

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm, double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var model = SweepDoubles.Model;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // Refuses rather than substituting a fake — see SweepDoubles for why that is the whole point here.
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-enrichment");
        if (embedder is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });

        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
        };

        string[] arms = [None, LinkOnly, NoveltyOnly, Both];
        PrintPreamble(shapes, arms, model);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var links = new ConcurrentBag<(string Arm, int Edges)>();
        var judged = new ConcurrentBag<(string Arm, int Salient)>();

        async ValueTask RunOneAsync(int seed, Shape shape, string arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            var enriched = arm != None;
            var linking = arm is LinkOnly or Both;
            var novelty = arm is NoveltyOnly or Both;

            // MinSimilarity is the ONLY difference between a linking and a non-linking enriched arm: both
            // embed, both search, both index. That is what makes this attributable to the EDGES.
            var options = new GraphMemoryOptions { MinSimilarity = linking ? 0.6 : NoLinking };

            var counting = new SweepDoubles.CountingSaliencePolicy();
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);

            var engine = new GraphMemoryEngine(
                "enrichment",
                store,
                options: options,
                retrievability: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]),
                agePolicies: [agePolicy],
                embedder: enriched ? embedder : null,
                vectors: enriched ? new InMemoryVectorStore() : null,
                // Novelty is what salience READS, so dropping salience is how the novelty arm is switched
                // off without touching the embed at all.
                saliencePolicies: novelty ? [counting] : [new NeutralSaliencePolicy()],
                ranking: rrf);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            links.Add((arm, SimilarEdges(db)));
            judged.Add((arm, counting.Salient));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        // Sequential over arms within a cell would be enough, but the embedder cache makes the whole run
        // I/O-light after the first pass, so the usual parallel shape applies.
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        PrintControls(links.ToList(), judged.ToList(), orderChecks.ToList(), embedder);
        PrintTable(rows.ToList(), shapes, arms);
        PrintVerdict(rows.ToList(), shapes);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s) × "
            + $"{shapes.Length} shape(s) × {arms.Length} arm(s); {embedder.Misses} embed call(s), "
            + $"{embedder.Hits} cache hit(s).");
        return 0;
    }

    private static void PrintPreamble(IReadOnlyList<Shape> shapes, IReadOnlyList<string> arms, string model)
    {
        Console.WriteLine("memory-enrichment — WHY an embedder costs recall quality (TASKS.md Part 69)\n");
        Console.WriteLine("Registering an embedder + vector store costs recall quality on this corpus. Two");
        Console.WriteLine("WRITE-TIME mechanisms could explain it, and nothing separated them:");
        Console.WriteLine("  (a) similarity LINKING adds edges that change what traversal reaches");
        Console.WriteLine("  (b) NOVELTY feeds salience, which changes what is admitted and how it decays");
        Console.WriteLine();
        Console.WriteLine("The 2x2 that separates them, using knobs that already ship:");
        Console.WriteLine($"  {None,-13} no embedder at all — the model-free floor");
        Console.WriteLine($"  {LinkOnly,-13} embed + search + LINK; salience neutral    -> (a) alone");
        Console.WriteLine($"  {NoveltyOnly,-13} embed + search, MinSimilarity>1 so NO edge -> (b) alone");
        Console.WriteLine($"  {Both,-13} the shipped enriched configuration         -> (a)+(b)");
        Console.WriteLine();
        Console.WriteLine($"Embedder: {model} (REAL). SemanticSeedK stays 0, so no arm has a query-time");
        Console.WriteLine("semantic path and the whole difference is write-time.");
        Console.WriteLine();
        Console.WriteLine($"Shapes: {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"Arms:   {string.Join(", ", arms)}");
        Console.WriteLine();
    }

    /// <summary>
    /// The controls that make the factorial believable. Without these the arms are labels: a "link-only" arm
    /// that silently wrote no edges, or a "novelty-only" arm whose salience never fired, would produce a
    /// clean-looking table attributing the effect to a mechanism that never ran.
    /// </summary>
    private static void PrintControls(IReadOnlyList<(string Arm, int Edges)> links,
        IReadOnlyList<(string Arm, int Salient)> judged, IReadOnlyList<bool> order,
        SweepDoubles.CachingEmbedder embedder)
    {
        Console.WriteLine("Controls (each arm must have done what its NAME claims):");

        static int Total(IEnumerable<int> xs) => xs.Sum();
        foreach (var arm in new[] { None, LinkOnly, NoveltyOnly, Both })
        {
            var edges = Total(links.Where(l => l.Arm == arm).Select(l => l.Edges));
            var salient = Total(judged.Where(j => j.Arm == arm).Select(j => j.Salient));
            var wantEdges = arm is LinkOnly or Both;
            var wantSalient = arm is NoveltyOnly or Both;
            var ok = (edges > 0) == wantEdges && (salient > 0) == wantSalient;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {arm,-13} similar-edges={edges,-7} salient-writes={salient,-7}"
                + $" (expected edges {(wantEdges ? ">0" : "0")}, salient {(wantSalient ? ">0" : "0")})");
        }

        Console.WriteLine($"  {(order.All(o => o) ? "OK  " : "FAIL")} corpus order identical in every cell "
            + $"({order.Count(o => o)}/{order.Count})");
        Console.WriteLine($"  ---- embedder: {embedder.Misses} distinct text(s) embedded, "
            + $"{embedder.Hits} served from cache");
        Console.WriteLine();
    }

    private static void PrintTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<string> arms)
    {
        var classes = rows.Select(r => r.Class).Distinct()
            .OrderBy(c => MemoryPolicySweep.ClassOrder.TryGetValue(c, out var i) ? i : int.MaxValue)
            .ToList();

        Console.WriteLine("miss-rate / pollution-rate, mean over seeds (lower is better)\n");
        Console.WriteLine($"{"shape",-16}{"class",-18}" + string.Concat(arms.Select(a => $"{a,-22}")));
        Console.WriteLine(new string('-', 34 + (22 * arms.Count)));

        foreach (var shape in shapes)
        {
            foreach (var cls in classes)
            {
                var cells = arms.Select(arm =>
                {
                    var cell = rows.Where(r => r.Shape == shape.Label && r.Class == cls && r.Arm == arm).ToList();
                    return cell.Count == 0
                        ? $"{"-",-22}"
                        : $"{cell.Average(c => c.MissRate),6:F4} / {cell.Average(c => c.PollutionRate),-11:F4}";
                });
                Console.WriteLine($"{shape.Label,-16}{cls,-18}" + string.Concat(cells));
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// The attribution. Each mechanism's cost is its arm MINUS the model-free floor, so the two are read on
    /// one scale, and <c>both</c> is compared against their sum to say whether they simply add.
    /// </summary>
    private static void PrintVerdict(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes)
    {
        Console.WriteLine("Attribution — mean miss-rate delta vs the model-free floor (positive = WORSE)\n");
        Console.WriteLine($"{"shape",-16}{"(a) linking",-16}{"(b) novelty",-16}{"both",-16}{"a+b",-16}additive?");
        Console.WriteLine(new string('-', 92));

        foreach (var shape in shapes)
        {
            double Mean(string arm) =>
                rows.Where(r => r.Shape == shape.Label && r.Arm == arm).Select(r => r.MissRate).DefaultIfEmpty().Average();

            var floor = Mean(None);
            var a = Mean(LinkOnly) - floor;
            var b = Mean(NoveltyOnly) - floor;
            var both = Mean(Both) - floor;
            var sum = a + b;
            var additive = Math.Abs(both - sum) < 0.01 ? "yes" : $"no (gap {both - sum:+0.0000;-0.0000})";

            Console.WriteLine($"{shape.Label,-16}{a,-16:+0.0000;-0.0000}{b,-16:+0.0000;-0.0000}"
                + $"{both,-16:+0.0000;-0.0000}{sum,-16:+0.0000;-0.0000}{additive}");
        }

        // PER CLASS, and it is not a refinement — the aggregate above is a MEAN OVER OPPOSING EFFECTS and
        // reporting it alone would understate both. Measured 2026-08-15: linking's aggregate cost looks
        // small (+0.01 to +0.08) while underneath it drives `topical` misses to ZERO and `critical-rare`
        // misses from 0.16 to 0.77 in the same run. A single averaged number describes neither.
        Console.WriteLine();
        Console.WriteLine("Attribution BY CLASS — the same deltas, averaged over shapes\n");
        var classes = rows.Select(r => r.Class).Distinct()
            .OrderBy(c => MemoryPolicySweep.ClassOrder.TryGetValue(c, out var i) ? i : int.MaxValue)
            .ToList();

        Console.WriteLine($"{"class",-26}{"(a) linking",-16}{"(b) novelty",-16}{"both",-16}dominant");
        Console.WriteLine(new string('-', 90));
        foreach (var cls in classes)
        {
            double Mean(string arm) =>
                rows.Where(r => r.Class == cls && r.Arm == arm).Select(r => r.MissRate).DefaultIfEmpty().Average();

            var floor = Mean(None);
            var a = Mean(LinkOnly) - floor;
            var b = Mean(NoveltyOnly) - floor;
            var both = Mean(Both) - floor;
            var dominant = Math.Abs(a) < 1e-9 && Math.Abs(b) < 1e-9
                ? "neither (both flat)"
                : Math.Abs(a) > Math.Abs(b) ? "(a) linking" : "(b) novelty";

            Console.WriteLine($"{cls,-26}{a,-16:+0.0000;-0.0000}{b,-16:+0.0000;-0.0000}"
                + $"{both,-16:+0.0000;-0.0000}{dominant}");
        }

        Console.WriteLine();
        Console.WriteLine("Read this as attribution, never as a recommendation. This corpus defines relevance");
        Console.WriteLine("LEXICALLY — ground truth is 'the entry whose id the query names' — so a semantic");
        Console.WriteLine("neighbour is wrong here BY CONSTRUCTION. The honest claim is which mechanism moves");
        Console.WriteLine("the number, not that enrichment is harmful to a real consumer.");
    }

    /// <summary>
    /// Similarity edges the run actually wrote, read straight from the store after the replay.
    /// </summary>
    /// <remarks>
    /// A decorator over <see cref="IMemoryGraphStore"/> would be the obvious shape and is the wrong one here:
    /// that interface has fourteen members, so the control would be ~40 lines of pass-through that breaks
    /// every time the contract grows — and it would be counting CALLS rather than edges. One query counts the
    /// thing the arm is named for.
    /// </remarks>
    private static int SimilarEdges(MemoryPolicySweep.SweepDb db)
    {
        using var conn = db.Factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM lyntai_memory_edge WHERE kind = 'similar'";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
