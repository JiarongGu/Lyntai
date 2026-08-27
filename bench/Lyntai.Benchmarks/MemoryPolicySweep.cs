using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Lyntai.Tests.Memory.Corpus;
using Microsoft.Data.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// Replay the deterministic corpus against a LIVE SQLite-backed <see cref="GraphMemoryEngine"/> under a
/// FULL <c>{difficulty-live, difficulty-inert} x {ranking}</c> 2x2 grid, across a grid of
/// <see cref="CorpusShape"/>s and across many seeds, paired so every seed's four arms see the
/// byte-identical corpus. Not a BenchmarkDotNet benchmark; see <c>Program.cs</c>'s own <c>--sweep</c>
/// branch.
///
/// <para><b>Retargeted a THIRD time (2026-08-11, fsrs-properly plan, Task 4) — and the question changed
/// again, because the comparison changed again.</b> Originally a FOUR-arm <c>{ranking} x {forgetting}</c>
/// sweep (does <see cref="DsrRetrievability"/> or the exponential curve it shared the domain with,
/// <c>HalfLifeRetrievability</c>, forget better — answered, closed, <c>docs/DECISIONS.md</c> D49 made DSR
/// the registered default). Task 1 of THIS plan deleted that curve, which collapsed this file to a
/// two-arm, RANKING-only sweep (<c>Multiplicative+Dsr</c> vs <c>RRF+Dsr</c>) — there was nothing left to
/// pair difficulty against, because difficulty was still frozen dead state at that point. Task 2 made
/// <see cref="DsrRetrievability.Reinforce"/> maintain <see cref="MemoryDecayState.Difficulty"/> on every
/// review, which reopened a genuine SECOND axis — not a different curve shape, the SAME curve with one
/// axis switched on or off (<see cref="DsrOptions.DifficultyChangeWeight"/> and
/// <see cref="DsrOptions.DifficultyReversionWeight"/> both at zero is the inert control; see
/// <see cref="PrintControlsConfirmed"/>'s own difficulty-isolation clause for why BOTH, not one). This file now sweeps THAT
/// axis, crossed with the ranking axis it always covered — a full 2x2, not one-factor-at-a-time, because
/// both axes are cheap to cross fully and design spec §5 asks for exactly this: does difficulty-live beat
/// difficulty-inert, AND does the answer depend on which ranking policy is judging it (the mechanism the
/// predecessor sweep's own `topical` finding pinned on the ranking policy, not the curve).</para>
///
/// <para><b>Paired by seed — the design's central move, unchanged across all three retargets.</b> For a
/// fixed <c>(seed, shape)</c>, <see cref="MemoryCorpus.Generate"/> is a pure function, so all four arms
/// below replay the SAME corpus object. Subtracting one arm's <see cref="RecallQuality.MissRate"/> from
/// another's AT THE SAME SEED therefore cancels corpus-to-corpus variance — the dominant noise source a
/// single-seed measurement could never separate from a real policy difference. This file's whole job is
/// turning one paired draw into <c>N</c>, chosen from a real pilot rather than guessed.</para>
///
/// <para><b>Pilot, then scale.</b> <see cref="PilotSeedCount"/> seeds run first; their paired differences
/// give a real standard deviation for EACH of three effects (ranking, difficulty, and their interaction —
/// see <see cref="WorstEffectSd"/>), and the WORST of the three sizes the seed count actually needed to
/// detect <see cref="MinDetectableEffect"/> at <see cref="TargetPower"/> power. A sweep that cannot detect
/// the smallest difference worth acting on must say so rather than running for an hour and reporting a
/// null that would be read as evidence — see <see cref="PrintPilotDecision"/>.</para>
///
/// <para><b>Four confound controls, C0-C3, kept derived and gated — not weakened by any of the above —
/// plus a NEW isolation clause on C2 for this task.</b> Every control below is read back from what was
/// ACTUALLY built or ACTUALLY replayed, across EVERY <c>(seed, shape, arm)</c> combination the final run
/// touches, never from an ingredient variable this method closes over:
/// <list type="number">
/// <item><b>C0 — step-ordered, interleaved replay.</b> <see cref="ReplayAsync"/> walks
/// <c>corpus.Steps</c> in one ordered pass; the OBSERVED write/query sequence is compared against the
/// corpus's own DECLARED sequence for every single replay in the grid, and any mismatch fails the run.</item>
/// <item><b>C1 — no retention policies.</b> Every engine is built directly (never through
/// <c>AddMemoryEngine</c>), so <c>SalienceRetentionPolicy</c> never enters the retention collection.
/// Confirmed by reflecting <c>ModulatedRetrievability</c>'s own <c>_retentionPolicies</c> field on every
/// constructed engine.</item>
/// <item><b>C2 — each arm's own ranking options record, equalized on
/// <see cref="MultiplicativeRankingOptions.RelativeFloor"/>/<see cref="ReciprocalRankFusionOptions.RelativeFloor"/>
/// — AND, new this task, each arm's own <see cref="DsrOptions"/>, isolated so the difficulty axis is the
/// ONLY thing that differs between the live and inert curve.</b> RRF's own floor is constructed FROM
/// Multiplicative's, so the two stay equal even if the shipped default moves. The inert
/// <see cref="DsrOptions"/> is constructed FROM the live one via a record <c>with</c> expression
/// (<c>liveOptions with { DifficultyChangeWeight = 0, DifficultyReversionWeight = 0 }</c>), never as a
/// second, independently-typed-out options record — so every OTHER constant (<c>InitialStability</c>,
/// <c>Decay</c>, <c>ReinforceGain</c>, ...) is identical by construction, and the read-back below confirms
/// that identity from what each constructed engine's own curve actually holds, not merely from the two
/// options objects this method happened to build.</item>
/// <item><b>C3 — undamped age policy.</b> A single, deliberately STATELESS <see cref="PerWriteAgePolicy"/> (see
/// its own doc — <c>Advance</c> ignores its <c>engine</c> argument and returns a constant) is shared across
/// every parallel engine; sharing it is safe for exactly that reason, unlike the DEFAULT
/// <c>BurstDampenedAgePolicy</c>, which keys real bookkeeping per engine and would flatten this replay's
/// whole interference axis under a fast in-process burst regardless.</item>
/// </list>
/// See <see cref="PrintControlsConfirmed"/> for where all four (plus the new isolation clause) are actually
/// checked, and <see cref="RunAsync"/>'s return value for how a mismatch on any of them fails the process.</para>
///
/// <para><b>Parallel by construction, not by afterthought.</b> Each <c>(seed, shape, arm)</c> combination
/// gets its own throwaway SQLite database with no shared mutable state (<see cref="SweepDb"/>), and the
/// objects every combination DOES share — the age policy, the two ranking policies, the two curves — are all
/// stateless (pure functions of their inputs; see C1/C3 above and the curve-sharing note in
/// <see cref="RunAsync"/> itself). <see cref="RunAsync"/> therefore fans every combination out through
/// <see cref="Parallel.ForEachAsync{TSource}(System.Collections.Generic.IEnumerable{TSource},ParallelOptions,System.Func{TSource,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask})"/>
/// at <see cref="Environment.ProcessorCount"/> degrees of parallelism, which is what keeps a grid this size
/// runnable in minutes rather than hours (see design doc §4).</para>
/// </summary>
internal static class MemoryPolicySweep
{
    // Matches the corpus-harness's own long-standing seed (MemoryDefaultRecallQualityTests, the pre-Task-2
    // sweep) so the FIRST pilot seed is traceable to the same measurement lineage; seeds 2..N are simply
    // BaseSeed+1, BaseSeed+2, ... — MemoryCorpus.Generate's own doc guarantees a different seed "almost
    // certainly" produces different content, and there is no reason to decorrelate further for a PRNG-backed
    // generator (unlike, say, a linear congruential one where nearby seeds could correlate).
    private const int BaseSeed = 12345;

    // Run this many seeds first, measure the paired difference's own standard deviation from them, THEN
    // decide how many more (if any) are actually needed — never guess N up front (task brief, Step 2).
    private const int PilotSeedCount = 5;

    // Never run more than this many seeds regardless of what the pilot's SD implies — a runtime ceiling,
    // DISCLOSED (PrintDropped) rather than silently capping the grid. Matches the design doc's own worked
    // example (§4: "Thirty seeds over six shapes is 720 combinations ... parallelizes ... to ~15 minutes"),
    // not an arbitrary number picked here. (With four arms instead of two, the grid this ceiling now bounds
    // is twice as large per seed — see PrintDropped for the resulting wall-clock disclosure.)
    private const int MaxSeedCount = 30;

    // The smallest MissRate difference worth acting on — the falsification plan's own Task 2 brief, used
    // verbatim ("a sweep that cannot detect a 0.10 difference must not run for an hour and then report a
    // null"). This is what sizes N from the pilot's SD, and what a cell's own verdict is judged against.
    private const double MinDetectableEffect = 0.10;

    // Conventional target power for the sample-size formula — NOT specified by the brief, so named here
    // rather than buried in a formula: an 80%/5% design is the standard default absent a stated preference,
    // and is reported alongside the SD/N it produced so the assumption is visible, not hidden.
    private const double TargetPower = 0.80;

    // z_{alpha/2} for a two-sided 95% test and z_beta for 80% power — used ONLY to size N from the pilot's
    // own SD. The FINAL report's intervals never use these; they use the t-distribution (TCritical95), which
    // is the honest choice once N might be as small as the pilot itself.
    private const double ZAlpha2 = 1.9599639845400545;
    private const double ZBeta80 = 0.8416212335729143;

    private const int QueryLimit = 10;

    private sealed record Arm(string Label, IMemoryRankingPolicy Ranking, IMemoryRetrievabilityPolicy Retrieval);

    private sealed record Shape(string Label, CorpusShape Value);

    /// <summary>One replay's result for one <c>(seed, shape, class, arm)</c> cell — already averaged over
    /// that class's own queries WITHIN this one seed's replay (<c>quality.Average(...)</c>), so this is the
    /// PAIRED unit's own value: four of these (one per arm), at the same <see cref="Seed"/> and
    /// <see cref="Shape"/>, come from the identical corpus.</summary>
    private sealed record Row(int Seed, string Shape, string Class, string Arm, int N, double MissRate, double PollutionRate);

    private sealed record EngineFacts(int Seed, string Shape, string Arm, string AgePolicyType, int RetentionCount,
        string RankingType, string RankingOptions, double RankingRelativeFloor, string CurveType,
        double CurveInitialStability, string CurveOptions, double DifficultyChangeWeight,
        double DifficultyReversionWeight);

    private static readonly Dictionary<string, (string RankingType, string CurveType)> ExpectedArmTypes =
        new(StringComparer.Ordinal)
        {
            ["Multiplicative+Live"] = (nameof(MultiplicativeRankingPolicy), nameof(DsrRetrievability)),
            ["Multiplicative+Inert"] = (nameof(MultiplicativeRankingPolicy), nameof(DsrRetrievability)),
            ["RRF+Live"] = (nameof(ReciprocalRankFusionPolicy), nameof(DsrRetrievability)),
            ["RRF+Inert"] = (nameof(ReciprocalRankFusionPolicy), nameof(DsrRetrievability)),
        };

    /// <summary>An arm's label ends in <c>+Live</c> or <c>+Inert</c> — this is the single place that mapping
    /// is spelled out, so every other method asks THIS rather than re-deriving it from a substring test.</summary>
    private static bool IsLiveArm(string armLabel) => armLabel.EndsWith("+Live", StringComparison.Ordinal);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var parallelism = Environment.ProcessorCount;

        // Stateless by construction (see PerWriteAgePolicy.Advance — it ignores its own `engine` argument
        // and returns a constant MemoryTick), so sharing ONE instance across every parallel engine below
        // never mixes bookkeeping the way the engine's own default BurstDampenedAgePolicy would (C3).
        var agePolicy = new PerWriteAgePolicy();
        IReadOnlyList<IMemoryRetentionPolicy> retentionPolicies = [];
        var graphOptions = new GraphMemoryOptions();

        // The difficulty axis (design spec §2/§5, this task): the INERT options record is DERIVED from the
        // live one via `with`, not typed out a second time — so every constant this task is not about
        // (InitialStability, Decay, ReinforceGain, ...) is identical between the two arms by construction,
        // and the C2 read-back below confirms that identity from what each engine's own curve actually
        // holds. `DifficultyChangeWeight = 0` ALONE is no longer the inert control since DsrOptions grew
        // `DifficultyReversionWeight` (fsrs-properly plan Task 2, fix round 1) — that weight is a SEPARATE
        // restoring force DifficultyChangeWeight does not gate, so a fact
        // (`DifficultyChangeWeight_alone_at_zero_is_NOT_the_inert_control`) now pins that BOTH must be zero
        // together. See PrintControlsConfirmed's difficulty-isolation clause for the values this run asserts every constructed engine
        // actually holds.
        var difficultyLiveOptions = new DsrOptions();
        var difficultyInertOptions = difficultyLiveOptions with { DifficultyChangeWeight = 0, DifficultyReversionWeight = 0 };

        // C2's ranking equalization clause, unchanged from Task 2 of the predecessor plan: RRF's own floor
        // is DERIVED from Multiplicative's, not a second literal — so the two stay equal even if the
        // shipped default ever moves, and a future change to one cannot silently re-open the confound the
        // very first sweep only disclosed.
        var multiplicativeOptions = new MultiplicativeRankingOptions();
        var rrfOptions = new ReciprocalRankFusionOptions { RelativeFloor = multiplicativeOptions.RelativeFloor };

        IMemoryRetrievabilityPolicy Wrap(IMemoryRetrievabilityPolicy inner) =>
            new ModulatedRetrievability(inner, retentionPolicies);

        // Both ranking arms of a given difficulty setting share the SAME curve instance, and both difficulty
        // arms of a given ranking policy share the SAME ranking-policy instance — mirroring the predecessor
        // sweep's own "share, don't duplicate" discipline for exactly the same reason: it is what lets the
        // C2 read-back below assert an identity that holds by construction rather than by two coincidentally
        // equal objects. All four objects are pure/stateless (they only ever read the per-call state handed
        // to them), so sharing across the whole parallel grid — not just across two arms — is safe, the same
        // way the predecessor sweep already shared one DsrOptions/DsrRetrievability across all 720 of its
        // own combinations.
        var liveCurve = new DsrRetrievability(difficultyLiveOptions);
        var inertCurve = new DsrRetrievability(difficultyInertOptions);
        var multiplicative = new MultiplicativeRankingPolicy(multiplicativeOptions);
        var rrf = new ReciprocalRankFusionPolicy(rrfOptions);

        var arms = new[]
        {
            new Arm("Multiplicative+Live", multiplicative, Wrap(liveCurve)),
            new Arm("Multiplicative+Inert", multiplicative, Wrap(inertCurve)),
            new Arm("RRF+Live", rrf, Wrap(liveCurve)),
            new Arm("RRF+Inert", rrf, Wrap(inertCurve)),
        };

        // Unchanged from every predecessor: one-factor-at-a-time against CorpusShape.Default for the CORPUS
        // shape grid. This task crosses ranking x difficulty FULLY (all four arms, every shape) rather than
        // one-factor-at-a-time — see the class doc for why that crossing is the point this time.
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

        PrintPreamble(arms, shapes, multiplicativeOptions.RelativeFloor, rrfOptions.RelativeFloor,
            difficultyLiveOptions, difficultyInertOptions);

        // Corpus generation is a pure function of (seed, shape) — cached so every arm at the same (seed,
        // shape) replays the identical OBJECT, not merely identical content, and so a seed reused across the
        // pilot/scale boundary is never regenerated.
        var corpusCache = new ConcurrentDictionary<(int Seed, string ShapeLabel), MemoryCorpus>();
        MemoryCorpus CorpusFor(int seed, Shape shape) =>
            corpusCache.GetOrAdd((seed, shape.Label), key => MemoryCorpus.Generate(shape.Value, key.Seed));

        var rows = new ConcurrentBag<Row>();
        var c0Checks = new ConcurrentBag<(int Seed, string Shape, string Arm, bool Confirmed, int Writes, int Queries)>();
        var engineFacts = new ConcurrentBag<EngineFacts>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = CorpusFor(seed, shape);
            var declaredOrder = corpus.Steps.Select(CorpusStepMarker).ToList();

            using var db = new SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine(
                "sweep",
                store,
                options: graphOptions,
                retrievability: arm.Retrieval,
                agePolicies: [agePolicy],
                ranking: arm.Ranking);

            // F1 (carried forward): read back what THIS engine actually holds before it is used for
            // anything — never the ingredient variables this method built.
            engineFacts.Add(ReadBack(seed, shape.Label, arm.Label, engine));

            var replay = await ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label, quality.Count,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));

            c0Checks.Add((seed, shape.Label, arm.Label, declaredOrder.SequenceEqual(replay.ObservedOrder),
                replay.ObservedOrder.Count(c => c == 'W'), replay.ObservedOrder.Count(c => c == 'Q')));
        }

        // ---- PILOT: PilotSeedCount seeds, every shape, every arm — run first, decide N from what it shows.
        var pilotSeeds = Enumerable.Range(0, PilotSeedCount).Select(i => BaseSeed + i).ToList();
        var pilotStopwatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            from seed in pilotSeeds from shape in shapes from arm in arms select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));
        var pilotElapsed = pilotStopwatch.Elapsed;

        var pilotRowsSnapshot = rows.ToList();
        var (pilotSd, pilotSdShape, pilotSdClass, pilotSdEffect) = WorstEffectSd(pilotRowsSnapshot, shapes);
        var requiredNRaw = pilotSd <= 0
            ? PilotSeedCount
            : (int)Math.Ceiling(Math.Pow((ZAlpha2 + ZBeta80) * pilotSd / MinDetectableEffect, 2));
        var cappedByCeiling = requiredNRaw > MaxSeedCount;
        var requiredN = Math.Clamp(Math.Max(requiredNRaw, PilotSeedCount), PilotSeedCount, MaxSeedCount);

        PrintPilotDecision(pilotSeeds, pilotSd, pilotSdShape, pilotSdClass, pilotSdEffect, requiredNRaw, requiredN,
            cappedByCeiling, pilotElapsed);

        // ---- SCALE: top up to the derived N, reusing the pilot's own seeds/data rather than discarding them.
        var allSeeds = Enumerable.Range(0, requiredN).Select(i => BaseSeed + i).ToList();
        var additionalSeeds = allSeeds.Skip(PilotSeedCount).ToList();
        if (additionalSeeds.Count > 0)
        {
            var scaleStopwatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                from seed in additionalSeeds from shape in shapes from arm in arms select (seed, shape, arm),
                new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));
            Console.WriteLine($"Scale-up: {additionalSeeds.Count} additional seed(s) x {shapes.Length} shapes x " +
                $"{arms.Length} arms = {additionalSeeds.Count * shapes.Length * arms.Length} combinations in " +
                $"{scaleStopwatch.Elapsed.TotalSeconds:F1}s.");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("Scale-up: none needed — the pilot's own seed count already meets the derived N.");
            Console.WriteLine();
        }

        var allRows = rows.ToList();

        PrintArmTable(allRows, shapes, arms);
        PrintEffectsTable(allRows, shapes, allSeeds.Count);

        var controlsOk = PrintControlsConfirmed(c0Checks.ToList(), engineFacts.ToList(), arms,
            multiplicativeOptions.RelativeFloor, rrfOptions.RelativeFloor, difficultyLiveOptions, difficultyInertOptions);

        PrintPollutionNote(allRows);
        PrintDropped(stopwatch.Elapsed, allSeeds);

        if (!controlsOk)
            Console.Error.WriteLine("MemoryPolicySweep: at least one confound control did NOT hold — see the " +
                "'confirmed' block above. The table printed above is NOT trustworthy. Exiting non-zero.");

        return controlsOk ? 0 : 1;
    }

    private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Reads back what a CONSTRUCTED <see cref="GraphMemoryEngine"/> actually holds — via reflection
    /// into its own private fields — never the ingredient variables passed into its constructor. Extended
    /// this task to also read the curve's own <see cref="DsrOptions.DifficultyChangeWeight"/>/
    /// <see cref="DsrOptions.DifficultyReversionWeight"/> values (never checked before — the predecessor only
    /// reflected the options record's <c>ToString()</c> as a whole), so the difficulty-isolation clause below
    /// is verified from what each engine actually holds rather than from the two options objects this
    /// method's caller happened to construct. Deliberately fragile to a field rename in <c>Lyntai.Core</c>
    /// (throws loudly rather than silently reporting nothing).</summary>
    private static EngineFacts ReadBack(int seed, string shapeLabel, string armLabel, GraphMemoryEngine engine)
    {
        var engineType = typeof(GraphMemoryEngine);
        var agePolicies = (IReadOnlyList<IMemoryAgePolicy>)
            engineType.GetField("_agePolicies", NonPublicInstance)!.GetValue(engine)!;
        if (agePolicies.Count != 1)
            throw new InvalidOperationException(
                "C3 cannot be verified: expected exactly one registered IMemoryAgePolicy, found "
                + $"{agePolicies.Count} — this sweep's own construction changed to register more than one.");
        var agePolicy = agePolicies[0];
        var policy = (IMemoryRetrievabilityPolicy)engineType.GetField("_policy", NonPublicInstance)!.GetValue(engine)!;
        var ranking = (IMemoryRankingPolicy)engineType.GetField("_ranking", NonPublicInstance)!.GetValue(engine)!;

        int retentionCount;
        IMemoryRetrievabilityPolicy curve;
        if (policy is ModulatedRetrievability modulated)
        {
            var modulatedType = typeof(ModulatedRetrievability);
            var retentionArray =
                (Array)modulatedType.GetField("_retentionPolicies", NonPublicInstance)!.GetValue(modulated)!;
            retentionCount = retentionArray.Length;
            curve = (IMemoryRetrievabilityPolicy)modulatedType.GetField("_inner", NonPublicInstance)!.GetValue(modulated)!;
        }
        else
        {
            retentionCount = 0;
            curve = policy;
        }

        var curveOptionsField = curve.GetType().GetField("_options", NonPublicInstance)
            ?? throw new InvalidOperationException(
                $"C2 cannot be verified: no '_options' field on {curve.GetType().Name}. A refactor in " +
                "Lyntai.Core renamed it — fix this read-back rather than weakening the control.");
        var curveOptionsValue = curveOptionsField.GetValue(curve)!;
        var curveOptions = curveOptionsValue.ToString()!;

        // NEW this task: the two difficulty-update weights, read off the ACTUAL options object the
        // constructed engine's curve holds — this is what lets PrintControlsConfirmed assert the live arms
        // truly carry FSRS-6's own nonzero defaults and the inert arms truly carry zero, rather than trusting
        // that difficultyLiveOptions/difficultyInertOptions (the ingredient variables RunAsync built) are
        // what actually made it into each engine.
        var difficultyChangeWeightProperty = curveOptionsValue.GetType().GetProperty(nameof(DsrOptions.DifficultyChangeWeight))
            ?? throw new InvalidOperationException(
                "The difficulty-isolation check cannot be verified: no public 'DifficultyChangeWeight' " +
                $"property on {curveOptionsValue.GetType().Name}.");
        var difficultyReversionWeightProperty = curveOptionsValue.GetType().GetProperty(nameof(DsrOptions.DifficultyReversionWeight))
            ?? throw new InvalidOperationException(
                "The difficulty-isolation check cannot be verified: no public 'DifficultyReversionWeight' " +
                $"property on {curveOptionsValue.GetType().Name}.");
        var difficultyChangeWeight = (double)difficultyChangeWeightProperty.GetValue(curveOptionsValue)!;
        var difficultyReversionWeight = (double)difficultyReversionWeightProperty.GetValue(curveOptionsValue)!;

        // The ranking policy's OWN options record, read the identical way — this is what lets
        // PrintControlsConfirmed assert RelativeFloor is actually equal on every constructed engine, not
        // merely on the two options objects RunAsync happened to build.
        var rankingOptionsField = ranking.GetType().GetField("_options", NonPublicInstance)
            ?? throw new InvalidOperationException(
                $"C2's RelativeFloor-equalization check cannot be verified: no '_options' field on " +
                $"{ranking.GetType().Name}. A refactor in Lyntai.Core renamed it — fix this read-back rather " +
                "than weakening the control.");
        var rankingOptionsValue = rankingOptionsField.GetValue(ranking)!;
        var rankingOptions = rankingOptionsValue.ToString()!;
        var relativeFloorProperty = rankingOptionsValue.GetType().GetProperty("RelativeFloor")
            ?? throw new InvalidOperationException(
                $"C2's RelativeFloor-equalization check cannot be verified: no public 'RelativeFloor' " +
                $"property on {rankingOptionsValue.GetType().Name}.");
        var rankingRelativeFloor = (double)relativeFloorProperty.GetValue(rankingOptionsValue)!;

        return new EngineFacts(seed, shapeLabel, armLabel, agePolicy.GetType().Name, retentionCount,
            ranking.GetType().Name, rankingOptions, rankingRelativeFloor, curve.GetType().Name,
            curve.InitialStability, curveOptions, difficultyChangeWeight, difficultyReversionWeight);
    }

    // internal, not private: MemorySpacingSweep replays the identical way against the identical corpus, and
    // ONE copy of the replay is what keeps the two studies comparable. Its ARMS and its CONTROLS are its own
    // — only this mechanism is shared.
    internal sealed record ReplayResult(
        Dictionary<string, List<RecallQuality>> ByClass, IReadOnlyList<char> ObservedOrder);

    /// <summary>Walks <paramref name="corpus"/>'s <c>Steps</c> IN ORDER, one at a time, against the one live
    /// <paramref name="engine"/> — unchanged from every predecessor; see <see cref="MemoryCorpus"/>'s own
    /// ordering contract.</summary>
    internal static async Task<ReplayResult> ReplayAsync(MemoryCorpus corpus, GraphMemoryEngine engine, int limit)
    {
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var taskKey = firstWrite.TaskKey;
        var scope = firstWrite.Scope;

        var refToCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        // the inverse, for CorpusExpand — which names a corpus id, while ExpandAsync takes a reference
        var corpusIdToRef = new Dictionary<string, MemoryRef>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, List<RecallQuality>>(StringComparer.Ordinal);
        var observedOrder = new List<char>();

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    observedOrder.Add('W');
                    var memRef = await engine.RememberAsync(w.Write);
                    var corpusId = ExtractCorpusId(w.Write.Content);
                    refToCorpusId[memRef.Id] = corpusId;
                    corpusIdToRef[corpusId] = memRef;
                    break;

                case CorpusQuery q:
                    observedOrder.Add('Q');
                    var recall = await engine.RecallAsync(new MemoryQuery(taskKey, scope, q.Text, Limit: limit));
                    var recalledIds = recall.Items
                        .Select(i => refToCorpusId.TryGetValue(i.Reference.Id, out var cid) ? cid : i.Reference.Id)
                        .ToList();
                    // q.SupportNeeded, not the default: a frequency query's answer is n OF its relevant set,
                    // and dropping it here would silently score that class by strict all-of instead.
                    var quality = RecallQuality.Measure(recalledIds, q.RelevantIds, limit, q.SupportNeeded);

                    var cls = ClassifyQuery(q);
                    if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = [];
                    list.Add(quality);

                    if (!byClass.TryGetValue("all (combined)", out var all)) byClass["all (combined)"] = all = [];
                    all.Add(quality);
                    break;

                // The consumer opening an entry it just saw. Contributes NO RecallQuality of its own — an
                // expansion is an input to the engine's learning, never a retrieval being scored — so a shape
                // with expansions is still measured on exactly its queries and the two are comparable.
                case CorpusExpand e:
                    observedOrder.Add('X');
                    if (corpusIdToRef.TryGetValue(e.EntryId, out var expandRef))
                        await ((IExpandableMemory)engine).ExpandAsync(expandRef);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(CorpusStep)} '{step.GetType().Name}'. CorpusStep is a sealed " +
                        "hierarchy precisely so this switch stays exhaustive — a step silently skipped here " +
                        "is a corpus the engine never actually replayed.");
            }
        }

        return new ReplayResult(byClass, observedOrder);
    }

    /// <summary>The corpus id embedded in an entry's own content — the only link between what a corpus
    /// DECLARED relevant and what an engine returned.
    /// <para>Internal rather than private because a second sweep needs the same mapping, and a second copy of
    /// a parsing rule is how two of them drift; <c>pitfalls.md</c> records that shape under a helper whose
    /// fourth reader spelled the read out itself and got it wrong.</para></summary>
    internal static string ExtractCorpusId(string content)
    {
        var parts = content.Split(' ', 3);
        return parts.Length >= 2 ? parts[1] : content;
    }

    /// <summary>The one-character marker a step contributes to the C0 order check. Its own method so the
    /// three sweeps cannot disagree — and so adding a step KIND cannot silently be absorbed into another's
    /// letter, which is exactly what the previous `is CorpusWrite ? 'W' : 'Q'` would have done to an
    /// expansion: the order check would still have passed while comparing the wrong thing.</summary>
    internal static char CorpusStepMarker(CorpusStep step) => step switch
    {
        CorpusWrite => 'W',
        CorpusQuery => 'Q',
        CorpusExpand => 'X',
        _ => throw new InvalidOperationException($"Unhandled {nameof(CorpusStep)} '{step.GetType().Name}'."),
    };

    // "{subject} recallcue" — the subject-cued attribute cluster. Keyed on the marker rather than on a
    // subject word, because the subjects are ordinary nouns a future class could reuse while the marker
    // belongs to this class alone and appears in no entry's content.
    /// <summary>Which class a query belongs to, for per-class reporting.
    /// <para><b>The attribute test asks the LEXICON, not the English wording.</b> It used to read
    /// <c>EndsWith(" recallcue")</c>, which silently misclassified every Chinese attribute cue into "other" —
    /// so the first Chinese sweep reported no attribute class at all, and a reader would have taken its
    /// absence for "not exercised" rather than "mislabelled". The other three tests match on the corpus's
    /// ASCII IDS (<c>critical</c>, <c>hot</c>, <c>topic</c>), which are language-independent by design, so
    /// they need no such routing.</para>
    /// <para><b>The two ROUTINE queries are separate classes, not one.</b> The frequency question is asked
    /// twice with IDENTICAL text, and the two ground truths are opposite — phase A at the first, phase B
    /// alone at the last. Pooling them would average away exactly the contrast the class exists to
    /// show.</para></summary>
    internal static string ClassifyQuery(CorpusQuery q) =>
        // objective (1): the probe matches no authoritative entry, so its relevant set is the giveaway —
        // classified by GROUND TRUTH rather than by wording, because the wording deliberately says nothing
        // about what it must return. The routine pair below is classified the same way for the other reason:
        // its wording is the same at both ends, so only the ground truth can tell them apart.
        GroundTruthIsAll(q, "authoritative") ? "authoritative (grade-admitted)"
        : GroundTruthIsAll(q, "routineA") ? "routine (phase A)"
        : GroundTruthIsAll(q, "routineB") ? "routine (after the regime change)"
        : IsAnyAttributeCue(q.Text) ? "attribute (subject cue)"
        : q.Text.Contains("critical", StringComparison.Ordinal) ? "critical-rare"
        : q.Text.Contains("hot", StringComparison.Ordinal)
            ? (q.RelevantIds.Count > 0 ? "hot-ephemeral (in-window)" : "hot-ephemeral (stale)")
        : q.Text.Contains("topic", StringComparison.Ordinal) ? "topical"
        : "other";

    /// <summary>A query whose whole ground truth comes from one id family. Language-independent by
    /// construction: the corpus's ids stay ASCII in every lexicon while its wording does not.</summary>
    private static bool GroundTruthIsAll(CorpusQuery q, string idPrefix) =>
        q.RelevantIds.Count > 0 && q.RelevantIds.All(id => id.StartsWith(idPrefix, StringComparison.Ordinal));

    /// <summary>An attribute cue in ANY generated language. Asks each lexicon rather than hard-coding a
    /// wording, so adding a language cannot silently drop a class out of every report.</summary>
    private static bool IsAnyAttributeCue(string text) =>
        Enum.GetValues<CorpusLanguage>().Any(l => CorpusLexicon.For(l).IsAttributeCue(text));

    internal static readonly Dictionary<string, int> ClassOrder = new(StringComparer.Ordinal)
    {
        ["all (combined)"] = 0,
        // objective (1) leads: it is the only class with no acceptable failure rate
        ["authoritative (grade-admitted)"] = -1,
        ["attribute (subject cue)"] = 1,   // the cluster class — see MemoryCorpus.AttributeCount
        ["critical-rare"] = 2,
        ["hot-ephemeral (in-window)"] = 3,
        ["hot-ephemeral (stale)"] = 4,
        ["topical"] = 5,
        // the frequency pair, in timeline order — the second is the discriminating one, where the correct
        // answer is phase B alone and phase A scores as pollution (see CorpusShape.RoutineCount)
        ["routine (phase A)"] = 6,
        ["routine (after the regime change)"] = 7,
        ["other"] = 8,
    };

    /// <summary>Pure corpus arithmetic — no engine involved, and (per <see cref="MemoryCorpus"/>'s own doc)
    /// SEED-INVARIANT: entry counts and the timeline's structure are a pure function of shape alone, only
    /// entry CONTENT varies with the seed. So this is safe to compute once, from any one seed's corpus, and
    /// describes every seed at that shape.</summary>
    private static (int WriteCount, int MaxAge) DiagnoseShape(MemoryCorpus corpus)
    {
        var position = 0;
        var positionAtWrite = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxAge = 0;

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    position++;
                    positionAtWrite[ExtractCorpusId(w.Write.Content)] = position;
                    break;

                case CorpusQuery q:
                    foreach (var id in q.RelevantIds)
                        if (positionAtWrite.TryGetValue(id, out var writtenAt))
                            maxAge = Math.Max(maxAge, position - writtenAt);
                    break;
            }
        }

        return (position, maxAge);
    }

    private static void PrintPreamble(IReadOnlyList<Arm> arms, IReadOnlyList<Shape> shapes,
        double multiplicativeFloor, double rrfFloor, DsrOptions liveOptions, DsrOptions inertOptions)
    {
        Console.WriteLine("=== Memory policy sweep (paired by seed) ===");
        Console.WriteLine($"Base seed: {BaseSeed} (pilot seeds are BaseSeed..BaseSeed+{PilotSeedCount - 1}; the");
        Console.WriteLine("scale-up, if any, continues the same sequence — see the pilot decision below).");
        Console.WriteLine($"Query limit: {QueryLimit} (fixed across every shape and arm)");
        Console.WriteLine();
        Console.WriteLine("Four confound controls apply (C0-C3), plus C2's equalization AND isolation clauses");
        Console.WriteLine("— none are certified in this preamble. See 'C0-C3 - confirmed' after the tables:");
        Console.WriteLine("every line there is read back from the engines ACTUALLY constructed across the FULL");
        Console.WriteLine("grid this run settles on (or, for C0, from what was actually replayed), never from");
        Console.WriteLine("an ingredient variable this method could describe before construction even happens.");
        Console.WriteLine("A mismatch on ANY control makes this process exit non-zero.");
        Console.WriteLine();
        Console.WriteLine($"RelativeFloor equalized across ranking arms: Multiplicative={multiplicativeFloor:F3}, " +
            $"RRF={rrfFloor:F3}.");
        Console.WriteLine($"Difficulty axis: Live carries DifficultyChangeWeight={liveOptions.DifficultyChangeWeight:F4}, " +
            $"DifficultyReversionWeight={liveOptions.DifficultyReversionWeight:F4} (FSRS-6's own defaults); Inert " +
            $"carries both at exactly 0 — the ONLY two constants that differ between the two curve instances,");
        Console.WriteLine($"  the inert record having been derived from the live one via a record 'with' " +
            "expression rather than typed out separately.");
        Console.WriteLine();
        Console.WriteLine($"Arms ({arms.Count}): {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine();

        var initialStability = arms[0].Retrieval.InitialStability;
        var uniformStability = arms.All(a => Math.Abs(a.Retrieval.InitialStability - initialStability) < 1e-9);
        Console.WriteLine("Corpus shapes (one-factor-at-a-time against CorpusShape.Default) — writes and the");
        Console.WriteLine("largest write-to-query gap any query's relevant set waited through, as a multiple of");
        Console.WriteLine(uniformStability
            ? $"InitialStability ({initialStability:F0}, shared by every arm below). SEED-INVARIANT (structure"
            : "InitialStability — WARNING: arms have DIFFERENT InitialStability values, so no single ratio "
              + "below is meaningful; compare each arm's own InitialStability from 'C0-C3 — confirmed' "
              + "directly. SEED-INVARIANT (structure");
        Console.WriteLine("is a pure function of shape; only entry CONTENT varies with the seed), so computed");
        Console.WriteLine($"once per shape from seed {BaseSeed} and true of every seed this run touches:");
        foreach (var s in shapes)
        {
            var previewCorpus = MemoryCorpus.Generate(s.Value, BaseSeed);
            var (writes, maxAge) = DiagnoseShape(previewCorpus);
            var ratio = uniformStability ? (maxAge / initialStability).ToString("F2") : "n/a";
            Console.WriteLine($"  {s.Label,-16} {s.Value}");
            Console.WriteLine($"  {"",-16} writes={writes}, max age={maxAge}, max age/InitialStability={ratio}");
        }
        Console.WriteLine();
    }

    private static bool ReuseMultiplies(string cls) => cls is "topical" or "hot-ephemeral (in-window)";

    /// <summary>Per-arm MISSRATE/POLLUTIONRATE, now a MEAN ± 95% CI across every seed the run settled on,
    /// never a bare point estimate — task brief Step 4 ("no bare point estimate appears in a verdict
    /// sentence" extends to every printed number here, not only the effect table). Four arms this task
    /// (was two) — the grouping/ordering logic below is unchanged and generic over however many arms are
    /// passed in.</summary>
    private static void PrintArmTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, IReadOnlyList<Arm> arms)
    {
        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i);
        var armOrder = arms.Select((a, i) => (a.Label, i)).ToDictionary(x => x.Label, x => x.i);
        var shapeReuse = shapes.ToDictionary(s => s.Label, s => Math.Max(1, s.Value.ReuseRatio));

        var cells = rows
            .GroupBy(r => (r.Shape, r.Class, r.Arm))
            .Select(g => new
            {
                g.Key.Shape,
                g.Key.Class,
                g.Key.Arm,
                QueriesPerSeed = g.First().N,
                Seeds = g.Count(),
                Miss = Ci95(g.Select(r => r.MissRate).ToList()),
                Pollution = Ci95(g.Select(r => r.PollutionRate).ToList()),
            })
            .OrderBy(c => shapeOrder[c.Shape])
            .ThenBy(c => ClassOrder.GetValueOrDefault(c.Class, 99))
            .ThenBy(c => armOrder[c.Arm])
            .ToList();

        Console.WriteLine("=== Per-arm recall quality: mean +/- 95% CI across paired seeds ===");
        Console.WriteLine($"{"Shape",-16} {"Class",-24} {"Arm",-24} {"Q/seed",7} {"Seeds",6} " +
            $"{"MissRate (95% CI)",-24} {"PollutionRate (95% CI)",-24}");
        Console.WriteLine(new string('-', 16 + 1 + 24 + 1 + 24 + 1 + 7 + 1 + 6 + 1 + 24 + 1 + 24));

        string? lastShape = null;
        foreach (var c in cells)
        {
            if (lastShape is not null && c.Shape != lastShape) Console.WriteLine();
            lastShape = c.Shape;
            var qPerSeed = ReuseMultiplies(c.Class)
                ? $"{c.QueriesPerSeed}({c.QueriesPerSeed / shapeReuse[c.Shape]})"
                : c.QueriesPerSeed.ToString();
            Console.WriteLine($"{c.Shape,-16} {c.Class,-24} {c.Arm,-24} {qPerSeed,7} {c.Seeds,6} " +
                $"{FormatCi(c.Miss),-24} {FormatCi(c.Pollution),-24}");
        }

        Console.WriteLine();
        Console.WriteLine("Q/seed = queries in this cell within ONE seed's replay (structurally identical every");
        Console.WriteLine("seed); the parenthesized number, where shown, is that count divided by ReuseRatio —");
        Console.WriteLine("topical/hot-ephemeral(in-window) reuse repeats are correlated draws of the same");
        Console.WriteLine("retrieval decision (interposed by ONE filler write since Task 1, not zero, but still");
        Console.WriteLine("not independent), so N there means N/ReuseRatio, not the raw count. Seeds = the");
        Console.WriteLine("PAIRED sample size backing each cell's own CI — see the effects tables below for the");
        Console.WriteLine("cross-arm comparisons this table cannot make on its own.");
        Console.WriteLine();
    }

    /// <summary>The three effects a full 2x2 factorial supports: the ranking main effect, the difficulty main
    /// effect, and their interaction — see <see cref="EffectsPerSeed"/> for the exact per-seed formulas and
    /// sign conventions. <b>The interaction is printed as its own table, not folded into a footnote</b> (task
    /// brief: "the interaction term is a result, not noise") — it answers whether difficulty-live's own
    /// benefit (or cost) depends on which ranking policy is judging recall, which is exactly the mechanism
    /// the predecessor sweep's `topical` finding pinned on the RANKING policy rather than the curve.
    /// <para>Computed on <see cref="Row.MissRate"/> only (never PollutionRate): on a full page PollutionRate
    /// is an exact linear restatement of MissRate (see <see cref="PrintPollutionNote"/>'s own identity), so a
    /// second paired-difference table built on it would report the same signal twice under a different name
    /// rather than independent corroboration.</para></summary>
    private static void PrintEffectsTable(IReadOnlyList<Row> rows, IReadOnlyList<Shape> shapes, int seedCount)
    {
        var shapeOrder = shapes.Select((s, i) => (s.Label, i)).ToDictionary(x => x.Label, x => x.i);

        var cells = rows
            .Select(r => (r.Shape, r.Class))
            .Distinct()
            .OrderBy(x => shapeOrder[x.Shape])
            .ThenBy(x => ClassOrder.GetValueOrDefault(x.Class, 99))
            .ToList();

        void PrintOneEffectTable(string title, string legend, Func<SeedEffects, double> select,
            Func<Interval, string> verdict)
        {
            Console.WriteLine(title);
            Console.WriteLine(legend);
            Console.WriteLine();
            Console.WriteLine($"{"Shape",-16} {"Class",-24} {"n",3}  {"Effect",-20} Verdict");
            Console.WriteLine(new string('-', 90));

            string? lastShape = null;
            foreach (var (shape, cls) in cells)
            {
                var perSeed = EffectsPerSeed(rows, shape, cls);
                if (perSeed.Count == 0) continue;

                if (lastShape is not null && shape != lastShape) Console.WriteLine();
                lastShape = shape;

                var effect = Ci95(perSeed.Values.Select(select).ToList());

                Console.WriteLine($"{shape,-16} {cls,-24} {perSeed.Count,3}  {FormatSigned(effect),-20} " +
                    $"{verdict(effect)}");
            }
            Console.WriteLine();
        }

        PrintOneEffectTable(
            "=== Paired DIFFICULTY effect on MissRate: 95% CI ===",
            "DifficultyEffect = mean over ranking arms of (Inert-MissRate - Live-MissRate); positive = Live better.",
            e => e.DifficultyEffect,
            ci => EffectVerdict(ci, "Live better", "Inert better"));

        PrintOneEffectTable(
            "=== Paired RANKING effect on MissRate: 95% CI ===",
            "RankingEffect = mean over difficulty arms of (Multiplicative-MissRate - RRF-MissRate); positive = RRF better.",
            e => e.RankingEffect,
            ci => EffectVerdict(ci, "RRF better", "Multiplicative better"));

        PrintOneEffectTable(
            "=== Paired DIFFICULTY x RANKING interaction on MissRate: 95% CI ===",
            "Interaction = (Inert-MissRate - Live-MissRate)|Multiplicative - (Inert-MissRate - Live-MissRate)|RRF;\n" +
            "positive = difficulty-live helps MORE under Multiplicative than under RRF; negative = the reverse.",
            e => e.InteractionEffect,
            InteractionVerdict);

        Console.WriteLine($"n = paired seeds backing that (shape, class) cell (<= {seedCount}; a cell can carry");
        Console.WriteLine("fewer if a class's query set is empty for every seed at that shape — none are, on");
        Console.WriteLine("this grid, but the count is read from the data rather than assumed).");
        Console.WriteLine("Each verdict reads its own effect (printed as part of the same CI machinery, not");
        Console.WriteLine("repeated as a bare number) against the null (does the 95% CI exclude 0?) and against");
        Console.WriteLine($"the smallest effect worth acting on ({MinDetectableEffect:F2}) — see PrintPilotDecision");
        Console.WriteLine("for where that threshold and this run's own achieved N came from.");
        Console.WriteLine();
    }

    private readonly record struct SeedEffects(double RankingEffect, double DifficultyEffect, double InteractionEffect);

    /// <summary>Every per-seed effect this file reports, computed once per <c>(shape, class)</c> cell from
    /// the four arms' <see cref="Row.MissRate"/> at that seed. Let <c>ml</c>/<c>mi</c>/<c>rl</c>/<c>ri</c> be
    /// Multiplicative+Live, Multiplicative+Inert, RRF+Live, RRF+Inert respectively:
    /// <list type="bullet">
    /// <item><c>RankingEffect = ((ml - rl) + (mi - ri)) / 2</c> — the ranking main effect, averaged over both
    /// difficulty arms; positive means RRF misses LESS (RRF better), matching the predecessor sweep's own
    /// sign convention exactly (there it was the ONLY axis; here it is the same subtraction, averaged over
    /// the second axis that now also varies).</item>
    /// <item><c>DifficultyEffect = ((mi - ml) + (ri - rl)) / 2</c> — the difficulty main effect, averaged over
    /// both ranking arms; positive means Live misses LESS (Live better) — this is the design spec §5
    /// question, "does difficulty-live beat difficulty-inert," stated as one signed number with a CI.</item>
    /// <item><c>InteractionEffect = (mi - ml) - (ri - rl)</c> — the classic 2x2 interaction contrast: how much
    /// MORE (or less) difficulty-live helps under Multiplicative than it does under RRF. Zero means the
    /// difficulty effect is the SAME under both rankings (the two axes act independently); a nonzero,
    /// CI-excluding-zero value means the previous sweep's own lesson holds again — a curve's own effect on
    /// recall cannot be read without naming which ranking policy is doing the judging.</item>
    /// </list>
    /// Missing an arm at a given seed (should not happen on this grid; the corpus's class set is seed-
    /// invariant) skips that seed rather than throwing, so a partial grid degrades the sample size visibly
    /// (the printed <c>n</c>) instead of crashing the whole report.</summary>
    private static Dictionary<int, SeedEffects> EffectsPerSeed(IReadOnlyList<Row> rows, string shape, string cls)
    {
        var result = new Dictionary<int, SeedEffects>();
        foreach (var g in rows.Where(r => r.Shape == shape && r.Class == cls).GroupBy(r => r.Seed))
        {
            var byArm = g.ToDictionary(r => r.Arm, r => r.MissRate, StringComparer.Ordinal);
            if (!byArm.TryGetValue("Multiplicative+Live", out var ml)) continue;
            if (!byArm.TryGetValue("Multiplicative+Inert", out var mi)) continue;
            if (!byArm.TryGetValue("RRF+Live", out var rl)) continue;
            if (!byArm.TryGetValue("RRF+Inert", out var ri)) continue;

            var rankingEffect = ((ml - rl) + (mi - ri)) / 2;
            var difficultyEffect = ((mi - ml) + (ri - rl)) / 2;
            var interaction = (mi - ml) - (ri - rl);

            result[g.Key] = new SeedEffects(rankingEffect, difficultyEffect, interaction);
        }
        return result;
    }

    /// <summary>Judges a main effect against BOTH the null (does the 95% CI exclude 0?) and the smallest
    /// effect worth acting on (<see cref="MinDetectableEffect"/>) — never printed as a bare label without the
    /// interval it came from sitting right next to it in the same table row. Shared by both main-effect
    /// tables; <paramref name="positiveLabel"/>/<paramref name="negativeLabel"/> name which side "positive"
    /// and "negative" mean for THIS effect (see each call site).</summary>
    private static string EffectVerdict(Interval effect, string positiveLabel, string negativeLabel)
    {
        if (effect.N < 2) return "n<2, no CI";
        var excludesZero = effect.Lo > 0 || effect.Hi < 0;
        if (!excludesZero) return "null (CI spans 0)";
        if (Math.Abs(effect.Mean) < MinDetectableEffect) return "distinguishable, but < 0.10 (not actionable)";
        return effect.Mean > 0 ? $"{positiveLabel} (beyond 0.10)" : $"{negativeLabel} (beyond 0.10)";
    }

    /// <summary>Same null/threshold judgment as <see cref="EffectVerdict"/>, worded for the interaction
    /// specifically — "better"/"worse" has no meaning for an interaction term, so this names what a nonzero
    /// value actually says (the difficulty effect differs BY ranking arm) rather than reusing the main-effect
    /// wording.</summary>
    private static string InteractionVerdict(Interval interaction)
    {
        if (interaction.N < 2) return "n<2, no CI";
        var excludesZero = interaction.Lo > 0 || interaction.Hi < 0;
        if (!excludesZero) return "null (CI spans 0) — difficulty effect consistent across ranking arms";
        if (Math.Abs(interaction.Mean) < MinDetectableEffect) return "distinguishable, but < 0.10 (not actionable)";
        return interaction.Mean > 0
            ? "difficulty-live helps MORE under Multiplicative than under RRF (beyond 0.10)"
            : "difficulty-live helps MORE under RRF than under Multiplicative (beyond 0.10)";
    }

    /// <summary>Finds the LARGEST standard deviation across ALL THREE paired effects
    /// (<see cref="SeedEffects.RankingEffect"/>, <see cref="SeedEffects.DifficultyEffect"/>,
    /// <see cref="SeedEffects.InteractionEffect"/>) over every <c>(shape, class)</c> cell the pilot touched —
    /// the CONSERVATIVE basis for sizing N: using the worst cell AND the worst effect, rather than an average
    /// or a single chosen one, means the derived N gives every cell and every effect in the grid at least the
    /// stated detection power, not just the one this method happened to pick. (The predecessor sized N from
    /// the ranking effect alone, because it was the only effect that existed; this task adds two more axes to
    /// check, and a difficulty or interaction effect noisier than the ranking one would otherwise size N too
    /// small for the very question this task exists to answer.)</summary>
    private static (double Sd, string Shape, string Class, string Effect) WorstEffectSd(
        IReadOnlyList<Row> pilotRows, IReadOnlyList<Shape> shapes)
    {
        (string Name, Func<SeedEffects, double> Select)[] effects =
        [
            ("RankingEffect", e => e.RankingEffect),
            ("DifficultyEffect", e => e.DifficultyEffect),
            ("InteractionEffect", e => e.InteractionEffect),
        ];

        var worstSd = 0.0;
        var worstShape = "";
        var worstClass = "";
        var worstEffect = "";

        foreach (var shape in shapes)
        {
            var classes = pilotRows.Where(r => r.Shape == shape.Label).Select(r => r.Class).Distinct();
            foreach (var cls in classes)
            {
                var perSeed = EffectsPerSeed(pilotRows, shape.Label, cls);
                if (perSeed.Count < 2) continue;
                foreach (var (name, select) in effects)
                {
                    var sd = SampleStdDev(perSeed.Values.Select(select).ToList());
                    if (sd > worstSd)
                    {
                        worstSd = sd;
                        worstShape = shape.Label;
                        worstClass = cls;
                        worstEffect = name;
                    }
                }
            }
        }

        return (worstSd, worstShape, worstClass, worstEffect);
    }

    private static void PrintPilotDecision(IReadOnlyList<int> pilotSeeds, double pilotSd, string pilotSdShape,
        string pilotSdClass, string pilotSdEffect, int requiredNRaw, int requiredN, bool cappedByCeiling,
        TimeSpan pilotElapsed)
    {
        Console.WriteLine("=== Pilot -> scale decision ===");
        Console.WriteLine($"Pilot seeds ({pilotSeeds.Count}): {string.Join(", ", pilotSeeds)}");
        Console.WriteLine($"Pilot wall clock: {pilotElapsed.TotalSeconds:F1}s " +
            $"({pilotSeeds.Count} seeds x 6 shapes x 4 arms = {pilotSeeds.Count * 24} combinations, parallel " +
            $"at {Environment.ProcessorCount} degrees).");
        Console.WriteLine();
        Console.WriteLine("Worst-case standard deviation found across ALL THREE paired effects (ranking,");
        Console.WriteLine($"difficulty, interaction — MissRate) over every (shape, class) cell the pilot touched: " +
            $"SD = {pilotSd:F4}");
        Console.WriteLine($"  (found on shape={pilotSdShape}, class={pilotSdClass}, effect={pilotSdEffect}) — the");
        Console.WriteLine("  CONSERVATIVE basis: this is the noisiest (cell, effect) pair in the pilot, so the N");
        Console.WriteLine("  it implies gives every OTHER cell and every OTHER effect at least the same");
        Console.WriteLine("  detection power, not less.");
        Console.WriteLine();
        Console.WriteLine($"Target: detect a {MinDetectableEffect:F2} MissRate difference at " +
            $"{TargetPower:P0} power, two-sided alpha=0.05");
        Console.WriteLine($"  (z_alpha/2={ZAlpha2:F4}, z_beta={ZBeta80:F4})");
        Console.WriteLine($"  required N = ceil(((z_alpha/2 + z_beta) x SD / effect)^2) = " +
            $"ceil((({ZAlpha2:F2}+{ZBeta80:F2}) x {pilotSd:F4} / {MinDetectableEffect:F2})^2) = {requiredNRaw}");
        Console.WriteLine($"Chosen N, clamped to [{PilotSeedCount}, {MaxSeedCount}]: {requiredN}" +
            (cappedByCeiling
                ? $" — CAPPED: the raw requirement ({requiredNRaw}) exceeds the {MaxSeedCount}-seed runtime " +
                  "ceiling; see PrintDropped for what that leaves undetected."
                : ""));
        Console.WriteLine();
    }

    /// <summary>Strips the two difficulty-update weights out of a <see cref="DsrOptions"/>'s own
    /// <c>ToString()</c>, leaving everything else — used to confirm the live and inert curve's options are
    /// IDENTICAL apart from the two fields this task's whole comparison is about. A textual diff rather than
    /// a full reflective property walk: <see cref="DsrOptions"/>'s record-generated <c>ToString()</c> already
    /// enumerates every property in declaration order, so redacting two known substrings and comparing what
    /// remains is exactly as strong as comparing every OTHER property individually, with far less code.</summary>
    private static string RedactDifficultyWeights(string curveOptions) => Regex.Replace(
        curveOptions,
        @"DifficultyChangeWeight = [^,}]+|DifficultyReversionWeight = [^,}]+",
        "<redacted>");

    private static bool PrintControlsConfirmed(
        IReadOnlyList<(int Seed, string Shape, string Arm, bool Confirmed, int Writes, int Queries)> c0Checks,
        IReadOnlyList<EngineFacts> facts, IReadOnlyList<Arm> arms, double expectedMultFloor, double expectedRrfFloor,
        DsrOptions expectedLiveOptions, DsrOptions expectedInertOptions)
    {
        var ok = true;

        Console.WriteLine("C0-C3 — confirmed, not asserted: every line below is read back from what actually");
        Console.WriteLine($"ran, across the FULL grid this run settled on ({facts.Count} constructed engines),");
        Console.WriteLine("never from a variable this file built before construction.");
        Console.WriteLine();

        var c0Confirmed = c0Checks.Count(c => c.Confirmed);
        Console.WriteLine($"C0 — observed vs declared: {c0Confirmed}/{c0Checks.Count} (seed, shape, arm) replays");
        Console.WriteLine("had an OBSERVED write/query sequence — recorded live, one character per step");
        Console.WriteLine("ReplayAsync actually processed — that matched the corpus's own DECLARED sequence.");
        var c0Failed = c0Checks.Where(c => !c.Confirmed).ToList();
        if (c0Failed.Count > 0)
        {
            Console.WriteLine($"  MISMATCH in {c0Failed.Count} run(s) — every row above for these is NOT trustworthy:");
            foreach (var f in c0Failed.Take(20))
                Console.WriteLine($"    seed={f.Seed} {f.Shape}/{f.Arm}: {f.Writes} writes, {f.Queries} queries observed");
            if (c0Failed.Count > 20) Console.WriteLine($"    ... and {c0Failed.Count - 20} more");
            ok = false;
        }
        Console.WriteLine();

        Console.WriteLine($"C1/C3 — confirmed from all {facts.Count} constructed engines (reflection into");
        Console.WriteLine("GraphMemoryEngine's own _agePolicies/_policy fields):");
        var agePolicyTypes = facts.Select(f => f.AgePolicyType).Distinct().ToList();
        if (agePolicyTypes.Count == 1 && agePolicyTypes[0] == nameof(PerWriteAgePolicy))
            Console.WriteLine($"  C3 — age retrievability: {agePolicyTypes[0]} (confirmed uniform on all {facts.Count} engines)");
        else
        {
            Console.WriteLine($"  C3 — MISMATCH: expected {nameof(PerWriteAgePolicy)} on every engine; observed: "
                + string.Join(", ", agePolicyTypes));
            ok = false;
        }

        var retentionCounts = facts.Select(f => f.RetentionCount).Distinct().ToList();
        if (retentionCounts.Count == 1 && retentionCounts[0] == 0)
            Console.WriteLine($"  C1 — retention policies: 0 (confirmed on all {facts.Count} engines)");
        else
        {
            Console.WriteLine("  C1 — MISMATCH: expected 0 retention policies on every engine; observed counts: "
                + string.Join(", ", retentionCounts));
            ok = false;
        }
        Console.WriteLine();

        Console.WriteLine("C2 — confirmed per arm (reflection into _policy/_ranking, checked against the label):");
        foreach (var arm in arms)
        {
            var armFacts = facts.Where(f => f.Arm == arm.Label).ToList();
            var rankingTypes = armFacts.Select(f => f.RankingType).Distinct().ToList();
            var curveTypes = armFacts.Select(f => f.CurveType).Distinct().ToList();
            var curveOptionsValues = armFacts.Select(f => f.CurveOptions).Distinct().ToList();
            var (expectedRanking, expectedCurve) = ExpectedArmTypes[arm.Label];

            var uniform = rankingTypes.Count == 1 && curveTypes.Count == 1;
            var matchesLabel = uniform && rankingTypes[0] == expectedRanking && curveTypes[0] == expectedCurve;

            if (matchesLabel)
                Console.WriteLine($"  {arm.Label,-24} {rankingTypes[0]} + {curveTypes[0]}: {curveOptionsValues[0]}");
            else
            {
                Console.WriteLine($"  {arm.Label,-24} MISMATCH — label implies {expectedRanking} + {expectedCurve};");
                Console.WriteLine($"    {"",-22} observed ranking={string.Join("/", rankingTypes)}, "
                    + $"curve={string.Join("/", curveTypes)} across its {armFacts.Count} engines");
                ok = false;
            }
        }
        Console.WriteLine();

        // RelativeFloor equalization, DERIVED from every constructed engine's own ranking options, not
        // merely from the two options objects RunAsync built — gated exactly like C0-C3.
        var multFloors = facts.Where(f => f.RankingType == nameof(MultiplicativeRankingPolicy))
            .Select(f => f.RankingRelativeFloor).Distinct().ToList();
        var rrfFloors = facts.Where(f => f.RankingType == nameof(ReciprocalRankFusionPolicy))
            .Select(f => f.RankingRelativeFloor).Distinct().ToList();
        var floorsEqualized = multFloors.Count == 1 && rrfFloors.Count == 1
            && Math.Abs(multFloors[0] - rrfFloors[0]) < 1e-9
            && Math.Abs(multFloors[0] - expectedMultFloor) < 1e-9
            && Math.Abs(rrfFloors[0] - expectedRrfFloor) < 1e-9;
        if (floorsEqualized)
            Console.WriteLine($"C2 (ranking) — RelativeFloor equalized: Multiplicative={multFloors[0]:F3} == " +
                $"RRF={rrfFloors[0]:F3}, confirmed on all {facts.Count} constructed engines.");
        else
        {
            Console.WriteLine($"C2 (ranking) — RelativeFloor MISMATCH: Multiplicative={string.Join("/", multFloors)}, " +
                $"RRF={string.Join("/", rrfFloors)} (expected {expectedMultFloor:F3} on both).");
            ok = false;
        }
        Console.WriteLine();

        // NEW this task: the difficulty-isolation clause. Two checks, both derived from the ACTUAL
        // DsrOptions each constructed engine's curve holds (never the ingredient variables RunAsync built):
        // (1) every Live-arm engine carries FSRS-6's own nonzero defaults and every Inert-arm engine carries
        // exactly zero on both weights; (2) every OTHER constant on the options record is identical between
        // the Live and Inert arms — confirmed via RedactDifficultyWeights rather than trusted from the
        // `with` expression that built them, because a read-back that trusted its own ingredient would not
        // be a control at all.
        var liveFacts = facts.Where(f => IsLiveArm(f.Arm)).ToList();
        var inertFacts = facts.Where(f => !IsLiveArm(f.Arm)).ToList();
        var liveWeightsOk = liveFacts.Count > 0
            && liveFacts.All(f => Math.Abs(f.DifficultyChangeWeight - expectedLiveOptions.DifficultyChangeWeight) < 1e-9
                && Math.Abs(f.DifficultyReversionWeight - expectedLiveOptions.DifficultyReversionWeight) < 1e-9);
        var inertWeightsOk = inertFacts.Count > 0
            && inertFacts.All(f => f.DifficultyChangeWeight == 0 && f.DifficultyReversionWeight == 0);
        if (liveWeightsOk && inertWeightsOk)
            Console.WriteLine($"C2 (difficulty) — weights confirmed: Live carries " +
                $"DifficultyChangeWeight={expectedLiveOptions.DifficultyChangeWeight:F4}/" +
                $"DifficultyReversionWeight={expectedLiveOptions.DifficultyReversionWeight:F4} on all " +
                $"{liveFacts.Count} engines; Inert carries 0/0 on all {inertFacts.Count} engines.");
        else
        {
            Console.WriteLine("C2 (difficulty) — weight MISMATCH: at least one Live or Inert engine did not carry " +
                "the expected DifficultyChangeWeight/DifficultyReversionWeight — the difficulty axis is not " +
                "isolated as claimed.");
            ok = false;
        }

        var liveOptionsStrings = liveFacts.Select(f => RedactDifficultyWeights(f.CurveOptions)).Distinct().ToList();
        var inertOptionsStrings = inertFacts.Select(f => RedactDifficultyWeights(f.CurveOptions)).Distinct().ToList();
        var isolationOk = liveOptionsStrings.Count == 1 && inertOptionsStrings.Count == 1
            && liveOptionsStrings[0] == inertOptionsStrings[0];
        if (isolationOk)
            Console.WriteLine("C2 (difficulty) — isolation confirmed: every OTHER DsrOptions constant " +
                "(InitialStability, Decay, MaxStability, ReinforceGain, ...) is identical between the Live and " +
                "Inert curve, on every constructed engine — the difficulty axis is the ONLY thing that differs.");
        else
        {
            Console.WriteLine("C2 (difficulty) — isolation MISMATCH: the Live and Inert curves disagree on a " +
                "constant OTHER than the two difficulty weights — this is no longer the isolated control the " +
                "design calls for.");
            Console.WriteLine($"    Live (redacted):  {string.Join(" | ", liveOptionsStrings)}");
            Console.WriteLine($"    Inert (redacted): {string.Join(" | ", inertOptionsStrings)}");
            ok = false;
        }
        Console.WriteLine();

        Console.WriteLine("GraphMemoryOptions.Decay governs EDGE-WEIGHT decay identically for every arm above");
        Console.WriteLine("(GraphMemoryEngine.EffectiveEdgeWeight, re-ranking graph-hop neighbours) — a SEPARATE");
        Console.WriteLine("shared record from each arm's own curve options shown above, which governs CONNECTION-");
        Console.WriteLine("STRENGTH decay inside that arm's own curve (…Retrievability.EffectiveStrength), fed");
        Console.WriteLine("by co-activation edges GraphMemoryEngine.ReinforceAsync writes on EVERY recall — LIVE");
        Console.WriteLine("in this harness, not dead code (predecessor's own class doc has the full history).");
        Console.WriteLine();

        return ok;
    }

    private static void PrintPollutionNote(IReadOnlyList<Row> rows)
    {
        var applicable = rows
            .Where(r => r.Class is "critical-rare" or "topical" or "hot-ephemeral (in-window)")
            .ToList();
        var matches = applicable.Where(r => Math.Abs(r.PollutionRate - (0.9 + 0.1 * r.MissRate)) < 1e-6).ToList();
        var exceptions = applicable.Except(matches).ToList();

        Console.WriteLine($"Pollution-vs-miss: PollutionRate = 0.9 + 0.1xMissRate EXACTLY in {matches.Count}/"
            + $"{applicable.Count} critical-rare/topical/hot-ephemeral(in-window) rows (relevant-set size 1,");
        Console.WriteLine("limit 10, full page) — in THOSE rows PollutionRate is a RESTATEMENT of MissRate, not");
        Console.WriteLine("independent corroboration of it; read MissRate as the number that discriminates arms");
        Console.WriteLine("(which is why the effects tables above are built on MissRate only).");
        if (exceptions.Count > 0)
        {
            // Grouped by (shape, class, arm) rather than printed per raw seed row — the raw list scales with
            // seed count and would otherwise dump dozens-to-hundreds of near-identical lines.
            var grouped = exceptions
                .GroupBy(e => (e.Shape, e.Class, e.Arm))
                .Select(g => (g.Key.Shape, g.Key.Class, g.Key.Arm, SeedsAffected: g.Count(),
                    MeanMiss: g.Average(x => x.MissRate), MeanPollution: g.Average(x => x.PollutionRate)))
                .OrderBy(g => g.Shape).ThenBy(g => g.Class).ThenBy(g => g.Arm)
                .ToList();
            var multiplicative = exceptions.Count(e => e.Arm.StartsWith("Multiplicative", StringComparison.Ordinal));
            var rrf = exceptions.Count(e => e.Arm.StartsWith("RRF", StringComparison.Ordinal));
            Console.WriteLine($"  {exceptions.Count} exception row(s) across {grouped.Count} distinct (shape, class,");
            Console.WriteLine("  arm) cell(s) — page did not fill limit. TWO different arm families, two different");
            Console.WriteLine("  plausible causes, NOT distinguished by this sweep:");
            Console.WriteLine($"    {multiplicative} on Multiplicative arms — plausibly its own RelativeFloor");
            Console.WriteLine("    pruning low-scoring candidates.");
            Console.WriteLine($"    {rrf} on RRF arms — CANNOT be RelativeFloor alone now that it is equalized to");
            Console.WriteLine("    the same nonzero value as Multiplicative's; more likely GatherAsync/SeedAsync");
            Console.WriteLine("    itself gathered fewer than `limit` raw candidates for that query.");
            foreach (var g in grouped.Take(20))
                Console.WriteLine($"    {g.Shape}/{g.Class}/{g.Arm}: {g.SeedsAffected} seed(s), mean " +
                    $"miss={g.MeanMiss:F3} pollution={g.MeanPollution:F3}");
            if (grouped.Count > 20) Console.WriteLine($"    ... and {grouped.Count - 20} more cells");
        }
        Console.WriteLine();
    }

    private static void PrintDropped(TimeSpan elapsed, IReadOnlyList<int> seeds)
    {
        Console.WriteLine($"Wall clock (whole run, pilot + scale-up): {elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"All seeds used ({seeds.Count}): {string.Join(", ", seeds)}");
        Console.WriteLine();
        Console.WriteLine("NOT swept (dropped deliberately for runtime — a silent cap would read as \"covered");
        Console.WriteLine("everything\", so this is stated rather than left implicit):");
        Console.WriteLine("  - The full 4-parameter factorial over {ReuseRatio, NoiseDensity, CriticalRarity,");
        Console.WriteLine("    CandidateCount}. Still ONE-FACTOR-AT-A-TIME against CorpusShape.Default (6");
        Console.WriteLine("    shapes) — only the RANKING x DIFFICULTY axes are crossed fully, not the shape grid.");
        Console.WriteLine($"  - Seed count is capped at {MaxSeedCount} regardless of what the pilot's own SD");
        Console.WriteLine("    implies (see the pilot decision above for whether THIS run hit that ceiling) —");
        Console.WriteLine("    a runtime bound, not a claim that more seeds would find nothing.");
        Console.WriteLine("  - Salience as a swept axis — SalienceRetentionPolicy is entirely absent (C1), so its");
        Console.WriteLine("    interaction with the difficulty axis is not measured here at all.");
        Console.WriteLine("  - Within-arm hyperparameters (HopAttenuation, RRF's K, ReinforceGain, the MAGNITUDE");
        Console.WriteLine("    of DifficultyChangeWeight/DifficultyReversionWeight, ...) — every arm uses its");
        Console.WriteLine("    policy's shipped defaults; this compares ON vs OFF for difficulty, not a");
        Console.WriteLine("    hyperparameter search over intermediate weight values.");
        Console.WriteLine("  - CandidateCount beyond 40 — kept small to bound SQLite I/O time per combination.");
        Console.WriteLine("  - PollutionRate's own paired effect sizes — computed on MissRate only; see");
        Console.WriteLine("    PrintEffectsTable's own doc for why (an exact restatement on full pages).");
        // Measured 2026-08-14: every cell's difficulty-live and difficulty-inert arms came back identical to
        // three decimals, because Reinforce's growth term is ReinforceGain x exp(-DifficultyWeight x (d-1))
        // x ... and ReinforceGain SHIPS AT ZERO (D54). Difficulty is the first factor multiplied by that
        // zero, so this axis cannot move anything at shipped defaults. Stated here rather than left for a
        // reader to notice, because two arms that cannot differ reporting equal numbers reads exactly like
        // "measured, no regression" — the failure this whole NOT-swept block exists to prevent. Pinned by
        // DsrRetrievabilityTests.Difficulty_changes_nothing_while_ReinforceGain_is_zero_and_something_once_it_is_not,
        // which fails if that default ever moves, so this paragraph is revisited with it.
        Console.WriteLine("  - The DIFFICULTY axis is INERT at shipped defaults — its two arms are");
        Console.WriteLine("    structurally identical while DsrOptions.ReinforceGain is 0 (D54), because");
        Console.WriteLine("    difficulty only ever multiplies that gain. Equal arms here are NOT evidence");
        Console.WriteLine("    that difficulty does nothing; they are evidence this run could not see it.");
        Console.WriteLine("    Re-run with ReinforceGain > 0 to measure the axis at all.");
        Console.WriteLine("  - RelativeFloor as its OWN swept axis — it is EQUALIZED (a fixed value shared by");
        Console.WriteLine("    both ranking arms), not varied.");
    }

    // ---- Small statistics helpers — deliberately NOT a dependency on a stats package: this sweep needs a
    // mean, a sample standard deviation, and a two-sided 95% CI via the t-distribution, and nothing else.

    private static double SampleStdDev(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0;
        var mean = xs.Average();
        var sumSq = xs.Sum(x => (x - mean) * (x - mean));
        return Math.Sqrt(sumSq / (xs.Count - 1));
    }

    // Two-sided 95% t critical values (t_{0.975, df}), standard table for small df, interpolated between the
    // named breakpoints and asymptoting to the normal z (1.96) at df >= 120. Not a statistics package — just
    // enough to report an honest interval when N may be as small as the pilot itself.
    private static readonly (int Df, double T)[] TTable =
    [
        (1, 12.706), (2, 4.303), (3, 3.182), (4, 2.776), (5, 2.571),
        (6, 2.447), (7, 2.365), (8, 2.306), (9, 2.262), (10, 2.228),
        (12, 2.179), (14, 2.145), (16, 2.120), (18, 2.101), (20, 2.086),
        (24, 2.064), (30, 2.042), (40, 2.021), (60, 2.000), (120, 1.980),
    ];

    private static double TCritical95(int df)
    {
        if (df <= 0) return double.NaN;
        if (df >= 120) return 1.960;
        for (var i = 0; i < TTable.Length; i++)
        {
            if (TTable[i].Df == df) return TTable[i].T;
            if (TTable[i].Df > df)
            {
                if (i == 0) return TTable[0].T;
                var (loDf, loT) = TTable[i - 1];
                var (hiDf, hiT) = TTable[i];
                var frac = (double)(df - loDf) / (hiDf - loDf);
                return loT + frac * (hiT - loT);
            }
        }
        return 1.960;
    }

    internal readonly record struct Interval(double Mean, double HalfWidth, int N)
    {
        public double Lo => Mean - HalfWidth;
        public double Hi => Mean + HalfWidth;
    }

    internal static Interval Ci95(IReadOnlyList<double> xs)
    {
        var n = xs.Count;
        if (n == 0) return new Interval(0, double.NaN, 0);
        var mean = xs.Average();
        if (n < 2) return new Interval(mean, double.NaN, n);
        var se = SampleStdDev(xs) / Math.Sqrt(n);
        return new Interval(mean, TCritical95(n - 1) * se, n);
    }

    private static string FormatCi(Interval ci) =>
        ci.N < 2 ? $"{ci.Mean:F3} (n={ci.N})" : $"{ci.Mean:F3}+/-{ci.HalfWidth:F3}";

    private static string FormatSigned(Interval ci) =>
        ci.N < 2
            ? $"{ci.Mean:+0.000;-0.000;0.000} (n={ci.N})"
            : $"{ci.Mean:+0.000;-0.000;0.000}+/-{ci.HalfWidth:F3}";

    private static string ScratchDbDir()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_memory-sweep-dbs"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A fresh, migrated SQLite db per <c>(seed, shape, arm)</c> combination — created, migrated,
    /// deleted. No shared mutable state with any other combination, which is what makes the whole grid safe
    /// to fan out through <see cref="Parallel.ForEachAsync{TSource}(System.Collections.Generic.IEnumerable{TSource},ParallelOptions,System.Func{TSource,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask})"/>.
    /// </summary>
    internal sealed class SweepDb : IDisposable
    {
        private readonly string _path = Path.Combine(ScratchDbDir(), $"sweep-{Guid.NewGuid():N}.db");

        public SweepDb()
        {
            MigrationRunnerService.MigrateUp(_path);
            Factory = new SqliteConnectionFactory(_path);
        }

        public SqliteConnectionFactory Factory { get; }

        /// <summary>The db file, so a study whose subject is COST can measure it on disk. Every other sweep
        /// here reports recall quality and never needs it.
        /// <para>NOT named <c>Path</c>: that shadows <see cref="System.IO.Path"/> for the whole class, and
        /// the field initializer below calls <c>Path.Combine</c>.</para></summary>
        public string DbPath => _path;

        public void Dispose()
        {
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString()))
                SqliteConnection.ClearPool(c);
            foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            {
                try { File.Delete(f); } catch { /* still pooled somewhere — gitignored scratch anyway */ }
            }
        }
    }
}
