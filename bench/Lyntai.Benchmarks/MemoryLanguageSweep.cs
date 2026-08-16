using System.Collections.Concurrent;
using System.Diagnostics;

using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>What does this library's recall quality actually look like in a language that writes no spaces?</b>
///
/// <para><b>Why this exists.</b> Every recall-quality figure this repository has ever published was measured
/// on English, space-separated text — the friendliest tokenization the library supports — and that was
/// recorded as a known blind spot rather than measured (design §5.7.0). Looking at it directly on 2026-08-12
/// found the blind spot was hiding a defect, not just a favourable condition: a CJK query was split on
/// whitespace, so an entire sentence became ONE token and could only match an entry containing that exact
/// substring. <c>SearchTerms</c> fixed the retrieval path (<c>docs/DECISIONS.md</c> D55). This closes the
/// other half — the MEASUREMENT — because a fix that is pinned by tests is still not a fix whose cost anyone
/// has read off an instrument.</para>
///
/// <para><b>One factor: <see cref="CorpusLanguage"/>.</b> Everything else is held at the shipped 3.0 default —
/// <see cref="ReciprocalRankFusionPolicy"/> ranking, <see cref="DsrRetrievability"/> with its shipped
/// options, difficulty live — because the question is what a consumer deploying this release in Chinese
/// actually gets, not where the policy space is best.</para>
///
/// <para><b>The pairing is unusually strong here, and it is what makes the comparison mean anything.</b> The
/// arms do NOT replay the identical corpus object — they cannot, the text is the whole difference. They
/// replay STRUCTURALLY IDENTICAL corpora: same step kinds in the same order, same entry ids, same
/// ground-truth sets, same interference counts, differing only in text. That is pinned directly by
/// <c>MemoryCorpusTests.Every_language_produces_a_timeline_structurally_identical_to_english</c>, and it
/// holds because the generator's control flow depends only on the shape and the seed, and every filler list
/// has the same length so the seeded PRNG advances in lockstep. Without that property a gap between the arms
/// could be a difference in the timelines rather than in the language, and nothing here would be
/// interpretable.</para>
///
/// <para><b>FIVE arms, not two, and this doc said two until 2026-08-14.</b> <see cref="CorpusLanguage"/> is
/// read with <c>Enum.GetValues</c>, so the roster is whatever that enum declares — English, Chinese,
/// Japanese, Korean and <c>ChineseMixed</c> (Chinese technical prose with English terms embedded without
/// spaces, the shape a Latin word inside a CJK run used to be shredded by). The pinning test was renamed
/// from <c>The_two_languages_…</c> to the theory above when the axis grew, and the two citations here were
/// not repointed: a <c>&lt;c&gt;</c>-quoted name resolves nothing, so neither the compiler nor any gate
/// could see it.</para>
///
/// <para><b>What a gap MEANS, decided before the numbers arrive.</b> A Chinese run is not expected to match
/// English: a spaceless sentence expands to many more trigrams than an English sentence has words, so the
/// query is wider and pollution should rise. The honest reading is therefore directional — <b>this reports
/// the size of a known cost, and adopts nothing.</b> It is a measurement, not a tuning run, and there is no
/// arm here that anyone could ship.</para>
/// </summary>
internal static class MemoryLanguageSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 30;
    private const int QueryLimit = 10;

    private sealed record Arm(string Label, CorpusLanguage Language);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm, double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();
        IReadOnlyList<IMemoryRetentionPolicy> retentionPolicies = [];
        var graphOptions = new GraphMemoryOptions();

        var shipped = new DsrOptions();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });
        var curve = new ModulatedRetrievability(new DsrRetrievability(shipped), retentionPolicies);

        // English FIRST and by construction: it is the reference every other arm is differenced against, and
        // the run-time pairing control below compares each arm's timeline to arms[0]'s.
        var arms = Enum.GetValues<CorpusLanguage>()
            .OrderBy(l => l == CorpusLanguage.English ? 0 : 1)
            .ThenBy(l => l.ToString(), StringComparer.Ordinal)
            .Select(l => new Arm(l.ToString(), l))
            .ToArray();

        // The attribute cluster is carried on EVERY shape here, unlike the other sweeps. It is the class the
        // language question actually turns on: a cue that names only the subject, with the whole cluster
        // declared relevant, is the case a consumer described ("even if I don't mention my wife, this entire
        // relationship should stay relevant") and the only class whose retrieval runs purely on language
        // rather than on the corpus's ASCII ids.
        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-reuse", baseline with { ReuseRatio = 10 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
            // the cue kind is itself a factor for CJK, and the English measurement already showed the gap
            // between the two dwarfs every policy effect — so both conditions run in both languages
            new("common-cue", baseline with { AttributeCue = AttributeCueKind.SharesCommonTokens }),
        };

        PrintPreamble(arms, shapes, shipped);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel, CorpusLanguage Lang), MemoryCorpus>();
        MemoryCorpus CorpusFor(int seed, Shape shape, Arm arm) =>
            corpusCache.GetOrAdd((seed, shape.Label, arm.Language),
                key => MemoryCorpus.Generate(shape.Value with { Language = key.Lang }, key.Seed));

        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var skeletonChecks = new ConcurrentBag<bool>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = CorpusFor(seed, shape, arm);
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            // The pairing control, checked at RUN time rather than trusted from a unit test: this arm's
            // timeline must be structurally identical to the English arm's at the same (seed, shape). An arm
            // whose corpus quietly diverged would produce a difference this study would report as a LANGUAGE
            // effect, which is precisely the failure it must not have.
            skeletonChecks.Add(Skeleton(corpus)
                .SequenceEqual(Skeleton(CorpusFor(seed, shape, arms[0]))));

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "language",
                new SqliteMemoryGraphStore(db.Factory),
                options: graphOptions,
                retrievability: curve,
                agePolicies: [agePolicy],
                ranking: rrf);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        var all = rows.ToList();
        PrintControlsConfirmed(orderChecks.ToList(), skeletonChecks.ToList());
        PrintComparison(all, shapes, arms);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit, per the measurement doc's own rule —");
        Console.WriteLine("a silent cap reads as \"covered everything\"):");
        Console.WriteLine("  - Mixed-script content beyond what each arm already carries. Real deployments");
        Console.WriteLine("    interleave scripts constantly; each corpus here is its own prose plus ASCII ids,");
        Console.WriteLine("    which is one point in that space. (Japanese does mix kanji/hiragana/katakana");
        Console.WriteLine("    inside one spaceless run, so it covers more of it than the others.)");
        Console.WriteLine("  - Any language outside CJK+English. Nothing here says what happens to Thai, which");
        Console.WriteLine("    is spaceless and NOT in SearchTerms' spaceless-script ranges at all.");
        Console.WriteLine("  - Policy alternatives. Everything but the language is held at the 3.0 shipped");
        Console.WriteLine("    default, so this measures the release rather than searching the policy space.");
        Console.WriteLine("  - Any claim that the Chinese arm is 'good enough'. This reports the size of a");
        Console.WriteLine("    known cost; §5.7.0's objective is what says whether a number is acceptable.");
        return 0;
    }

    /// <summary>Ids and ground truth only — the text is exactly what the arms are allowed to differ in.
    /// Mirrors <c>MemoryCorpusTests.Every_language_produces_a_timeline_structurally_identical_to_english</c>
    /// deliberately: two independent readers of the same property, not one deriving from the other.</summary>
    private static List<string> Skeleton(MemoryCorpus corpus) =>
        [.. corpus.Steps.Select(s => s switch
        {
            CorpusWrite w => $"W:{w.Write.Content.Split(' ')[1]}",
            CorpusQuery q => $"Q:{string.Join(",", q.RelevantIds)}",
            CorpusExpand e => $"E:{e.EntryId}",
            _ => throw new InvalidOperationException($"unhandled step {s.GetType().Name}"),
        })];

    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes, DsrOptions shipped)
    {
        Console.WriteLine("=== Language sensitivity: English vs Chinese (docs/DECISIONS.md D55) ===");
        Console.WriteLine();
        Console.WriteLine("QUESTION: what does recall quality look like in a language that writes no spaces?");
        Console.WriteLine();
        Console.WriteLine("Every recall-quality figure this repository has published was measured on English,");
        Console.WriteLine("space-separated text — the friendliest tokenization the library supports. That was a");
        Console.WriteLine("recorded blind spot (design §5.7.0), and looking at it directly found it was hiding a");
        Console.WriteLine("DEFECT rather than a favourable condition: a CJK query was split on whitespace, so a");
        Console.WriteLine("whole sentence became one token and matched only an exact substring. SearchTerms fixed");
        Console.WriteLine("retrieval (D55); this closes the measurement half.");
        Console.WriteLine();
        Console.WriteLine("THIS ADOPTS NOTHING. There is no shippable arm here — the language is the consumer's,");
        Console.WriteLine("not a setting. It reports the SIZE of a known cost.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms ({arms.Count}), one factor: {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine("Held at the SHIPPED 3.0 default: ranking = ReciprocalRankFusionPolicy, curve =");
        Console.WriteLine($"  DsrRetrievability (InitialStability={shipped.InitialStability}, Decay={shipped.Decay}, " +
            $"ReinforceGain={shipped.ReinforceGain}), difficulty live.");
        Console.WriteLine($"Corpus shapes ({shapes.Count}), all carrying the attribute cluster (AttributeCount=3): " +
            string.Join(", ", shapes.Select(s => s.Label)));
        Console.WriteLine("Pairing: the arms replay STRUCTURALLY IDENTICAL corpora — same step kinds, same ids,");
        Console.WriteLine("same ground truth, differing only in text — verified per (seed, shape) at run time.");
    }

    private static void PrintControlsConfirmed(IReadOnlyList<bool> orderChecks, IReadOnlyList<bool> skeletonChecks)
    {
        Console.WriteLine();
        Console.WriteLine("--- Controls confirmed ---");
        Console.WriteLine($"  replay order matched the declared timeline: {orderChecks.Count(x => x)}/{orderChecks.Count}");
        Console.WriteLine($"  arm corpora structurally identical to English: {skeletonChecks.Count(x => x)}/{skeletonChecks.Count}");
        if (orderChecks.Any(x => !x) || skeletonChecks.Any(x => !x))
            Console.WriteLine("  *** A CONTROL FAILED — every number below is uninterpretable. ***");
    }

    /// <summary>Paired by (seed, shape, class): Chinese minus English, so corpus variance cancels and the
    /// interval is over the DIFFERENCE rather than over two independent means.</summary>
    private static void PrintComparison(IReadOnlyList<Row> all, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        var classes = all.Select(r => r.Class).Distinct()
            .OrderBy(c => MemoryPolicySweep.ClassOrder.GetValueOrDefault(c, 99))
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        var reference = arms[0].Label;
        var others = arms.Skip(1).ToList();

        Console.WriteLine();
        Console.WriteLine($"--- MissRate vs {reference}, paired by (seed, shape, class) ---");
        Console.WriteLine("(positive delta = that language misses MORE; CI is the paired 95% interval on the");
        Console.WriteLine("difference, so corpus variance cancels. * = the interval excludes zero.)");

        foreach (var arm in others)
        {
            Console.WriteLine();
            Console.WriteLine($"{arm.Label} vs {reference}");
            Console.WriteLine($"  {"shape",-16} {"class",-26} {reference,9} {arm.Label,9} {"delta",9}  95% CI");

            foreach (var shape in shapes)
                foreach (var cls in classes)
                {
                    var pairs = PairsFor(all, shape.Label, cls, reference, arm.Label);
                    if (pairs.Count == 0) continue;

                    var baseline = pairs.Average(p => p.Reference);
                    var other = pairs.Average(p => p.Other);
                    var ci = MemoryPolicySweep.Ci95([.. pairs.Select(p => p.Other - p.Reference)]);

                    Console.WriteLine($"  {shape.Label,-16} {cls,-26} {baseline,9:F4} {other,9:F4} " +
                        $"{other - baseline,9:F4}  [{ci.Lo,7:F4}, {ci.Hi,7:F4}]" +
                        $"{(ci.Lo > 0 || ci.Hi < 0 ? " *" : "")}");
                }
        }

        Console.WriteLine();
        Console.WriteLine("--- PollutionRate by language (means only, matching the other sweeps) ---");
        Console.WriteLine();
        Console.Write($"{"shape",-16} {"class",-26}");
        foreach (var arm in arms) Console.Write($"{arm.Label,10}");
        Console.WriteLine();

        foreach (var shape in shapes)
            foreach (var cls in classes)
            {
                var cells = arms
                    .Select(a => all.Where(r => r.Shape == shape.Label && r.Class == cls && r.Arm == a.Label)
                                    .ToList())
                    .ToList();
                if (cells.Any(c => c.Count == 0)) continue;

                Console.Write($"{shape.Label,-16} {cls,-26}");
                foreach (var cell in cells) Console.Write($"{cell.Average(r => r.PollutionRate),10:F4}");
                Console.WriteLine();
            }
    }

    private static List<(int Seed, double Reference, double Other)> PairsFor(
        IReadOnlyList<Row> all, string shape, string cls, string reference, string other)
    {
        Dictionary<int, double> ByArm(string arm) =>
            all.Where(r => r.Shape == shape && r.Class == cls && r.Arm == arm)
               .ToDictionary(r => r.Seed, r => r.MissRate);

        var a = ByArm(reference);
        var b = ByArm(other);

        return [.. a.Where(e => b.ContainsKey(e.Key))
            .Select(e => (e.Key, e.Value, b[e.Key]))
            .OrderBy(p => p.Key)];
    }
}
