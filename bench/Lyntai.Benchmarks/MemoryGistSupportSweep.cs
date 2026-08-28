using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// Which rule selects the CURRENT regime of a recurring cluster — the gist tier's support question. Phase A
/// is the older, larger regime (8 members at <c>RoutineCount=12</c>); phase B is the newer, smaller one (4).
/// <para><b>Two modes.</b> <c>--screen</c> is the generative size ladder alone, on one shape — see
/// <see cref="RunScreenAsync"/>. The default is the full sweep — see <see cref="RunFullAsync"/> — which
/// replays the corpus grid across seeds and both pacing regimes and scores every combining form as
/// post-processing over each replay's ONE snapshot.</para>
/// <para><b>A rule scoring perfectly on BOTH answer arms is a BUG REPORT, not a winner.</b> The two arms
/// declare contradictory answers over identical writes — <see cref="RoutineAnswer.Recent"/> names phase B and
/// <see cref="RoutineAnswer.Standing"/> names phase A — so nothing can honestly win both.</para>
/// <para><b><c>mean</c> is NOT a finding, and reading it as one is the trap this run prints a retrievability
/// band to prevent.</b> At the snapshot phase B has never been recalled and was written immediately before,
/// so every B member sits at or above every A member on all 600 replays. Retrievability is capped at 1, so
/// <c>mean(B) &gt;= mean(A)</c> then holds BY DEFINITION: <c>mean</c> cannot select phase A on this corpus,
/// and 600 replays did not test it. <c>sum</c> and <c>count</c> do not normalize by size, so phase A's eight
/// members can outweigh phase B's four — those arms are real measurements.</para>
/// <para><b>Measured 2026-08-28: 600 replays, every control held, no rule won both arms.</b> <c>sum</c>
/// INVERTS with pacing — phase A under bulk, phase B under spaced, 300/300 each way — which is real, because
/// it turns on whether mean r(A) sits above or below 0.5 and the burst damping is what decides that. The
/// theta curve LOCATES phase A's band rather than ranking rules: bulk holds A through 0.6, keeps A 290/300
/// at 0.7, splits three ways at 0.8, flips to B at 0.9; spaced holds A at 0.1, splits 250B/50A at 0.2 and is
/// B from 0.3 up. THREE cells are non-unanimous; only bulk's two are seed-split (55/60, 40/60 shapes).
/// <c>ConnectionBoost=0</c> moved no cell on either clock. gemma-3 4b it answered phase B on 300/300 pairs
/// with ZERO order disagreements — the recency reading, though the prompt LABELS which option is recent.</para>
/// </summary>
internal static class MemoryGistSupportSweep
{
    private const int Seed = 12345;
    private const int SeedCount = 5;
    private const int RecallLimit = 10;
    private const int Routines = 12;
    private static readonly CorpusShape ScreenShape = CorpusShape.Default with { RoutineCount = Routines };

    internal static async Task<int> RunAsync(string[] args) =>
        args.Contains("--screen") ? await RunScreenAsync() : await RunFullAsync(args);

    // ---------------------------------------------------------------- the ladder screen

    /// <summary>
    /// The generative size ladder alone, on one shape: can a model answer a BINARY regime question at all,
    /// and at what size? The measured 3-4B judge floor is a fact about 40-way verification, not about this.
    /// <para><b>Position is COUNTERBALANCED and a rung must win both orders.</b> A small model asked to pick
    /// between two labelled options has a position bias, and a rung that always answers "2" scores 50% while
    /// looking like a partial success.</para>
    /// <para><b>ONE rung per invocation.</b> The model is whatever <c>SweepDoubles.ChatModel</c> resolves
    /// (<c>LYNTAI_LIVE_CHAT_MODEL</c>), and this method prints ONE verdict line — so the four-rung ladder
    /// below is four separate runs against four servers, and no single command produces that table.</para>
    /// <para><b>Measured 2026-08-28, llama-server, <c>RoutineCount=12</c>, seed 12345 — all four rungs run,
    /// none NOT TESTED.</b> gemma-3 270m it: A-first EARLIER, B-first LATER — a pure "always answer option 1"
    /// bias, does NOT survive. gemma-3 1b it: A-first LATER, B-first EARLIER — a pure "always answer option
    /// 2" bias, does NOT survive. gemma-3 4b it (ref): LATER in both orders, content-driven, SURVIVES.
    /// Llama-3.2 1B Instruct Q4_K_M (ctrl, size held = rung 2): A-first EARLIER, B-first LATER — the SAME
    /// direction as the 270m rung and the OPPOSITE of gemma-3 1b's, does NOT survive.</para>
    /// <para><b>The control did not separate generation from size.</b> Both 1B-class rungs fail by pure
    /// position bias, each in its own direction, so this shape's floor sits strictly between 1B and 4B
    /// parameters rather than at a family boundary within 1B.</para>
    /// </summary>
    private static async Task<int> RunScreenAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-support");
        if (chat is null) return 1;

        var corpus = MemoryCorpus.Generate(ScreenShape, Seed);
        var (query, phaseA, phaseB) = RoutineMaterial(corpus);

        Console.WriteLine($"memory-support --screen · model={chat.Model} · shape=RoutineCount=12 · seed={Seed}");
        Console.WriteLine($"  phase A: {phaseA.Count} entries (earlier)   phase B: {phaseB.Count} entries (later)");
        Console.WriteLine();

        // A position-biased rung emits the SAME digit in both orders. The digit-to-regime mapping flips
        // between orders, so that digit cannot be correct twice - which is what counterbalancing catches.
        var warm = await AskAsync(chat, query, phaseA, phaseB, bFirst: false);
        var swapped = await AskAsync(chat, query, phaseA, phaseB, bFirst: true);

        // "regime", never "option": a regime is what Describe() prints and what the pass condition tests,
        // while an "option" is a POSITION in the prompt and flips between the two orders.
        Console.WriteLine($"  A-first  -> answered {Describe(warm)}   (correct: the LATER regime)");
        Console.WriteLine($"  B-first  -> answered {Describe(swapped)}   (correct: the LATER regime)");
        Console.WriteLine();

        var passed = warm is Verdict.Later && swapped is Verdict.Later;
        Console.WriteLine(passed
            ? "  VERDICT: survives - both orders correct. Eligible for the full sweep."
            : "  VERDICT: does NOT survive. Record it as dropped; do not spend the grid on it.");
        return 0;
    }

    // ---------------------------------------------------------------- the full sweep

    /// <summary>Which regime a rule picked. <see cref="Tie"/> is a real outcome, not an error: two integer
    /// counts tie often, and a rule that cannot choose has not answered.</summary>
    private enum Pick { PhaseA, PhaseB, Tie }

    /// <summary>Both regimes' stored decay states at one point in one replay. The STATES rather than their
    /// retrievabilities, so every combining form AND every curve variant is post-processing over the same
    /// snapshot — which is what makes a dense theta sweep and the ConnectionBoost control free.</summary>
    private sealed record StateSnapshot(IReadOnlyList<MemoryDecayState> A, IReadOnlyList<MemoryDecayState> B);

    /// <summary>The pacing regime a replay's injected clock models.</summary>
    private sealed record ClockArm(string Label, TimeSpan Step, string Note);

    /// <summary>One combining form over a regime's member retrievabilities.</summary>
    private sealed record Rule(string Label, Func<IReadOnlyList<double>, double> Score);

    /// <summary>One replay, plus everything a control needs read back from what actually ran.</summary>
    private sealed record Cell(
        string Clock, string Shape, int Seed, int RoutineCount, Pick RecentDeclared, Pick StandingDeclared,
        StateSnapshot Before, StateSnapshot After, bool OrderOk, bool PolicyClockOk, bool StoreClockOk,
        bool DiffersOnlyInDeclaredAnswer);

    /// <summary>
    /// The CARDINALITY axis, added 2026-08-28 — the question `TASKS.md` Part 105 opened, and the one the
    /// first 600-replay run held constant at 12.
    /// <para>B is <c>Math.Max(1, RoutineCount / 3)</c> and A is the remainder, so |A|/|B| is 2 only at
    /// multiples of 3 and reaches <b>4.0 at 5</b>. These four rungs give ratios 2.0, 4.0, 3.0, 2.0 — and the
    /// two ratio-2.0 rungs sit at different SIZES (3 and 12) deliberately, because without them a result that
    /// moves across this axis cannot be attributed to the RATIO rather than to |A| simply growing.</para>
    /// <para><b><c>RoutineSupport</c>'s all-of clamp does not reach this sweep</b>, stated because
    /// <c>CorpusShape.RoutineSupport</c> warns that an arm can silently stop exercising the n-of branch: at
    /// 3, 5 and 8 the default support of 3 is at or above |B|, so <c>RecallQuality.Measure</c> WOULD score
    /// those queries by all-of. This sweep never calls it — it scores retrievability snapshots and reads the
    /// declared answer off <c>RelevantIds</c> — so the clamp changes nothing here.</para>
    /// </summary>
    private static readonly int[] GridRoutineCounts = [3, 5, 8, 12];

    /// <summary>The 60 shapes of <see cref="CorpusGrid"/> crossed with <see cref="GridRoutineCounts"/>. The
    /// base grid is SHARED with <c>MemoryCorpusTests</c> rather than restated here, so this sweep's claim to
    /// run the grid the corpus invariants are proved over cannot go quietly false when either side moves.
    /// </summary>
    private static IEnumerable<(string Label, CorpusShape Shape)> Grid()
    {
        foreach (var shape in CorpusGrid.Shapes())
        foreach (var routines in GridRoutineCounts)
            yield return ($"n{shape.NoiseDensity}/c{shape.CandidateCount}/r{shape.ReuseRatio}/k{routines}",
                shape with { RoutineCount = routines });
    }

    /// <summary>|A| and |B| for a routine count, from the corpus's own rule rather than a second copy of the
    /// arithmetic — <see cref="CorpusShape.RoutineRegimes"/>.</summary>
    private static (int A, int B) Regimes(int routineCount) => CorpusShape.RoutineRegimes(routineCount);

    private static readonly ClockArm[] Clocks =
    [
        new("bulk", TimeSpan.FromMilliseconds(100),
            "inside BurstDampenedAgePolicy's own 5s window, so one import arbitrates within itself"),
        new("spaced", TimeSpan.FromSeconds(10),
            "outside it, so every write starts its own burst and the damping degenerates to per-write ticks"),
    ];

    private static Rule[] Rules()
    {
        var rules = new List<Rule>
        {
            new("sum", v => v.Sum()),
            new("mean", v => v.Count == 0 ? 0 : v.Average()),
        };
        for (var i = 1; i <= 9; i++)
        {
            var theta = i / 10.0;
            rules.Add(new Rule($"count@{theta:F1}", v => v.Count(x => x >= theta)));
        }
        return [.. rules];
    }

    /// <summary>
    /// The full sweep: <see cref="Grid"/> x <see cref="SeedCount"/> seeds x <see cref="Clocks"/>, with sum,
    /// mean and count-at-theta scored as post-processing over each replay's ONE snapshot, against BOTH
    /// declared answers.
    /// <para><b>The two answer arms share one replay, and that is a control rather than a shortcut.</b>
    /// <see cref="RoutineAnswer"/> moves the final query's declared answer and nothing else, so the timeline
    /// the engine sees is identical - checked per (shape, seed) rather than assumed.</para>
    /// <para><b>The snapshot is taken the instant BEFORE the final routine query, and the run MEASURES what
    /// that is worth rather than asserting it</b>: rescoring the whole table off the after-query snapshot
    /// moves 2 of 44 cells (<c>count@0.9</c> under bulk, both curves). The mechanism is that query
    /// REINFORCING PHASE A on 127/300 bulk replays, not its pinning phase B — B already sits at ~0.99, so
    /// pinning it to 1 moves nothing.</para>
    /// <para><b>Both clocks are INJECTED</b> — the store's, the engine's and the age policy's — because a
    /// real one measures how fast this host replayed, not a deployment's pacing. What the run MEASURED is on
    /// the class doc; this method's job is only how the measurement is taken.</para>
    /// </summary>
    private static async Task<int> RunFullAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var shapes = Grid().ToList();
        var seeds = Enumerable.Range(0, SeedCount).Select(i => Seed + i).ToList();
        var rules = Rules();

        PrintFullPreamble(shapes.Count, seeds, rules);

        var cells = new ConcurrentBag<Cell>();
        await Parallel.ForEachAsync(
            from shape in shapes from seed in seeds from clock in Clocks select (shape, seed, clock),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) =>
            {
                var recent = MemoryCorpus.Generate(item.shape.Shape, item.seed);
                var standing = MemoryCorpus.Generate(
                    item.shape.Shape with { RoutineAnswer = RoutineAnswer.Standing }, item.seed);

                var replay = await ReplayAsync(recent, item.clock.Step);
                cells.Add(new Cell(item.clock.Label, item.shape.Label, item.seed,
                    item.shape.Shape.RoutineCount,
                    DeclaredOf(recent), DeclaredOf(standing), replay.Before, replay.After,
                    replay.OrderOk, replay.PolicyClockOk, replay.StoreClockOk,
                    DiffersOnlyInDeclaredAnswer(recent, standing)));
            });

        var all = cells.ToList();
        var curves = new (string Label, DsrRetrievability Curve)[]
        {
            ("default", new DsrRetrievability()),
            ("ConnectionBoost=0", new DsrRetrievability(new DsrOptions { ConnectionBoost = 0 })),
        };

        var loud = false;
        var moved = new List<string>();
        foreach (var clock in Clocks)
        {
            var forClock = all.Where(c => c.Clock == clock.Label).ToList();
            var scored = new Dictionary<string, IReadOnlyList<ArmScore>>(StringComparer.Ordinal);
            foreach (var (curveLabel, curve) in curves)
            {
                var table = rules.Select(r => Score(r.Label, forClock, s => Select(r, s, curve))).ToList();
                scored[curveLabel] = table;
                PrintRetrievabilityBands(clock, curveLabel, forClock, curve);
                PrintArmTable(clock, curveLabel, table);
                PrintCardinalityTable(clock, curveLabel, forClock, rules, curve);
                loud |= ReportImpossibleWins(clock.Label, curveLabel, table);
            }
            moved.Add(CompareCurves(clock.Label, scored["default"], scored["ConnectionBoost=0"]));
        }

        Console.WriteLine("=== ConnectionBoost=0 control ===");
        foreach (var line in moved) Console.WriteLine("  " + line);
        Console.WriteLine("  Scope: the control re-reads the SAME stored states with the connection term off, so");
        Console.WriteLine("  it removes the graph's contribution to the rule's INPUT. It does not re-rank: the");
        Console.WriteLine("  ranker's own contribution to stored Stability would need a second replay, which");
        Console.WriteLine("  this run does not do and does not claim.");
        Console.WriteLine();

        loud |= await RunModelArmAsync(args, all, shapes, seeds);
        var controlsOk = PrintControls(all, shapes.Count, seeds.Count, rules, curves);

        Console.WriteLine($"Total: {all.Count} replays in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        if (!controlsOk)
            Console.Error.WriteLine("memory-support: a control did NOT hold - the table above is not trustworthy.");
        return controlsOk && !loud ? 0 : 1;
    }

    private sealed record ArmScore(string Rule, int PicksA, int PicksB, int Ties, int StableShapes,
        int ShapeCount, double RecentAccuracy, double StandingAccuracy);

    /// <summary>Scores one rule over one clock's replays against BOTH declared answers. Accuracy is measured
    /// against the answer each corpus actually DECLARED, never against a hard-coded phase — which is what
    /// lets <see cref="ReportImpossibleWins"/> catch two arms wired to the same ground truth.
    /// <para><b>On THIS grid the two accuracy columns are the pick columns transposed — arithmetically, not
    /// approximately.</b> The standing corpus is GENERATED but never replayed; C4 holds on EVERY cell so the
    /// two declared answers differ and neither ties, C5 holds on every cell so both regimes are fully
    /// enumerated (stated as a relation rather than a count, which the cardinality axis quadrupled), and
    /// <see cref="RoutineAnswer.Recent"/> names phase B. Every cell therefore carries
    /// <c>RecentDeclared = PhaseB</c> and <c>StandingDeclared = PhaseA</c>, which makes
    /// <c>RecentAccuracy == PicksB/n</c> and <c>StandingAccuracy == PicksA/n</c>. Read "recent 1.000 /
    /// standing 0.000" as ONE measurement stated twice, never as two.</para>
    /// <para><b>The standing ARM still earns its keep even though the standing COLUMN is redundant.</b> The
    /// corpus axis is what shows the same writes carrying two defensible answers — the argument the gist
    /// tier's shape turns on — and it is what <see cref="ReportImpossibleWins"/> checks against. Dropping the
    /// arm to remove a duplicated column would drop the control with it.</para></summary>
    private static ArmScore Score(string rule, IReadOnlyList<Cell> cells, Func<StateSnapshot, Pick> select)
    {
        var picks = cells.Select(c => (c.Shape, Pick: select(c.Before), c.RecentDeclared, c.StandingDeclared)).ToList();
        var byShape = picks.GroupBy(p => p.Shape, StringComparer.Ordinal).ToList();
        return new ArmScore(
            rule,
            picks.Count(p => p.Pick == Pick.PhaseA),
            picks.Count(p => p.Pick == Pick.PhaseB),
            picks.Count(p => p.Pick == Pick.Tie),
            byShape.Count(g => g.Select(p => p.Pick).Distinct().Count() == 1),
            byShape.Count,
            picks.Count == 0 ? 0 : (double)picks.Count(p => p.Pick == p.RecentDeclared) / picks.Count,
            picks.Count == 0 ? 0 : (double)picks.Count(p => p.Pick == p.StandingDeclared) / picks.Count);
    }

    private static Pick Select(Rule rule, StateSnapshot snapshot, DsrRetrievability curve)
    {
        var a = rule.Score(Read(snapshot.A, curve));
        var b = rule.Score(Read(snapshot.B, curve));
        if (Math.Abs(a - b) <= 1e-12) return Pick.Tie;
        return a > b ? Pick.PhaseA : Pick.PhaseB;
    }

    private static IReadOnlyList<double> Read(IReadOnlyList<MemoryDecayState> states, DsrRetrievability curve) =>
        [.. states.Select(s => curve.Retrievability(s))];

    // ---------------------------------------------------------------- replay

    private sealed record ReplayResult(StateSnapshot Before, StateSnapshot After, bool OrderOk,
        bool PolicyClockOk, bool StoreClockOk);

    private static async Task<ReplayResult> ReplayAsync(MemoryCorpus corpus, TimeSpan step)
    {
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var writeCount = corpus.Steps.OfType<CorpusWrite>().Count();
        var finalRoutineQuery = FinalRoutineQuery(corpus);

        // Read by three seams - the age policy, the store and the engine - and stepped only on a write, so
        // "the wall clock is read nowhere" is a property of construction. The age policy and the store get
        // SEPARATE counters: one shared counter passes on the store's reads alone even if the policy had
        // been left on a wall clock. The ENGINE's clock is deliberately uncounted - it feeds PruneAsync
        // only, which this replay never calls, so no control can distinguish injecting it from not.
        // Single-threaded per replay by design.
        var now = DateTimeOffset.UnixEpoch;
        var policyReads = 0;
        var storeReads = 0;
        DateTimeOffset EngineClock() => now;
        DateTimeOffset StoreClock() { storeReads++; return now; }
        DateTimeOffset PolicyClock() { policyReads++; return now; }

        const string engineName = "gist-support";
        var store = new InMemoryMemoryGraphStore(StoreClock);
        var engine = new GraphMemoryEngine(engineName, store,
            agePolicies: [new BurstDampenedAgePolicy(clock: PolicyClock)], clock: EngineClock);

        var observed = new StringBuilder();
        StateSnapshot? before = null;
        // Both snapshots bracket the FINAL ROUTINE QUERY specifically, rather than `after` being taken once
        // the timeline has run out. Those were equivalent only because `Grid()` left AuthoritativeCount and
        // HeadlineOnlyCount at 0, which made that query genuinely last — a shape turning either on would have
        // silently changed what the After-vs-Before diagnostic measures, and nothing would have reported it.
        StateSnapshot? after = null;

        foreach (var s in corpus.Steps)
        {
            switch (s)
            {
                case CorpusWrite w:
                    observed.Append('W');
                    now += step;
                    await engine.RememberAsync(w.Write);
                    break;

                case CorpusQuery q:
                {
                    observed.Append('Q');
                    var isFinalRoutine = ReferenceEquals(q, finalRoutineQuery);
                    if (isFinalRoutine)
                        before = await SnapshotAsync(store, engineName, firstWrite, writeCount);
                    await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: RecallLimit));
                    if (isFinalRoutine)
                        after = await SnapshotAsync(store, engineName, firstWrite, writeCount);
                    break;
                }

                case CorpusExpand:
                    observed.Append('X');
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(CorpusStep)} '{s.GetType().Name}' - CorpusStep is sealed to three "
                        + "cases precisely so this switch stays exhaustive.");
            }
        }

        var declared = string.Concat(corpus.Steps.Select(MemoryPolicySweep.CorpusStepMarker));
        return new ReplayResult(before!, after!, observed.ToString() == declared,
            policyReads == writeCount, storeReads >= writeCount);
    }

    /// <summary>Reads the stored state with a null-query <c>SeedAsync</c> - the enumeration path, which does
    /// NOT reinforce what it returns - and buckets every routine member into its regime.</summary>
    private static async Task<StateSnapshot> SnapshotAsync(InMemoryMemoryGraphStore store, string engine,
        MemoryWrite firstWrite, int writeCount)
    {
        var nodes = await store.SeedAsync(engine, firstWrite.TaskKey, firstWrite.Scope,
            query: null, limit: writeCount + 10);

        var a = new List<MemoryDecayState>();
        var b = new List<MemoryDecayState>();
        foreach (var node in nodes)
        {
            var id = MemoryPolicySweep.ExtractCorpusId(node.Content);
            if (id.StartsWith("routineA", StringComparison.Ordinal)) a.Add(node.DecayState);
            else if (id.StartsWith("routineB", StringComparison.Ordinal)) b.Add(node.DecayState);
        }
        return new StateSnapshot(a, b);
    }

    // ---------------------------------------------------------------- the model arm

    /// <summary>
    /// The one rung the ladder left standing, consulted once per (shape, seed) at the final routine query.
    /// <para><b>Additive, never a gate.</b> The mechanical arms above are model-free and run in full whether
    /// or not a model answers; this arm is included when one is reachable and DISCLOSED as skipped when it is
    /// not. A bounded run that omits what it dropped reads as having covered everything.</para>
    /// <para><b>Clock-independent by construction:</b> the prompt carries the entries' TEXT, never their
    /// retrievability, so one judgement serves both pacing regimes.</para>
    /// </summary>
    /// <returns><c>true</c> when the arm won both answer arms - impossible, so a wiring report.</returns>
    private static async Task<bool> RunModelArmAsync(string[] args, IReadOnlyList<Cell> cells,
        IReadOnlyList<(string Label, CorpusShape Shape)> shapes, IReadOnlyList<int> seeds)
    {
        Console.WriteLine("=== model arm ===");
        if (args.Contains("--skip-model"))
        {
            Console.WriteLine("  SKIPPED by --skip-model. The mechanical arms above are model-free and ran in full.");
            Console.WriteLine();
            return false;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var chat = new SweepDoubles.OpenAiCompatibleChat(http, SweepDoubles.BaseUrl, SweepDoubles.ChatModel);
        if (!await chat.ReachableAsync())
        {
            Console.WriteLine($"  SKIPPED: no chat model answered at {SweepDoubles.BaseUrl} " +
                $"({SweepDoubles.ChatModel}).");
            Console.WriteLine("  This arm measures what a MODEL is worth, so a scripted stand-in would measure the");
            Console.WriteLine("  stand-in. The mechanical arms above are model-free and ran in full; this one is");
            Console.WriteLine($"  additive. Point {SweepDoubles.UrlVariable} at an OpenAI-compatible endpoint and");
            Console.WriteLine($"  name the model with {SweepDoubles.ChatModelVariable} to include it.");
            Console.WriteLine();
            return false;
        }

        var judged = (from shape in shapes from seed in seeds select (shape, seed)).ToList();
        Console.WriteLine($"  model={chat.Model} at {SweepDoubles.BaseUrl}");
        Console.WriteLine($"  {judged.Count} (shape, seed) pairs x 2 presentation orders = " +
            $"{judged.Count * 2} calls");

        var declared = cells.GroupBy(c => (c.Shape, c.Seed))
            .ToDictionary(g => g.Key, g => (g.First().RecentDeclared, g.First().StandingDeclared));

        var picks = new List<((string Label, CorpusShape Shape) Shape, int Seed, Pick Pick)>();
        var latencies = new List<double>();
        foreach (var (shape, seed) in judged)
        {
            var corpus = MemoryCorpus.Generate(shape.Shape, seed);
            var (query, phaseA, phaseB) = RoutineMaterial(corpus);

            var one = Stopwatch.StartNew();
            var aFirst = await AskAsync(chat, query, phaseA, phaseB, bFirst: false);
            var bFirst = await AskAsync(chat, query, phaseA, phaseB, bFirst: true);
            latencies.Add(one.Elapsed.TotalSeconds / 2);

            // Counterbalanced: a pick counts only when both orders agree, so a position-biased answer -
            // the SAME digit twice, which cannot be correct in both mappings - lands as Tie rather than 50%.
            picks.Add((shape, seed, aFirst == bFirst ? ToPick(aFirst) : Pick.Tie));
        }

        var n = picks.Count;
        var recent = picks.Count(p => p.Pick == declared[(p.Shape.Label, p.Seed)].RecentDeclared);
        var standing = picks.Count(p => p.Pick == declared[(p.Shape.Label, p.Seed)].StandingDeclared);
        Console.WriteLine();
        Console.WriteLine($"  {"Arm",-22} {"picks A",8} {"picks B",8} {"disagree",9} {"recent",8} {"standing",9}");
        Console.WriteLine($"  {"model (both orders)",-22} {picks.Count(p => p.Pick == Pick.PhaseA),8} " +
            $"{picks.Count(p => p.Pick == Pick.PhaseB),8} {picks.Count(p => p.Pick == Pick.Tie),9} " +
            $"{(double)recent / n,8:F3} {(double)standing / n,9:F3}");
        Console.WriteLine();
        // Every figure here is WARM, including the first: the reachability probe above already loaded the
        // model and ran one completion, so no judgement pays a cold start. The first is printed separately
        // only because it is the one that pays this prompt's own prefill against an empty KV cache.
        Console.WriteLine("  latency per MODEL CALL, all WARM - the reachability probe loads the model and runs");
        Console.WriteLine("  one completion before any judgement, so no judgement here pays a cold start:");
        Console.WriteLine($"    first judgement's calls {latencies[0]:F2}s each (pays this prompt's own prefill);");
        Console.WriteLine($"    mean over the remaining {Math.Max(0, latencies.Count - 1)} judgements: " +
            $"{(latencies.Count > 1 ? latencies.Skip(1).Average() : double.NaN):F2}s/call.");
        Console.WriteLine();

        var impossible = n > 0 && recent == n && standing == n;
        if (impossible)
            Console.Error.WriteLine("  !! model wins BOTH answer arms - impossible; the arms are not wired to " +
                "different ground truth.");
        return impossible;
    }

    private static Pick ToPick(Verdict v) => v switch
    {
        Verdict.Later => Pick.PhaseB,
        Verdict.Earlier => Pick.PhaseA,
        _ => Pick.Tie,
    };

    // ---------------------------------------------------------------- reporting

    private static void PrintFullPreamble(int shapeCount, IReadOnlyList<int> seeds, IReadOnlyList<Rule> rules)
    {
        Console.WriteLine("=== memory-support: which rule selects a recurring cluster's CURRENT regime ===");
        Console.WriteLine($"Shapes: {shapeCount} (NoiseDensity x CandidateCount x ReuseRatio x RoutineCount)");
        Console.WriteLine("RoutineCount rungs: " + string.Join(", ", GridRoutineCounts.Select(k =>
        {
            var (a, b) = Regimes(k);
            return $"{k} -> |A|/|B| = {a}/{b} = {(double)a / b:F2}";
        })));
        Console.WriteLine($"Seeds: {seeds[0]}..{seeds[^1]} ({seeds.Count})   Clocks: {Clocks.Length}   " +
            $"=> {shapeCount * seeds.Count * Clocks.Length} replays");
        Console.WriteLine($"Rules: {string.Join(", ", rules.Select(r => r.Label))}");
        Console.WriteLine();
        Console.WriteLine("Phase A is the OLDER, larger regime (8 members); phase B the NEWER, smaller one (4).");
        Console.WriteLine("Both answer arms score the SAME replay against two declared answers:");
        Console.WriteLine("  recent   - RoutineAnswer.Recent   declares phase B correct");
        Console.WriteLine("  standing - RoutineAnswer.Standing declares phase A correct");
        Console.WriteLine("They are contradictory over identical writes, so no rule can honestly win both.");
        Console.WriteLine();
        Console.WriteLine("Both clocks are INJECTED; the wall clock is read nowhere:");
        foreach (var c in Clocks)
            Console.WriteLine($"  {c.Label,-7} +{c.Step.TotalMilliseconds:F0}ms per write - {c.Note}");
        Console.WriteLine();
    }

    /// <summary>
    /// Where each regime's members actually SIT on the retrievability axis at the snapshot — printed before
    /// the picks, because the picks alone hide what decides them.
    /// <para><b>The line that matters is the last one.</b> Retrievability is capped at 1, so on any replay
    /// where every phase-B member is at or above every phase-A member, <c>mean(B) &gt;= mean(A)</c> holds by
    /// definition and <c>mean</c> CANNOT select phase A — no number of replays tests it. <c>sum</c> and
    /// <c>count</c> do not normalize by size, so phase A's eight members can still outweigh phase B's four
    /// and those arms stay real measurements. Without this block a reader takes "mean picks B on 600/600"
    /// for a finding, which is the mistake this output exists to prevent.</para>
    /// </summary>
    private static void PrintRetrievabilityBands(ClockArm clock, string curve, IReadOnlyList<Cell> cells,
        DsrRetrievability curveImpl)
    {
        var a = cells.Select(c => Read(c.Before.A, curveImpl)).ToList();
        var b = cells.Select(c => Read(c.Before.B, curveImpl)).ToList();
        var dominated = cells.Select((_, i) => (A: a[i], B: b[i]))
            .Count(p => p.A.Count > 0 && p.B.Count > 0 && p.B.Min() >= p.A.Max());

        Console.WriteLine($"--- clock={clock.Label}   curve={curve}   retrievability at the snapshot ---");
        Console.WriteLine($"{"Regime",-10} {"members",8} {"min",8} {"max",8} {"per-replay mean in",-22}");
        Print("phase A", a);
        Print("phase B", b);
        Console.WriteLine($"every phase-B member >= every phase-A member on {dominated}/{cells.Count} replays.");
        if (dominated == cells.Count)
            Console.WriteLine("  => r is CAPPED at 1, so mean(B) >= mean(A) holds BY DEFINITION here: `mean` "
                + "cannot select\n     phase A, and this instrument does not test it. `sum`/`count` do not "
                + "normalize by size, so\n     phase A's 8 members can still outweigh phase B's 4 - those "
                + "arms remain real measurements.");
        Console.WriteLine();

        // Every aggregate is guarded, not just `means`. These bands print BEFORE the controls that would
        // catch an empty regime, so on a shape carrying no routine class (RoutineCount = 0) an unguarded
        // Min()/Max() throws here and the run dies before saying why.
        void Print(string label, IReadOnlyList<IReadOnlyList<double>> perReplay)
        {
            var flat = perReplay.SelectMany(v => v).ToList();
            var means = perReplay.Where(v => v.Count > 0).Select(v => v.Average()).ToList();
            if (flat.Count == 0 || means.Count == 0)
            {
                Console.WriteLine($"{label,-10} {0,8} {"empty",8} {"empty",8} " +
                    $"{"(no members enumerated)",-22}");
                return;
            }
            Console.WriteLine($"{label,-10} {flat.Count,8} {flat.Min(),8:F3} {flat.Max(),8:F3} " +
                $"{$"[{means.Min():F3}, {means.Max():F3}]",-22}");
        }
    }

    /// <summary>The cardinality axis, scored per rung — the question this sweep held constant until
    /// 2026-08-28. A rule that answers the declared regime at |A|/|B| = 2 need not answer it at 4.
    /// <para>Printed per CLOCK and per CURVE rather than pooled, because <c>sum</c> already inverts with
    /// pacing: a cardinality row averaged over both clocks would combine two opposite results into one
    /// meaningless number, which is the reading this table exists to make impossible.</para></summary>
    private static void PrintCardinalityTable(ClockArm clock, string curve, IReadOnlyList<Cell> forClock,
        IReadOnlyList<Rule> rules, DsrRetrievability curveImpl)
    {
        Console.WriteLine($"--- clock={clock.Label}   curve={curve}   by RoutineCount (|A|/|B|) ---");
        Console.Write($"{"Rule",-12}");
        foreach (var k in GridRoutineCounts)
        {
            var (a, b) = Regimes(k);
            Console.Write($"  {$"k={k} {a}/{b}={(double)a / b:F2}",-18}");
        }
        Console.WriteLine();
        Console.WriteLine(new string('-', 12 + (GridRoutineCounts.Length * 20)));

        foreach (var rule in rules)
        {
            Console.Write($"{rule.Label,-12}");
            foreach (var k in GridRoutineCounts)
            {
                var rung = forClock.Where(c => c.RoutineCount == k).ToList();
                var s = Score(rule.Label, rung, sn => Select(rule, sn, curveImpl));
                var n = s.PicksA + s.PicksB + s.Ties;
                var (label, hits) = s.PicksA >= s.PicksB && s.PicksA >= s.Ties ? ("A", s.PicksA)
                    : s.PicksB >= s.Ties ? ("B", s.PicksB)
                    : ("tie", s.Ties);
                Console.Write($"  {$"{label} {hits}/{n}",-18}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    private static void PrintArmTable(ClockArm clock, string curve, IReadOnlyList<ArmScore> table)
    {
        Console.WriteLine($"--- clock={clock.Label}   curve={curve} ---");
        Console.WriteLine($"{"Rule",-12} {"picks A",8} {"picks B",8} {"ties",6} {"recent",8} {"standing",9} " +
            $"{"stable",8}");
        Console.WriteLine(new string('-', 64));
        foreach (var r in table)
            Console.WriteLine($"{r.Rule,-12} {r.PicksA,8} {r.PicksB,8} {r.Ties,6} {r.RecentAccuracy,8:F3} " +
                $"{r.StandingAccuracy,9:F3} {$"{r.StableShapes}/{r.ShapeCount}",8}");
        Console.WriteLine("stable = shapes on which every seed produced the same pick.");
        Console.WriteLine();
    }

    /// <summary>The fail-loud property, as a check rather than a note: a rule that scores perfectly on both
    /// answer arms has found a wiring defect, because the arms declare contradictory answers.</summary>
    private static bool ReportImpossibleWins(string clock, string curve, IReadOnlyList<ArmScore> table)
    {
        var loud = false;
        foreach (var r in table.Where(r => r.RecentAccuracy == 1.0 && r.StandingAccuracy == 1.0))
        {
            Console.Error.WriteLine($"  !! {clock}/{curve}/{r.Rule} wins BOTH answer arms - impossible; the " +
                "arms are not wired to different ground truth.");
            loud = true;
        }
        return loud;
    }

    private static string CompareCurves(string clock, IReadOnlyList<ArmScore> a, IReadOnlyList<ArmScore> b)
    {
        var pairs = a.Zip(b).ToList();
        var worst = pairs.Max(p => Math.Abs(p.First.RecentAccuracy - p.Second.RecentAccuracy));
        var flipped = pairs
            .Where(p => Majority(p.First) != Majority(p.Second))
            .Select(p => p.First.Rule)
            .ToList();
        return flipped.Count == 0
            ? $"{clock}: verdict did NOT move - every rule selects the same regime under both curves; " +
              $"max |d recent-acc| = {worst:F3}."
            : $"{clock}: verdict MOVED on {string.Join(", ", flipped)} - the finding is partly about the " +
              $"ranker; max |d recent-acc| = {worst:F3}.";
    }

    private static Pick Majority(ArmScore s) =>
        s.PicksA > s.PicksB && s.PicksA > s.Ties ? Pick.PhaseA
        : s.PicksB > s.PicksA && s.PicksB > s.Ties ? Pick.PhaseB
        : Pick.Tie;

    /// <summary>Every control this run can check, read back from what actually ran rather than from the
    /// ingredients this file built. Two of them are DIAGNOSTICS rather than pass/fail and say so, and C0
    /// says outright that it cannot fail against today's loop.</summary>
    private static bool PrintControls(IReadOnlyList<Cell> cells, int shapeCount, int seedCount,
        IReadOnlyList<Rule> rules, IReadOnlyList<(string Label, DsrRetrievability Curve)> curves)
    {
        var expected = shapeCount * seedCount * Clocks.Length;
        var order = cells.Count(c => c.OrderOk);
        var policyClock = cells.Count(c => c.PolicyClockOk);
        var storeClock = cells.Count(c => c.StoreClockOk);
        var identical = cells.Count(c => c.DiffersOnlyInDeclaredAnswer);
        var differ = cells.Count(c => c.RecentDeclared != c.StandingDeclared
            && c.RecentDeclared != Pick.Tie && c.StandingDeclared != Pick.Tie);
        // C5 is a FUNCTION of the cell's own shape, not the literal 8/4 it was until 2026-08-28. That literal
        // was the RoutineCount=12 split, so the moment the cardinality axis landed the control failed on
        // 1800 of 2400 cells and correctly refused to publish the table — it was asserting the constant the
        // new axis exists to vary. `DECISIONS.md` D60 is the general form: a cross-arm rule is a function,
        // never a sentence.
        var populated = cells.Count(c =>
        {
            var (a, b) = Regimes(c.RoutineCount);
            return c.Before.A.Count == a && c.Before.B.Count == b;
        });
        var pinnedB = cells.Count(c => c.After.B.Count > 0 && c.After.B.All(s => s.Age <= 0));
        var touchedA = cells.Count(c => c.After.A.Any(s => s.Age <= 0));

        Console.WriteLine("=== controls (read back from what ran) ===");
        Console.WriteLine($"  replays                                   {cells.Count}/{expected}");
        Console.WriteLine($"  C0 step-ordered, interleaved replay       {order}/{cells.Count}");
        Console.WriteLine($"  C1 age policy read the clock 1x per write {policyClock}/{cells.Count}");
        Console.WriteLine($"  C2 store read the injected clock          {storeClock}/{cells.Count}");
        Console.WriteLine($"  C3 arms differ ONLY in declared answer    {identical}/{cells.Count}");
        Console.WriteLine($"  C4 the two declared answers DIFFER        {differ}/{cells.Count}");
        Console.WriteLine($"  C5 both regimes fully enumerated (per k)  {populated}/{cells.Count}");
        Console.WriteLine("  C0 CANNOT fail against today's replay loop - it appends one marker per step in");
        Console.WriteLine("  order and throws on an unknown case, so observed==declared is structural. It is");
        Console.WriteLine("  free insurance against a future edit that batches or skips, not evidence today.");
        Console.WriteLine("  No control covers the ENGINE's clock: it feeds PruneAsync only, never called here,");
        Console.WriteLine("  so injecting it is belt-and-braces and un-observable either way.");
        Console.WriteLine();

        PrintSnapshotPointDiagnostic(cells, rules, curves, pinnedB, touchedA);

        var ok = cells.Count == expected && order == cells.Count && policyClock == cells.Count
            && storeClock == cells.Count && identical == cells.Count && differ == cells.Count
            && populated == cells.Count;
        Console.WriteLine(ok ? "  ALL PASS/FAIL CONTROLS HELD." : "  A CONTROL FAILED - see the counts above.");
        Console.WriteLine();
        return ok;
    }

    /// <summary>
    /// What the final routine query does to BOTH regimes, and — decisively — whether scoring the table off
    /// the AFTER snapshot would have changed any cell.
    /// <para><b>The last question is the only one that settles this, so it is measured rather than argued.</b>
    /// Phase B sits within a hair of the ceiling before that query, so pinning it to exactly 1 removes what
    /// little spread it had and need not move a rule; the half that could corrupt the comparison is the
    /// query REINFORCING PHASE A. Rather than reason about either, the whole rule table is recomputed from
    /// <c>After</c> and the differing cells are counted.</para>
    /// </summary>
    private static void PrintSnapshotPointDiagnostic(IReadOnlyList<Cell> cells, IReadOnlyList<Rule> rules,
        IReadOnlyList<(string Label, DsrRetrievability Curve)> curves, int pinnedB, int touchedA)
    {
        var curve = new DsrRetrievability();
        Console.WriteLine("=== why the snapshot is taken BEFORE the final routine query (diagnostic) ===");
        foreach (var clock in Clocks)
        {
            var forClock = cells.Where(c => c.Clock == clock.Label).ToList();
            var before = forClock.Select(c => Read(c.Before.A, curve).Average()).ToList();
            var after = forClock.Select(c => Read(c.After.A, curve).Average()).ToList();
            var lifted = before.Zip(after).Count(p => p.Second > p.First + 1e-9);
            Console.WriteLine($"  {clock.Label,-7} mean r(phase A): before {before.Average():F3} -> after " +
                $"{after.Average():F3}   lifted on {lifted}/{forClock.Count} replays");
        }
        Console.WriteLine($"  phase A had >=1 member reset to Age<=0 by that query on {touchedA}/{cells.Count} " +
            $"replays; phase B was pinned to r == 1 on {pinnedB}/{cells.Count}.");

        var moved = new List<string>();
        var total = 0;
        foreach (var clock in Clocks)
        {
            var forClock = cells.Where(c => c.Clock == clock.Label).ToList();
            foreach (var (curveLabel, c) in curves)
                foreach (var rule in rules)
                {
                    total++;
                    var b = Score(rule.Label, forClock, s => Select(rule, s, c));
                    var a = ScoreAfter(rule, forClock, c);
                    if (b.PicksA != a.PicksA || b.PicksB != a.PicksB || b.Ties != a.Ties)
                        moved.Add($"{clock.Label}/{curveLabel}/{rule.Label}");
                }
        }

        Console.WriteLine($"  Scoring the SAME table off the AFTER snapshot instead moves {moved.Count}/{total} " +
            "cells" + (moved.Count == 0 ? "." : $": {string.Join(", ", moved)}."));
        Console.WriteLine(moved.Count == 0
            ? "  So on THIS corpus the snapshot point is a precaution, not a correction: it changes no cell\n"
              + "  in this table. It stays where it is because reading state the scored query just wrote is\n"
              + "  wrong on principle - but this run does not get to claim it rescued a number."
            : "  So the snapshot point IS load-bearing here: the cells above would differ if it moved.");
        Console.WriteLine();
    }

    /// <summary>The same scoring as <see cref="Score"/>, over each replay's AFTER snapshot — the only way to
    /// answer whether the snapshot POINT changes a result rather than merely changing some numbers.</summary>
    private static ArmScore ScoreAfter(Rule rule, IReadOnlyList<Cell> cells, DsrRetrievability curve)
    {
        var swapped = cells.Select(c => c with { Before = c.After }).ToList();
        return Score(rule.Label, swapped, s => Select(rule, s, curve));
    }

    // ---------------------------------------------------------------- shared corpus reading

    /// <summary>Which regime a corpus's final routine query DECLARES correct, read off that corpus rather
    /// than assumed from the arm's name.</summary>
    private static Pick DeclaredOf(MemoryCorpus corpus)
    {
        var ids = FinalRoutineQuery(corpus).RelevantIds;
        if (ids.Count == 0) return Pick.Tie;
        if (ids.All(i => i.StartsWith("routineB", StringComparison.Ordinal))) return Pick.PhaseB;
        if (ids.All(i => i.StartsWith("routineA", StringComparison.Ordinal))) return Pick.PhaseA;
        return Pick.Tie;
    }

    /// <summary>
    /// Whether two corpora differ in the DECLARED ANSWER and in nothing else — the exact claim C3's label
    /// makes, rather than the weaker "the parts I bothered to compare match".
    /// <para>Writes are compared on every field <see cref="MemoryWrite"/> carries, including
    /// <c>Metadata</c> pair by pair (record equality would compare that dictionary by reference and pass
    /// two different bags). Queries are compared on text and support, and their <c>RelevantIds</c> are
    /// required to differ at EXACTLY ONE step — the final routine query. A second differing query, or
    /// none, fails: both mean the arms are not the single controlled substitution they are sold as.</para>
    /// </summary>
    private static bool DiffersOnlyInDeclaredAnswer(MemoryCorpus a, MemoryCorpus b)
    {
        var left = a.Steps;
        var right = b.Steps;
        if (left.Count != right.Count) return false;

        var final = FinalRoutineQuery(a);
        var answersDiffer = 0;
        for (var i = 0; i < left.Count; i++)
        {
            switch (left[i], right[i])
            {
                case (CorpusWrite x, CorpusWrite y) when SameWrite(x.Write, y.Write):
                    break;

                case (CorpusExpand e, CorpusExpand f) when e.EntryId == f.EntryId:
                    break;

                case (CorpusQuery x, CorpusQuery y) when x.Text == y.Text && x.SupportNeeded == y.SupportNeeded:
                    if (x.RelevantIds.SequenceEqual(y.RelevantIds, StringComparer.Ordinal)) break;
                    if (!ReferenceEquals(x, final)) return false;   // some OTHER query's answer moved
                    answersDiffer++;
                    break;

                default:
                    return false;
            }
        }
        return answersDiffer == 1;
    }

    private static bool SameWrite(MemoryWrite x, MemoryWrite y) =>
        x.TaskKey == y.TaskKey && x.Scope == y.Scope && x.Content == y.Content
        && x.Headline == y.Headline && x.Grade == y.Grade
        && (x.Metadata?.Count ?? 0) == (y.Metadata?.Count ?? 0)
        && (x.Metadata is null || x.Metadata.All(kv =>
            y.Metadata!.TryGetValue(kv.Key, out var v) && v == kv.Value));

    private static CorpusQuery FinalRoutineQuery(MemoryCorpus corpus) =>
        corpus.Steps.OfType<CorpusQuery>()
            .Last(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)));

    // ---------------------------------------------------------------- the model prompt

    private enum Verdict { Later, Earlier, Unparsed }

    private static string Describe(Verdict v) => v switch
    {
        Verdict.Later => "the LATER regime",
        Verdict.Earlier => "the EARLIER regime",
        _ => "UNPARSED",
    };

    private static async Task<Verdict> AskAsync(SweepDoubles.OpenAiCompatibleChat chat, string query,
        IReadOnlyList<string> phaseA, IReadOnlyList<string> phaseB, bool bFirst)
    {
        var first = bFirst ? phaseB : phaseA;
        var second = bFirst ? phaseA : phaseB;
        var firstIsLater = bFirst;

        var answer = await chat.AskAsync(Prompt(query, first, second, firstIsLater));
        var trimmed = answer?.Trim();
        if (trimmed is null || trimmed.Length == 0) return Verdict.Unparsed;

        return trimmed[0] switch
        {
            '1' => firstIsLater ? Verdict.Later : Verdict.Earlier,
            '2' => firstIsLater ? Verdict.Earlier : Verdict.Later,
            _ => Verdict.Unparsed,
        };
    }

    // Recency is STATED rather than implied. The mechanical arms read it from retrievability, so withholding
    // it here would compare a model without the signal against rules that have it.
    private static string Prompt(string query, IReadOnlyList<string> first, IReadOnlyList<string> second,
        bool firstIsLater)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Question: {query}");
        sb.AppendLine();
        sb.AppendLine($"Option 1 ({(firstIsLater ? "more recent" : "older")}, {first.Count} entries):");
        foreach (var text in first) sb.AppendLine($"  - {text}");
        sb.AppendLine();
        sb.AppendLine($"Option 2 ({(firstIsLater ? "older" : "more recent")}, {second.Count} entries):");
        foreach (var text in second) sb.AppendLine($"  - {text}");
        sb.AppendLine();
        sb.Append("Which option describes what is true NOW? Reply with only the digit 1 or 2.");
        return sb.ToString();
    }

    /// <summary>The routine query and each regime's entry texts, read straight off the corpus timeline.</summary>
    private static (string Query, IReadOnlyList<string> PhaseA, IReadOnlyList<string> PhaseB) RoutineMaterial(
        MemoryCorpus corpus)
    {
        var phaseA = new List<string>();
        var phaseB = new List<string>();
        foreach (var write in corpus.Steps.OfType<CorpusWrite>())
        {
            var id = MemoryPolicySweep.ExtractCorpusId(write.Write.Content);
            if (id.StartsWith("routineA", StringComparison.Ordinal)) phaseA.Add(write.Write.Content);
            else if (id.StartsWith("routineB", StringComparison.Ordinal)) phaseB.Add(write.Write.Content);
        }

        var query = FinalRoutineQuery(corpus);
        return (query.Text, phaseA, phaseB);
    }
}
