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
/// <para><b>Arms differ in EXACTLY the ranking weight.</b> Every arm embeds, links, judges salience and
/// applies <see cref="SalienceRetentionPolicy"/> identically, so decay resistance and store admission — the
/// two consumers of salience that are not ranking — are held constant.</para>
///
/// <para><b>It refuses to run without a real embedder, for a sharper reason than cost.</b> Salience reads
/// NOVELTY, which the engine derives from a similarity search it performs only when an embedder and a vector
/// store are both present; without them <see cref="StructuralSaliencePolicy"/> declines on every write. RRF
/// then ranks by COMPETITION (<b>D82</b>), so a uniformly-absent signal contributes the same constant at
/// every weight and every arm is the same engine.</para>
///
/// <para><b>The control is DISTINCT VALUES, never the firing count.</b> A knob that scales a signal is
/// unmeasurable while that signal is tied across candidates, so the verdict refuses to interpret its own
/// table unless the values genuinely differ. Firing is presence; only distinct values are discrimination.
/// See <c>.claude/knowledge/pitfalls.md</c> for the measured version of both traps.</para>
///
/// <para>The question, the result and what neither settles live in <c>TASKS.md</c> Part 65 and
/// <c>docs/memory.md</c> §5; <c>PrintNotSwept</c> states the limits at the point of use.</para>
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

    private sealed record Row(int Seed, string Shape, string Class, string Arm, CorpusLanguage Language,
        double MissRate, double PollutionRate);

    /// <summary>
    /// The SECOND run, over the axis this repository knows is its blind spot.
    ///
    /// <para>Run one established a monotonic ladder on space-separated English, which is the friendliest
    /// tokenization the library supports. This asks whether its WINNER survives the unfriendly ones, so it
    /// narrows to the decisive pair — silent against the shipped default — and spends the budget on
    /// languages instead. A result that holds in five writing systems is a different claim from one measured
    /// in the easiest.</para>
    /// </summary>
    private const string ShippedArm = "1.0";

    public static async Task<int> RunAsync(bool acrossLanguages = false)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-salience-weight");
        if (embedder is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();

        // A coarse ladder around the shipped 1: silent, half a voice, the default, and a dominant one. The
        // `off` arm is the interesting end — it is what a bounded-admission rule would approach. The language
        // run keeps only the decisive pair, because five writing systems x four arms is budget spent on
        // resolution this question does not need.
        Arm[] arms = acrossLanguages
            ? [new("0", 0), new(ShippedArm, 1.0)]
            : [new("0", 0), new("0.5", 0.5), new(ShippedArm, 1.0), new("2.0", 2.0)];

        // English FIRST, so it reproduces run one's cells and a difference there is an instrument bug.
        CorpusLanguage[] languages = acrossLanguages
            ? [.. Enum.GetValues<CorpusLanguage>().OrderBy(l => l == CorpusLanguage.English ? 0 : 1)]
            : [CorpusLanguage.English];

        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new(Regression, baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
        };

        PrintPreamble(arms, shapes, languages);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel, CorpusLanguage Lang), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var judged = new ConcurrentBag<(string Arm, int Salient, int Judged, int Distinct)>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm, CorpusLanguage language)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label, language),
                key => MemoryCorpus.Generate(shape.Value with { Language = key.Lang }, key.Seed));
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
                rows.Add(new Row(seed, shape.Label, cls, arm.Label, language,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            judged.Add((arm.Label, counting.Salient, counting.Judged, counting.DistinctValues));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms from lang in languages
            select (seed, shape, arm, lang),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm, item.lang));

        var measurable = PrintControls([.. judged], [.. orderChecks], embedder);
        foreach (var language in languages)
        {
            if (languages.Length > 1) Console.WriteLine($"--- {language} ---\n");
            var forLanguage = rows.Where(r => r.Language == language).ToArray();
            PrintTable(forLanguage, shapes, arms);
            PrintVerdict(forLanguage, shapes, arms, measurable);
            Console.WriteLine();
        }
        if (languages.Length > 1) PrintLanguageVerdict([.. rows], languages, shapes);
        PrintNotSwept();

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s) x "
            + $"{shapes.Length} shape(s) x {arms.Length} arm(s) x {languages.Length} language(s); "
            + $"{embedder.Misses} embed call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>
    /// The whole point of the language run in one table: does the decisive arm still win everywhere?
    ///
    /// <para><b>BOTH metrics, per language, and the first version of this method printed only miss.</b> That
    /// is the identical defect <see cref="PrintVerdict"/> was already fixed for one function up — and it did
    /// real damage: a "5/5 shapes better" summary counting miss alone read as unanimous, and the shipped
    /// default was changed on it before the pollution column was looked at. Under §5.7.0 a large miss
    /// reduction for a small pollution rise is ACCEPTED and <b>the reverse is not</b>, so a verdict that
    /// cannot see pollution cannot reach a verdict at all.</para>
    /// </summary>
    private static void PrintLanguageVerdict(Row[] rows, CorpusLanguage[] languages, Shape[] shapes)
    {
        Console.WriteLine("Across languages — does w=0 still beat the shipped 1.0?\n");
        Console.WriteLine("  Split by shape class, because averaging them collapses the structure this study is");
        Console.WriteLine($"  about: `{Regression}` is the shape the regression lives on, and the other "
            + $"{shapes.Length - 1} are what a\n  bound is supposed to protect.\n");
        Console.WriteLine($"  {"language",-14} {"regression (miss / poll)",-26} {"ordinary (miss / poll)",-26} §5.7.0");

        foreach (var language in languages)
        {
            double Mean(Func<Row, double> metric, string shape, string arm) => rows
                .Where(r => r.Language == language && r.Shape == shape && r.Arm == arm)
                .Select(metric).DefaultIfEmpty(0).Average();

            double Delta(Func<Row, double> metric, string shape) =>
                Mean(metric, shape, "0") - Mean(metric, shape, ShippedArm);

            double Over(Func<Row, double> metric, bool regression) => shapes
                .Where(s => (s.Label == Regression) == regression)
                .Select(s => Delta(metric, s.Label)).DefaultIfEmpty(0).Average();

            var (regMiss, regPoll) = (Over(r => r.MissRate, true), Over(r => r.PollutionRate, true));
            var (ordMiss, ordPoll) = (Over(r => r.MissRate, false), Over(r => r.PollutionRate, false));

            // §5.7.0 is lexicographic: a miss GAIN outweighing a pollution COST is accepted, the reverse is
            // refused. Judged on the ORDINARY shapes, because that is where a bound has to not cost anything
            // — a language can win big on the regression shape and still be a bad trade everywhere else, and
            // an average over both would hide exactly that.
            var verdict = ordMiss < 0 && -ordMiss > ordPoll ? "accepted" : "REFUSED (ordinary)";

            Console.WriteLine($"  {language,-14} "
                + $"{$"{regMiss,8:+0.0000;-0.0000;0.0000} / {regPoll:+0.0000;-0.0000;0.0000}",-26} "
                + $"{$"{ordMiss,8:+0.0000;-0.0000;0.0000} / {ordPoll:+0.0000;-0.0000;0.0000}",-26} {verdict}");
        }

        Console.WriteLine();
        Console.WriteLine("  A REFUSED row means miss improved and pollution rose by MORE on the shapes a bound");
        Console.WriteLine("  is meant to leave alone — the trade §5.7.0 explicitly rejects, however good the");
        Console.WriteLine("  regression column looks beside it. Counting shapes that improved on MISS is not a");
        Console.WriteLine("  verdict and never was; reading one as a verdict is how this study briefly");
        Console.WriteLine("  concluded the shipped default should move.");
    }

    private static void PrintPreamble(Arm[] arms, Shape[] shapes, CorpusLanguage[] languages)
    {
        Console.WriteLine("memory-salience-weight — is the many-candidates regression recoverable by a BOUND?\n");
        Console.WriteLine("Salience helps on most shapes and hurts `many-candidates`, where 40 competitors mean");
        Console.WriteLine("admitting salient entries displaces relevant ones (TASKS.md Part 65). This asks");
        Console.WriteLine("whether turning salience's RANKING voice down recovers it without losing the gains.");
        Console.WriteLine();
        Console.WriteLine($"  arms (SalienceWeight): {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shapes:                {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"  languages:             {string.Join(", ", languages)}");
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
