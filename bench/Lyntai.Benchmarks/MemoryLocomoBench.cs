using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>LoCoMo, the benchmark this repository publishes no number against.</b> Every recall figure here is
/// measured on a synthetic corpus that calls itself "a comparison instrument, not a claim about your data",
/// so nothing this library reports can be ranked against Mem0, Zep or Letta in EITHER direction.
/// <para><b>What it does and does not buy, stated first.</b> The published LoCoMo scores are produced with a
/// frontier reader model; this runs whatever <c>SweepDoubles.ChatModel</c> resolves locally. An absolute
/// score from here is therefore NOT comparable to a published one, and the run says so in its own output.
/// What IS comparable is the arm difference: every arm answers the same questions with the same reader, so
/// the only thing varying is the memory layer.</para>
/// <para><b>The ablation is the point.</b> <c>vector</c> is the same embedder and the same k with no graph,
/// no decay, no salience and no ranking policy — so <c>lyntai</c> minus <c>vector</c> is what this library
/// adds over plain similarity search. <c>full</c> puts the whole conversation in the prompt and is the
/// no-memory-needed ceiling. See <c>docs/memory.md</c> §5.</para>
/// </summary>
internal static class MemoryLocomoBench
{
    internal const string DataVariable = "LYNTAI_LOCOMO_PATH";

    /// <summary>Categories 1–4 are the 1540 questions the literature scores. 5 is LoCoMo's adversarial
    /// class and is excluded by the published protocol, so it is excluded here rather than quietly folded
    /// in — including it would make this number incomparable to the field's own for a second reason.</summary>
    private static readonly int[] ScoredCategories = [1, 2, 3, 4];

    private const int RecallLimit = 20;

    /// <summary>
    /// The multi-shot arm's name and shape. <b>A single top-k is not the mode this engine is built for.</b>
    /// Its entries are a MAP: <c>ExpandAsync</c> walks edges out from what a recall returned, and — in its
    /// own words — "expanding a node reinforces it … digging in one direction is exactly what should make
    /// that direction more retrievable next time". A one-shot benchmark cannot see any of that, so measuring
    /// only one-shot measures a vector index wearing a graph engine's name.
    /// <para><b>The budget and the size-matched control are what keep it honest.</b> Three shots return more
    /// text than one, and more context alone raises a reader's score, so the arm is capped at
    /// <see cref="ShotBudget"/> items and cosine is run at BOTH sizes. Beating <c>vector</c> while feeding
    /// twice its context proves nothing; beating <c>vector-40</c> is the claim.</para>
    /// </summary>
    private const string TwoShot = "lyntai-2shot";
    private const string ThreeShot = "lyntai-3shot";
    private const int ShotBudget = 2 * RecallLimit;
    /// <summary>How many of the previous shot's entries a shot buys the detail on. <b>It is a harness
    /// parameter, not a property of the engine</b>, and it binds hard: at 3 against a 20-entry first load,
    /// a two-shot arm upgrades 15% of what it holds and the rest stays a headline. Sweep it with
    /// <c>--seeds</c> before reading a multi-shot arm as the design's ceiling.</summary>
    private static int expandSeeds = 3;

    /// <summary>
    /// The largest context confirmed to reach this machine's reader INTACT, and the reason the
    /// <c>full</c> arm carries a warning rather than a crown.
    /// <para>Measured by needle probe rather than assumed: a passcode at the very top of the prompt was
    /// still recoverable at 85,508 characters and was not at 109,908, so somewhere between them the head is
    /// dropped. The <c>full</c> arm feeds ~107,000, which is inside that gap — <b>its score is a FLOOR, not
    /// the ceiling the arm is named for</b>, and reading it as "even the whole conversation does badly" is
    /// the wrong conclusion.</para>
    /// <para>It is a property of the DEPLOYMENT's reader, never of this library: the chat seam speaks
    /// OpenAI-compatible HTTP and cannot portably ask for a window, and a server may truncate silently. Set
    /// <c>LYNTAI_READER_WINDOW_CHARS</c> where a run has a bigger one.</para>
    /// </summary>
    private static int ReaderWindowChars =>
        int.TryParse(Environment.GetEnvironmentVariable("LYNTAI_READER_WINDOW_CHARS"), out var v) ? v : 85_000;
    private const int Seed = 12345;

    private static readonly string[] CategoryNames =
        ["", "multi-hop", "temporal", "open-domain", "single-hop", "adversarial(excluded)"];

    private sealed record Question(string ConvId, string Text, string Gold, int Category,
        IReadOnlyList<string> Evidence);

    private sealed record Turn(string Speaker, string Text, string Date, string DiaId);

    public static async Task<int> RunAsync(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(DataVariable)
            ?? Path.Combine("devtools", "_bench-data", "locomo10.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-locomo: dataset not found at '{path}'.");
            Console.Error.WriteLine("  Download it (2.8 MB, MIT) and re-run:");
            Console.Error.WriteLine("    curl -sSL -o devtools/_bench-data/locomo10.json \\");
            Console.Error.WriteLine("      https://raw.githubusercontent.com/snap-research/locomo/main/data/locomo10.json");
            Console.Error.WriteLine($"  Or point {DataVariable} at a copy.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-locomo");
        if (embedder is null) return 1;
        // The retrieval diagnostic needs no reader at all, so it does not demand one — a machine with an
        // embedder but no chat model can still run the half that measures the memory layer.
        // `--shots` is the diagnostic for what this engine is actually FOR: a first load that is small and
        // only related, then expansion that buys the detail. It needs no reader — evidence-hit per shot is
        // the model-free form of "did shot 2 find the memory shot 1 only pointed at" — and it prices each
        // shot in items, characters and milliseconds, because a cheaper context is the point rather than a
        // side effect.
        var shotsOnly = args.Contains("--shots");
        // `--ranks` is the LoCoMo half of `TASKS.md` Part 109. `ReciprocalRankFusionOptions.K` is GLOBAL, and
        // the LongMemEval ladder found K = 120 cuts `stale@k` by ~12 points there for nothing measurable. But
        // K selects a REGIME rather than a sharpness, and this is the opposite workload — a SEARCH one that
        // wants old material FOUND rather than suppressed — so the direction here is not predictable from
        // that class and has to be measured. Model-free, and scored offline from one ingestion for the reason
        // `RankLadder` gives.
        var ranksOnly = args.Contains("--ranks");
        var needsReader = !args.Contains("--retrieval") && !shotsOnly && !ranksOnly;
        var chat = needsReader ? await SweepDoubles.TryRealChatAsync(http, "memory-locomo") : null;
        if (needsReader && chat is null) return 1;

        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : 200;
        var wantFull = args.Contains("--full");
        // `--retrieval` is the MODEL-FREE diagnostic: does the recalled set contain the evidence turn
        // LoCoMo names? It separates a retrieval failure from a reading failure, which an accuracy table
        // conflates — and it does so with no reader and no judge, so neither can be blamed or credited.
        var retrievalOnly = args.Contains("--retrieval");
        var dump = args.Contains("--dump");
        // The judge doubles the model calls, and it is the half this run trusts least. `--no-judge` keeps
        // the model-free grades and drops it.
        var judged = !args.Contains("--no-judge");
        if (ArgValue(args, "--seeds") is { } sd && int.TryParse(sd, out var seedCount)) expandSeeds = seedCount;
        // `--expand-floor` is D98's `GraphMemoryOptions.ExpansionRetrievabilityFloor`, which ships at 0. It
        // gates a WALK, so unlike the K ladder it cannot be scored offline from one ingestion: the floor
        // changes which neighbours are FETCHED, and every later step reads a store the earlier ones moved.
        // One real run per value is therefore the only honest way to price it.
        var expandFloor = ArgValue(args, "--expand-floor") is { } ff && double.TryParse(ff, out var ef) ? ef : 0;
        var probed = false;
        // The QA arms are a DIFFERENT question from the retrieval ladder's, so they are a different list.
        // The ladder varies ranking knobs against a model-free metric; QA asks what a reader can do with
        // what came back, and its arms are the retrieval MODES — one shot, three shots, and cosine at both
        // sizes. Reusing the ladder here would spend five reader passes measuring ranking knobs.
        string[] arms = ranksOnly
            ? ["ranks"]
            : shotsOnly
            ? ["shot-1", "shot-2", "shot-3", "vector", $"vector-{ShotBudget}", "full"]
            : retrievalOnly
                ? wantFull
                    ? ["lyntai", "+sem", "+sem+hop0", "+sem80", "+sem80+hop0", "+forget0", "+forget0+oracle",
                        "+rel-only", "vector", "full"]
                    : ["lyntai", "+sem", "+sem+hop0", "+sem80", "+sem80+hop0", "+forget0", "+forget0+oracle",
                        "+rel-only", "vector"]
                : ["lyntai", TwoShot, ThreeShot, "vector", $"vector-{ShotBudget}", "full"];

        var (conversations, questions) = Load(path);
        var sampled = Stratify(questions, take);

        PrintPreamble(chat?.Model ?? "(none - retrieval only)", embedder,
            conversations.Count, questions.Count, sampled.Count, arms, retrievalOnly || shotsOnly || ranksOnly);

        // Printed even at 0, so a run says which arm it is rather than leaving the reader to infer it from
        // the absence of a flag — the same reason the LongMemEval preamble prints its variant unconditionally.
        if (shotsOnly)
            Console.WriteLine($"Expansion floor: {expandFloor:F2}   "
                + "(GraphMemoryOptions.ExpansionRetrievabilityFloor, D98; ships at 0)\n");

        var stopwatch = Stopwatch.StartNew();
        var correct = new Dictionary<(string Arm, int Category), int>();
        var asked = new Dictionary<(string Arm, int Category), int>();
        var unknown = new Dictionary<string, int>();
        var returned = new Dictionary<string, int>();
        var f1 = new Dictionary<(string Arm, int Category), double>();
        var exact = new Dictionary<(string Arm, int Category), int>();
        var chars = new Dictionary<string, long>();
        var millis = new Dictionary<string, double>();

        // ONE probe across every conversation, because the ladder's rates are over the whole sample. It is
        // installed as the shipped arm's ranking policy and delegates untouched, so it describes the run that
        // produced the published `lyntai` row rather than a reconstruction of it.
        var rankProbe = new EvidenceRankProbe(new ReciprocalRankFusionPolicy());

        foreach (var (convId, turns) in conversations)
        {
            var mine = sampled.Where(q => q.ConvId == convId).ToList();
            if (mine.Count == 0) continue;

            // Per CONVERSATION, not once per run: a control that checks one of ten while reading as "the
            // clone works" is the partial scan this repository files under precision-without-provenance.
            var cloneChecked = false;

            // ONE PRISTINE STORE PER ARM, and this is a correctness requirement rather than tidiness.
            // A recall REINFORCES what it returns, so three arms sharing a store would each mutate the decay
            // state the next one reads. That is not hypothetical: it moved this table's own numbers between
            // runs (lyntai 10.0% -> 5.5%) when a fourth arm was added, with the seed and the data unchanged.
            // Turning reinforcement off would isolate them more cheaply, but it also measures a
            // configuration nobody ships and whose own doc calls it the worst arm for recall quality - so
            // the cost of a fresh ingestion per arm is paid instead. Embeddings are cached across arms, so
            // what is actually repeated is the SQLite and graph work.
            // EVERY ARM HERE KEEPS RETRIEVABILITY ON, and that is the point rather than an oversight.
            // `RetrievabilityWeight = 0` measures this engine with its defining feature switched off, so
            // any score it reaches says only that a disabled decay model behaves like a vector index.
            // Burying old unreinforced material is what this library is FOR (design §5.7.0); the defect
            // worth chasing is narrower — when a recall does spend its 20 slots, it should spend them on
            // the best 20 by its own objective.
            //
            // So each arm turns one knob that is arguably a MISALLOCATION rather than a philosophy:
            // `hop0` asks whether a graph-walk candidate should earn rank credit equal to a relevance
            // signal, and `sem80` whether 20 semantic seeds are simply outnumbered in a pool of
            // `RecallLimit x CandidateMultiplier` = 80 candidates competing for 20 places.
            // In QA mode the ladder is not the subject, so only the two engine-backed MODES are ingested —
            // and they get separate stores for the reason the paragraph above gives, which applies with
            // extra force here: ExpandAsync reinforces what it walks, so a three-shot arm sharing a store
            // would hand the one-shot arm a graph it had already dug through.
            (string Arm, GraphMemoryOptions? Options, IMemoryRankingPolicy? Ranking,
                IMemoryVerificationPolicy? Verification)[] configs = ranksOnly
                ? [("lyntai", null, rankProbe, null)]
                : retrievalOnly
                ? Ladder()
                : shotsOnly
                    // The explicit options object is INERT at floor 0 — the engine's own fallback is
                    // `options ?? new GraphMemoryOptions()` — so an unswept run is byte-identical to one
                    // that never passed one, which archive Part 119 measured rather than assumed.
                    ? [(ThreeShot, new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor }, null, null)]
                    : [("lyntai", null, null, null), (ThreeShot, null, null, null)];

            (string Arm, GraphMemoryOptions? Options, IMemoryRankingPolicy? Ranking,
                IMemoryVerificationPolicy? Verification)[] Ladder() =>
            [
                ("lyntai", null, null, null),
                ("+sem", new GraphMemoryOptions { SemanticSeedK = RecallLimit }, null, null),
                ("+sem+hop0", new GraphMemoryOptions { SemanticSeedK = RecallLimit },
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { HopWeight = 0 }), null),
                ("+sem80", new GraphMemoryOptions { SemanticSeedK = 80 }, null, null),
                ("+sem80+hop0", new GraphMemoryOptions { SemanticSeedK = 80 },
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { HopWeight = 0 }), null),

                // Added 2026-08-31 to SPLIT the -26 gap against `vector`, which the ladder above could not:
                // every arm in it keeps RetrievabilityWeight at its shipped 1, so forgetting votes on the
                // ordering in all of them and none of them can say whether it is the deficit.
                //
                // `+forget0` removes exactly that vote. LoCoMo asks about months of history UNIFORMLY, so it
                // rewards a perfect archive and penalises forgetting BY CONSTRUCTION — this arm prices how
                // much of the gap is that construction rather than a ranking defect.
                ("+forget0", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { RetrievabilityWeight = 0 }),
                    null),

                // The VERDICT arms, added 2026-08-31. D59 decomposed this subsystem's misses and found 100%
                // reachable-but-outranked and 0% unreachable, naming `IMemoryVerificationPolicy` — a judge
                // applied BEFORE the cut, so a buried answer is PROMOTED — as the fix. Neither field
                // benchmark had ever wired that seam: `grep -c` returned 0 in both, so every LoCoMo figure
                // on record was taken with the largest known lever switched off.
                //
                // `+oracle` is the CEILING, not a score. A perfect judge endorses exactly the evidence
                // LoCoMo names, so this measures whether the evidence is within `VerificationDepth` of the
                // candidate pool at all — D59's "reachable-but-outranked" question asked of THIS workload.
                // If a perfect judge cannot close the gap, no real model will, and that is worth knowing
                // before spending a model run. It is the same stance `memory-annotation` takes with a
                // perfect annotator.
                //
                // Built on `+forget0` rather than the defaults because that is the measured best engine
                // configuration (60.0% against 54.5%), so the judge is priced on top of what already works
                // rather than against a baseline we know is beaten.
                ("+forget0+oracle", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { RetrievabilityWeight = 0 }),
                    new EvidenceOracleVerifier(mine.ToDictionary(q => q.Text, q => q.Evidence.ToHashSet()))),

                // The BRIDGE arm, and the one that decides where to look next. Relevance alone through the
                // engine's own pipeline (salience already ships at 0, D89), so it scores the same quantity
                // `vector` does and differs only in WHICH CANDIDATES reached the ranker.
                //   - lands near `vector`  => the pool is fine and the WEIGHTS carry the gap;
                //   - stays far below      => the pool/seeding is the deficit and no weight can fix it.
                // Part 113 established the pool CONTAINS the evidence; it did not establish at what depth,
                // and those two readings need different work.
                ("+rel-only", null, new ReciprocalRankFusionPolicy(
                    new ReciprocalRankFusionOptions { RetrievabilityWeight = 0, HopWeight = 0 }), null),
            ];

            // The dia_id rides along in the CONTENT so an evidence hit is checkable without a model. It is
            // in the text every arm reads, so none is advantaged.
            var texts = turns.Select(t => $"[{t.Date}] ({t.DiaId}) {t.Speaker}: {t.Text}").ToList();
            var vectorIndex = new List<(string Text, float[] Vector)>();
            foreach (var text in texts) vectorIndex.Add((text, await embedder.EmbedAsync(text)));

            // Warm the QUERY embeddings before anything is timed. The embedder cache is shared across arms,
            // so whichever arm ran first would otherwise pay for every embed and the ms column would be
            // measuring arm ORDER rather than retrieval work.
            if (shotsOnly) foreach (var q in mine) await embedder.EmbedAsync(q.Text);

            var recalled = new Dictionary<(string Arm, string Question), List<string>>();
            foreach (var (arm, options, ranking, verification) in configs)
            {
                // INGESTED ONCE into a template nothing reads, then CLONED per question. A recall reinforces
                // what it returned and ExpandAsync reinforces what it walks, so questions sharing one store
                // are not independent trials — `shot-1` moved 30.0% to 28.0% between two runs differing only
                // in how much a LATER shot expanded. Copying a migrated file is what makes a per-question
                // store affordable where re-ingesting every question is not.
                using var template = new MemoryPolicySweep.SweepDb();
                var vectors = new InMemoryVectorStore();
                var ingest = new GraphMemoryEngine("locomo",
                    new SqliteMemoryGraphStore(template.Factory), options: options,
                    embedder: embedder, vectors: vectors, ranking: ranking, verification: verification);

                foreach (var text in texts)
                    await ingest.RememberAsync(new MemoryWrite(convId, "session", text));

                // The vector store is deliberately SHARED across questions: RememberAsync is the only thing
                // that writes it, so it is read-only once ingestion is done, and re-embedding per question
                // would cost more than the rest of the study. The graph store is the one that mutates on
                // READ, and it is the one being cloned.
                GraphMemoryEngine Fresh(MemoryPolicySweep.SweepDb clone) =>
                    new("locomo", new SqliteMemoryGraphStore(clone.Factory), options: options,
                        embedder: embedder, vectors: vectors, ranking: ranking, verification: verification);

                // CONTROL, added for TASKS.md Part 109: SemanticSeedK = 20 moved evidence-hit by 0.0 points
                // and the read path looks correct on inspection, so the question is whether the vectors are
                // there and whether a search finds them. Reading it back from what actually ran is the only
                // way to tell an option that does nothing from an option that is never reached.
                if (options?.SemanticSeedK > 0 && !probed)
                {
                    probed = true;
                    // on its own clone: the probe issues a real recall, and a recall reinforces — running it
                    // against the template would hand every question below a store it had already read
                    using var probeDb = template.Clone();
                    var engine = Fresh(probeDb);
                    var collection = $"locomo|{convId}|session";
                    var probeVector = await embedder.EmbedAsync(mine[0].Text);
                    var all = await vectors.SearchAsync(collection, probeVector, 100_000);
                    var topK = await vectors.SearchAsync(collection, probeVector, RecallLimit);
                    var collections = await vectors.ListCollectionsAsync($"locomo|{convId}|");
                    Console.WriteLine($"  CONTROL {arm}/{convId}: collections={collections.Count} "
                        + $"[{string.Join(",", collections)}] vectors={all.Count} of {texts.Count} turns; "
                        + $"semantic top-{RecallLimit} returned {topK.Count}, "
                        + $"ids parse as long: {topK.Count(m => long.TryParse(m.Id, out _))}");

                    // The two RELEVANCE SCALES, side by side. D93 records that within one recall these are
                    // not commensurable — a lexical rank ramp, a real cosine on a semantic seed, a flat 1 on
                    // a graph walk — and that is the standing hypothesis for why seeding changes nothing:
                    // a cosine of 0.6 cannot outrank a rank-ramp 1.0 however much more relevant it is.
                    var probeRecall = await engine.RecallAsync(
                        new MemoryQuery(convId, "session", mine[0].Text, Limit: RecallLimit));
                    Console.WriteLine("    returned Relevance : "
                        + string.Join(", ", probeRecall.Items.Take(8).Select(i => i.Relevance.ToString("F3"))));
                    Console.WriteLine("    semantic  cosines  : "
                        + string.Join(", ", topK.Take(8).Select(m => m.Score.ToString("F3"))));
                }

                foreach (var q in mine)
                {
                    // A PRIVATE copy of the ingested store, so this question reads what ingestion left and
                    // not what the previous question reinforced. Cloned before the clock starts: the copy is
                    // harness cost and the ms column prices retrieval.
                    using var qdb = template.Clone();
                    var engine = Fresh(qdb);

                    // CONTROL, once per conversation: a clone that silently lost rows would present as a
                    // recall-quality regression rather than as a broken harness, which is the direction that
                    // gets published. Counted from the store, not assumed from the copy having succeeded.
                    if (!cloneChecked)
                    {
                        cloneChecked = true;
                        var present = await new SqliteMemoryGraphStore(qdb.Factory)
                            .SeedAsync("locomo", convId, "session", null, texts.Count + 16);
                        Console.WriteLine($"  CONTROL clone/{convId}: {present.Count} of {texts.Count} "
                            + "ingested turns present in the per-question copy");
                    }

                    // The K ladder needs ONE recall per question and nothing else: the probe observes the pool
                    // that recall produced and scores every K over it offline. Evidence is handed to the
                    // probe rather than checked here, so the metric sits beside the ranks it is scoring.
                    if (ranksOnly)
                    {
                        rankProbe.Evidence = q.Evidence;
                        await engine.RecallAsync(
                            new MemoryQuery(convId, "session", q.Text, Limit: RecallLimit));
                        continue;
                    }

                    var clock = Stopwatch.StartNew();

                    // Two shots and three come from ONE walk, because three is a strict superset of two.
                    // Snapshotting mid-walk keeps them exactly nested — any difference between the arms is
                    // the extra shot and nothing else — and costs one ingestion rather than two.
                    // MaxEntries is passed EXPLICITLY: the library derives twice what step 1 returned, which
                    // equals ShotBudget only when a question's recall fills its limit, so leaving it null
                    // would shrink the bound on short recalls and move published figures for no defect.
                    var walkOptions = new MemoryWalkOptions
                    {
                        SeedsPerStep = expandSeeds,
                        Hops = 1,
                        MaxItems = ShotBudget,
                    };

                    List<string> context = [];
                    var reached = 0;

                    await foreach (var step in engine.WalkAsync(
                        new MemoryQuery(convId, "session", q.Text, Limit: RecallLimit), walkOptions))
                    {
                        context = [.. step.Items.Select(i => i.Content ?? i.Headline)];

                        // the single-shot arms take step 1 and stop; only ThreeShot walks
                        if (arm != ThreeShot)
                        {
                            recalled[(arm, q.Text)] = context;
                            break;
                        }

                        reached = step.Number;
                        Snapshot(step.Number, context);
                        if (!shotsOnly && step.Number is 2 or 3)
                            recalled[(step.Number == 2 ? TwoShot : ThreeShot, q.Text)] = context;
                        if (step.Number >= 3) break;
                    }

                    // A walk ENDS when a step moves nothing, where the pre-surface loop ran shots 2 and 3
                    // unconditionally and re-snapshotted the unchanged context. Filling the gap keeps that:
                    // dropping a row would change the DENOMINATOR of every rate below rather than the
                    // retrieval, which is a harness difference wearing a result's clothes.
                    if (arm == ThreeShot)
                        for (var shot = reached + 1; shot <= 3; shot++)
                        {
                            Snapshot(shot, context);
                            if (!shotsOnly)
                                recalled[(shot == 2 ? TwoShot : ThreeShot, q.Text)] = context;
                        }

                    // Each shot is priced where it happens: cumulative items, characters and elapsed time,
                    // plus whether the evidence has arrived YET. That last one is the model-free form of
                    // "shot 1 finds something related, shot 2 finds the thing".
                    void Snapshot(int shot, IReadOnlyList<string> body)
                    {
                        if (!shotsOnly) return;
                        var name = $"shot-{shot}";
                        recalled[(name, q.Text)] = [.. body];
                        if (q.Evidence.Count == 0) return;
                        var key = (name, q.Category);
                        asked[key] = asked.GetValueOrDefault(key) + 1;
                        returned[name] = returned.GetValueOrDefault(name) + body.Count;
                        chars[name] = chars.GetValueOrDefault(name) + body.Sum(b => b.Length);
                        millis[name] = millis.GetValueOrDefault(name) + clock.Elapsed.TotalMilliseconds;
                        if (q.Evidence.Any(e => body.Any(g => g.Contains($"({e})", StringComparison.Ordinal))))
                            correct[key] = correct.GetValueOrDefault(key) + 1;
                    }
                }
            }

            if (shotsOnly)
            {
                // The controls, priced the same way: cosine at both sizes, and the whole conversation. `full`
                // holds the evidence by construction, so its 100% is a definition rather than a result — it
                // is here for its COST columns, which are the point of the comparison.
                foreach (var q in mine.Where(q => q.Evidence.Count > 0))
                {
                    foreach (var (arm, k) in new[] { ("vector", RecallLimit), ($"vector-{ShotBudget}", ShotBudget) })
                    {
                        var clock = Stopwatch.StartNew();
                        var got = (await TopKAsync(embedder, vectorIndex, q.Text, k)).ToList();
                        millis[arm] = millis.GetValueOrDefault(arm) + clock.Elapsed.TotalMilliseconds;
                        var key = (arm, q.Category);
                        asked[key] = asked.GetValueOrDefault(key) + 1;
                        returned[arm] = returned.GetValueOrDefault(arm) + got.Count;
                        chars[arm] = chars.GetValueOrDefault(arm) + got.Sum(g => g.Length);
                        if (q.Evidence.Any(e => got.Any(g => g.Contains($"({e})", StringComparison.Ordinal))))
                            correct[key] = correct.GetValueOrDefault(key) + 1;
                    }

                    var fullKey = ("full", q.Category);
                    asked[fullKey] = asked.GetValueOrDefault(fullKey) + 1;
                    correct[fullKey] = correct.GetValueOrDefault(fullKey) + 1;
                    returned["full"] = returned.GetValueOrDefault("full") + texts.Count;
                    chars["full"] = chars.GetValueOrDefault("full") + texts.Sum(t => t.Length);
                }
                Console.WriteLine($"  {convId}: {turns.Count} turns ingested, "
                    + $"{mine.Count(q => q.Evidence.Count > 0)} question(s) walked");
                continue;
            }

            if (ranksOnly)
            {
                // The probe accumulated everything while those recalls ran. There is no per-arm set to score
                // here, because this mode's "arms" are values of K rather than configurations — one recall
                // produces the pool, and every K is scored over it offline.
                Console.WriteLine($"  {convId}: {turns.Count} turns ingested, "
                    + $"{mine.Count(q => q.Evidence.Count > 0)} question(s) ranked");
                continue;
            }

            if (retrievalOnly)
            {
                foreach (var q in mine.Where(q => q.Evidence.Count > 0))
                {
                    var sets = configs.Select(c => (c.Arm, Got: recalled[(c.Arm, q.Text)]))
                        .Append(("vector", (await TopKAsync(embedder, vectorIndex, q.Text, RecallLimit)).ToList()));

                    foreach (var (arm, got) in sets)
                    {
                        var key = (arm, q.Category);
                        asked[key] = asked.GetValueOrDefault(key) + 1;
                        returned[arm] = returned.GetValueOrDefault(arm) + got.Count;
                        if (q.Evidence.Any(e => got.Any(g => g.Contains($"({e})", StringComparison.Ordinal))))
                            correct[key] = correct.GetValueOrDefault(key) + 1;
                    }
                }
                Console.WriteLine($"  {convId}: {turns.Count} turns x {configs.Length} arm(s) ingested, "
                    + $"{mine.Count(q => q.Evidence.Count > 0)} question(s) probed");
                continue;
            }

            foreach (var q in mine)
            {
                foreach (var arm in arms)
                {
                    // Every `lyntai*` arm reads the set its own pristine-store engine already produced
                    // above, so the QA table and the retrieval table are scoring the SAME recalls rather
                    // than two independently-drawn ones.
                    IReadOnlyList<string> pieces = arm switch
                    {
                        "vector" => [.. await TopKAsync(embedder, vectorIndex, q.Text, RecallLimit)],
                        _ when arm == $"vector-{ShotBudget}" =>
                            [.. await TopKAsync(embedder, vectorIndex, q.Text, ShotBudget)],
                        "full" => texts,
                        _ => recalled[(arm, q.Text)],
                    };
                    var context = string.Join("\n", pieces);
                    returned[arm] = returned.GetValueOrDefault(arm) + pieces.Count;

                    var hypothesis = (await chat!.AskAsync(AnswerPrompt(context, q.Text), maxTokens: 48))?.Trim() ?? "";
                    var key = (arm, q.Category);
                    asked[key] = asked.GetValueOrDefault(key) + 1;
                    // What the arm SPENT. A three-shot arm returns more text than a one-shot one, and more
                    // context raises any reader's score, so an accuracy column read without this one cannot
                    // tell a better memory from a bigger one.
                    chars[arm] = chars.GetValueOrDefault(arm) + context.Length;
                    if (hypothesis.Length == 0 || hypothesis.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        unknown[arm] = unknown.GetValueOrDefault(arm) + 1;
                        continue;
                    }

                    // The MODEL-FREE grade is primary and the judge sits beside it, not above it. A model is
                    // not better at exact comparison (`model-decoupling.md`), and this judge is the same 4B
                    // model that wrote the answer — so token-F1 against the gold string is the number to
                    // trust, and the judge's job is to catch a right answer worded differently.
                    f1[key] = f1.GetValueOrDefault(key) + TokenF1(hypothesis, q.Gold);
                    if (Normalize(hypothesis).SequenceEqual(Normalize(q.Gold)))
                        exact[key] = exact.GetValueOrDefault(key) + 1;
                    if (judged && await JudgeAsync(chat, q, hypothesis))
                        correct[key] = correct.GetValueOrDefault(key) + 1;
                }
            }

            Console.WriteLine($"  {convId}: {turns.Count} turns ingested, {mine.Count} question(s) asked");
        }

        if (ranksOnly) PrintRanks(rankProbe);
        else if (shotsOnly) PrintShots(arms, correct, asked, returned, chars, millis);
        else PrintResults(arms, correct, asked, unknown, returned, retrievalOnly, f1, exact, chars, judged);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>Cosine top-k over the SAME embedder — the naive-RAG arm, with no graph, decay, salience or
    /// ranking policy in the path. It is the ablation the arm table rests on, so it deliberately shares the
    /// embedder instance rather than building a second one.</summary>
    private static async Task<IEnumerable<string>> TopKAsync(
        IEmbedder embedder, List<(string Text, float[] Vector)> index, string query, int k)
    {
        var q = await embedder.EmbedAsync(query);
        return index.Select(e => (e.Text, Score: Cosine(q, e.Vector)))
            .OrderByDescending(e => e.Score).Take(k).Select(e => e.Text);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static string AnswerPrompt(string context, string question) =>
        $"""
         Answer the question using ONLY the conversation excerpts below.
         Reply with the answer alone - a few words, no sentence, no explanation.
         If the excerpts do not contain the answer, reply exactly: unknown

         Excerpts:
         {context}

         Question: {question}
         Answer:
         """;

    /// <summary>SQuAD's answer normalization — lowercase, drop punctuation and the articles — so "The Bahamas"
    /// and "bahamas" are one string. It is what makes a model-free grade possible over a model's prose.</summary>
    private static string[] Normalize(string s) =>
        new string([.. s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("a" or "an" or "the"))
            .ToArray();

    /// <summary>Token overlap F1 against the gold answer — the standard extractive-QA measure, and the one
    /// number here no model produced. It is reported as the PRIMARY grade for that reason: this machine's
    /// judge is the same small model that wrote the answer, and a self-grading reader is generous to its own
    /// phrasing.</summary>
    private static double TokenF1(string hypothesis, string gold)
    {
        var h = Normalize(hypothesis);
        var g = Normalize(gold);
        if (h.Length == 0 || g.Length == 0) return h.Length == g.Length ? 1 : 0;

        var pool = g.ToList();
        var shared = h.Count(w => pool.Remove(w));
        if (shared == 0) return 0;
        double precision = (double)shared / h.Length, recall = (double)shared / g.Length;
        return 2 * precision * recall / (precision + recall);
    }

    /// <summary>Grades a hypothesis against the gold answer with the SAME model that answered. That is a
    /// known weakness and is disclosed in the preamble rather than hidden: a self-grading reader can be
    /// generous to its own phrasing. It is used because the alternative on this machine is no grader at all.
    /// </summary>
    private static async Task<bool> JudgeAsync(SweepDoubles.OpenAiCompatibleChat chat, Question q, string hypothesis)
    {
        var verdict = await chat.AskAsync(
            $"""
             Grade a short answer. Reply with exactly one word: YES or NO.

             Question: {q.Text}
             Reference answer: {q.Gold}
             Candidate answer: {hypothesis}

             Does the candidate state the same fact as the reference? YES or NO:
             """, maxTokens: 3);
        return verdict?.TrimStart().StartsWith("YES", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static (List<(string Id, List<Turn> Turns)>, List<Question>) Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var conversations = new List<(string, List<Turn>)>();
        var questions = new List<Question>();

        foreach (var sample in doc.RootElement.EnumerateArray())
        {
            var id = sample.GetProperty("sample_id").GetString()!;
            var conv = sample.GetProperty("conversation");
            var turns = new List<Turn>();

            foreach (var prop in conv.EnumerateObject())
            {
                if (!prop.Name.StartsWith("session_", StringComparison.Ordinal)
                    || prop.Name.EndsWith("date_time", StringComparison.Ordinal)
                    || prop.Value.ValueKind != JsonValueKind.Array) continue;

                var date = conv.TryGetProperty($"{prop.Name}_date_time", out var d) ? d.GetString() ?? "" : "";
                foreach (var t in prop.Value.EnumerateArray())
                    turns.Add(new Turn(t.GetProperty("speaker").GetString() ?? "",
                        t.GetProperty("text").GetString() ?? "", date,
                        t.TryGetProperty("dia_id", out var did) ? did.GetString() ?? "" : ""));
            }
            conversations.Add((id, turns));

            foreach (var qa in sample.GetProperty("qa").EnumerateArray())
            {
                var category = qa.GetProperty("category").GetInt32();
                if (!ScoredCategories.Contains(category)) continue;
                if (!qa.TryGetProperty("answer", out var gold)) continue;
                var evidence = qa.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array
                    ? ev.EnumerateArray().Select(e => e.ToString()).ToList()
                    : [];
                questions.Add(new Question(id, qa.GetProperty("question").GetString() ?? "",
                    gold.ToString(), category, evidence));
            }
        }
        return (conversations, questions);
    }

    /// <summary>A seeded, category-STRATIFIED sample. Proportional rather than equal-per-category, so the
    /// mix matches the benchmark's own and the overall number means the same thing a full run's would.</summary>
    private static List<Question> Stratify(List<Question> all, int take)
    {
        if (take >= all.Count) return all;
        var rng = new Random(Seed);
        var picked = new List<Question>();
        foreach (var group in all.GroupBy(q => q.Category).OrderBy(g => g.Key))
        {
            var want = (int)Math.Round(take * (double)group.Count() / all.Count);
            picked.AddRange(group.OrderBy(_ => rng.Next()).Take(Math.Max(1, want)));
        }
        return picked;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void PrintPreamble(string model, SweepDoubles.CachingEmbedder embedder,
        int conversations, int total, int sampled, IReadOnlyList<string> arms, bool retrievalOnly)
    {
        if (retrievalOnly)
        {
            Console.WriteLine("=== LoCoMo: evidence-hit@k, the MODEL-FREE half ===");
            Console.WriteLine();
            Console.WriteLine("Does the recalled set contain the evidence turn LoCoMo names for the question?");
            Console.WriteLine("No reader and no judge, so a difference here is the MEMORY LAYER and nothing");
            Console.WriteLine("else - which is what separates a retrieval failure from a reading failure.");
            Console.WriteLine();
            Console.WriteLine($"Conversations: {conversations}   Questions: {sampled} of {total}, seeded {Seed}");
            Console.WriteLine($"Arms: {string.Join(", ", arms)}   k = {RecallLimit}   embedder {SweepDoubles.Model}");
            Console.WriteLine();
            return;
        }

        Console.WriteLine("=== LoCoMo: the benchmark this repository had no number against ===");
        Console.WriteLine();
        Console.WriteLine("*** AN ABSOLUTE SCORE HERE IS NOT COMPARABLE TO A PUBLISHED ONE. *** Published");
        Console.WriteLine($"LoCoMo results are produced with a frontier reader; this ran {model} locally, and");
        Console.WriteLine("the reader sets the ceiling far more than the memory layer does. What IS comparable");
        Console.WriteLine("is the ARM DIFFERENCE: every arm answers the same questions with the same reader.");
        Console.WriteLine();
        Console.WriteLine($"Conversations: {conversations}   Scored categories: 1-4 (the published protocol)");
        Console.WriteLine($"Questions: {sampled} sampled from {total}, seeded {Seed}, stratified by category");
        Console.WriteLine($"Arms: {string.Join(", ", arms)}   recall limit {RecallLimit}   embedder {SweepDoubles.Model}");
        Console.WriteLine();
        Console.WriteLine("The grader is the SAME model that answered, which can be generous to its own");
        Console.WriteLine("phrasing. Stated rather than hidden; a second grader is the obvious next control.");
        Console.WriteLine();
    }

    /// <summary>Observes the candidate pool a recall saw and scores the K ladder over it, delegating the real
    /// ranking untouched — so the arm it sits in IS the shipped arm, and the numbers describe the run that
    /// produced the published `lyntai` row rather than a reconstruction of it.
    /// <para>The metric is LoCoMo's own: does the top-k hold a turn the dataset names as evidence? The dia id
    /// rides in the content, so no reader and no judge is involved.</para></summary>
    private sealed class EvidenceRankProbe(IMemoryRankingPolicy inner) : IMemoryRankingPolicy
    {
        private readonly List<int> _pools = [];

        internal IReadOnlyList<string> Evidence { get; set; } = [];

        internal int[] HitAtK { get; } = new int[RankLadder.K.Length];

        internal int Scored { get; private set; }

        internal int Unreachable { get; private set; }

        internal int Agreed { get; private set; }

        internal int Compared { get; private set; }

        internal int MedianPool => _pools.Count == 0 ? 0 : _pools.Order().ElementAt(_pools.Count / 2);

        public IReadOnlyList<RankedMemory> Rank(
            IReadOnlyList<MemoryCandidate> candidates, in MemoryRankingContext context)
        {
            var result = inner.Rank(candidates, context);
            if (Evidence.Count == 0) return result;

            var ladder = new RankLadder(candidates);
            _pools.Add(ladder.Pool.Count);
            Scored++;

            // Counted, not excluded. A question whose evidence never entered the pool is a SEEDING failure
            // rather than a fusion one — but dropping it would flatter every K by the same amount and break
            // the one thing that makes this table comparable to the published row, which is that the shipped
            // K reproduces it over the same denominator.
            if (!ladder.Pool.Any(Holds)) Unreachable++;

            for (var k = 0; k < RankLadder.K.Length; k++)
            {
                var top = ladder.TopAt(RankLadder.K[k], context.Limit);
                if (top.Any(Holds)) HitAtK[k]++;

                if (RankLadder.K[k] != RankLadder.Shipped) continue;
                Compared++;
                if (RankLadder.AgreesWithShipped(top, result, context.Limit)) Agreed++;
            }
            return result;
        }

        private bool Holds(MemoryCandidate c) =>
            Evidence.Any(e => c.Node.Content.Contains($"({e})", StringComparison.Ordinal));
    }

    /// <summary>The K ladder, and the two controls that decide whether it is a table about the formula this
    /// library actually runs.</summary>
    private static void PrintRanks(EvidenceRankProbe probe)
    {
        var scored = Math.Max(1, probe.Scored);
        Console.WriteLine();
        Console.WriteLine($"{"K",-8} {"evidence-hit",14}   (same pool, same ranks, scored offline)");
        Console.WriteLine(new string('-', 62));
        for (var k = 0; k < RankLadder.K.Length; k++)
            Console.WriteLine($"{RankLadder.K[k],-8:F0} {(double)probe.HitAtK[k] / scored,14:P1}"
                + (RankLadder.K[k] == RankLadder.Shipped ? "   <- shipped" : ""));

        Console.WriteLine();
        Console.WriteLine($"  CONTROL 1: the K={RankLadder.Shipped:F0} row reproduces the SHIPPED policy's own "
            + $"top-{RecallLimit} on {probe.Agreed}/{probe.Compared} recalls.");
        Console.WriteLine("  Anything below 100% means the replica is not the formula this library runs, and");
        Console.WriteLine("  the ladder above is a table about something else.");
        Console.WriteLine();
        Console.WriteLine("  CONTROL 2: that same row IS this run's `lyntai` arm, so it must match the");
        Console.WriteLine("  published retrieval figure for the same sample. If it does not, the ladder is not");
        Console.WriteLine("  comparable to the table it is meant to extend.");
        Console.WriteLine();
        Console.WriteLine($"  Scored {probe.Scored} question(s) carrying evidence; {probe.Unreachable} had no");
        Console.WriteLine("  evidence in the candidate pool at all. Those stay in the DENOMINATOR on purpose:");
        Console.WriteLine("  they are a seeding failure rather than a fusion one, and dropping them would");
        Console.WriteLine($"  flatter every K equally. Pool size (median): {probe.MedianPool}.");
    }

    /// <summary>What each shot BUYS against what it COSTS. The rate alone would say a bigger context is a
    /// better memory, so every accuracy column here has its price beside it and the last column divides one
    /// by the other.</summary>
    private static void PrintShots(IReadOnlyList<string> arms,
        Dictionary<(string, int), int> correct, Dictionary<(string, int), int> asked,
        Dictionary<string, int> returned, Dictionary<string, long> chars, Dictionary<string, double> millis)
    {
        Console.WriteLine();
        Console.WriteLine("=== What each SHOT buys, and what it costs (no model in the loop) ===");
        Console.WriteLine();
        Console.WriteLine("A recall returns HEADLINES - the engine withholds associative content until an");
        Console.WriteLine("expansion asks for it, which is what makes the first load cheap. So shot 1 is an");
        Console.WriteLine("index of what is related, and shots 2-3 buy the detail on whatever looked worth it.");
        Console.WriteLine();
        Console.WriteLine($"{"Arm",-12} {"evidence-hit",13} {"items/q",9} {"chars/q",9} {"ms/q",8} {"hit / 1k chars",15}");
        Console.WriteLine(new string('-', 70));

        foreach (var arm in arms)
        {
            var ask = ScoredCategories.Sum(c => asked.GetValueOrDefault((arm, c)));
            if (ask == 0) continue;
            var rate = (double)ScoredCategories.Sum(c => correct.GetValueOrDefault((arm, c))) / ask;
            var perQuestion = (double)chars.GetValueOrDefault(arm) / ask;
            Console.WriteLine($"{arm,-12} {rate,13:P1} "
                + $"{(double)returned.GetValueOrDefault(arm) / ask,9:F1} {perQuestion,9:F0} "
                + $"{(millis.TryGetValue(arm, out var ms) ? $"{ms / ask:F1}" : "-"),8} "
                + $"{(perQuestion > 0 ? rate / (perQuestion / 1000) : 0),15:F3}");
        }

        Console.WriteLine();
        Console.WriteLine("  'full' is 100% BY CONSTRUCTION - it holds every turn, so its hit rate is a");
        Console.WriteLine("  definition rather than a result. It is in the table for its cost columns, which");
        Console.WriteLine("  are what a design that refuses to blow up the context is measured against.");
        Console.WriteLine($"  `vector-{ShotBudget}` is the SIZE-MATCHED control: beating `vector` while feeding");
        Console.WriteLine("  twice its context proves nothing, so cosine is run at both sizes.");
        Console.WriteLine("  'ms/q' is memory-layer time only - no reader. Query embeddings are warmed before");
        Console.WriteLine("  any arm is timed, so this column is retrieval work rather than arm ORDER.");
    }

    private static void PrintResults(IReadOnlyList<string> arms,
        Dictionary<(string, int), int> correct, Dictionary<(string, int), int> asked,
        Dictionary<string, int> unknown, Dictionary<string, int> returned, bool retrievalOnly,
        Dictionary<(string, int), double> f1, Dictionary<(string, int), int> exact,
        Dictionary<string, long> chars, bool judged)
    {
        Console.WriteLine();
        Console.WriteLine(retrievalOnly
            ? "=== evidence-hit@k by arm and category (no model in the loop) ==="
            : "=== token-F1 by arm and category (MODEL-FREE grading of a model's answer) ===");
        Console.Write($"{"Arm",-14}");
        foreach (var c in ScoredCategories) Console.Write($"  {CategoryNames[c],-14}");
        Console.WriteLine($"  {"overall",-10} {(retrievalOnly ? "items/q" : "unknown"),8}");
        Console.WriteLine(new string('-', 14 + (ScoredCategories.Length * 16) + 22));

        foreach (var arm in arms)
        {
            Console.Write($"{arm,-14}");
            double hit = 0;
            var ask = 0;
            foreach (var c in ScoredCategories)
            {
                var a = asked.GetValueOrDefault((arm, c));
                var k = retrievalOnly ? correct.GetValueOrDefault((arm, c)) : f1.GetValueOrDefault((arm, c));
                hit += k; ask += a;
                Console.Write($"  {(a == 0 ? "-" : retrievalOnly
                    ? $"{k / a:P1} ({k}/{a})" : $"{k / a:P1}"),-14}");
            }
            var trailer = retrievalOnly
                ? (ask == 0 ? "-" : $"{(double)returned.GetValueOrDefault(arm) / ask:F1}")
                : unknown.GetValueOrDefault(arm).ToString();
            Console.Write($"  {(ask == 0 ? "-" : $"{hit / ask:P1}"),-10} {trailer,8}");
            Console.WriteLine();
        }

        Console.WriteLine();
        if (retrievalOnly)
        {
            Console.WriteLine("  'items/q' is how many entries the arm actually RETURNED per question. An arm");
            Console.WriteLine("  well under k is not losing on ranking - it is being filtered before ranking,");
            Console.WriteLine("  which is a different defect and one this column exists to expose.");
            return;
        }

        // WHAT EACH ARM SPENT, beside what it scored. Without this a reader cannot tell a better memory from
        // a bigger one: three shots return more text than one, and more context lifts any reader.
        Console.WriteLine($"{"Arm",-14} {"token-F1",10} {"exact",10} {(judged ? "judge" : "judge(off)"),10} "
            + $"{"unknown",8} {"items/q",8} {"chars/q",9}");
        Console.WriteLine(new string('-', 76));
        foreach (var arm in arms)
        {
            var ask = ScoredCategories.Sum(c => asked.GetValueOrDefault((arm, c)));
            if (ask == 0) continue;
            var em = ScoredCategories.Sum(c => exact.GetValueOrDefault((arm, c)));
            var jd = ScoredCategories.Sum(c => correct.GetValueOrDefault((arm, c)));
            Console.WriteLine($"{arm,-14} "
                + $"{ScoredCategories.Sum(c => f1.GetValueOrDefault((arm, c))) / ask,10:P1} "
                + $"{(double)em / ask,10:P1} {(judged ? $"{(double)jd / ask:P1}" : "-"),10} "
                + $"{unknown.GetValueOrDefault(arm),8} "
                + $"{(double)returned.GetValueOrDefault(arm) / ask,8:F1} "
                + $"{(double)chars.GetValueOrDefault(arm) / ask,9:F0}");
        }

        Console.WriteLine();
        Console.WriteLine("  'token-F1' is the PRIMARY column and no model produced it - it is overlap against");
        Console.WriteLine("  the gold string. 'judge' is the same small model that wrote the answer, so it is");
        Console.WriteLine("  reported beside F1 rather than instead of it: a self-grader is generous to its own");
        Console.WriteLine("  phrasing, and the two disagreeing is information rather than a defect.");
        Console.WriteLine("  'unknown' counts answers where the reader said the excerpts contained none - a HIGH");
        Console.WriteLine("  count is a retrieval failure, not a reasoning one.");
        Console.WriteLine("  'chars/q' is the context each arm SPENT. Read every accuracy column against it;");
        Console.WriteLine($"  `vector-{ShotBudget}` exists so the multi-shot arms face a size-matched control.");

        // An arm whose context does not FIT is not a weak arm, and a table that cannot tell the difference
        // invites exactly the wrong conclusion — that retrieval barely matters because even the whole
        // conversation scored badly.
        var over = arms.Where(a =>
        {
            var ask = ScoredCategories.Sum(c => asked.GetValueOrDefault((a, c)));
            return ask > 0 && (double)chars.GetValueOrDefault(a) / ask > ReaderWindowChars;
        }).ToList();
        if (over.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"  ! {string.Join(", ", over)} feeds more than this reader can hold "
            + $"({ReaderWindowChars} chars, measured by needle probe - see ReaderWindowChars).");
        Console.WriteLine("    The head of the prompt is dropped, so that row is a FLOOR and not a ceiling.");
        Console.WriteLine("    Do NOT read it as 'even full context does badly'.");
    }

    /// <summary>
    /// A PERFECT judge: it endorses exactly the turns LoCoMo names as evidence, and nothing else.
    ///
    /// <para><b>It measures a CEILING, not a score</b>, and the distinction is the whole reason it runs
    /// first. <c>IMemoryVerificationPolicy</c> promotes endorsed candidates ahead of the caller's limit, so
    /// with a perfect judge this arm answers one question: <b>is the evidence inside
    /// <c>VerificationDepth</c> of the candidate pool at all?</b> That is D59's "reachable-but-outranked"
    /// decomposition asked of THIS workload. If a perfect judge cannot close the gap to <c>vector</c>, no
    /// real model will, and the model run is not worth spending — the same stance
    /// <c>memory-annotation</c> takes with a perfect annotator.</para>
    ///
    /// <para><b>What it therefore does NOT say:</b> nothing about any real judge's accuracy. A gap that
    /// closes here is an upper bound a model then has to earn.</para>
    /// </summary>
    /// <param name="evidenceByQuery">Question text to the dia_ids LoCoMo declares as its evidence.</param>
    private sealed class EvidenceOracleVerifier(IReadOnlyDictionary<string, HashSet<string>> evidenceByQuery)
        : IMemoryVerificationPolicy
    {
        public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
            CancellationToken ct = default)
        {
            if (!evidenceByQuery.TryGetValue(request.Query, out var evidence))
                return Task.FromResult(MemoryVerification.NoOpinion);

            // The dia_id rides along in the CONTENT as "(dia_id)" — the same token the evidence-hit metric
            // matches on, so the oracle and the score agree about what evidence IS by construction.
            var ids = request.Candidates
                .Where(c => evidence.Any(e =>
                    c.Headline.Contains($"({e})", StringComparison.Ordinal)))
                .Select(c => c.Id)
                .ToList();

            // A genuine "none of these answered it" is a real verdict and must NOT be reported as
            // NoOpinion — the engine treats those differently, and collapsing them is the defect
            // MemoryVerification's own docs warn about.
            return Task.FromResult(new MemoryVerification(ids));
        }
    }
}
