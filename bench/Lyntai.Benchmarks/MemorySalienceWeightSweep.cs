using System.Collections.Concurrent;
using System.Diagnostics;

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
/// <c>memory-salience-weight</c> — one factor:
/// <see cref="ReciprocalRankFusionOptions.SalienceWeight"/>, the BOUND on how loud a voice salience gets in
/// ranking.
/// </summary>
/// <remarks>
/// <para><b>The question.</b> Salience improves recall on most corpus shapes and makes <c>many-candidates</c>
/// measurably WORSE: with forty competitors, admitting salient entries displaces relevant ones
/// (<c>TASKS.md</c> Part 65). That is a real regression on design §5.7.0's line 2 traded for a gain on line 1,
/// which is the correct direction — and it is the sharpest known cost of a shipped default. What is open is
/// whether a BOUND recovers it: can salience's ranking voice be turned down enough to stop the displacement
/// while keeping its gains elsewhere?</para>
///
/// <para><b>Why this needed a real embedder, and why running it without one would have been worse than not
/// running it.</b> Salience reads NOVELTY; the engine derives novelty from a similarity search it performs
/// only when an embedder and a vector store are both present. Without them
/// <see cref="StructuralSaliencePolicy"/> declines on EVERY write, so salience is uniformly absent — and RRF
/// ranks by COMPETITION (<c>docs/DECISIONS.md</c> <b>D82</b>), so a signal every candidate ties on
/// contributes the same constant at every weight. Arm 0 and arm 2 would be the same engine, the curve would
/// be perfectly flat, every existing control would be green, and the output would read as a clean
/// exoneration. That trap is recorded in <c>.claude/knowledge/pitfalls.md</c>; this sweep exists on the other
/// side of it and refuses to run without a model.</para>
///
/// <para><b>The control that makes the flat-curve reading impossible, and it is the point of the file.</b>
/// A knob that scales a signal is unmeasurable when the signal is CONSTANT across candidates, and constant
/// has two shapes — nobody salient, and everybody salient. So every cell reports salient writes against
/// judged writes, and the verdict refuses to interpret a flat curve unless that ratio is strictly between
/// them. "Each arm carried a different weight" is not the question; "did the thing the weight scales
/// actually vary" is.</para>
///
/// <para><b>Arms differ in EXACTLY the ranking weight.</b> Every arm embeds, links, judges salience and
/// applies <see cref="SalienceRetentionPolicy"/> identically — so decay resistance and store admission, the
/// two consumers of salience that are not ranking, are held constant. Whatever moves is the ranking voice.
/// </para>
///
/// <para><b>What this cannot settle.</b> Not the best value — four coarse arms show direction and rough
/// magnitude, and a default moves on a measurement rather than on an argument (D49, D54), but not on one
/// run. Not the other two consumers of salience. And it inherits every blind spot of the corpus, which
/// defines relevance LEXICALLY (<c>TASKS.md</c> Part 69) — so a salience gain that is really a semantic one
/// cannot appear here at all.</para>
/// </remarks>
internal static class MemorySalienceWeightSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 10;
    private const int QueryLimit = 10;

    /// <summary>The shape the whole study is about.</summary>
    private const string Regression = "many-candidates";

    private sealed record Arm(string Label, double Weight);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm,
        double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-salience-weight");
        if (embedder is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();

        // A coarse ladder around the shipped 1: silent, half a voice, the default, and a dominant one. The
        // `off` arm is the interesting end — it is what a bounded-admission rule would approach.
        Arm[] arms = [new("0", 0), new("0.5", 0.5), new("1.0", 1.0), new("2.0", 2.0)];

        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new(Regression, baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
        };

        PrintPreamble(arms, shapes);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var judged = new ConcurrentBag<(string Arm, int Salient, int Judged, int Distinct)>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            // Every other constant inherited from one shared options object, so a cell differs from its
            // neighbour in exactly the swept value.
            var ranking = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
            {
                RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor,
                SalienceWeight = arm.Weight,
            });

            var counting = new SweepDoubles.CountingSaliencePolicy();
            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "salience-weight",
                new SqliteMemoryGraphStore(db.Factory),
                // Retention is IDENTICAL in every arm: salience's other two consumers are held constant, so
                // the only thing the ladder moves is how loudly salience speaks in the ranking.
                retrievability: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]),
                agePolicies: [agePolicy],
                embedder: embedder,
                vectors: new InMemoryVectorStore(),
                saliencePolicies: [counting],
                ranking: ranking);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            judged.Add((arm.Label, counting.Salient, counting.Judged, counting.DistinctValues));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        var measurable = PrintControls([.. judged], [.. orderChecks], embedder);
        PrintTable([.. rows], shapes, arms);
        PrintVerdict([.. rows], shapes, arms, measurable);
        PrintNotSwept();

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s) x "
            + $"{shapes.Length} shape(s) x {arms.Length} arm(s); {embedder.Misses} embed call(s), "
            + $"{embedder.Hits} cache hit(s).");
        return 0;
    }

    private static void PrintPreamble(Arm[] arms, Shape[] shapes)
    {
        Console.WriteLine("memory-salience-weight — is the many-candidates regression recoverable by a BOUND?\n");
        Console.WriteLine("Salience helps on most shapes and hurts `many-candidates`, where 40 competitors mean");
        Console.WriteLine("admitting salient entries displaces relevant ones (TASKS.md Part 65). This asks");
        Console.WriteLine("whether turning salience's RANKING voice down recovers it without losing the gains.");
        Console.WriteLine();
        Console.WriteLine($"  arms (SalienceWeight): {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shapes:                {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"  seeds: {SeedCount}, limit {QueryLimit}, embedder {SweepDoubles.Model} (REAL)");
        Console.WriteLine();
        Console.WriteLine("  `1.0` is the SHIPPED default. Retention and store admission are identical in every");
        Console.WriteLine("  arm, so a difference is the ranking voice and nothing else.\n");
    }

    /// <summary>
    /// The controls, and the first one decides whether the table below means anything at all.
    /// </summary>
    /// <returns>Whether the swept signal genuinely varied — false makes every number here uninterpretable.</returns>
    private static bool PrintControls(IReadOnlyList<(string Arm, int Salient, int Judged, int Distinct)> judged,
        IReadOnlyList<bool> order, SweepDoubles.CachingEmbedder embedder)
    {
        Console.WriteLine("Controls:");

        var salient = judged.Sum(j => j.Salient);
        var total = judged.Sum(j => j.Judged);
        var ratio = total == 0 ? 0 : (double)salient / total;

        // DISTINCT VALUES is the control, not the firing count. Firing says the signal appeared; only
        // distinct values say it can DISCRIMINATE, and RRF ranks by competition (D82) so a tied signal
        // contributes the same constant at every weight. The first run of this sweep reported 98.9% firing,
        // which passes any presence test while being nearly uniform on that axis.
        var distinct = judged.Count == 0 ? 0 : judged.Max(j => j.Distinct);
        var measurable = distinct > 1;

        Console.WriteLine($"  salience fired on {salient}/{total} writes ({ratio:P1}) — presence only, which "
            + "is NOT the control");
        Console.WriteLine($"  distinct salience values: {distinct} — "
            + (measurable
                ? "the weight has something to scale ✓"
                : "TIED, so this sweep can measure NOTHING ✗"));

        if (!measurable)
        {
            Console.WriteLine("    A signal every candidate ties on contributes the same constant at every");
            Console.WriteLine("    weight (D82), so the curve below is flat as an ARTIFACT, not as a finding.");
        }

        // Salience is judged on the WRITE path and the weight only affects RANKING, so the count must not
        // move between arms. If it does, the arms differ in more than the swept value.
        var perArm = judged.GroupBy(j => j.Arm)
            .Select(g => (Arm: g.Key, Salient: g.Sum(x => x.Salient)))
            .OrderBy(x => x.Arm).ToList();
        var identical = perArm.Select(x => x.Salient).Distinct().Count() <= 1;
        Console.WriteLine($"  per-arm salient counts {(identical ? "identical ✓" : "DIFFER ✗")}: "
            + string.Join(", ", perArm.Select(x => $"{x.Arm}={x.Salient}")));
        if (!identical)
            Console.WriteLine("    The weight must not change what is WRITTEN. Arms differ in more than ranking.");

        Console.WriteLine($"  corpus replay order preserved in {order.Count(o => o)}/{order.Count} cell(s)");
        Console.WriteLine($"  embedder: {embedder.Misses} call(s), {embedder.Hits} cache hit(s) — one set of");
        Console.WriteLine("    vectors shared by every arm, which is what makes the comparison paired.");
        Console.WriteLine();
        return measurable;
    }

    private static void PrintTable(Row[] rows, Shape[] shapes, Arm[] arms)
    {
        Console.WriteLine("Mean miss / pollution by shape and weight (all classes pooled):\n");
        Console.WriteLine($"  {"shape",-16} {string.Join("  ", arms.Select(a => $"w={a.Label,-14}"))}");
        foreach (var shape in shapes)
        {
            var cells = arms.Select(arm =>
            {
                var subset = rows.Where(r => r.Shape == shape.Label && r.Arm == arm.Label).ToList();
                return subset.Count == 0
                    ? "—".PadRight(16)
                    : $"{subset.Average(c => c.MissRate):F3}/{subset.Average(c => c.PollutionRate):F3}".PadRight(16);
            });
            Console.WriteLine($"  {shape.Label,-16} {string.Join("  ", cells)}");
        }
        Console.WriteLine();
    }

    private static void PrintVerdict(Row[] rows, Shape[] shapes, Arm[] arms, bool measurable)
    {
        Console.WriteLine("Verdict:");
        if (!measurable)
        {
            Console.WriteLine("  NONE — the swept signal did not vary, so no reading of the table is supported.");
            Console.WriteLine("  Fix the instrument before fixing the engine.");
            return;
        }

        double Miss(string shape, string arm) =>
            rows.Where(r => r.Shape == shape && r.Arm == arm).Select(r => r.MissRate).DefaultIfEmpty(0).Average();

        double Pollution(string shape, string arm) =>
            rows.Where(r => r.Shape == shape && r.Arm == arm).Select(r => r.PollutionRate).DefaultIfEmpty(0).Average();

        const string shipped = "1.0";
        Console.WriteLine("  (deltas against the shipped 1.0; negative = better)\n");
        foreach (var arm in arms.Where(a => a.Label != shipped))
        {
            double Delta(Func<string, string, double> metric, bool regressionShape) => regressionShape
                ? metric(Regression, arm.Label) - metric(Regression, shipped)
                : shapes.Where(s => s.Label != Regression)
                    .Select(s => metric(s.Label, arm.Label) - metric(s.Label, shipped))
                    .DefaultIfEmpty(0).Average();

            Console.WriteLine($"  w={arm.Label,-4} {Regression,-16} miss {Delta(Miss, true):+0.0000;-0.0000;0.0000}"
                + $"  pollution {Delta(Pollution, true):+0.0000;-0.0000;0.0000}");
            Console.WriteLine($"       {"other shapes",-16} miss {Delta(Miss, false):+0.0000;-0.0000;0.0000}"
                + $"  pollution {Delta(Pollution, false):+0.0000;-0.0000;0.0000}");
        }

        Console.WriteLine();
        Console.WriteLine("  READ BOTH COLUMNS. Design §5.7.0 is lexicographic: MissRate is objective (2), the");
        Console.WriteLine("  primary number, and PollutionRate is (3) — explicitly NOT co-equal. \"A change that");
        Console.WriteLine("  trades a large miss reduction for a small pollution rise is accepted, one that");
        Console.WriteLine("  trades the reverse is not.\" An arm that improves miss everywhere while raising");
        Console.WriteLine("  pollution slightly is therefore a WIN under the stated objective, not a wash — and");
        Console.WriteLine("  reporting miss alone would have hidden the trade that decides it.");
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The BEST value. Four coarse arms show direction and rough magnitude; no default");
        Console.WriteLine("    moves off 1.0 on one run (D49, D54).");
        Console.WriteLine("  - Salience's OTHER two consumers. Decay resistance and store admission are held");
        Console.WriteLine("    constant on purpose, so this prices the ranking voice alone — a bound on");
        Console.WriteLine("    ADMISSION is a different mechanism and would need its own study.");
        Console.WriteLine("  - The MultiplicativeRankingPolicy arm: RRF is the registered default, and its");
        Console.WriteLine("    SalienceWeight is a different quantity from Multiplicative's rank boost.");
        Console.WriteLine("  - Lexical ground truth: this corpus defines relevance by id-in-query, so a");
        Console.WriteLine("    salience gain that is really a semantic one cannot show up here at all.");
    }
}
