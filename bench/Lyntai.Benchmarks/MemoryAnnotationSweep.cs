using System.Collections.Concurrent;
using System.Diagnostics;

using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>Does knowing what a fact is ABOUT move the one number nothing else could?</b>
///
/// <para>Cluster recall sits at the no-graph floor — <c>miss = 1 - 1/AttributeCount = 0.667</c> — and is
/// IDENTICAL at recall limit 10 and 50, which proves those entries are never gathered and that no ranking
/// policy can reach them. The edge census says why: co-activation links whatever a recall returned together,
/// which produced 2 of 3 cluster pairs in English and 0 in Chinese. <c>IMemoryAnnotationPolicy</c> is the
/// only mechanism that addresses it; this measures whether it does.</para>
///
/// <para><b>The annotator here is PERFECT by construction, and that is the point rather than a flaw.</b> It
/// reads the corpus's own id convention and returns one shared subject for every attribute entry — so this
/// measures the MECHANISM'S CEILING, not a model's accuracy. Two different questions, and conflating them is
/// how a disappointing model gets blamed on a sound design or vice versa. A real annotator can only do worse
/// than this; if the ceiling is low, no prompt will rescue it.</para>
///
/// <para>English and Chinese, because the whole argument for putting a model here rather than in the
/// tokenizer is that the judgement is language-independent. If the gain appears in one language and not the
/// other, that argument is wrong and it should show up here.</para>
/// </summary>
internal static class MemoryAnnotationSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 20;
    private const int QueryLimit = 10;

    /// <summary>Returns one shared subject for every attribute entry, read from the corpus's own id
    /// convention ("{leading} attribute0 …"). Deterministic, so a run is reproducible and a difference is
    /// the mechanism's rather than a sampling artefact.</summary>
    private sealed class PerfectAnnotator : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            var parts = request.Write.Content.Split(' ');
            var id = parts.Length > 1 ? parts[1] : "";
            return Task.FromResult(id.StartsWith("attribute", StringComparison.Ordinal)
                ? new MemoryAnnotation(["owner"])
                : MemoryAnnotation.None);
        }
    }

    private sealed record Arm(string Label, bool Annotated);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Language, string Shape, string Class, string Arm,
        double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });

        Arm[] arms = [new("off", false), new("annotated", true)];
        CorpusLanguage[] languages = [CorpusLanguage.English, CorpusLanguage.Chinese];

        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("common-cue", baseline with { AttributeCue = AttributeCueKind.SharesCommonTokens }),
        };

        PrintPreamble(arms, shapes, languages);

        var rows = new ConcurrentBag<Row>();

        async ValueTask RunOneAsync(int seed, CorpusLanguage language, Shape shape, Arm arm)
        {
            var corpus = MemoryCorpus.Generate(shape.Value with { Language = language }, seed);

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine("annotation", new SqliteMemoryGraphStore(db.Factory),
                retrievability: new DsrRetrievability(), agePolicies: [new PerWriteAgePolicy()], ranking: rrf,
                annotation: arm.Annotated ? new PerfectAnnotator() : null);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, language.ToString(), shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds
            from language in languages
            from shape in shapes
            from arm in arms
            select (seed, language, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.language, item.shape, item.arm));

        PrintComparison([.. rows], shapes, languages);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{languages.Length} language(s), {shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT measured, stated rather than left implicit:");
        Console.WriteLine("  - A REAL annotator's accuracy. This one is perfect by construction, so every");
        Console.WriteLine("    number here is the mechanism's CEILING. A model can only do worse.");
        Console.WriteLine("  - The cost of annotating: one model call per write, plus one no-query read for");
        Console.WriteLine("    context. Nothing here is a latency measurement.");
        Console.WriteLine("  - Whether a real annotator produces STABLE subjects across writes, which is the");
        Console.WriteLine("    property the whole mechanism rests on and the one most likely to fail in the");
        Console.WriteLine("    field — a model that phrases the same entity differently each time links");
        Console.WriteLine("    nothing, and would look exactly like the 'off' arm here.");
        return 0;
    }

    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes,
        IReadOnlyList<CorpusLanguage> languages)
    {
        Console.WriteLine("=== Subject annotation: does knowing what a fact is ABOUT move cluster recall? ===");
        Console.WriteLine();
        Console.WriteLine("QUESTION: cluster recall sits at the no-graph floor (miss = 1 - 1/AttributeCount =");
        Console.WriteLine("0.667) and is IDENTICAL at recall limit 10 and 50 — so those entries are never");
        Console.WriteLine("gathered and no ranking policy can reach them. Annotation is the only mechanism");
        Console.WriteLine("that addresses it. This measures whether it does.");
        Console.WriteLine();
        Console.WriteLine("THE ANNOTATOR IS PERFECT BY CONSTRUCTION. It reads the corpus's own id convention");
        Console.WriteLine("and returns one shared subject for every attribute entry, so these numbers are the");
        Console.WriteLine("MECHANISM'S CEILING, not a model's accuracy. A real annotator can only do worse; if");
        Console.WriteLine("the ceiling is low, no prompt rescues it.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms: {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"Languages: {string.Join(", ", languages)} — the argument for a model here is that");
        Console.WriteLine("  the judgement is language-independent; if the gain appears in one and not the");
        Console.WriteLine("  other, that argument is wrong and it shows up here.");
        Console.WriteLine($"Shapes (AttributeCount=3): {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine("Pairing: both arms replay the IDENTICAL corpus at each (seed, language, shape), so");
        Console.WriteLine("the per-seed difference cancels corpus variance.");
    }

    private static void PrintComparison(IReadOnlyList<Row> all, IReadOnlyList<Shape> shapes,
        IReadOnlyList<CorpusLanguage> languages)
    {
        const string cluster = "attribute (subject cue)";

        Console.WriteLine();
        Console.WriteLine("--- MissRate on the CLUSTER class, paired by (seed, language, shape) ---");
        Console.WriteLine("(negative delta = annotation RECOVERS material; 0.6667 is the no-graph floor)");
        Console.WriteLine();
        Console.WriteLine($"{"language",-10} {"shape",-16} {"off",9} {"annotated",10} {"delta",9}  95% CI");

        foreach (var language in languages)
            foreach (var shape in shapes)
                PrintRow(all, language, shape, cluster);

        Console.WriteLine();
        Console.WriteLine("--- MissRate on ALL queries combined — does it cost the rest of recall? ---");
        Console.WriteLine();
        Console.WriteLine($"{"language",-10} {"shape",-16} {"off",9} {"annotated",10} {"delta",9}  95% CI");

        foreach (var language in languages)
            foreach (var shape in shapes)
                PrintRow(all, language, shape, "all (combined)");

        Console.WriteLine();
        Console.WriteLine("--- PollutionRate on the CLUSTER class (means only) ---");
        Console.WriteLine();
        Console.WriteLine($"{"language",-10} {"shape",-16} {"off",9} {"annotated",10} {"delta",9}");

        foreach (var language in languages)
            foreach (var shape in shapes)
            {
                var cells = Cells(all, language, shape, cluster);
                if (cells.Off.Count == 0 || cells.On.Count == 0) continue;
                var off = cells.Off.Average(r => r.PollutionRate);
                var on = cells.On.Average(r => r.PollutionRate);
                Console.WriteLine($"{language,-10} {shape.Label,-16} {off,9:F4} {on,10:F4} {on - off,9:F4}");
            }

        Console.WriteLine();
        Console.WriteLine("* = the paired 95% CI excludes zero.");
    }

    private static (List<Row> Off, List<Row> On) Cells(IReadOnlyList<Row> all, CorpusLanguage language,
        Shape shape, string cls)
    {
        bool Match(Row r, string arm) =>
            r.Language == language.ToString() && r.Shape == shape.Label && r.Class == cls && r.Arm == arm;
        return ([.. all.Where(r => Match(r, "off"))], [.. all.Where(r => Match(r, "annotated"))]);
    }

    private static void PrintRow(IReadOnlyList<Row> all, CorpusLanguage language, Shape shape, string cls)
    {
        var (offRows, onRows) = Cells(all, language, shape, cls);
        if (offRows.Count == 0 || onRows.Count == 0) return;

        var offBySeed = offRows.ToDictionary(r => r.Seed, r => r.MissRate);
        var onBySeed = onRows.ToDictionary(r => r.Seed, r => r.MissRate);
        var paired = offBySeed.Where(e => onBySeed.ContainsKey(e.Key))
            .Select(e => (Off: e.Value, On: onBySeed[e.Key])).ToList();
        if (paired.Count == 0) return;

        var off = paired.Average(p => p.Off);
        var on = paired.Average(p => p.On);
        var ci = MemoryPolicySweep.Ci95([.. paired.Select(p => p.On - p.Off)]);

        Console.WriteLine($"{language,-10} {shape.Label,-16} {off,9:F4} {on,10:F4} {on - off,9:F4}  " +
            $"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]{(ci.Lo > 0 || ci.Hi < 0 ? " *" : "")}");
    }
}
