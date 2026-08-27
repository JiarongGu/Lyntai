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
/// <c>memory-importance</c> — one factor: WHAT the salience signal measures. Novelty (the shipped
/// <see cref="StructuralSaliencePolicy"/>) against a perfect IMPORTANCE oracle, with a salience-off control.
/// </summary>
/// <remarks>
/// <para><b>The question.</b> <b>D89</b> priced novelty-as-salience and took its ranking voice to zero, which
/// is not a finding about importance: novelty is monotone in "unlike anything already stored", so sustained
/// significance decays on that axis exactly as it is confirmed while a one-off triviality reads as maximal.
/// Where that is the corpus, novelty is not merely uninformative — it is inverted.</para>
///
/// <para><b>The arms differ in the SIGNAL and nothing else.</b> The oracle mirrors
/// <see cref="StructuralSaliencePolicy"/>'s arithmetic exactly — same <see cref="SalienceOptions"/>, same
/// clamp, same "return Empty at the neutral 1" — and substitutes the entry's CLASS for its novelty, so a cell
/// differs from its neighbour in what the number MEANS, never in how large it may be.</para>
///
/// <para><b>Ranking is held at the shipped <c>SalienceWeight = 0</c>.</b> Salience then acts through decay
/// resistance and store admission and does not vote on order, which is D45's actual claim — "does not fade
/// away", not "first priority". Survival is the question, so it is asked on the configuration that ships. A
/// ranking voice, and a combination arm, belong to the follow-up.</para>
///
/// <para><b>It refuses to run without a real embedder</b>, for D89's reason: novelty comes from a similarity
/// probe the engine performs only when an embedder and vector store are both present, so without one the
/// novelty arm silently BECOMES the salience-off control and the table reads as a win.</para>
///
/// <para><b>The oracle is a CEILING, never an accuracy</b> — <c>memory-annotation</c>'s stance for a perfect
/// annotator. A weak result kills the idea outright; a strong one only says a real rater is worth costing.
/// <c>PrintNotSwept</c> states the rest of the limits at the point of use.</para>
/// </remarks>
internal static class MemoryImportanceSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 10;
    private const int QueryLimit = 10;

    /// <summary>The shape the whole study is about: noise that is TEXTUALLY DIVERSE, where novelty is known
    /// to be wrong. Under templated noise the second entry onward reads as familiar, so the shipped policy's
    /// suspected failure mode is unreachable by construction and every arm would look alike.</summary>
    private const string Decisive = "diverse-noise";

    private const string ShippedArm = "novelty";

    private sealed record Arm(string Label, Func<IMemorySaliencePolicy> Policy);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm, CorpusLanguage Language,
        double MissRate, double PollutionRate);

    /// <summary>
    /// Ground truth as a salience signal: the classes whose material a deployment would call consequential.
    ///
    /// <para><c>critical</c> is the rare fact that must survive interference to be found once, later;
    /// <c>attribute</c> is the fact stated once and thereafter referred to only by its subject. Both are
    /// "matters, and is not repeated". <c>topical</c> is the working set — relevant constantly, but relevance
    /// is not importance and marking it would make this policy a relevance oracle instead. <c>noise</c> is
    /// never a right answer.</para>
    ///
    /// <para><b>It marks a CLASS, never a query's answer set.</b> A critical entry is important whether or not
    /// the query being asked is its own, so the oracle boosts it for every recall — which is where its
    /// pollution cost comes from and is the honest half of the trade. A policy that knew the per-query answer
    /// would be measuring "does knowing the answer help", which is not a question.</para>
    /// </summary>
    private sealed class ClassOracleSaliencePolicy(SalienceOptions? options = null) : IMemorySaliencePolicy
    {
        private readonly SalienceOptions _options = options ?? new SalienceOptions();

        /// <summary>Bit 32 — the base of the CONSUMER range. The library owns 0-31, and a policy declaring
        /// <see cref="MemorySalienceProvenance.None"/> is refused at construction, because None means
        /// "nothing computed this" and must stay distinguishable from a policy's own identity.</summary>
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            ArgumentNullException.ThrowIfNull(write);

            var id = MemoryPolicySweep.ExtractCorpusId(write.Content);
            var important =
                id.StartsWith("critical", StringComparison.Ordinal) ||
                id.StartsWith("attribute", StringComparison.Ordinal);

            // Deliberately the SAME arithmetic and the same ceiling as StructuralSaliencePolicy, so the arms
            // cannot differ in how loud they are allowed to be — only in what they are loud ABOUT.
            var salience = Math.Clamp(1 + (_options.NoveltyWeight * (important ? 1 : 0)), 1, _options.MaxSalience);

            return salience == 1
                ? MemorySignals.Empty
                : MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
        }
    }

    public static async Task<int> RunAsync(bool acrossLanguages = false)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-importance");
        if (embedder is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();

        // `none` is the control that makes the other two readable: without it, two arms that both do nothing
        // useful are indistinguishable from two arms that both help equally.
        Arm[] arms = acrossLanguages
            ? [new(ShippedArm, () => new StructuralSaliencePolicy()), new("oracle", () => new ClassOracleSaliencePolicy())]
            : [new("none", () => new NeutralSaliencePolicy()),
               new(ShippedArm, () => new StructuralSaliencePolicy()),
               new("oracle", () => new ClassOracleSaliencePolicy())];

        // English FIRST, so a difference in its cells against run one is an instrument bug rather than a
        // finding about language.
        CorpusLanguage[] languages = acrossLanguages
            ? [.. Enum.GetValues<CorpusLanguage>().OrderBy(l => l == CorpusLanguage.English ? 0 : 1)]
            : [CorpusLanguage.English];

        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new(Decisive, baseline with { NoiseDensity = 40, NoiseKind = CorpusNoiseKind.Diverse }),
            new("templated-noise", baseline with { NoiseDensity = 40 }),
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

            var counting = new SweepDoubles.CountingSaliencePolicy(arm.Policy());
            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "importance",
                new SqliteMemoryGraphStore(db.Factory),
                retrievability: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]),
                agePolicies: [agePolicy],
                embedder: embedder,
                vectors: new InMemoryVectorStore(),
                saliencePolicies: [counting],
                // The SHIPPED ranking configuration: SalienceWeight is 0, so salience speaks through decay
                // resistance and store admission only. Changing it here would price two things at once.
                ranking: new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
                {
                    RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor,
                }));

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

        var measurable = PrintControls([.. judged], [.. orderChecks], arms);
        foreach (var language in languages)
        {
            if (languages.Length > 1) Console.WriteLine($"--- {language} ---\n");
            var forLanguage = rows.Where(r => r.Language == language).ToArray();
            PrintTable(forLanguage, shapes, arms);
            PrintVerdict(forLanguage, shapes, measurable);
            Console.WriteLine();
        }
        PrintNotSwept();

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s) x "
            + $"{shapes.Length} shape(s) x {arms.Length} arm(s) x {languages.Length} language(s); "
            + $"{embedder.Misses} embed call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    private static void PrintPreamble(Arm[] arms, Shape[] shapes, CorpusLanguage[] languages)
    {
        Console.WriteLine();
        Console.WriteLine("memory-importance — WHAT the salience signal measures, holding everything else fixed.");
        Console.WriteLine();
        Console.WriteLine($"  arms       {string.Join(" / ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shapes     {string.Join(" / ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"  languages  {string.Join(" / ", languages)}");
        Console.WriteLine($"  seeds      {SeedCount}, ranking SalienceWeight = 0 (shipped)");
        Console.WriteLine();
        Console.WriteLine($"  `{Decisive}` is the shape the study is about: novelty is monotone in \"unlike anything");
        Console.WriteLine("  already stored\", so only textually diverse junk can expose it rating trivia as salient.");
        Console.WriteLine();
    }

    /// <summary>
    /// The controls — and C1 asks a DIFFERENT question from the one <c>memory-salience-weight</c> asks, which
    /// cost this sweep its first run to notice.
    ///
    /// <para><b>Distinct values is the right control for a knob that SCALES a signal, and the wrong one
    /// here.</b> D89's sweep varies <c>SalienceWeight</c> over one policy, so a signal every candidate ties on
    /// contributes the same constant at every weight (D82) and its curve is flat as an artefact — only
    /// distinct VALUES can rule that out. This sweep varies the POLICY at a fixed weight, and its oracle is
    /// binary by construction: important writes take one value and the rest return
    /// <see cref="MemorySignals.Empty"/>. Run one therefore reported "1 distinct value" and withheld a verdict
    /// over an arm that was discriminating perfectly, on 1520 of 10456 writes.</para>
    ///
    /// <para><b>So the question is DISCRIMINATION, not variety: strictly between none and all.</b> An arm that
    /// marks nothing has no signal; one that marks everything has no signal either, and both are invisible to
    /// a distinct-value count. Variety is still printed, because for a graded policy it is the sharper
    /// statement and a future arm may be graded — but it no longer decides whether the table can be read.</para>
    /// </summary>
    private static bool PrintControls(
        (string Arm, int Salient, int Judged, int Distinct)[] judged, bool[] orderChecks, Arm[] arms)
    {
        Console.WriteLine("Controls");
        Console.WriteLine($"  C0 replay order matches the corpus: {orderChecks.Count(x => x)}/{orderChecks.Length}");

        var measurable = true;
        foreach (var arm in arms)
        {
            var forArm = judged.Where(j => j.Arm == arm.Label).ToArray();
            var distinct = forArm.Length == 0 ? 0 : forArm.Max(j => j.Distinct);
            var salient = forArm.Sum(j => j.Salient);
            var total = forArm.Sum(j => j.Judged);

            // `none` is the control: it is SUPPOSED to mark nothing, and an arm that marks nothing is exactly
            // what it is for. Every other arm has to separate some writes from the rest.
            var silent = arm.Label == "none";
            var discriminates = salient > 0 && salient < total;
            var ok = silent ? salient == 0 : discriminates;
            if (!ok) measurable = false;

            var share = total == 0 ? 0 : (double)salient / total;
            Console.WriteLine($"  C1 {arm.Label,-8} salient {salient}/{total} ({share:P1}), "
                + $"{distinct} distinct value(s)  {(ok ? "ok" : "<== SUSPECT")}");
        }

        if (!measurable)
        {
            Console.WriteLine();
            Console.WriteLine("  ⚠ An arm marked either nothing or everything, so its column separates no entry");
            Console.WriteLine("    from any other. The verdict is withheld rather than computed over it.");
        }

        Console.WriteLine();
        return measurable;
    }

    private static void PrintTable(Row[] rows, Shape[] shapes, Arm[] arms)
    {
        var classes = rows.Select(r => r.Class).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Console.WriteLine($"  {"shape",-16} {"class",-26} {string.Join("  ", arms.Select(a => $"{a.Label,-16}"))}");
        Console.WriteLine($"  {"",-16} {"",-26} {string.Join("  ", arms.Select(_ => $"{"miss / poll",-16}"))}");

        foreach (var shape in shapes)
        {
            foreach (var cls in classes)
            {
                var cells = arms.Select(a =>
                {
                    var cell = rows.Where(r => r.Shape == shape.Label && r.Class == cls && r.Arm == a.Label).ToArray();
                    return cell.Length == 0
                        ? $"{"-",-16}"
                        : $"{cell.Average(r => r.MissRate):F3} / {cell.Average(r => r.PollutionRate):F3}".PadRight(16);
                });
                Console.WriteLine($"  {shape.Label,-16} {cls,-26} {string.Join("  ", cells)}");
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// BOTH metrics, per class — and printing only miss is the defect this sweep's sibling shipped once and
    /// changed a default on. §5.7.0 accepts a large miss reduction for a small pollution rise and REFUSES the
    /// reverse, so a verdict that cannot see pollution has not reached one.
    /// </summary>
    private static void PrintVerdict(Row[] rows, Shape[] shapes, bool measurable)
    {
        if (!measurable)
        {
            Console.WriteLine("Verdict withheld — see C1.");
            return;
        }

        Console.WriteLine("Does the ORACLE beat the shipped novelty signal? (negative = oracle better)\n");
        // `topical` is a COLUMN, not a footnote. Store admission is zero-sum, so whatever the oracle promotes
        // displaces something, and the displaced material is by definition what it did not mark. A verdict
        // showing only the marked classes reads as a free win — the same shape as showing miss without
        // pollution, which this sweep's sibling shipped once and changed a default on.
        Console.WriteLine($"  {"shape",-16} {"critical-rare",-18} {"attribute",-18} {"topical (cost)",-18} "
            + $"{"all",-18}");

        foreach (var shape in shapes)
        {
            double Delta(Func<Row, double> metric, string cls)
            {
                double Mean(string arm) => rows
                    .Where(r => r.Shape == shape.Label && r.Class.StartsWith(cls, StringComparison.Ordinal)
                                && r.Arm == arm)
                    .Select(metric).DefaultIfEmpty(double.NaN).Average();
                return Mean("oracle") - Mean(ShippedArm);
            }

            string Cell(string cls)
            {
                var (miss, poll) = (Delta(r => r.MissRate, cls), Delta(r => r.PollutionRate, cls));
                return double.IsNaN(miss) ? $"{"-",-18}" : $"{miss,+6:F2} / {poll,+6:F2}".PadRight(18);
            }

            Console.WriteLine($"  {shape.Label,-16} {Cell("critical")} {Cell("attribute")} {Cell("topical")} "
                + $"{Cell("all")}");
        }

        Console.WriteLine();
        Console.WriteLine($"  The cell that decides the BENEFIT is `{Decisive}` x critical-rare. The cell that");
        Console.WriteLine("  decides whether it is affordable is `topical`, because store admission is zero-sum:");
        Console.WriteLine("  what the oracle promotes displaces material it did not mark. §5.7.0 accepts a large");
        Console.WriteLine("  miss reduction for a small POLLUTION rise; it says nothing about trading one class's");
        Console.WriteLine("  miss for another's, and that trade is what this table has to be read for.");
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine();
        Console.WriteLine("What this does NOT settle");
        Console.WriteLine("  - The oracle is a CEILING. It reads ground truth off the corpus id, so no real rater —");
        Console.WriteLine("    model or host-declared — reaches it. A weak result kills the idea; a strong one only");
        Console.WriteLine("    says a rater is worth costing.");
        Console.WriteLine("  - Ranking is NOT swept. SalienceWeight stays at the shipped 0, so this prices survival");
        Console.WriteLine("    (decay resistance + store admission) and says nothing about a ranking voice.");
        Console.WriteLine("  - The arms differ in COVERAGE as well as signal, unavoidably: novelty declines below");
        Console.WriteLine("    SalienceOptions.MinimumComparables while the oracle judges from write 1. That is a real");
        Console.WriteLine("    advantage of knowing rather than inferring, not an artefact — but it is not the");
        Console.WriteLine("    ratings DIFFERING, so read a small win with it in mind.");
        Console.WriteLine("  - The corpus has no LOW-IMPORTANCE-BUT-SOMETIMES-RELEVANT class. Its noise is never a");
        Console.WriteLine("    right answer, so routine material is priced as junk rather than as background, which");
        Console.WriteLine("    is the softer and more common real case.");
        Console.WriteLine("  - Relevance here is LEXICAL (D89), so a gain that is really semantic cannot appear.");
    }
}
