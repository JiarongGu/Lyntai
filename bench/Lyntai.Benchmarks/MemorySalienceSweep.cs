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
/// <para>Salience ships ON for two of its three consumers — decay resistance and store admission priority;
/// only the rank boost is opt-in (<c>docs/DECISIONS.md</c> D45). It was invisible to every earlier study for
/// a structural reason worth knowing: <see cref="StructuralSaliencePolicy"/> scores
/// <see cref="SalienceContext.Novelty"/>, which the engine derives only when an
/// <see cref="Lyntai.Embeddings.IEmbedder"/> AND an <see cref="IVectorStore"/> are both supplied, and no
/// harness supplied either.</para>
///
/// <para><b>It runs against a REAL embedder and refuses without one (2026-08-28)</b> — salience reads
/// NOVELTY, which a bag-of-words fake turns into a different quantity; <c>docs/memory.md</c> §5 carries the
/// argument and the two-embedder readings. Both arms get the IDENTICAL shared, caching embedder instance and
/// a vector store, so enrichment is held CONSTANT and only salience varies.</para>
///
/// <para><b>The OFF arm registers <see cref="NeutralSaliencePolicy"/>, and passing <c>null</c> instead is the
/// defect this sweep shipped with until 2026-08-30.</b> <c>GraphMemoryEngine.NormalizeSaliencePolicies</c>
/// substitutes the shipped <see cref="StructuralSaliencePolicy"/> for a null or empty collection, so the old
/// off arm judged every write at the shipped weight and wrote the signal store admission reads — making the
/// pair a test of RETENTION with admission on in both arms, while reporting itself as measuring both
/// consumers. A control now asserts the off arm was consulted and declined; see <c>docs/FIXES.md</c>. Both
/// arms enrich identically, and the ON arm additionally registers
/// <see cref="SalienceRetentionPolicy"/>.</para>
///
/// <para><b>What this can and cannot settle.</b> It measures salience's NET effect on this corpus. It does
/// NOT test the concern that novelty inverts on noisy input — this corpus's noise is TEMPLATED
/// (<c>"item noise{n} was {filler} mentioned once and never again"</c>), sharing a skeleton with every other
/// class, so the second noise entry onward reads as FAMILIAR rather than novel. <b>A real embedder does not
/// lift this</b> — near-identical templated text is near-identical vectors under any embedder — so the
/// failure mode is unreachable by construction here, and <c>memory-importance</c>'s <c>diverse-noise</c>
/// shape is what reaches it. Stated so a null result is not misread as clearing the design question.</para>
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

    internal const string CeilingLadder = "ceiling";
    internal const string NoveltyLadder = "novelty";

    public static async Task<int> RunAsync(string? ladder = null)
    {
        // REFUSES rather than substitutes, 2026-08-28, the same discipline `memory-salience-weight` and
        // `memory-enrichment` already carry. Salience reads NOVELTY, which the engine derives from a
        // similarity search — so through `FakeEmbedder`, a feature-hashed bag of words, "unlike anything
        // already stored" degenerates into "shares few words with anything already stored". That is a
        // different quantity, and `docs/task-archive.md` Part 69 withdrew the numbers taken through it.
        // One embedder is shared across every replay and CACHES, so the arms see identical vectors and the
        // cost is one embed per distinct text rather than one per replay.
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var sharedEmbedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-salience");
        if (sharedEmbedder is null) return 1;

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

        // The CEILING ladder, added 2026-08-28. `SalienceOptions.MaxSalience` is the ceiling on reported
        // salience and therefore on BOTH consumers that ship ON — `ModulatedRetrievability` widens
        // `CandidateCutoff` by exactly it — and its own XML doc says "Unmeasured — a starting point". So the
        // "bounded-admission rule" `TASKS.md` Part 65 asks someone to design is this NUMBER, and sweeping it
        // needs no library change and no registration change: at `MaxSalience = 1` the clamp makes
        // StructuralSaliencePolicy return MemorySignals.Empty while it stays registered.
        //
        // `Max1` is therefore expected to equal `Off`, and that is the point of keeping the Off arm: it is a
        // self-check on that reading of the clamp rather than an assertion about it.
        var armOptions = new Dictionary<string, SalienceOptions?>(StringComparer.Ordinal);
        string[] arms;
        // Two shapes rather than six: the arm count multiplies the run, and these are the reference and the
        // shape Part 65 is actually about. Every class still reports, `attribute` included.
        Shape[] LadderShapes() => [.. shapes.Where(s => s.Label is "baseline" or "many-candidates")];

        switch (ladder)
        {
            case CeilingLadder:
                arms = [OffLabel, "Max1", "Max2", "Max3", "Max4"];
                foreach (var (label, max) in new[] { ("Max1", 1.0), ("Max2", 2.0), ("Max3", 3.0), ("Max4", 4.0) })
                    armOptions[label] = new SalienceOptions { MaxSalience = max };
                shapes = LadderShapes();
                break;

            // The MAGNITUDE ladder. `MaxSalience` turned out to be a switch (see above), so `NoveltyWeight`
            // is the only knob that can scale salience at all.
            //
            // It runs ALL SIX shapes where the ceiling ladder runs two, because the question it now answers
            // is different: the ceiling ladder asked whether a knob does anything, and this one asks what a
            // shipped DEFAULT should be. Two shapes cannot answer that — the on/off pair is significant on
            // five of six under one embedder and reverses sign on `high-noise` under the other, so a default
            // chosen on `baseline` + `many-candidates` is chosen on the two that happen to agree.
            //
            // Rungs bracket the decision rather than sampling it evenly: `NW0` is the measured neutral (the
            // clamp makes the policy report nothing at all), `NW1.5` is what ships, and `NW3` is the far side
            // that is already known to be worse. `NW0.5`/`NW1` are what would reveal a sweet spot BELOW the
            // shipped weight, which is the only outcome that argues for a value neither 0 nor 1.5.
            //
            // `NW-1.5` was an arm here until 2026-08-30 and is now `SalienceTests
            // .A_negative_novelty_weight_is_INERT_rather_than_inverting`. It tested a documented CLAIM, not a
            // value, and the claim is arithmetic — a clamp floored at 1 cannot invert — so a deterministic
            // fact pins it for free where an arm cost a sixth of a run that needs a real model.
            case NoveltyLadder:
                arms = [OffLabel, "NW0", "NW0.5", "NW1", "NW1.5", "NW3"];
                foreach (var (label, w) in new[] { ("NW0", 0.0), ("NW0.5", 0.5), ("NW1", 1.0), ("NW1.5", 1.5), ("NW3", 3.0) })
                    armOptions[label] = new SalienceOptions { NoveltyWeight = w };
                break;

            default:
                arms = [OffLabel, OnLabel];
                armOptions[OnLabel] = null;   // null = the SHIPPED defaults, so this path is unchanged
                break;
        }

        PrintPreamble(shapes, arms);

        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        var rows = new ConcurrentBag<Row>();
        var orderChecks = new ConcurrentBag<bool>();
        var retentionCounts = new ConcurrentBag<(string Arm, int Count)>();
        var judged = new ConcurrentBag<(string Arm, int Salient, int Judged, int Distinct)>();

        async ValueTask RunOneAsync(int seed, Shape shape, string arm)
        {
            var corpus = corpusCache.GetOrAdd((seed, shape.Label),
                key => MemoryCorpus.Generate(shape.Value, key.Seed));
            var declaredOrder = corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker).ToList();

            var on = arm != OffLabel;
            var armOpts = on ? armOptions[arm] : null;
            // Both arms enrich identically — same embedder INSTANCE, same vector store shape — so the
            // difference below is salience and not "the engine performed a vector search".
            var embedder = sharedEmbedder;
            var vectors = new InMemoryVectorStore();

            // EVERY arm wraps its policy, the OFF arm included, so "salient" and "distinct" mean the same
            // thing in every row of the controls table.
            //
            // The off arm is an explicit NeutralSaliencePolicy and passing `null` here is WRONG — a defect
            // this sweep shipped from its first run until 2026-08-30. `GraphMemoryEngine
            // .NormalizeSaliencePolicies` substitutes the shipped `StructuralSaliencePolicy` for a null or
            // empty collection ("empty does NOT mean off", the contract `TASKS.md` Part 65 names), so the
            // OFF arm judged every write at the shipped NoveltyWeight and wrote the signal store admission
            // reads. It therefore compared retention-on against retention-off with salience's admission
            // consumer ON IN BOTH, while reporting itself as measuring both consumers. See `docs/FIXES.md`.
            var counting = new SweepDoubles.CountingSaliencePolicy(
                on ? new StructuralSaliencePolicy(armOpts) : new NeutralSaliencePolicy());
            IReadOnlyList<IMemoryRetentionPolicy> retention =
                on ? [new SalienceRetentionPolicy(armOpts)] : [];

            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine(
                "salience",
                store,
                options: graphOptions,
                retrievability: new ModulatedRetrievability(new DsrRetrievability(), retention),
                agePolicies: [agePolicy],
                embedder: embedder,
                vectors: vectors,
                saliencePolicies: [counting],
                ranking: rrf);

            retentionCounts.Add((arm, retention.Count));

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            orderChecks.Add(declaredOrder.SequenceEqual(replay.ObservedOrder));
            judged.Add((arm, counting.Salient, counting.Judged, counting.DistinctValues));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        var varying = PrintControls(retentionCounts.ToList(), judged.ToList(), orderChecks.ToList());
        PrintTable(rows.ToList(), shapes, arms);
        var perArm = arms.Skip(1)
            .Select(treatment =>
            {
                var (miss, pollution) = PrintVerdict(rows.ToList(), shapes, treatment, OffLabel, varying);
                return (Arm: treatment, Miss: miss, Pollution: pollution);
            })
            .ToList();
        if (perArm.Count > 1) PrintLadderVerdict(perArm, OffLabel, varying);

        // The SECOND contrast, and on a ladder it is the one that answers the question — see NeutralRung.
        // Off differs from every registered arm in more than the knob, so a rung-vs-Off delta cannot be
        // attributed to the knob alone; rung-vs-tied-rung can.
        if (NeutralRung(arms, varying) is { } neutral)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 78));
            Console.WriteLine($"Re-paired against {neutral}, the TIED rung: registration held constant, only the");
            Console.WriteLine("knob varying. This is the ladder's own contrast; everything above it also carries");
            Console.WriteLine($"whatever separates {neutral} from {OffLabel}.");
            Console.WriteLine(new string('=', 78));

            var vsNeutral = arms.Where(a => a != OffLabel && a != neutral)
                .Select(treatment =>
                {
                    var (miss, pollution) = PrintVerdict(rows.ToList(), shapes, treatment, neutral, varying);
                    return (Arm: treatment, Miss: miss, Pollution: pollution);
                })
                .ToList();
            if (vsNeutral.Count > 1) PrintLadderVerdict(vsNeutral, neutral, varying);
        }

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {seeds.Count} seed(s), " +
            $"{shapes.Length} shape(s), {arms.Length} arm(s); " +
            $"{sharedEmbedder.Misses} embed call(s), {sharedEmbedder.Hits} cache hit(s).");
        Console.WriteLine();
        Console.WriteLine("NOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The rank boost. SalienceRankWeight is opt-in and stays 0 (D45), so this");
        Console.WriteLine("    measures the two consumers that actually ship ON.");
        // Named per mode rather than as a blanket claim: this footer said all three constants were unswept
        // on every path, which stopped being true the day the ladders landed.
        Console.WriteLine(ladder switch
        {
            CeilingLadder => "  - NoveltyWeight and MinimumComparables (MaxSalience IS this run's axis).",
            NoveltyLadder => "  - MaxSalience and MinimumComparables (NoveltyWeight IS this run's axis).",
            _ => "  - SalienceOptions' own constants (NoveltyWeight, MaxSalience, MinimumComparables).",
        });
        Console.WriteLine("  - Whether novelty INVERTS on noisy input. This corpus's noise is templated, so a");
        Console.WriteLine("    null result here does NOT clear that concern — see the class doc.");
        Console.WriteLine($"  - Embedder realism is no longer a caveat: novelty is measured through the REAL");
        Console.WriteLine($"    {SweepDoubles.Model}, not a bag-of-words fake (changed 2026-08-28).");
        return 0;
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
        Console.WriteLine("noise is TEMPLATED, so it reads as familiar rather than novel under ANY embedder -");
        Console.WriteLine("a real one does not lift this. A null result here does NOT clear that concern.");
        Console.WriteLine();
        Console.WriteLine($"Base seed: {BaseSeed}, seeds: {SeedCount}, query limit: {QueryLimit}");
        Console.WriteLine($"Arms ({arms.Count}): {string.Join(", ", arms)}");
        Console.WriteLine($"Corpus shapes ({shapes.Count}): {string.Join(", ", shapes.Select(s => s.Label))}");
    }

    /// <summary>
    /// The controls, and the DISTINCT-VALUE one decides whether the table below means anything.
    /// </summary>
    /// <returns>Which arms produced a signal that genuinely varies. An arm absent from this set is one whose
    /// numbers describe the corpus rather than the knob.</returns>
    private static HashSet<string> PrintControls(IReadOnlyList<(string Arm, int Count)> retention,
        IReadOnlyList<(string Arm, int Salient, int Judged, int Distinct)> judged, IReadOnlyList<bool> orderChecks)
    {
        Console.WriteLine();
        Console.WriteLine("=== Controls ===");
        foreach (var arm in retention.Select(r => r.Arm).Distinct().Order(StringComparer.Ordinal))
        {
            var counts = retention.Where(r => r.Arm == arm).Select(r => r.Count).Distinct().ToList();
            Console.WriteLine($"  {arm,-12} retention policies: {string.Join("/", counts)}");
        }

        var totalSalient = judged.Sum(j => j.Salient);
        if (totalSalient == 0)
            throw new InvalidOperationException(
                "No arm ever judged a single write salient, so every treatment is silently identical to " +
                $"{OffLabel} and the comparison is vacuous. StructuralSaliencePolicy reports the neutral 1 " +
                "until an engine holds SalienceOptions.MinimumComparables entries — check that before " +
                "reading any number below as evidence that salience does nothing.");

        // THE positive control, and the one whose absence let this sweep publish figures from an OFF arm
        // that was not off. `GraphMemoryEngine` substitutes the shipped policy for a null or empty salience
        // collection, so an off arm has to REGISTER a neutral policy — and the only way to know it did is to
        // see it consulted (Judged > 0) and declining (Salient == 0). Asserting Salient == 0 alone would
        // pass on an arm that was never asked, which is exactly the shape that shipped.
        var off = judged.Where(j => j.Arm == OffLabel).ToList();
        var (offJudged, offSalient) = (off.Sum(j => j.Judged), off.Sum(j => j.Salient));
        if (off.Count == 0 || offJudged == 0 || offSalient != 0)
            throw new InvalidOperationException(
                $"{OffLabel} is not off: it was consulted on {offJudged} write(s) and judged {offSalient} of "
                + "them salient (both must be 'consulted, judged none'). A null or empty salience collection "
                + "takes the SHIPPED StructuralSaliencePolicy — 'empty does NOT mean off' — so the off arm "
                + "must register a NeutralSaliencePolicy explicitly. Every number below would otherwise "
                + "compare two arms that both write the signal store admission reads.");

        // FIRING is not VARYING, and only the second one licenses reading a magnitude ladder. RRF ranks by
        // competition (D82), so a signal every candidate ties on contributes the same constant at every
        // rung — a perfectly flat curve with every ordinary control green. An arm whose clamp pins salience
        // to the neutral value (NW0, Max1) legitimately reports one distinct value and is a CONTROL rather
        // than a rung, which is why this reports per arm instead of failing the run.
        var varying = new HashSet<string>(StringComparer.Ordinal);
        Console.WriteLine();
        Console.WriteLine($"  {"arm",-12} {"salient/judged",18} {"distinct values",16}  reading");
        foreach (var arm in judged.Select(j => j.Arm).Distinct().Order(StringComparer.Ordinal))
        {
            var forArm = judged.Where(j => j.Arm == arm).ToList();
            var (salient, total) = (forArm.Sum(j => j.Salient), forArm.Sum(j => j.Judged));
            var distinct = forArm.Max(j => j.Distinct);
            if (distinct > 1) varying.Add(arm);

            Console.WriteLine($"  {arm,-12} {$"{salient}/{total}",18} {distinct,16}  " +
                (distinct > 1 ? "varies — a rung" : "tied — a CONTROL, not a rung"));
        }

        Console.WriteLine($"  replay order matched the corpus's declared order: " +
            $"{orderChecks.Count(c => c)}/{orderChecks.Count}");
        return varying;
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

    /// <summary>
    /// The paired difference for one treatment, on BOTH metrics.
    ///
    /// <para><b>It printed Δ miss alone until 2026-08-30, which is the defect
    /// <c>.claude/knowledge/pitfalls.md</c> records this file's sibling committing and paying for.</b> There,
    /// a roll-up counting miss-better shapes read as unanimous and a shipped ranking default was changed on
    /// it; the pollution column, once looked at, showed the trade design §5.7.0 explicitly refuses, and the
    /// default was reverted. The table above this always carried both columns — a per-arm report and its
    /// SUMMARY are two sites, and the summary is the one people act on.</para>
    ///
    /// <para>§5.7.0 is lexicographic: miss is objective (2) and pollution is (3), explicitly NOT co-equal. A
    /// large miss reduction for a small pollution rise is ACCEPTED; the reverse is not. So a verdict has to
    /// evaluate that ordering rather than tally the primary term.</para>
    /// </summary>
    /// <returns>The mean combined Δ on each metric, for a caller ranking arms against each other.</returns>
    private static (double Miss, double Pollution) PrintVerdict(IReadOnlyList<Row> rows,
        IReadOnlyList<Shape> shapes, string treatment, string baseline, IReadOnlySet<string> varying)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Paired difference, {treatment} vs {baseline} (negative = salience helps) ===");
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Δ miss",9} {"95% CI",22} {"Δ poll",9}");

        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i, StringComparer.Ordinal);
        var combined = new List<double>();
        var combinedPollution = new List<double>();
        var significant = 0;
        var cells = 0;

        foreach (var cell in rows
            .GroupBy(r => (r.Shape, r.Class))
            .OrderBy(g => shapeOrder[g.Key.Shape])
            .ThenBy(g => MemoryPolicySweep.ClassOrder.TryGetValue(g.Key.Class, out var o) ? o : int.MaxValue))
        {
            var bySeed = cell.GroupBy(r => r.Seed).ToDictionary(g => g.Key,
                g => g.ToDictionary(r => r.Arm, r => (r.MissRate, r.PollutionRate), StringComparer.Ordinal));
            var paired = bySeed.Values
                .Where(a => a.ContainsKey(treatment) && a.ContainsKey(baseline)).ToList();
            if (paired.Count == 0) continue;

            var diffs = paired.Select(a => a[treatment].MissRate - a[baseline].MissRate).ToList();
            var pollutionDiffs = paired.Select(a => a[treatment].PollutionRate - a[baseline].PollutionRate).ToList();

            var ci = MemoryPolicySweep.Ci95(diffs);
            var excludesZero = !double.IsNaN(ci.HalfWidth) && (ci.Lo > 0 || ci.Hi < 0);
            cells++;
            if (excludesZero) significant++;
            if (cell.Key.Class == "all (combined)")
            {
                combined.Add(ci.Mean);
                combinedPollution.Add(pollutionDiffs.Average());
            }

            Console.WriteLine($"{cell.Key.Shape,-16} {cell.Key.Class,-24} " +
                $"{ci.Mean,9:F4} {$"[{ci.Lo,7:F4}, {ci.Hi,7:F4}]",22}{(excludesZero ? " *" : "  ")}" +
                $" {pollutionDiffs.Average(),9:F4}");
        }

        Console.WriteLine();
        Console.WriteLine("  * = the 95% interval excludes zero (MISS only — the CI is on the primary term).");

        var mean = combined.Count == 0 ? 0 : combined.Average();
        var pollution = combinedPollution.Count == 0 ? 0 : combinedPollution.Average();

        Console.WriteLine();
        Console.WriteLine($"Mean combined Δ: miss {mean:+0.0000;-0.0000;0.0000}, " +
            $"pollution {pollution:+0.0000;-0.0000;0.0000}   ({significant}/{cells} miss cells significant)");
        if (!varying.Contains(treatment))
            Console.WriteLine("  NOTE: this arm's salience signal is TIED (one distinct value), so it is a " +
                "control rather than a rung — read it as a check on the clamp, not as a measured magnitude.");

        Console.WriteLine();
        Console.WriteLine((mean, pollution) switch
        {
            // Lexicographic, in the order §5.7.0 states. Miss decides; pollution only breaks the near-tie
            // band where miss has said nothing, and can never overturn a miss result on its own.
            ( < -0.01, _) => "=> HELPS. Miss improves beyond the near-tie band, which under §5.7.0 (2) settles\n" +
                             "   it whatever pollution did — a miss gain outweighing a pollution cost is the\n" +
                             "   trade the objective ACCEPTS.",
            ( > 0.01, _) => "=> HURTS. Miss is worse on the primary term, so no pollution gain rescues it:\n" +
                            "   improving (3) while breaking (2) is a regression by construction.",
            (_, < -0.005) => "=> NEAR-TIE on miss, better on POLLUTION. The tiebreak the objective allows: (3)\n" +
                             "   only speaks where (2) is silent, and it is silent here.",
            (_, > 0.005) => "=> NEAR-TIE on miss, worse on POLLUTION. Refused — this is the exact trade §5.7.0\n" +
                            "   names as unacceptable, and it is invisible to a verdict that reads miss alone.",
            _ => "=> INERT on both metrics. Not a clean bill of health: it is surface with no demonstrated\n" +
                 "   benefit, and the templated-noise caveat means the inversion concern is untestable here.",
        });

        return (mean, pollution);
    }

    /// <summary>
    /// The ladder in one table — which rung a shipped default should be, given every rung's own verdict.
    ///
    /// <para><b>It ranks on the OBJECTIVE, not on a column.</b> "N/N shapes better on miss" is a count on one
    /// metric wearing the costume of a verdict; sorting by miss alone would do the same thing with more
    /// decimal places. Both figures are printed for every rung so a reader can disagree with the ordering.</para>
    /// </summary>
    private static void PrintLadderVerdict(IReadOnlyList<(string Arm, double Miss, double Pollution)> arms,
        string baseline, IReadOnlySet<string> varying)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Which rung, on the stated objective (vs {baseline}) ===");
        Console.WriteLine($"  {"arm",-12} {"Δ miss",10} {"Δ poll",10}  signal");
        foreach (var (arm, miss, poll) in arms)
            Console.WriteLine($"  {arm,-12} {miss,10:+0.0000;-0.0000;0.0000} {poll,10:+0.0000;-0.0000;0.0000}  " +
                (varying.Contains(arm) ? "varies" : "tied (control)"));

        // The best rung is the SMALLEST miss — including negative, which is the treatment paying for itself.
        // Ties on miss break on pollution, which is the same ordering every per-arm verdict above applies,
        // stated once for the roll-up.
        var best = arms.OrderBy(a => a.Miss).ThenBy(a => a.Pollution).FirstOrDefault();
        Console.WriteLine();
        Console.WriteLine(best.Arm is null
            ? "  No arm produced a paired difference."
            : $"  Lowest cost on the objective: {best.Arm} (miss {best.Miss:+0.0000;-0.0000;0.0000}, " +
              $"pollution {best.Pollution:+0.0000;-0.0000;0.0000}).");
        Console.WriteLine();
        Console.WriteLine("  READ THIS AS A COST ORDERING, NOT AS A DEFAULT. This corpus's noise is TEMPLATED,");
        Console.WriteLine("  which puts the novelty-inversion case out of reach, and a shipped default also");
        Console.WriteLine("  answers to embedder sensitivity — the on/off pair differs by ~2.5x between two real");
        Console.WriteLine("  embedders and REVERSES sign on one shape. One ladder on one embedder is an input to");
        Console.WriteLine("  that decision, not the decision.");
    }

    /// <summary>
    /// The ladder's TIED rung — an arm whose clamp makes the policy report nothing while it stays registered
    /// — or null when the ladder has none.
    ///
    /// <para><b>It is the ladder's real baseline, and <c>SalienceOff</c> is not.</b> Measured 2026-08-30 on
    /// the widened ladder: <c>NW0</c> emits zero salience on 51330 writes and is still significantly WORSE
    /// than <c>SalienceOff</c> on <c>high-reuse</c> (+0.0401) and <c>high-noise</c> (+0.0493) — a gap larger
    /// than the entire spread between the rungs it was being used to rank. So a rung-vs-Off delta carries
    /// whatever that difference is plus the weight's own effect, and the two cannot be separated. Pairing
    /// each rung against the tied rung holds registration constant and varies only the knob, which is the
    /// contrast the ladder was always claiming to draw.</para>
    ///
    /// <para><b>Both contrasts are printed, deliberately.</b> The vs-Off table is where that confound is
    /// visible at all; deleting it would hide the finding that produced this method. Earlier ladders reported
    /// their tied rung as indistinguishable from Off, which was true on the two shapes they ran.</para>
    /// </summary>
    private static string? NeutralRung(IReadOnlyList<string> arms, IReadOnlySet<string> varying) =>
        arms.FirstOrDefault(a => a != OffLabel && !varying.Contains(a));
}
