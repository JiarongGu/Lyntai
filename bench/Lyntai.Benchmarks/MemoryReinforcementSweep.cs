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
/// <b>Does reinforcement itself hurt recall here, or only law 3's dependence on <c>r</c>?</b>
/// The one question <see cref="MemorySpacingSweep"/> raised and structurally could not answer
/// (<c>TASKS.md</c> Part 64, <c>docs/DECISIONS.md</c> D53).
///
/// <para><b>Why the previous study could not.</b> <see cref="DsrOptions.SpacingWeight"/> multiplies the
/// WHOLE increase term, so setting it to zero disables all three FSRS laws together — "recall never
/// strengthens anything", in the property's own words. Its best arm was therefore *reinforcement switched
/// off*, which is consistent with two very different diagnoses and distinguishes neither.
/// <see cref="DsrOptions.ReinforceGain"/> has the identical problem. Nothing shipped separates them.</para>
///
/// <para><b>The three arms, chosen so exactly one comparison answers it.</b>
/// <list type="number">
/// <item><c>Shipped</c> — <see cref="DsrRetrievability"/> at its defaults. Reinforcement ON, and
/// <c>r</c>-dependent.</item>
/// <item><c>NoReinforce</c> — the same curve with <c>SpacingWeight = 0</c>. Reinforcement OFF. This
/// reproduces the previous study's boundary arm, so the two studies can be read against each other.</item>
/// <item><c>FrozenR</c> — reinforcement ON, <c>r</c>-dependence REMOVED
/// (<see cref="FrozenSpacingRetrievability"/>). The new arm, and the whole point.</item>
/// </list></para>
///
/// <para><b>How to read the result, decided before the run so the reading is not fitted to it:</b>
/// <list type="bullet">
/// <item><c>FrozenR</c> ≈ <c>NoReinforce</c> (both much better than <c>Shipped</c>) ⇒ the damage is law 3's
/// <c>r</c>-dependence, exactly as the premise-mismatch argument predicts. Lowering the shipped
/// <c>SpacingWeight</c> is then a real fix rather than a symptom-treatment.</item>
/// <item><c>FrozenR</c> ≈ <c>Shipped</c> (both much worse than <c>NoReinforce</c>) ⇒ the damage is
/// reinforcement ITSELF, whatever its shape — Part 64's hypothesis (b), positive feedback on the ranker's
/// own preferences with no correctness signal to check them. Tuning <c>SpacingWeight</c> would then be
/// treating a symptom, and the real work is a correctness signal.</item>
/// <item>Anything in between ⇒ both contribute, and the split is the number to report.</item>
/// </list></para>
/// </summary>
internal static class MemoryReinforcementSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 30;
    private const int QueryLimit = 10;
    private const string ShippedLabel = "Shipped";

    private sealed record Arm(string Label, IMemoryRetrievabilityPolicy Curve);

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

        var shippedCurve = new DsrRetrievability(shipped);
        var arms = new[]
        {
            new Arm(ShippedLabel, shippedCurve),
            new Arm("NoReinforce", new DsrRetrievability(shipped with { SpacingWeight = 0 })),
            new Arm("FrozenR", new FrozenSpacingRetrievability(shippedCurve)),
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

        PrintPreamble(arms, shapes);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var reinforceCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "reinforcement",
                new SqliteMemoryGraphStore(db.Factory),
                options: graphOptions,
                retrievability: new ModulatedRetrievability(arm.Curve, retentionPolicies),
                agePolicies: [agePolicy],
                ranking: rrf);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            if (arm.Curve is FrozenSpacingRetrievability frozen)
                reinforceCounts.AddOrUpdate("FrozenR-grew", frozen.GrewCount, (_, v) => v + frozen.GrewCount);
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        var all = rows.ToList();
        Console.WriteLine();
        Console.WriteLine("=== Controls ===");
        Console.WriteLine($"  replay order matched the corpus's declared order: " +
            $"{orderChecks.Count(c => c)}/{orderChecks.Count}");
        Console.WriteLine($"  FrozenR actually GREW a stability at least once: " +
            $"{(reinforceCounts.TryGetValue("FrozenR-grew", out var grew) && grew > 0 ? $"yes ({grew} times)" : "NO")}");
        if (!reinforceCounts.TryGetValue("FrozenR-grew", out var g) || g == 0)
            throw new InvalidOperationException(
                "FrozenR never grew a stability, so it is silently a second NoReinforce arm and the whole " +
                "comparison is vacuous — this is exactly the failure the arm exists to avoid.");

        PrintTable(all, shapes, arms);
        PrintVerdict(all, shapes, arms);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The reference r for FrozenR is the curve's own 0.5 anchor, not a swept value.");
        Console.WriteLine("    A different reference changes the MAGNITUDE of FrozenR's reinforcement, not");
        Console.WriteLine("    whether it depends on r, which is the only thing this study asks.");
        Console.WriteLine("  - Ranking and difficulty, both held at the SHIPPED default (RRF, live).");
        Console.WriteLine("  - Every other DsrOptions constant.");
        return 0;
    }

    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes)
    {
        Console.WriteLine("=== Reinforcement isolation (TASKS.md Part 64, docs/DECISIONS.md D53) ===");
        Console.WriteLine();
        Console.WriteLine("QUESTION: does reinforcement itself hurt recall here, or only law 3's dependence");
        Console.WriteLine("on r? MemorySpacingSweep could not tell: SpacingWeight multiplies the WHOLE");
        Console.WriteLine("increase, so its best arm (0) was reinforcement switched OFF, which is consistent");
        Console.WriteLine("with both diagnoses and distinguishes neither.");
        Console.WriteLine();
        Console.WriteLine("READING, fixed BEFORE the run so it is not fitted to the result:");
        Console.WriteLine("  FrozenR ~ NoReinforce  => law 3's r-dependence is the damage. Lowering the");
        Console.WriteLine("                            shipped SpacingWeight is a real fix.");
        Console.WriteLine("  FrozenR ~ Shipped      => reinforcement ITSELF is the damage, whatever its");
        Console.WriteLine("                            shape. Tuning SpacingWeight treats a symptom.");
        Console.WriteLine("  in between             => both contribute; the split is the number to report.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms ({arms.Count}): {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"Corpus shapes ({shapes.Count}): {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine("Pairing: every arm at a given (seed, shape) replays the identical corpus OBJECT.");
    }

    private static void PrintTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine("=== MissRate (mean over seeds) ===");
        Console.Write($"{"Shape",-16} {"Class",-24}");
        foreach (var arm in arms) Console.Write($" {arm.Label,13}");
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
                Console.Write(xs.Count == 0 ? $" {"—",13}" : $" {xs.Average(),13:F4}");
            }
            Console.WriteLine();
        }
    }

    private static void PrintVerdict(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Paired MissRate difference vs {ShippedLabel} (negative = better) ===");
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Arm",-13} {"Δ miss",9} {"95% CI",22}");

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var topicalNo = new List<double>();
        var topicalFrozen = new List<double>();

        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            var bySeed = cell.GroupBy(r => r.Seed)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Arm, r => r.MissRate, StringComparer.Ordinal));

            foreach (var arm in arms)
            {
                if (arm.Label == ShippedLabel) continue;
                var diffs = bySeed.Values
                    .Where(a => a.ContainsKey(arm.Label) && a.ContainsKey(ShippedLabel))
                    .Select(a => a[arm.Label] - a[ShippedLabel]).ToList();
                if (diffs.Count == 0) continue;

                var ci = MemoryPolicySweep.Ci95(diffs);
                var star = !double.IsNaN(ci.HalfWidth) && (ci.Lo > 0 || ci.Hi < 0) ? "  *" : "";
                Console.WriteLine($"{cell.Key.Shape,-16} {cell.Key.Class,-24} {arm.Label,-13} " +
                    $"{ci.Mean,9:F4} {$"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]",22}{star}");

                if (cell.Key.Class != "topical") continue;
                if (arm.Label == "NoReinforce") topicalNo.Add(ci.Mean); else topicalFrozen.Add(ci.Mean);
            }
        }

        Console.WriteLine();
        Console.WriteLine("  * = the 95% interval excludes zero.");
        Console.WriteLine();
        Console.WriteLine("=== Verdict for TASKS.md Part 64 ===");
        if (topicalNo.Count == 0 || topicalFrozen.Count == 0) { Console.WriteLine("no topical cells"); return; }

        var meanNo = topicalNo.Average();
        var meanFrozen = topicalFrozen.Average();
        Console.WriteLine($"Mean topical Δ vs Shipped:  NoReinforce {meanNo:F4}   FrozenR {meanFrozen:F4}");

        // How much of switching reinforcement OFF is recovered by merely removing the r-dependence?
        var recovered = Math.Abs(meanNo) < 1e-9 ? 0 : meanFrozen / meanNo;
        Console.WriteLine($"Share of the no-reinforcement benefit recovered by removing r-dependence alone: " +
            $"{recovered:P0}");
        Console.WriteLine();
        Console.WriteLine(recovered switch
        {
            >= 0.70 => "=> LAW 3's r-DEPENDENCE is the damage. Freezing r recovers most of the benefit while\n" +
                       "   still reinforcing, so the mechanism argument holds and lowering the shipped\n" +
                       "   SpacingWeight is a real fix rather than a symptom-treatment.",
            <= 0.30 => "=> REINFORCEMENT ITSELF is the damage, whatever its shape. Removing the r-dependence\n" +
                       "   recovers almost none of the benefit, so Part 64's hypothesis (b) stands: without a\n" +
                       "   correctness signal, reinforcement is positive feedback on the ranker's own\n" +
                       "   mistakes. Tuning SpacingWeight would treat a symptom.",
            _ => "=> BOTH contribute, in the proportion above. Neither single fix is sufficient on its own;\n" +
                 "   report the split rather than picking a story.",
        });
    }
}

/// <summary>Reinforcement with law 3's <c>r</c>-dependence REMOVED and nothing else changed — the isolation
/// arm, and the only new mechanism in this study.
///
/// <para><b>It does not duplicate the growth formula, deliberately.</b> A second copy of FSRS's three laws
/// would drift from the real one and the comparison would quietly stop being about <c>r</c>. Instead it calls
/// the REAL <see cref="DsrRetrievability.Reinforce"/> with <see cref="MemoryDecayState.Age"/> substituted so
/// that <c>r</c> is pinned at the curve's own <c>0.5</c> anchor — which is exactly <c>Age == Stability</c>, by
/// the <c>Stability</c> unit contract this library enforces
/// (<c>RetrievabilityPolicyContract.Stability_is_the_position_delta_at_which_retrievability_is_half</c>).
/// Same code path, same constants, one input frozen.</para>
///
/// <para><b>What is returned is the ORIGINAL state</b> with only the two fields <c>Reinforce</c> owns
/// (<c>Stability</c>, <c>Difficulty</c>) taken from the pinned call — so the substituted age never leaks back
/// into stored state.</para>
///
/// <para><b>One honest imprecision:</b> <c>r = 0.5</c> holds exactly at <c>Age == EffectiveStability</c>, and
/// effective stability includes the connection boost. For the great majority of entries (<c>Strength == 0</c>)
/// the two coincide; for a connected entry the pinned <c>r</c> drifts slightly off <c>0.5</c>. That leaves a
/// residual <c>r</c>-dependence far smaller than the one being removed, and it cannot manufacture the
/// result — it can only make this arm look slightly MORE like <c>Shipped</c>, which is the conservative
/// direction for the conclusion "freezing r recovers the benefit".</para>
///
/// <para><b>Decay is untouched</b>: <see cref="Retrievability"/> and <see cref="CandidateCutoff"/> forward
/// verbatim, so the arms differ in reinforcement alone and not in how anything is ranked or removed.</para></summary>
internal sealed class FrozenSpacingRetrievability(DsrRetrievability inner) : IMemoryRetrievabilityPolicy
{
    private int _grew;

    /// <summary>How many times this arm actually GREW a stability. Read by the study's own control: an arm
    /// that never grows anything is silently a second no-reinforcement arm, which would make the whole
    /// comparison vacuous while looking like a result.</summary>
    public int GrewCount => Volatile.Read(ref _grew);

    public double InitialStability => inner.InitialStability;

    public MemoryRetrievabilityProvenance Provenance => inner.Provenance;

    public double Retrievability(in MemoryDecayState state) => inner.Retrievability(state);

    public double CandidateCutoff(double minRetrievability) => inner.CandidateCutoff(minRetrievability);

    public double? DerivedGrade(in MemoryDecayState state) => inner.DerivedGrade(state);

    public MemoryDecayState Reinforce(in MemoryDecayState state)
    {
        var stability = state.Stability > 0 ? state.Stability : inner.InitialStability;
        var grown = inner.Reinforce(state with { Age = stability });
        if (grown.Stability > state.Stability) Interlocked.Increment(ref _grew);
        return state with { Stability = grown.Stability, Difficulty = grown.Difficulty };
    }
}
