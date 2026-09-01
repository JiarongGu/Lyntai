using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Llm;
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
    // Fused ranking's own three-shot walker (D103) — same walk shape as `ThreeShot`, joined to it in
    // `MultiShotArms` rather than given a parallel doc block, since that walk is what this one inherits.
    private const string FusedThreeShot = "lyntai-fused-3shot";

    /// <summary>The one-shot fused arm, and the REHYDRATED copy of it that isolates truncation.
    /// <para><c>lyntai-fused-full</c> reuses that arm's returned set UNCHANGED and swaps each item's
    /// headline for the whole turn behind it, so retrieval, ranking and slot count are identical and the
    /// only difference is how much of each found turn the reader sees. It therefore builds no engine and
    /// issues no second recall — see <c>docs/task-archive.md</c> Part 135 for why that isolation is the
    /// experiment: <c>evidence-hit@k</c> matches a <c>dia_id</c> that survives truncation, so the metric
    /// watching this stage cannot see the thing this arm varies.</para></summary>
    private const string FusedOneShot = "lyntai-fused";
    private const string FusedRehydrated = "lyntai-fused-full";
    // Arms whose walk continues past step 1. Only `ThreeShot` also publishes its step-2 snapshot as
    // `TwoShot` — nothing in `arms` names a fused two-shot form, so a member added here need not.
    private static readonly HashSet<string> MultiShotArms = [ThreeShot, FusedThreeShot];
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
        // `--composition` is docs/task-archive.md Part 135: what the fused walk's context is MADE of, not how well it
        // scores. It asks two questions the QA table's `chars/q` column averages away — how many of the 40
        // items are still a headline rather than full content, and whether any two of them carry the same
        // text — and it asks them of `lyntai-fused-3shot` and the size-matched `vector-40` together, because
        // a corpus with near-duplicate turns reaches both arms and only a DIFFERENCE is walk-specific.
        // Model-free and retrieval-side: no reader answers anything here, so neither can be credited.
        var composition = args.Contains("--composition");
        var needsReader = !args.Contains("--retrieval") && !shotsOnly && !ranksOnly && !composition;
        var chat = needsReader ? await SweepDoubles.TryRealChatAsync(http, "memory-locomo") : null;
        if (needsReader && chat is null) return 1;

        // The VERIFICATION judge is a different model role from the reader above, and it is wanted on the
        // model-free retrieval ladder where no reader is. D59 named a judge that PROMOTES before the cut as
        // this subsystem's fix and neither field benchmark had ever wired the seam; the ladder now prices it
        // against its own oracle ceiling.
        //
        // ADDITIVE and DISCLOSED, never a gate: the mechanical arms are model-free and run in full whether or
        // not a judge answers, and a run that silently dropped the arm would read as having measured it.
        var judgeChat = args.Contains("--retrieval") && !args.Contains("--no-judge")
            ? await SweepDoubles.TryRealChatAsync(http, "memory-locomo judge")
            : null;

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
            : composition
            ? [FusedThreeShot, $"vector-{ShotBudget}"]
            : shotsOnly
            ? ["shot-1", "shot-2", "shot-3", "vector", $"vector-{ShotBudget}", "full"]
            : retrievalOnly
                ? wantFull
                    ? ["lyntai", "+sem", "+sem+hop0", "+sem80", "+sem80+hop0", "+forget0", "+forget0+oracle",
                        .. judgeChat is null ? Array.Empty<string>() : ["+forget0+judge"],
                        "+sem+rel-only", "+sem+mult", "+sem80+mult", "+rel-only", "+sem+fuse", "+fuse",
                        "vector", "full"]
                    : ["lyntai", "+sem", "+sem+hop0", "+sem80", "+sem80+hop0", "+forget0", "+forget0+oracle",
                        .. judgeChat is null ? Array.Empty<string>() : ["+forget0+judge"],
                        "+sem+rel-only", "+sem+mult", "+sem80+mult", "+rel-only", "+sem+fuse", "+fuse",
                        "vector"]
                : ["lyntai", FusedOneShot, FusedRehydrated, FusedThreeShot, TwoShot, ThreeShot, "vector",
                    $"vector-{ShotBudget}", "full"];

        var (conversations, questions) = Load(path);
        var sampled = Stratify(questions, take);

        PrintPreamble(chat?.Model ?? "(none - retrieval only)", embedder,
            conversations.Count, questions.Count, sampled.Count, arms,
            retrievalOnly || shotsOnly || ranksOnly, composition);

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
        // `--composition`'s tallies, keyed by arm and — for the walk — by `<arm> shot-N`, so a per-shot row
        // and the final row come out of one pass.
        // The rehydration arm's own CONTROL. An item whose `(dia_id)` did not resolve silently falls back to
        // its headline, which would make the arm quietly partial — and a partial rehydration reads as a weak
        // RESULT rather than as a broken arm, the direction that gets published.
        var rehydrated = 0;
        var rehydrateMisses = 0;

        var comp = new Dictionary<string, CompositionTally>();
        CompositionTally Tally(string key) =>
            comp.TryGetValue(key, out var t) ? t : comp[key] = new CompositionTally();

        // Swap each returned item's (possibly truncated) text for the WHOLE turn behind it, keyed on the
        // `(dia_id)` the harness embeds. That marker surviving truncation is the finding this arm tests
        // (`docs/task-archive.md` Part 135) and is also what makes the swap possible at all.
        List<string> Rehydrate(IReadOnlyList<string> pieces, IReadOnlyDictionary<string, string> whole)
        {
            var full = new List<string>(pieces.Count);
            foreach (var piece in pieces)
            {
                rehydrated++;
                // The turn is `[{date}] ({dia_id}) {speaker}: {text}` and no date here carries a paren, so
                // the FIRST parenthesised run is the id.
                var open = piece.IndexOf('(', StringComparison.Ordinal);
                var close = open >= 0 ? piece.IndexOf(')', open + 1) : -1;
                var id = close > open ? piece[(open + 1)..close] : null;
                if (id is not null && whole.TryGetValue(id, out var turn)) full.Add(turn);
                else { rehydrateMisses++; full.Add(piece); }
            }
            return full;
        }

        // One WALK STEP's composition. `UpgradedCount` is the number Part 135 exists to count: a headline
        // raised to full content is what a shot buys beyond discovering a new entry, and it is invisible in
        // an item count.
        void Compose(string key, MemoryWalkStep step)
        {
            var t = Tally(key);
            t.Questions++;
            t.Items += step.Items.Count;
            t.NewItems += step.NewItems.Count;
            t.Upgraded += step.UpgradedCount;
            foreach (var item in step.Items)
                if (item.Content is null) { t.HeadlineItems++; t.HeadlineChars += item.Headline.Length; }
                else { t.ContentItems++; t.ContentChars += item.Content.Length; }
            // Priced the QA path's own way — joined, separators included — so a shot row and the arm row
            // below are the same quantity, and shot 3 agreeing with the arm is a consistency check rather
            // than a coincidence.
            t.PromptChars += step.Items.Sum(i => (i.Content ?? i.Headline).Length)
                + Math.Max(0, step.Items.Count - 1);
        }

        // One arm's FINAL set — what a reader would actually be handed. `items` is null for the cosine arm,
        // whose pieces are whole turns by construction and so carry no headline half.
        void Record(string key, IReadOnlyList<string> pieces, IReadOnlyList<MemoryItem>? items)
        {
            var t = Tally(key);
            t.Questions++;
            t.Items += pieces.Count;
            // The QA table's own column, computed the QA path's own way, so the control below compares like
            // with like rather than a reconstruction of it.
            t.PromptChars += string.Join("\n", pieces).Length;
            if (items is null)
            {
                t.ContentItems += pieces.Count;
                t.ContentChars += pieces.Sum(p => p.Length);
            }
            else
                foreach (var item in items)
                    if (item.Content is null) { t.HeadlineItems++; t.HeadlineChars += item.Headline.Length; }
                    else { t.ContentItems++; t.ContentChars += item.Content.Length; }

            var (exact, contained, near) = Duplication(pieces);
            t.ExactPairs += exact;
            t.ContainedPairs += contained;
            t.NearPairs += near;
        }
        // `--dump`'s per-item record. Collected in memory rather than streamed: 100 questions x 8 arms is
        // 800 lines, and a writer held open across the run is one more thing to get wrong on a throw.
        var dumped = new List<string>();

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

            // SemanticK is the vector CHANNEL's width, null for "leave it unregistered" — it cannot be a
            // prebuilt IMemorySeedSource, because the vector store one reads is created per arm below.
            (string Arm, GraphMemoryOptions? Options, IMemoryRankingPolicy? Ranking,
                IMemoryVerificationPolicy? Verification, int? SemanticK)[] configs = ranksOnly
                ? [("lyntai", null, rankProbe, null, null)]
                : composition
                // The SAME config the QA table's `lyntai-fused-3shot` row was measured with, copied rather
                // than re-derived: this mode explains that row's chars/q, so an arm differing from it in any
                // way would be explaining a different number. The control at the foot of the table asserts
                // the agreement instead of assuming it.
                ? [(FusedThreeShot, null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
                    {
                        RetrievabilityWeight = 0,
                        HopWeight = 0,
                    }), null, RecallLimit)]
                : retrievalOnly
                ? Ladder()
                : shotsOnly
                    // The explicit options object is INERT at floor 0 — the engine's own fallback is
                    // `options ?? new GraphMemoryOptions()` — so an unswept run is byte-identical to one
                    // that never passed one, which archive Part 119 measured rather than assumed.
                    ? [(ThreeShot, new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor }, null, null, null)]
                    // PRE-REGISTERED, 2026-09-01, before the first QA run of the fused arm.
                    //
                    // The retrieval half moved the best mechanical arm 63.5% -> 83.0% (D103,
                    // docs/memory.md §5). This arm asks the only question that follows: does that
                    // reach the READER, or does a 28.5-point retrieval gain wash out in answering?
                    //
                    // Prediction: PARTIAL conversion. gemma3:4b is the bottleneck on multi-hop and
                    // open-domain, so a positive but sub-linear delta is expected.
                    //   positive, sub-linear  => retrieval reaches the reader; the ceiling is the reader.
                    //   positive, ~linear     => the reader was never the bottleneck; retrieval was.
                    //   flat                  => the reader cannot use the extra evidence, so
                    //                            retrieval gains stop at this tier and a better
                    //                            reader is the only way to bank them.
                    //   NEGATIVE              => treat as an INSTRUMENT problem to find, not a
                    //                            finding. Extra correct evidence cannot make a
                    //                            reader worse; a drop means the arm differs from
                    //                            `lyntai` in some way beyond its ranking config.
                    //
                    // ABSOLUTE VALUES ARE NOT COMPARABLE TO ANY PUBLISHED NUMBER. They are a
                    // property of a 4B local reader. Only the ARM DIFFERENCE transfers -- TASKS.md
                    // Part 109 says exactly this. Do not place either figure beside a third party's.

                    // PRE-REGISTERED, 2026-09-01, before the first run of the fused multi-shot arm.
                    //
                    // `lyntai-fused` converted the retrieval gain (+6.9 token-F1 at equal context)
                    // but sits at 29.1% against `vector`'s 45.7% while BEATING it on evidence-hit.
                    // It also answered "the excerpts contained none" 18 times against vector's 5,
                    // on better evidence. The hypothesis: the engine returns HEADLINES (D100) and
                    // the reader cannot answer from a pointer. Expansion is what fetches content.
                    //
                    // Prediction: a MODEST improvement over `lyntai-fused`, still well below
                    // `vector` at matched context. Grounds: multi-shot on the SHIPPED ranking
                    // bought only +2.7 token-F1 (22.2 -> 24.9) for 2.2x the context, so expansion
                    // is already measured as expensive and weak; better seeds should help it but
                    // are unlikely to change its character.
                    //   near `vector` at matched chars => the headline hypothesis is RIGHT and
                    //                                     expansion is the fix. Biggest result.
                    //   modest gain, still trailing    => headlines are part of it, not all of it.
                    //   flat                           => expansion does not fetch what the reader
                    //                                     needs; the gap is the headline CONTENT.
                    //   NEGATIVE                       => instrument problem to find, not a
                    //                                     finding. More evidence cannot hurt.
                    //
                    // Absolute values are a property of a 4B local reader. Only the arm DIFFERENCE
                    // transfers (TASKS.md Part 109). Compare against `vector-40`, the size-matched
                    // control, not against `vector` alone.
                    : [("lyntai", null, null, null, null),
                        ("lyntai-fused", null,
                            new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
                            {
                                RetrievabilityWeight = 0,
                                HopWeight = 0,
                            }), null, RecallLimit),
                        (FusedThreeShot, null,
                            new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
                            {
                                RetrievabilityWeight = 0,
                                HopWeight = 0,
                            }), null, RecallLimit),
                        (ThreeShot, null, null, null, null)];

            (string Arm, GraphMemoryOptions? Options, IMemoryRankingPolicy? Ranking,
                IMemoryVerificationPolicy? Verification, int? SemanticK)[] Ladder() =>
            [
                ("lyntai", null, null, null, null),
                ("+sem", null, null, null, RecallLimit),
                ("+sem+hop0", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { HopWeight = 0 }), null,
                    RecallLimit),
                ("+sem80", null, null, null, 80),
                ("+sem80+hop0", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { HopWeight = 0 }), null, 80),

                // Added 2026-08-31 to SPLIT the -26 gap against `vector`, which the ladder above could not:
                // every arm in it keeps RetrievabilityWeight at its shipped 1, so forgetting votes on the
                // ordering in all of them and none of them can say whether it is the deficit.
                //
                // `+forget0` removes exactly that vote. LoCoMo asks about months of history UNIFORMLY, so it
                // rewards a perfect archive and penalises forgetting BY CONSTRUCTION — this arm prices how
                // much of the gap is that construction rather than a ranking defect.
                ("+forget0", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions { RetrievabilityWeight = 0 }),
                    null, null),

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
                    new EvidenceOracleVerifier(mine.ToDictionary(q => q.Text, q => q.Evidence.ToHashSet())),
                    null),

                // The REAL judge, beside its own ceiling. `+oracle` says how much promotion could recover;
                // this says how much a model actually does, and the gap between them is the model's error
                // rather than the mechanism's limit. Skipped when no chat model answers — the arm measures
                // what a MODEL is worth, so a scripted stand-in would measure the stand-in.
                .. judgeChat is null
                    ? Array.Empty<(string, GraphMemoryOptions?, IMemoryRankingPolicy?, IMemoryVerificationPolicy?,
                        int?)>()
                    : [("+forget0+judge", null,
                        new ReciprocalRankFusionPolicy(
                            new ReciprocalRankFusionOptions { RetrievabilityWeight = 0 }),
                        new LlmMemoryVerificationPolicy(new BenchClientFactory(judgeChat)), null)],

                // The FORMULA arms (2026-08-31): they test what makes the judge an add-on rather than a
                // rescue. `vector` is PURE tier-2 — one embedder, cosine, no graph, no judge — and scores
                // 80.5%, above this engine WITH a perfect judge at 77.5%. A plain formula beating
                // formula-plus-oracle puts the deficit in the formula tier, which is the shape
                // `model-decoupling.md` warns about: a model becoming the floor rather than the ceiling.
                //
                // Two mechanisms are suspected and each arm isolates one.
                //   - the vector CHANNEL is not registered by default, so the shipped engine embeds every write and then
                //     retrieves lexically, consulting none of it on read. `+sem` was worth only 3 points
                //     before per-source fusion; it now reads 76.5% (`docs/DECISIONS.md` D103).
                //   - RRF fuses RANKS (D82, "ranks by COMPETITION"), so a cosine of 0.742 against 0.713
                //     collapses to "1st against 8th" and the size of the difference — the whole signal on a
                //     semantic-matching workload — is discarded by construction. That is what RRF IS, not a
                //     defect in it; the question is whether it is the right fuser HERE.
                //
                // `MultiplicativeRankingPolicy` preserves magnitude, ships, and `CLAUDE.md` calls it one
                // line to restore. It has never been run against this benchmark.
                // THE arm that isolates the mixed-scale hypothesis (2026-08-31). The pool holds cosine's
                // top-20 and every other vote is off, so relevance alone orders it — a combination the
                // measured arms leave untested (`+rel-only` has no semantic seeds at 60.0%, `+sem+hop0`
                // keeps retrievability at 31.5%, `+sem` has every weight on at 76.5%).
                //
                // Reaching ~80% means the other WEIGHTS diluted a good ordering. Staying near 60% means the
                // defect is RELEVANCE ITSELF — a lexical hit carries a rank POSITION and a semantic seed a
                // COSINE, and no weighting of one field repairs two scales sharing it. `TASKS.md` Part 128.
                ("+sem+rel-only", null,
                    new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
                    {
                        RetrievabilityWeight = 0,
                        HopWeight = 0,
                    }), null, RecallLimit),

                ("+sem+mult", null, new MultiplicativeRankingPolicy(), null, RecallLimit),
                ("+sem80+mult", null, new MultiplicativeRankingPolicy(), null, 80),

                // The BRIDGE arm, and the one that decides where to look next. Relevance alone through the
                // engine's own pipeline (salience already ships at 0, D89), so it scores the same quantity
                // `vector` does and differs only in WHICH CANDIDATES reached the ranker.
                //   - lands near `vector`  => the pool is fine and the WEIGHTS carry the gap;
                //   - stays far below      => the pool/seeding is the deficit and no weight can fix it.
                // Part 113 established the pool CONTAINS the evidence; it did not establish at what depth,
                // and those two readings need different work.
                ("+rel-only", null, new ReciprocalRankFusionPolicy(
                    new ReciprocalRankFusionOptions { RetrievabilityWeight = 0, HopWeight = 0 }), null, null),

                // PRE-REGISTERED, 2026-08-31, before the first run. Spec §2.5.
                //
                // The relevance term's magnitude now scales with how many sources matched a
                // candidate, so at RelevanceWeight 1 against RetrievabilityWeight 1 the effective
                // weight shifts toward relevance as sources are turned on. NOT pre-corrected.
                //
                //   `+sem+fuse` clears 63.5% and `+fuse` does NOT move
                //       => the gain is per-source fusion, which is the hypothesis.
                //   BOTH move
                //       => at least part of it is the weight shift, and the honest next step is
                //          a RelevanceWeight ladder rather than adopting anything.
                //   Neither moves
                //       => mixed scale was real (63.5% proved that) but repairing it is not
                //          sufficient, and the deficit is the POOL rather than the ordering.
                ("+sem+fuse", null, null, null, RecallLimit),
                ("+fuse", null, null, null, null),
            ];

            // The dia_id rides along in the CONTENT so an evidence hit is checkable without a model. It is
            // in the text every arm reads, so none is advantaged.
            var texts = turns.Select(t => $"[{t.Date}] ({t.DiaId}) {t.Speaker}: {t.Text}").ToList();
            var vectorIndex = new List<(string Text, float[] Vector)>();
            foreach (var text in texts) vectorIndex.Add((text, await embedder.EmbedAsync(text)));

            // dia_id -> the WHOLE turn, for the rehydration arm. Built by INDEX against `texts` rather than
            // by projecting `turns` again, so the two can never disagree about what a turn's text is.
            var byDiaId = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < turns.Count; i++) byDiaId[turns[i].DiaId] = texts[i];

            // Warm the QUERY embeddings before anything is timed. The embedder cache is shared across arms,
            // so whichever arm ran first would otherwise pay for every embed and the ms column would be
            // measuring arm ORDER rather than retrieval work.
            if (shotsOnly) foreach (var q in mine) await embedder.EmbedAsync(q.Text);

            var recalled = new Dictionary<(string Arm, string Question), List<string>>();
            foreach (var (arm, options, ranking, verification, semanticK) in configs)
            {
                // INGESTED ONCE into a template nothing reads, then CLONED per question. A recall reinforces
                // what it returned and ExpandAsync reinforces what it walks, so questions sharing one store
                // are not independent trials — `shot-1` moved 30.0% to 28.0% between two runs differing only
                // in how much a LATER shot expanded. Copying a migrated file is what makes a per-question
                // store affordable where re-ingesting every question is not.
                using var template = new MemoryPolicySweep.SweepDb();
                var vectors = new InMemoryVectorStore();

                // null leaves the engine on its own default pair (lexical + subject), which is the shipped
                // wiring this benchmark measures; a width registers the vector channel BESIDE them. `+fuse`
                // is the exception: it isolates the lexical channel ALONE, so no candidate can ever carry
                // more than one source's rank and the per-source fusion under test has nothing to fuse.
                IMemorySeedSource[]? seeds = arm == "+fuse"
                    ? [new LexicalSeedSource()]
                    : semanticK is { } k
                        ? [new LexicalSeedSource(), new SubjectSeedSource(),
                            new SemanticSeedSource(embedder, vectors, new SemanticSeedOptions { K = k })]
                        : null;

                var ingest = new GraphMemoryEngine("locomo",
                    new SqliteMemoryGraphStore(template.Factory), options: options,
                    embedder: embedder, vectors: vectors, ranking: ranking, verification: verification,
                    seedSources: seeds);

                foreach (var text in texts)
                    await ingest.RememberAsync(new MemoryWrite(convId, "session", text));

                // The vector store is deliberately SHARED across questions: RememberAsync is the only thing
                // that writes it, so it is read-only once ingestion is done, and re-embedding per question
                // would cost more than the rest of the study. The graph store is the one that mutates on
                // READ, and it is the one being cloned.
                GraphMemoryEngine Fresh(MemoryPolicySweep.SweepDb clone) =>
                    new("locomo", new SqliteMemoryGraphStore(clone.Factory), options: options,
                        embedder: embedder, vectors: vectors, ranking: ranking, verification: verification,
                        seedSources: seeds);

                // CONTROL, added for TASKS.md Part 109: a semantic width of 20 moved evidence-hit by 0.0
                // points and the read path looks correct on inspection, so the question is whether the vectors
                // are there and whether a search finds them. Reading it back from what actually ran is the only
                // way to tell a channel that does nothing from a channel that is never reached.
                if (semanticK > 0 && !probed)
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
                    MemoryWalkStep? last = null;

                    await foreach (var step in engine.WalkAsync(
                        new MemoryQuery(convId, "session", q.Text, Limit: RecallLimit), walkOptions))
                    {
                        context = [.. step.Items.Select(i => i.Content ?? i.Headline)];
                        last = step;
                        if (composition) Compose($"{arm} shot-{step.Number}", step);

                        // single-shot arms take step 1 and stop; MultiShotArms walks on
                        if (!MultiShotArms.Contains(arm))
                        {
                            recalled[(arm, q.Text)] = context;
                            break;
                        }

                        reached = step.Number;
                        Snapshot(step.Number, context);
                        if (!shotsOnly && step.Number == 2 && arm == ThreeShot)
                            recalled[(TwoShot, q.Text)] = context;
                        if (!shotsOnly && step.Number == 3)
                            recalled[(arm, q.Text)] = context;
                        if (step.Number >= 3) break;
                    }

                    // A walk ENDS when a step moves nothing, where the pre-surface loop ran shots 2 and 3
                    // unconditionally and re-snapshotted the unchanged context. Filling the gap keeps that:
                    // dropping a row would change the DENOMINATOR of every rate below rather than the
                    // retrieval, which is a harness difference wearing a result's clothes.
                    if (MultiShotArms.Contains(arm))
                        for (var shot = reached + 1; shot <= 3; shot++)
                        {
                            Snapshot(shot, context);
                            // Same reason as the row above, applied to composition: a shot that did not
                            // happen still holds what the last one left, and it moved nothing — so the held
                            // set repeats with the DELTAS zeroed rather than the row being dropped.
                            if (composition && last is not null)
                                Compose($"{arm} shot-{shot}",
                                    new MemoryWalkStep(shot, last.Items, [], 0, last.Ran));
                            if (!shotsOnly && shot == 2 && arm == ThreeShot)
                                recalled[(TwoShot, q.Text)] = context;
                            if (!shotsOnly && shot == 3)
                                recalled[(arm, q.Text)] = context;
                        }

                    // The FINAL set is what the reader was handed, so its duplication is the number Part 135
                    // asks for. Recorded under the arm's own name, beside the per-shot rows above.
                    if (composition) Record(arm, context, last?.Items);

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

            if (composition)
            {
                // The SIZE-MATCHED control, composed the same way. Its pieces are whole turns, so its
                // headline column is 0 by construction and its duplication is the CORPUS's own — which is
                // the entire reason it is measured rather than assumed: a LoCoMo conversation that repeats
                // itself would give the walk near-duplicate items through no fault of the walk.
                foreach (var q in mine)
                    Record($"vector-{ShotBudget}",
                        (await TopKAsync(embedder, vectorIndex, q.Text, ShotBudget)).ToList(), null);

                Console.WriteLine($"  {convId}: {turns.Count} turns ingested, "
                    + $"{mine.Count} question(s) composed");
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
                        // Reuses the one-shot fused arm's OWN returned set — not a second recall — so
                        // retrieval, ranking and slot count are held exactly and only the text differs.
                        _ when arm == FusedRehydrated =>
                            Rehydrate(recalled[(FusedOneShot, q.Text)], byDiaId),
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
                        // Dumped too: an "unknown" is the reader refusing, which is the case a calibration
                        // most wants to see, and omitting it would make the dump disagree with the table.
                        if (dump)
                            dumped.Add(JsonSerializer.Serialize(new
                            {
                                arm, question = q.Text, gold = q.Gold, answer = hypothesis,
                                unknown = true, f1 = 0.0, exact = false, judge = (bool?)null,
                            }));
                        continue;
                    }

                    // The MODEL-FREE grade is primary and the judge sits beside it, not above it. A model is
                    // not better at exact comparison (`model-decoupling.md`), and this judge is the same 4B
                    // model that wrote the answer — so token-F1 against the gold string is the number to
                    // trust, and the judge's job is to catch a right answer worded differently.
                    var itemF1 = TokenF1(hypothesis, q.Gold);
                    var itemExact = Normalize(hypothesis).SequenceEqual(Normalize(q.Gold));
                    // The verdict is captured rather than tested inline, because `--dump` needs to record
                    // WHAT the judge said, not only that it agreed. Null means the judge never ran.
                    bool? verdict = judged ? await JudgeAsync(chat, q, hypothesis) : null;

                    f1[key] = f1.GetValueOrDefault(key) + itemF1;
                    if (itemExact) exact[key] = exact.GetValueOrDefault(key) + 1;
                    if (verdict is true) correct[key] = correct.GetValueOrDefault(key) + 1;
                    if (dump)
                        dumped.Add(JsonSerializer.Serialize(new
                        {
                            arm, question = q.Text, gold = q.Gold, answer = hypothesis,
                            unknown = false, f1 = itemF1, exact = itemExact, judge = verdict,
                        }));
                }
            }

            Console.WriteLine($"  {convId}: {turns.Count} turns ingested, {mine.Count} question(s) asked");
        }

        if (ranksOnly) PrintRanks(rankProbe);
        else if (composition) PrintComposition(comp);
        else if (shotsOnly) PrintShots(arms, correct, asked, returned, chars, millis);
        else PrintResults(arms, correct, asked, unknown, returned, retrievalOnly, f1, exact, chars, judged);

        // `--dump` was parsed and read by NOTHING until 2026-09-01 — a flag the usage advertised and the
        // run silently ignored. It survived because `var dump = args.Contains(...)` assigns a method
        // result, and C# only warns on an unused CONSTANT. It writes the per-item record the aggregate
        // table cannot carry: the answer, the gold, and what the judge said about them. The judge here is
        // the same model that wrote the answer, so calibrating it against a stronger one needs the items.
        if (dump)
        {
            // Says so rather than writing an empty file: the QA path is the only one that produces an
            // answer to dump, so `--retrieval --dump` has nothing to record. Reporting that is the whole
            // lesson of the flag having been dead — a silent no-op reads exactly like a working one.
            if (dumped.Count == 0)
                Console.WriteLine("\n  --dump: nothing to write. Only the QA path produces answers; "
                    + "`--retrieval`, `--shots` and `--ranks` have no reader.");
            else
            {
                var dumpPath = Path.Combine("devtools", $"_locomo-dump-{DateTime.Now:HHmmss}.jsonl");
                await File.WriteAllLinesAsync(dumpPath, dumped);
                Console.WriteLine($"\n  --dump: {dumped.Count} item(s) → {dumpPath}");
            }
        }
        // Reported whenever the arm ran, and reported even at zero: a rehydration that silently fell back
        // to headlines would present as "full content did not help", which is the opposite conclusion.
        if (rehydrated > 0)
            Console.WriteLine($"\n  CONTROL {FusedRehydrated}: {rehydrated - rehydrateMisses} of {rehydrated} "
                + $"item(s) rehydrated to the whole turn"
                + (rehydrateMisses > 0
                    ? $" - {rehydrateMisses} MISS(ES); the arm is partial and its result is not readable."
                    : " - no misses, so the arm differs from its source ONLY in truncation."));

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
        int conversations, int total, int sampled, IReadOnlyList<string> arms, bool retrievalOnly,
        bool composition)
    {
        // Its own header rather than the model-free one below: this mode computes no evidence-hit at all,
        // and a raw output whose banner names a metric it never measured is the provenance defect this
        // repository files under precision-without-provenance.
        if (composition)
        {
            Console.WriteLine("=== LoCoMo: what the returned CONTEXT is made of (task-archive Part 135) ===");
            Console.WriteLine();
            Console.WriteLine("NOT an accuracy measurement. No reader, no judge, and no evidence-hit column:");
            Console.WriteLine("this decomposes a cost the QA table already reported - headline text against");
            Console.WriteLine("expanded content, and whether any two returned items carry the same text.");
            Console.WriteLine();
            Console.WriteLine($"Conversations: {conversations}   Questions: {sampled} of {total}, seeded {Seed}");
            Console.WriteLine($"Arms: {string.Join(", ", arms)}   recall limit {RecallLimit}   "
                + $"seeds/step {expandSeeds}   embedder {SweepDoubles.Model}");
            Console.WriteLine();
            Console.WriteLine("`seeds/step` MUST match the run being explained - the QA table it decomposes was");
            Console.WriteLine("taken at 16, and this mode defaults to 3. The control at the foot checks it.");
            Console.WriteLine();
            return;
        }

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

    /// <summary>What one arm's returned set is MADE of, accumulated across questions. Every field is a
    /// running total; the print divides by <see cref="Questions"/>.</summary>
    private sealed class CompositionTally
    {
        public long Questions;
        public long Items;
        public long NewItems;
        /// <summary>Headlines raised to full content — <see cref="MemoryWalkStep.UpgradedCount"/> summed.
        /// It is what a shot buys beyond DISCOVERING an entry, and an item count cannot see it.</summary>
        public long Upgraded;
        public long HeadlineItems;
        public long HeadlineChars;
        public long ContentItems;
        public long ContentChars;
        /// <summary>The QA path's own <c>chars/q</c>: the joined prompt context, separators included.</summary>
        public long PromptChars;
        public long ExactPairs;
        public long ContainedPairs;
        public long NearPairs;
    }

    /// <summary>Pairwise overlap WITHIN one returned set. Three nested counts: identical texts, one text
    /// contained in another (the shape a headline and a longer turn take), and near-duplicates by token
    /// Jaccard. Counted as PAIRS, so a set holding three copies of one turn reports 3 rather than 1.</summary>
    private static (int Exact, int Contained, int Near) Duplication(IReadOnlyList<string> pieces)
    {
        const double NearThreshold = 0.8;
        var tokens = pieces.Select(Tokens).ToList();
        int exact = 0, contained = 0, near = 0;
        for (var i = 0; i < pieces.Count; i++)
            for (var j = i + 1; j < pieces.Count; j++)
            {
                // Counted into all three so the columns NEST: an identical pair is also a contained one and
                // also a near one, and a reader comparing the columns should not have to add them back up.
                if (string.Equals(pieces[i], pieces[j], StringComparison.Ordinal))
                {
                    exact++; contained++; near++;
                    continue;
                }
                if (pieces[i].Contains(pieces[j], StringComparison.Ordinal)
                    || pieces[j].Contains(pieces[i], StringComparison.Ordinal)) contained++;
                if (Jaccard(tokens[i], tokens[j]) >= NearThreshold) near++;
            }
        return (exact, contained, near);
    }

    /// <summary>POSITIVE CONTROL for <see cref="Duplication"/>, printed rather than trusted. Both arms
    /// report ZERO duplication, and a counter that could only ever return zero would look exactly the same —
    /// so the function is run against a set whose answer is known, and the table is withheld if it disagrees.
    /// </summary>
    private static (bool Ok, string Detail) DuplicationSelfCheck()
    {
        const string sentence = "the quick brown fox jumps over the lazy dog";
        string[] probe =
        [
            sentence,
            sentence,                                       // exact
            "the quick brown fox jumps over the lazy",      // a prefix: contained, and Jaccard 7/8
            "unrelated text about submarines",
        ];
        var (exact, contained, near) = Duplication(probe);
        return (exact == 1 && contained == 3 && near == 3,
            $"exact {exact}/1, contained {contained}/3, near {near}/3");
    }

    private static HashSet<string> Tokens(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())];

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count <= b.Count ? a.Count(b.Contains) : b.Count(a.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    /// <summary>What the fused walk's context is MADE of (`docs/task-archive.md` Part 135). No accuracy column: this
    /// mode explains a cost the QA table already reported and makes no claim about what that cost buys.
    /// </summary>
    private static void PrintComposition(Dictionary<string, CompositionTally> comp)
    {
        Console.WriteLine();
        Console.WriteLine("=== What the returned context is MADE of (no model in the loop) ===");
        Console.WriteLine();
        Console.WriteLine("A recall returns HEADLINES and an expansion upgrades them to full content, so a");
        Console.WriteLine("multi-shot arm's chars/q is a MIXTURE. The `upgraded` column counts that raise");
        Console.WriteLine("directly rather than inferring it from mean lengths.");
        Console.WriteLine();
        Console.WriteLine($"{"",-24} {"",7} {"",5} {"",5} {"----- headline -----",22} "
            + $"{"----- content -----",22} {"",8}");
        Console.WriteLine($"{"Arm / shot",-24} {"items/q",7} {"new",5} {"upgr",5} "
            + $"{"n",6} {"chars",8} {"avg",6} {"n",6} {"chars",8} {"avg",6} {"chars/q",8}");
        Console.WriteLine(new string('-', 110));

        foreach (var (key, t) in comp.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (t.Questions == 0) continue;
            double Per(long v) => (double)v / t.Questions;
            // The two AVERAGES are the point of this table: a headline is capped at HeadlineChars while a
            // content item is a whole turn, so the arm's chars/item is a mixture of two populations and
            // only their separate means say which one moved.
            var headlineAvg = t.HeadlineItems > 0 ? (double)t.HeadlineChars / t.HeadlineItems : 0;
            var contentAvg = t.ContentItems > 0 ? (double)t.ContentChars / t.ContentItems : 0;
            Console.WriteLine($"{key,-24} {Per(t.Items),7:F1} {Per(t.NewItems),5:F1} {Per(t.Upgraded),5:F1} "
                + $"{Per(t.HeadlineItems),6:F1} {Per(t.HeadlineChars),8:F0} {headlineAvg,6:F1} "
                + $"{Per(t.ContentItems),6:F1} {Per(t.ContentChars),8:F0} {contentAvg,6:F1} "
                + $"{Per(t.PromptChars),8:F0}");
        }

        Console.WriteLine();
        Console.WriteLine("Duplication WITHIN one question's returned set, as pairs per question. The counts");
        Console.WriteLine("nest: every exact pair is also contained and also near.");
        Console.WriteLine();

        var (controlOk, controlDetail) = DuplicationSelfCheck();
        Console.WriteLine($"  POSITIVE CONTROL on a known-duplicate set: "
            + $"{(controlOk ? "PASS" : "FAIL")} - {controlDetail}");
        if (!controlOk)
        {
            // Refuses to print rather than printing zeroes, because a broken counter and a clean corpus
            // produce the same table and only this line can tell them apart.
            Console.WriteLine("  Table WITHHELD: the counter does not detect duplication it was handed, so");
            Console.WriteLine("  a zero below would say nothing about the arms.");
            return;
        }
        Console.WriteLine();

        Console.WriteLine($"{"Arm",-26} {"exact",8} {"contained",11} {"near>=0.8",11} {"% items near-dup",17}");
        Console.WriteLine(new string('-', 78));
        foreach (var (key, t) in comp.Where(e => !e.Key.Contains(" shot-", StringComparison.Ordinal))
            .OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (t.Questions == 0) continue;
            // A pair count over an item count is the share of slots that COULD be freed if one member of
            // every near pair were dropped — an upper bound on what deduplication is worth, not a defect.
            var share = t.Items > 0 ? 100.0 * t.NearPairs / t.Items : 0;
            Console.WriteLine($"{key,-26} {(double)t.ExactPairs / t.Questions,8:F2} "
                + $"{(double)t.ContainedPairs / t.Questions,11:F2} {(double)t.NearPairs / t.Questions,11:F2} "
                + $"{share,16:F1}%");
        }
        Console.WriteLine();
        Console.WriteLine("CONTROL: the `chars/q` and `items/q` on the two ARM rows above must reproduce the");
        Console.WriteLine("QA table this mode explains. A mode that quietly measured a different arm would");
        Console.WriteLine("still print a plausible composition, so compare them before reading anything else.");
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

    /// <summary>
    /// Routes the SHIPPED <c>LlmMemoryVerificationPolicy</c> at this machine's local chat model.
    ///
    /// <para><b>Deliberately not a judge written here.</b> The question is what the seam a deployment would
    /// actually switch on is worth — so the arm has to exercise the shipped prompt, the shipped parsing, the
    /// shipped depth handling and the shipped fail-open behaviour. A bench-local judge would measure a prompt
    /// invented for the bench, and would flatter or damn the feature for reasons no consumer inherits.</para>
    ///
    /// <para>Both members return the same client because the bench registers exactly one; a policy asking for
    /// a NAMED client gets it rather than a <c>KeyNotFoundException</c>, which would fail the arm open and
    /// look like a judge that endorsed nothing.</para>
    /// </summary>
    private sealed class BenchClientFactory(SweepDoubles.OpenAiCompatibleChat chat) : ILlmClientFactory
    {
        private readonly BenchClient _client = new(chat);

        public ILlmClient Get(string name) => _client;

        public ILlmClient Get() => _client;

        public bool TryGet(string name, out ILlmClient client)
        {
            client = _client;
            return true;
        }

        public IReadOnlyList<string> Names => ["bench"];

        private sealed class BenchClient(SweepDoubles.OpenAiCompatibleChat chat) : ILlmClient
        {
            public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
            {
                // The judge's whole prompt is one user turn; join defensively in case that changes, rather
                // than indexing [0] and silently dropping a system message the policy started sending.
                var prompt = string.Join("\n", req.Messages.Select(m => m.Content));

                // Room for a list of ids. The policy's own parser decides what a valid answer is; a cap so
                // tight that a correct answer is truncated would be measured as the judge being wrong.
                var text = await chat.AskAsync(prompt, ct, maxTokens: 256).ConfigureAwait(false);

                return text is null
                    ? new LlmReply("", LlmVerdict.Failed, Detail: "bench chat returned nothing")
                    : new LlmReply(text, LlmVerdict.Ok);
            }

            /// <summary>The verification policy never streams — it asks one bounded question and parses the
            /// whole answer. Throwing rather than returning an empty sequence is deliberate: a silent empty
            /// stream would let a future caller believe it had read something.</summary>
            public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
                throw new NotSupportedException(
                    "the bench client backs a verification judge, which does not stream");
        }
    }
}
