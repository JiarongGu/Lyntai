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
/// <b>Is BOUNDED growth better than both compounding growth and no growth at all?</b> The question the
/// preceding four studies converge on, and the one that decides whether 3.0 should change a shipped default
/// (<c>TASKS.md</c> Part 64, <c>docs/DECISIONS.md</c> D53).
///
/// <para><b>The prediction being tested, stated before the run.</b> Three retrievability-raising mechanisms
/// were measured on 2026-08-12: salience (clamped) HELPS, the age reset a recall performs (age → 0, no
/// accumulation) HELPS, and stability growth (multiplies on EVERY recall, unbounded until a distant ceiling)
/// HURTS badly. The proposed principle is that what separates them is not where the signal comes from but
/// whether the effect ACCUMULATES WITHOUT BOUND. If that is right, a growth rule that cannot compound should
/// beat BOTH the shipped rule and switching growth off — and if it is wrong, no bounded variant will beat
/// simply not growing.</para>
///
/// <para><b>Why this is not the corpus-fitting D49 refused.</b> It contrasts functional FORMS, not values on
/// a continuum: compounding versus capped-compounding versus count-based. The constants inside each form are
/// deliberately round numbers rather than tuned ones, because the claim under test is about the shape of the
/// rule. A form that only wins at one carefully chosen constant would not support the principle anyway.</para>
///
/// <list type="number">
/// <item><c>Shipped</c> — <see cref="DsrRetrievability"/> at its defaults. Compounding, ceiling far away.</item>
/// <item><c>NoGrowth</c> — <c>ReinforceGain = 0</c>. The recall still TOUCHES (age resets); only the growth is
/// gone. This is the best configuration measured so far and the incumbent this must beat.</item>
/// <item><c>CappedGrowth</c> — the identical shipped rule with <c>MaxStability</c> pulled down to 3×
/// <c>InitialStability</c>. Compounding is still the mechanism; it just cannot run away. Isolates "bounded"
/// from "differently shaped".</item>
/// <item><c>CountGrowth</c> — <see cref="RecallCountGrowthRetrievability"/>: stability is a pure function of
/// how many times the entry has been recalled, never of its own current value, so it CANNOT compound by
/// construction. This is the principle's own shape.</item>
/// </list>
///
/// <para><b>Reading, fixed before the run:</b> if <c>CappedGrowth</c> and/or <c>CountGrowth</c> beat both
/// <c>Shipped</c> and <c>NoGrowth</c>, bounded growth is real and 3.0 has a concrete default to adopt. If
/// they land between the two, bounding helps but growth still does not earn its place. If <c>NoGrowth</c>
/// still wins outright, the honest conclusion is that this engine's growth mechanism earns nothing on this
/// corpus in ANY bounded form, which is a much stronger statement than any single knob.</para>
/// </summary>
internal static class MemoryBoundedGrowthSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 30;
    private const int QueryLimit = 10;
    private const string ShippedLabel = "Shipped";
    private const string NoGrowthLabel = "NoGrowth";

    private sealed record Arm(string Label, IMemoryRetrievabilityPolicy Curve);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm, double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var agePolicy = new PerWriteAgePolicy();
        IReadOnlyList<IMemoryRetentionPolicy> retention = [];
        var graphOptions = new GraphMemoryOptions();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });

        var shipped = new DsrOptions();
        var shippedCurve = new DsrRetrievability(shipped);

        var arms = new[]
        {
            new Arm(ShippedLabel, shippedCurve),
            new Arm(NoGrowthLabel, new DsrRetrievability(shipped with { ReinforceGain = 0 })),
            new Arm("CappedGrowth", new DsrRetrievability(shipped with { MaxStability = shipped.InitialStability * 3 })),
            new Arm("CountGrowth", new RecallCountGrowthRetrievability(shippedCurve, gain: 1.0)),
        };

        var baseline = CorpusShape.Default;
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("low-reuse", baseline with { ReuseRatio = 1 }),
            new("high-reuse", baseline with { ReuseRatio = 10 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("rare-critical", baseline with { CriticalRarity = 12 }),
        };

        PrintPreamble(arms, shapes, shipped);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var grew = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "bounded",
                new SqliteMemoryGraphStore(db.Factory),
                options: graphOptions,
                retrievability: new ModulatedRetrievability(arm.Curve, retention),
                agePolicies: [agePolicy],
                ranking: rrf);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            if (arm.Curve is RecallCountGrowthRetrievability c)
                grew.AddOrUpdate(arm.Label, c.GrewCount, (_, v) => v + c.GrewCount);
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        Console.WriteLine();
        Console.WriteLine("=== Controls ===");
        Console.WriteLine($"  replay order matched the corpus's declared order: " +
            $"{orderChecks.Count(c => c)}/{orderChecks.Count}");
        var countGrew = grew.TryGetValue("CountGrowth", out var g) ? g : 0;
        Console.WriteLine($"  CountGrowth actually GREW a stability: " +
            $"{(countGrew > 0 ? $"yes ({countGrew} times)" : "NO")}");
        if (countGrew == 0)
            throw new InvalidOperationException(
                "CountGrowth never grew a stability, so it is silently a second NoGrowth arm and the whole " +
                "comparison is vacuous while looking like a result.");

        var all = rows.ToList();
        PrintTable(all, shapes, arms);
        PrintVerdict(all, shapes, arms);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The constants inside each FORM. CappedGrowth's 3x and CountGrowth's gain are");
        Console.WriteLine("    round numbers, not tuned ones: the claim under test is the shape of the rule,");
        Console.WriteLine("    and a form that only wins at one chosen constant would not support it anyway.");
        Console.WriteLine("  - Salience, ranking and difficulty, all at shipped defaults.");
        Console.WriteLine("  - Any interaction between the growth form and the age policy.");
        return 0;
    }

    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes, DsrOptions shipped)
    {
        Console.WriteLine("=== Bounded growth: does limiting the compounding beat removing it? ===");
        Console.WriteLine();
        Console.WriteLine("PREDICTION UNDER TEST: three retrievability-raising mechanisms were measured today.");
        Console.WriteLine("Salience (clamped) helps. The age reset a recall performs (age -> 0, no");
        Console.WriteLine("accumulation) helps. Stability growth, which multiplies on EVERY recall, hurts");
        Console.WriteLine("badly. The proposed principle is that what separates them is whether the effect");
        Console.WriteLine("ACCUMULATES WITHOUT BOUND - not where the signal comes from.");
        Console.WriteLine();
        Console.WriteLine("If that is right, a growth rule that CANNOT compound should beat both the shipped");
        Console.WriteLine("rule and switching growth off. If it is wrong, no bounded variant beats NoGrowth.");
        Console.WriteLine();
        Console.WriteLine("Reading fixed BEFORE the run:");
        Console.WriteLine("  bounded arm beats Shipped AND NoGrowth => principle holds; 3.0 has a default to adopt");
        Console.WriteLine("  bounded arm lands between them        => bounding helps, growth still unearned");
        Console.WriteLine("  NoGrowth still wins outright          => growth earns nothing in ANY bounded form");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"InitialStability {shipped.InitialStability}, shipped MaxStability {shipped.MaxStability}");
        Console.WriteLine($"Arms ({arms.Count}): {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"Corpus shapes ({shapes.Count}): {string.Join(", ", shapes.Select(s => s.Label))}");
    }

    private static void PrintTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine("=== MissRate (mean over seeds) ===");
        Console.Write($"{"Shape",-16} {"Class",-24}");
        foreach (var arm in arms) Console.Write($" {arm.Label,14}");
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
                var xs = cell.Where(r => r.Arm == arm.Label).Select(r => r.MissRate).ToList();
                Console.Write(xs.Count == 0 ? $" {"—",14}" : $" {xs.Average(),14:F4}");
            }
            Console.WriteLine();
        }
    }

    private static void PrintVerdict(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Paired MissRate difference vs {NoGrowthLabel} — the incumbent to beat ===");
        Console.WriteLine("(negative = better than simply not growing)");
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Arm",-14} {"Δ miss",9} {"95% CI",22}");

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var combined = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            var bySeed = cell.GroupBy(r => r.Seed)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Arm, r => r.MissRate, StringComparer.Ordinal));

            foreach (var arm in arms)
            {
                if (arm.Label == NoGrowthLabel) continue;
                var diffs = bySeed.Values
                    .Where(a => a.ContainsKey(arm.Label) && a.ContainsKey(NoGrowthLabel))
                    .Select(a => a[arm.Label] - a[NoGrowthLabel]).ToList();
                if (diffs.Count == 0) continue;

                var ci = MemoryPolicySweep.Ci95(diffs);
                var star = !double.IsNaN(ci.HalfWidth) && (ci.Lo > 0 || ci.Hi < 0) ? "  *" : "";
                Console.WriteLine($"{cell.Key.Shape,-16} {cell.Key.Class,-24} {arm.Label,-14} " +
                    $"{ci.Mean,9:F4} {$"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]",22}{star}");

                if (cell.Key.Class != "all (combined)") continue;
                if (!combined.TryGetValue(arm.Label, out var list)) combined[arm.Label] = list = [];
                list.Add(ci.Mean);
            }
        }

        Console.WriteLine();
        Console.WriteLine("  * = the 95% interval excludes zero.");
        Console.WriteLine();
        Console.WriteLine($"=== Verdict for TASKS.md Part 64 ===");
        Console.WriteLine($"Mean COMBINED MissRate Δ vs {NoGrowthLabel} (negative = beats not growing):");
        foreach (var (arm, values) in combined.OrderBy(kv => kv.Value.Average()))
            Console.WriteLine($"  {arm,-14} {values.Average(),8:F4}");

        Console.WriteLine();
        if (combined.Count == 0) { Console.WriteLine("no combined cells"); return; }

        var best = combined.OrderBy(kv => kv.Value.Average()).First();
        var bestArm = best.Key;
        var bestMean = best.Value.Average();
        Console.WriteLine(bestMean switch
        {
            < -0.005 => $"=> BOUNDED GROWTH WINS. '{bestArm}' beats not growing at all, so the principle holds:\n" +
                        "   the damage was the COMPOUNDING, not the growth. 3.0 has a concrete default to\n" +
                        "   adopt rather than a knob to switch off, and the FSRS model keeps its place.",
            < 0.005 => $"=> NO BOUNDED FORM BEATS SIMPLY NOT GROWING (best '{bestArm}', within noise).\n" +
                       "   Bounding removes the harm but adds nothing back. The growth mechanism earns\n" +
                       "   nothing on this corpus in any form tested, which is a stronger and more\n" +
                       "   uncomfortable statement than any single knob - and it makes ReinforceGain = 0\n" +
                       "   the honest default rather than a blunt instrument.",
            _ => $"=> EVERY GROWTH FORM IS WORSE THAN NOT GROWING (best '{bestArm}' still positive).\n" +
                 "   Bounding does not rescue it. Growth is actively harmful here regardless of shape.",
        });
    }
}

/// <summary>Growth that CANNOT compound: stability is a pure function of how many times the entry has been
/// recalled, never of its own current value.
///
/// <para><b>This is the principle's own shape, which is why the arm exists.</b> The shipped rule multiplies
/// the CURRENT stability on every recall, so each reinforcement is applied on top of every previous one and a
/// small ranking bias becomes a permanent one. Here the same entry recalled the same number of times always
/// lands on the same stability, whatever order things happened in — so repetition raises retrievability
/// without the raise feeding itself.</para>
///
/// <para><b>Logarithmic in the count, deliberately</b>: the tenth recall should say less than the second, which
/// is the one intuition FSRS's stabilization-decay law and this rule agree on. The gain is a round number, not
/// a tuned one — the claim under test is the FORM.</para>
///
/// <para><b>Difficulty still comes from the real curve</b>, so this arm differs from the shipped one in the
/// stability rule alone rather than in two things at once. And the result is floored at the entry's current
/// stability, honouring <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>'s "never smaller than the current
/// one" guarantee.</para></summary>
internal sealed class RecallCountGrowthRetrievability(DsrRetrievability inner, double gain)
    : IMemoryRetrievabilityPolicy
{
    private int _grew;

    public int GrewCount => Volatile.Read(ref _grew);

    public double InitialStability => inner.InitialStability;

    public MemoryRetrievabilityProvenance Provenance => inner.Provenance;

    public double Retrievability(in MemoryDecayState state) => inner.Retrievability(state);

    public double CandidateCutoff(double minRetrievability) => inner.CandidateCutoff(minRetrievability);

    public double? DerivedGrade(in MemoryDecayState state) => inner.DerivedGrade(state);

    public MemoryDecayState Reinforce(in MemoryDecayState state)
    {
        // +1 because this recall is the one happening now; RecallCount is the stored, pre-touch value.
        var target = InitialStability * (1 + gain * Math.Log(1 + state.RecallCount + 1));
        var grown = Math.Max(state.Stability, target);
        if (grown > state.Stability) Interlocked.Increment(ref _grew);
        return state with { Stability = grown, Difficulty = inner.Reinforce(state).Difficulty };
    }
}
