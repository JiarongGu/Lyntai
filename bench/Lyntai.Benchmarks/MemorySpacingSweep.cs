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
/// <b>Is the <c>topical</c> regression responsive to <see cref="DsrOptions.SpacingWeight"/> at all?</b>
/// That is the whole question, and the answer is useful in both directions (<c>TASKS.md</c> Part 56, FSRS-C).
///
/// <para><b>What this is NOT, stated first because the distinction is the reason this may exist while FSRS-B
/// may not.</b> This is a SENSITIVITY study: it reports how a metric this repository already publishes moves
/// as one knob moves, and it adopts nothing. It is not parameter FITTING. Fitting <see cref="DsrOptions"/>
/// against this library's own review log is circular by construction — the log's grade is
/// <c>2 + 2 × Retrievability(state)</c>, a deterministic function of the very constants a fit would estimate,
/// and the log can only ever contain successes because an entry that is not returned never reaches
/// <c>Reinforce</c> (<c>docs/DECISIONS.md</c> D51's 2026-08-12 amendment). A sensitivity curve makes no claim
/// about the true parameter, so neither objection touches it.</para>
///
/// <para><b>Why the question is worth its runtime.</b> D49 shipped <see cref="DsrRetrievability"/> as the 3.0
/// default while disclosing a measured <c>topical</c> loss, and Part 56 named <c>SpacingWeight</c> as the
/// specific suspect: law 3's term <c>e^(w·(1−r)) − 1</c> shrinks as <c>r → 1</c>, so a topical reuse batch's
/// second and later touches gain almost nothing (measured: +125% on the first touch, ~2% after). If the
/// metric barely moves across the range below, the suspect is EXONERATED and the item closes. If it moves
/// sharply, the suspect is confirmed and the size of the available movement is on record — without anyone
/// having to pick a value from a corpus that cannot justify one.</para>
///
/// <para><b>Arms.</b> One factor, <c>SpacingWeight</c>, over a range bracketing the shipped default: the law
/// fully OFF (<c>0</c> — legal and documented as "law 3 off", not a sign error), half, the shipped
/// <c>1.5</c>, double, and quadruple. Every other constant is inherited from ONE
/// <see cref="DsrOptions"/> via <c>with</c>, never typed a second time, so the arms differ in exactly one
/// value by construction — the same discipline <see cref="MemoryPolicySweep"/> applies to its difficulty
/// arms, and the read-back below confirms it from what each engine actually holds.</para>
///
/// <para><b>Everything else is held at the SHIPPED default</b>, deliberately: ranking is
/// <see cref="ReciprocalRankFusionPolicy"/> and the difficulty axis is live, because the question is about
/// the configuration this release actually ships, not about a corner of the policy space. That also keeps
/// this a ONE-factor study, so a moving number has exactly one candidate explanation.</para>
///
/// <para><b>Reporting is paired against the shipped default arm</b>, seed by seed, the same pairing
/// <see cref="MemoryPolicySweep"/> uses: every arm at a given (seed, shape) replays the identical corpus
/// object, so the per-seed difference cancels corpus variance and the CI is over that difference rather than
/// over two independent means.</para>
/// </summary>
internal static class MemorySpacingSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 30;
    private const int QueryLimit = 10;

    /// <summary>The shipped value, and the arm every other arm is differenced AGAINST. Read from a
    /// default-constructed <see cref="DsrOptions"/> rather than written as a literal, so this study cannot
    /// silently keep differencing against a stale number if the default ever moves.</summary>
    private static readonly double ShippedSpacingWeight = new DsrOptions().SpacingWeight;

    private sealed record Arm(string Label, double SpacingWeight, IMemoryRetrievabilityPolicy Retrieval);

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

        // Derived from ONE options record via `with` — every constant this study is not about is identical
        // across arms by construction, not by being typed out five times and happening to agree.
        double[] weights = [0, ShippedSpacingWeight / 2, ShippedSpacingWeight, ShippedSpacingWeight * 2,
            ShippedSpacingWeight * 4];
        var arms = weights
            .Select(w => new Arm(
                Label(w),
                w,
                new ModulatedRetrievability(new DsrRetrievability(shipped with { SpacingWeight = w }),
                    retentionPolicies)))
            .ToArray();

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
        MemoryCorpus CorpusFor(int seed, Shape shape) =>
            corpusCache.GetOrAdd((seed, shape.Label), key => MemoryCorpus.Generate(shape.Value, key.Seed));

        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var readBacks = new ConcurrentBag<(string Arm, double SpacingWeight, string Ranking, string Curve)>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = CorpusFor(seed, shape);
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine(
                "spacing",
                new SqliteMemoryGraphStore(db.Factory),
                options: graphOptions,
                policy: arm.Retrieval,
                agePolicies: [agePolicy],
                ranking: rrf);

            readBacks.Add(ReadBack(arm, engine));

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
        PrintControlsConfirmed(readBacks.ToList(), orderChecks.ToList(), arms);
        PrintSensitivity(all, shapes, arms);
        PrintVerdict(all, shapes, arms);

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit, per the measurement doc's own rule —");
        Console.WriteLine("a silent cap reads as \"covered everything\"):");
        Console.WriteLine("  - Every other DsrOptions constant. This is ONE factor by design; a moving number");
        Console.WriteLine("    here therefore has exactly one candidate explanation, which is the point.");
        Console.WriteLine("  - Ranking and difficulty, both held at the SHIPPED default (RRF, live) — this");
        Console.WriteLine("    asks about the configuration 3.0 actually ships, not the policy space.");
        Console.WriteLine("  - Interaction between SpacingWeight and either of those. Unmeasured here.");
        Console.WriteLine("  - PollutionRate's own paired CIs — reported as means only; MissRate carries the");
        Console.WriteLine("    paired interval, matching MemoryPolicySweep's own choice and its reasoning.");
        Console.WriteLine("  - Any claim about the TRUE value of SpacingWeight. This study cannot make one;");
        Console.WriteLine("    see the class doc on why fitting is circular here and sensitivity is not.");
        return 0;
    }

    /// <summary>States the question, the arms and what is held constant BEFORE any number appears — so a
    /// reader who scrolls straight to the verdict still cannot mistake a sensitivity curve for a fit.</summary>
    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes, DsrOptions shipped)
    {
        Console.WriteLine("=== SpacingWeight sensitivity (TASKS.md Part 56, FSRS-C) ===");
        Console.WriteLine();
        Console.WriteLine("QUESTION: is the `topical` regression D49 shipped knowingly even RESPONSIVE to");
        Console.WriteLine("DsrOptions.SpacingWeight, the knob Part 56 named as its suspect?");
        Console.WriteLine();
        Console.WriteLine("THIS IS NOT PARAMETER FITTING, and the difference is why it may exist at all.");
        Console.WriteLine("It reports how an already-published metric moves as one knob moves, and adopts");
        Console.WriteLine("nothing. Fitting DsrOptions against this library's own review log is circular by");
        Console.WriteLine("construction: the log's grade is 2 + 2*Retrievability(state) — a function of the very");
        Console.WriteLine("constants a fit would estimate — and the log can only ever contain successes, because");
        Console.WriteLine("an entry that is not returned never reaches Reinforce. See docs/DECISIONS.md D51's");
        Console.WriteLine("2026-08-12 amendment. A sensitivity curve makes no claim about the true value, so");
        Console.WriteLine("neither objection touches it.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms ({arms.Count}), one factor: {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shipped default = {ShippedSpacingWeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
            " (read from DsrOptions, not written as a literal, so this cannot");
        Console.WriteLine("  keep differencing against a stale number if the default moves)");
        Console.WriteLine("  every other constant inherited from ONE DsrOptions via `with` — the arms differ in");
        Console.WriteLine($"  exactly one value by construction (InitialStability={shipped.InitialStability}, Decay={shipped.Decay})");
        Console.WriteLine($"Held at the SHIPPED default: ranking = ReciprocalRankFusionPolicy, difficulty = live.");
        Console.WriteLine($"Corpus shapes ({shapes.Count}), one-factor-at-a-time against CorpusShape.Default: " +
            string.Join(", ", shapes.Select(s => s.Label)));
        Console.WriteLine("Pairing: every arm at a given (seed, shape) replays the identical corpus OBJECT, so");
        Console.WriteLine("the per-seed difference cancels corpus variance.");
    }

    private static string Label(double w) =>
        $"Spacing={w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Read back what each constructed engine actually holds — never the ingredient variables above.
    /// Same discipline and the same reason as <see cref="MemoryPolicySweep"/>'s own read-back: an arm that
    /// silently carried the wrong weight would produce a beautifully flat curve and a false exoneration,
    /// which is exactly the direction this study must not fail in.</summary>
    private static (string Arm, double SpacingWeight, string Ranking, string Curve) ReadBack(
        Arm arm, GraphMemoryEngine engine)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var engineType = typeof(GraphMemoryEngine);
        var policy = (IMemoryRetrievabilityPolicy)engineType.GetField("_policy", flags)!.GetValue(engine)!;
        var ranking = (IMemoryRankingPolicy)engineType.GetField("_ranking", flags)!.GetValue(engine)!;

        var curve = policy is ModulatedRetrievability modulated
            ? (IMemoryRetrievabilityPolicy)typeof(ModulatedRetrievability)
                .GetField("_inner", flags)!.GetValue(modulated)!
            : policy;

        var optionsField = curve.GetType().GetField("_options", flags)
            ?? throw new InvalidOperationException(
                $"The one-factor control cannot be verified: no '_options' field on {curve.GetType().Name}. " +
                "A refactor in Lyntai.Core renamed it — fix this read-back rather than weakening the control.");
        var options = optionsField.GetValue(curve)!;
        var weightProperty = options.GetType().GetProperty(nameof(DsrOptions.SpacingWeight))
            ?? throw new InvalidOperationException(
                $"The one-factor control cannot be verified: no public 'SpacingWeight' on {options.GetType().Name}.");

        return (arm.Label, (double)weightProperty.GetValue(options)!, ranking.GetType().Name, curve.GetType().Name);
    }

    private static void PrintControlsConfirmed(
        IReadOnlyList<(string Arm, double SpacingWeight, string Ranking, string Curve)> readBacks,
        IReadOnlyList<bool> orderChecks, IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine("=== Controls confirmed (read off the CONSTRUCTED engines, not the variables) ===");

        var byArm = readBacks.GroupBy(r => r.Arm, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(),
            StringComparer.Ordinal);
        foreach (var arm in arms)
        {
            var seen = byArm[arm.Label];
            var weights = seen.Select(s => s.SpacingWeight).Distinct().ToList();
            var ok = weights.Count == 1 && Math.Abs(weights[0] - arm.SpacingWeight) < 1e-12;
            Console.WriteLine($"  {arm.Label,-16} SpacingWeight={string.Join("/", weights)} " +
                $"({seen.Count} engine(s)) {(ok ? "OK" : "MISMATCH — this arm did not carry its own weight")}");
            if (!ok)
                throw new InvalidOperationException(
                    $"Arm '{arm.Label}' was constructed with SpacingWeight(s) {string.Join("/", weights)} " +
                    $"rather than {arm.SpacingWeight}. A flat curve from mis-built arms is a FALSE exoneration.");
        }

        var rankings = readBacks.Select(r => r.Ranking).Distinct().ToList();
        var curves = readBacks.Select(r => r.Curve).Distinct().ToList();
        Console.WriteLine($"  one ranking policy across every arm: {string.Join("/", rankings)} " +
            $"({(rankings.Count == 1 ? "OK" : "NOT held constant")})");
        Console.WriteLine($"  one curve type across every arm:     {string.Join("/", curves)} " +
            $"({(curves.Count == 1 ? "OK" : "NOT held constant")})");
        Console.WriteLine($"  replay order matched the corpus's declared order: " +
            $"{orderChecks.Count(c => c)}/{orderChecks.Count}");
    }

    /// <summary>Mean MissRate per (shape, class, arm) — the raw surface, before any differencing.</summary>
    private static void PrintSensitivity(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes,
        IReadOnlyList<Arm> arms)
    {
        Console.WriteLine();
        Console.WriteLine("=== MissRate by SpacingWeight (mean over seeds) ===");
        Console.Write($"{"Shape",-16} {"Class",-24}");
        foreach (var arm in arms) Console.Write($" {arm.Label,14}");
        Console.WriteLine();

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var cells = rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue);

        foreach (var cell in cells)
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

    /// <summary>The answer: paired per-seed differences against the SHIPPED arm, with 95% CIs. A cell whose
    /// interval straddles zero says this knob did not move that class on this shape.</summary>
    private static void PrintVerdict(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        var shippedLabel = Label(ShippedSpacingWeight);

        Console.WriteLine();
        Console.WriteLine($"=== Paired MissRate difference vs the shipped arm ({shippedLabel}) ===");
        Console.WriteLine("Negative = FEWER misses than shipped (better). Paired per seed on the identical");
        Console.WriteLine("corpus, so corpus variance cancels and the CI is over the DIFFERENCE.");
        Console.WriteLine();
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Arm",-16} {"Δ miss",9} {"95% CI",22}");

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var worst = 0.0;
        var topicalMoved = false;

        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            var bySeed = cell.GroupBy(r => r.Seed)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Arm, r => r.MissRate, StringComparer.Ordinal));

            foreach (var arm in arms)
            {
                if (arm.Label == shippedLabel) continue;
                var diffs = bySeed.Values
                    .Where(a => a.ContainsKey(arm.Label) && a.ContainsKey(shippedLabel))
                    .Select(a => a[arm.Label] - a[shippedLabel])
                    .ToList();
                if (diffs.Count == 0) continue;

                var ci = MemoryPolicySweep.Ci95(diffs);
                var excludesZero = !double.IsNaN(ci.HalfWidth) && (ci.Lo > 0 || ci.Hi < 0);
                worst = Math.Max(worst, Math.Abs(ci.Mean));
                if (cell.Key.Class == "topical" && excludesZero && Math.Abs(ci.Mean) >= 0.01) topicalMoved = true;

                Console.WriteLine($"{cell.Key.Shape,-16} {cell.Key.Class,-24} {arm.Label,-16} " +
                    $"{ci.Mean,9:F4} {$"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]",22}{(excludesZero ? "  *" : "")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  * = the 95% interval excludes zero.");
        Console.WriteLine();
        Console.WriteLine("=== Verdict for TASKS.md Part 56 (FSRS-C) ===");
        Console.WriteLine($"Largest |Δ MissRate| anywhere in the grid: {worst:F4}");
        Console.WriteLine(topicalMoved
            ? "SpacingWeight DOES move `topical` beyond noise somewhere in the grid — the suspect Part 56\n" +
              "named is confirmed as responsive. Note what this still does NOT license: picking a value.\n" +
              "The size of the available movement is now on record; choosing a point on that curve needs an\n" +
              "outcome signal this library does not collect (D51's 2026-08-12 amendment)."
            : "SpacingWeight does NOT move `topical` beyond noise anywhere in this grid. The suspect Part 56\n" +
              "named is EXONERATED on this corpus, and FSRS-C closes: there is no tuning of this knob that\n" +
              "recovers the topical regression, so waiting for a fit to tune it was waiting for nothing.");
    }
}
