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
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>Salience ships ON and has never been measured once. Does it help?</b>
/// (<c>TASKS.md</c> Part 53.)
///
/// <para><b>Why "never once" is literal.</b> <see cref="MemoryPolicySweep"/>'s control C1 does not merely
/// omit salience — it reflects <c>ModulatedRetrievability</c>'s own retention collection on every constructed
/// engine to CONFIRM it is empty. And salience is doubly invisible even where nobody asserted its absence:
/// <see cref="StructuralSaliencePolicy"/> scores <see cref="SalienceContext.Novelty"/>, which the engine
/// derives from the similarity search it only performs when an <see cref="Lyntai.Embeddings.IEmbedder"/> AND
/// an <see cref="IVectorStore"/> are both supplied — and no harness supplied either. So both schema goldens,
/// the recall-quality pin and every policy study to date are salience-OFF, while the shipped default has it
/// ON for two of its three consumers (decay resistance and store admission priority; only the rank boost is
/// opt-in, per <c>docs/DECISIONS.md</c> D45).</para>
///
/// <para><b>The blocker on file was wrong, and cheaply so.</b> Part 53 recorded this as needing "a real
/// corpus … not codeable from here". <see cref="FakeEmbedder"/> is a deterministic feature-hashed
/// bag-of-words embedder that has been in the tree the whole time, and <see cref="InMemoryVectorStore"/>
/// ships in Core. Both arms below therefore get the identical embedder and vector store — enrichment,
/// similarity linking and the extra embed cost are held CONSTANT — and the only thing that varies is whether
/// anything judges salience and whether anything acts on it.</para>
///
/// <para><b>Two arms, and the OFF arm is not simply "no policies".</b> Both arms enrich identically; the ON
/// arm additionally registers <see cref="StructuralSaliencePolicy"/> (so writes are judged) and
/// <see cref="SalienceRetentionPolicy"/> (so the judgement lengthens a half-life). Holding enrichment
/// constant is what makes the difference attributable to salience rather than to the engine doing a vector
/// search at all.</para>
///
/// <para><b>What this can and cannot settle.</b> It measures salience's NET effect on this corpus. It does
/// NOT test the concern that novelty inverts on noisy input — this corpus's noise is TEMPLATED
/// (<c>"item noise{n} was {filler} mentioned once and never again"</c>), sharing a skeleton with every other
/// class, so under bag-of-words novelty the second noise entry onward reads as FAMILIAR rather than novel.
/// The corpus models noise as semantically irrelevant; that concern is about textually diverse junk, and
/// testing it needs a new corpus axis rather than an embedder. Stated here so a null result is not misread
/// as clearing the design question.</para>
/// </summary>
internal static class MemorySalienceSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 30;
    private const int QueryLimit = 10;
    private const string OffLabel = "SalienceOff";
    private const string OnLabel = "SalienceOn";

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm, double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();
        var graphOptions = new GraphMemoryOptions();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });

        // AttributeCount carries the subject-cued attribute CLUSTER — the "my wife is Alice, and the whole
        // relationship should stay relevant" case. Every shape gets it, because it is the class this study
        // most needs to price: salience's entire promise is "does not fade away", and a cluster stated once
        // and then only ever referred to obliquely is where fading away actually costs something.
        //
        // NOTE this makes the corpus different from the one earlier salience numbers were taken on, so those
        // are not comparable across this boundary — the arms here are still paired against each other on the
        // identical corpus, which is what the comparison rests on.
        var baseline = CorpusShape.Default with { AttributeCount = 3 };
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-reuse", baseline with { ReuseRatio = 10 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
        };

        string[] arms = [OffLabel, OnLabel];
        PrintPreamble(shapes, arms);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var retentionCounts = new ConcurrentBag<(string Arm, int Count)>();
        var judged = new ConcurrentBag<(string Arm, int Salient)>();

        async ValueTask RunOneAsync(int seed, Shape shape, string arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            var on = arm == OnLabel;
            // Both arms enrich identically — same embedder, same vector store — so the difference below is
            // salience and not "the engine performed a vector search".
            var embedder = new FakeEmbedder();
            var vectors = new InMemoryVectorStore();

            var counting = on ? new CountingSaliencePolicy() : null;
            IReadOnlyList<IMemoryRetentionPolicy> retention =
                on ? [new SalienceRetentionPolicy()] : [];

            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine(
                "salience",
                store,
                options: graphOptions,
                policy: new ModulatedRetrievability(new DsrRetrievability(), retention),
                agePolicies: [agePolicy],
                embedder: embedder,
                vectors: vectors,
                saliencePolicies: counting is null ? null : [counting],
                ranking: rrf);

            retentionCounts.Add((arm, retention.Count));

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            if (counting is not null) judged.Add((arm, counting.Salient));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        PrintControls(retentionCounts.ToList(), judged.ToList(), orderChecks.ToList());
        PrintTable(rows.ToList(), shapes, arms);
        PrintVerdict(rows.ToList(), shapes);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The rank boost. SalienceRankWeight is opt-in and stays 0 (D45), so this");
        Console.WriteLine("    measures the two consumers that actually ship ON.");
        Console.WriteLine("  - SalienceOptions' own constants (NoveltyWeight, MaxSalience, MinimumComparables).");
        Console.WriteLine("  - Whether novelty INVERTS on noisy input. This corpus's noise is templated, so a");
        Console.WriteLine("    null result here does NOT clear that concern — see the class doc.");
        Console.WriteLine("  - Embedder realism: FakeEmbedder is feature-hashed bag-of-words, so 'novelty'");
        Console.WriteLine("    here is lexical rather than semantic.");
        return 0;
    }

    /// <summary>Wraps the shipped policy purely to COUNT how often it judged anything salient. Without this
    /// the ON arm could be silently identical to OFF — a nearly-empty engine reports the neutral 1 until it
    /// holds <c>SalienceOptions.MinimumComparables</c> entries — and the study would read as "salience does
    /// nothing" when the truth was "salience never ran".</summary>
    private sealed class CountingSaliencePolicy : IMemorySaliencePolicy
    {
        private readonly StructuralSaliencePolicy _inner = new();
        private int _salient;

        public int Salient => Volatile.Read(ref _salient);

        public MemorySalienceProvenance Provenance => _inner.Provenance;

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            var signals = _inner.Signals(write, in context);
            if (signals.Count > 0) Interlocked.Increment(ref _salient);
            return signals;
        }
    }

    private static void PrintPreamble(IReadOnlyList<Shape> shapes, IReadOnlyList<string> arms)
    {
        Console.WriteLine("=== Salience: measured for the first time (TASKS.md Part 53) ===");
        Console.WriteLine();
        Console.WriteLine("Salience ships ON for two of its three consumers - decay resistance and store");
        Console.WriteLine("admission priority - and no measurement this repository has ever taken included it.");
        Console.WriteLine("MemoryPolicySweep's C1 control ASSERTS its absence; and novelty needs an embedder");
        Console.WriteLine("plus a vector store, which no harness supplied. Both arms here get both, so the");
        Console.WriteLine("only thing that varies is whether anything judges salience and acts on it.");
        Console.WriteLine();
        Console.WriteLine("WHAT THIS CANNOT SETTLE: whether novelty INVERTS on noisy input. This corpus's");
        Console.WriteLine("noise is TEMPLATED, so under bag-of-words novelty it reads as familiar rather than");
        Console.WriteLine("novel. A null result here does NOT clear that design concern.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms ({arms.Count}): {string.Join(", ", arms)}");
        Console.WriteLine($"Corpus shapes ({shapes.Count}): {string.Join(", ", shapes.Select(s => s.Label))}");
    }

    private static void PrintControls(IReadOnlyList<(string Arm, int Count)> retention,
        IReadOnlyList<(string Arm, int Salient)> judged, IReadOnlyList<bool> orderChecks)
    {
        Console.WriteLine();
        Console.WriteLine("=== Controls ===");
        foreach (var arm in retention.Select(r => r.Arm).Distinct().Order(StringComparer.Ordinal))
        {
            var counts = retention.Where(r => r.Arm == arm).Select(r => r.Count).Distinct().ToList();
            Console.WriteLine($"  {arm,-12} retention policies: {string.Join("/", counts)}");
        }

        var totalSalient = judged.Sum(j => j.Salient);
        Console.WriteLine($"  {OnLabel} judged something salient: " +
            $"{(totalSalient > 0 ? $"yes ({totalSalient} writes)" : "NO")}");
        if (totalSalient == 0)
            throw new InvalidOperationException(
                $"{OnLabel} never judged a single write salient, so it is silently identical to {OffLabel} " +
                "and the comparison is vacuous. StructuralSaliencePolicy reports the neutral 1 until an " +
                "engine holds SalienceOptions.MinimumComparables entries — check that before reading any " +
                "number below as evidence that salience does nothing.");

        Console.WriteLine($"  replay order matched the corpus's declared order: " +
            $"{orderChecks.Count(c => c)}/{orderChecks.Count}");
    }

    private static void PrintTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<string> arms)
    {
        Console.WriteLine();
        Console.WriteLine("=== MissRate / PollutionRate (mean over seeds) ===");
        Console.Write($"{"Shape",-16} {"Class",-24}");
        foreach (var arm in arms) Console.Write($" {arm + " miss",14} {arm + " poll",14}");
        Console.WriteLine();

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            Console.Write($"{cell.Key.Shape,-16} {cell.Key.Class,-24}");
            foreach (var arm in arms)
            {
                var xs = cell.Where(r => r.Arm == arm).ToList();
                if (xs.Count == 0) { Console.Write($" {"—",14} {"—",14}"); continue; }
                Console.Write($" {xs.Average(r => r.MissRate),14:F4} {xs.Average(r => r.PollutionRate),14:F4}");
            }
            Console.WriteLine();
        }
    }

    private static void PrintVerdict(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Paired MissRate difference, {OnLabel} vs {OffLabel} (negative = salience helps) ===");
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Δ miss",9} {"95% CI",22}");

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var combined = new List<double>();
        var significant = 0;
        var cells = 0;

        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            var bySeed = cell.GroupBy(r => r.Seed)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Arm, r => r.MissRate, StringComparer.Ordinal));
            var diffs = bySeed.Values
                .Where(a => a.ContainsKey(OnLabel) && a.ContainsKey(OffLabel))
                .Select(a => a[OnLabel] - a[OffLabel]).ToList();
            if (diffs.Count == 0) continue;

            var ci = MemoryPolicySweep.Ci95(diffs);
            var excludesZero = !double.IsNaN(ci.HalfWidth) && (ci.Lo > 0 || ci.Hi < 0);
            cells++;
            if (excludesZero) significant++;
            if (cell.Key.Class == "all (combined)") combined.Add(ci.Mean);

            Console.WriteLine($"{cell.Key.Shape,-16} {cell.Key.Class,-24} " +
                $"{ci.Mean,9:F4} {$"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]",22}{(excludesZero ? "  *" : "")}");
        }

        Console.WriteLine();
        Console.WriteLine("  * = the 95% interval excludes zero.");
        Console.WriteLine();
        Console.WriteLine("=== Verdict for TASKS.md Part 53 ===");
        var mean = combined.Count == 0 ? 0 : combined.Average();
        Console.WriteLine($"Mean combined Δ MissRate: {mean:F4}   ({significant}/{cells} cells significant)");
        Console.WriteLine();
        Console.WriteLine(mean switch
        {
            < -0.01 => "=> Salience HELPS on this corpus. The default-on setting is earning its place, which\n" +
                       "   is the first evidence either way it has ever had.",
            > 0.01 => "=> Salience HURTS on this corpus. A default-on mechanism is making recall worse, and\n" +
                      "   that is a shipped defect rather than an open question. Note what it still does not\n" +
                      "   prove: the templated-noise caveat means the MECHANISM of the harm is unidentified.",
            _ => "=> Salience is INERT on this corpus - no measurable effect either way. That is not a\n" +
                 "   clean bill of health: it is default-ON surface with no demonstrated benefit, and the\n" +
                 "   inversion concern is untestable here. The honest reading is that it costs an embed per\n" +
                 "   write and buys nothing this instrument can see.",
        });
    }
}
