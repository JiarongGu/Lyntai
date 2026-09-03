using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <b>LongMemEval's knowledge-update class — the benchmark where forgetting is supposed to HELP.</b>
///
/// <para>LoCoMo distributes its questions uniformly over months of history, so it rewards a perfect archive
/// and penalises decay by construction (<c>docs/memory.md</c> §5). This is the opposite shape. A
/// knowledge-update question carries exactly TWO dated sessions: an earlier one stating a fact and a later
/// one REVISING it. The turns that carry each are flagged, so the question this asks is not "can you find
/// it" but <b>"do you prefer the CURRENT value over the superseded one"</b> — which is the claim a decay
/// model actually makes, and one a flat cosine index has no mechanism to make at all.</para>
///
/// <para><b>The headline metric is preference, not recall.</b> Both facts are textually similar and both sit
/// in the store; an archive returns whichever the embedder likes. Scoring "did you retrieve the answer"
/// would hide that. So the arms are scored on whether the CURRENT turn outranks the STALE one, with plain
/// hit-rates beside it so a preference win cannot be manufactured by retrieving neither.</para>
///
/// <para>Model-free throughout, for the reason the LoCoMo harness gives: no reader and no judge, so neither
/// can be credited or blamed.</para>
///
/// <para><b>Two variants, and the difference between them is the measurement.</b> The oracle file carries
/// only the evidence sessions — two to six per question — so decay has almost nothing to bury.
/// <c>--haystack</c> puts the same questions among ~490 turns of distractors. The question ids are identical
/// in both files, so a seeded <c>--n</c> sample selects the same questions in each, and the digest printed in
/// the preamble is what proves a pair of runs is paired.</para>
/// </summary>
internal static class MemoryLongMemEvalBench
{
    internal const string DataVariable = "LYNTAI_LME_PATH";
    internal const string HaystackVariable = "LYNTAI_LME_S_PATH";
    private const int RecallLimit = 10;

    /// <summary>The multi-shot walk's two bounds, at CLASS scope because both shot curves and the walk they
    /// share read them — a per-method copy is how "shot 2" starts meaning two different things.</summary>
    private const int ShotBudget = 2 * RecallLimit;
    private const int ExpandSeeds = 3;
    private const int DefaultSeed = 20260829;
    private const string Task = "lme";
    private const string Scope = "session";

    /// <summary>The plain-cosine control. Not a <see cref="FieldArm"/> — it never touches the graph store,
    /// which is exactly what makes it the arm that cannot move when the engine changes.</summary>
    private const string VectorArm = "vector";

    /// <summary>Which engine arms a run scores, from <c>--arms</c>.
    ///
    /// <para><b>The default is the PUBLISHED pair</b> — <c>lyntai</c> and <c>vector</c> — so every figure on
    /// record reproduces from the command that produced it, and the ladder is opt-in. That is not timidity
    /// about defaults: an arm costs a full ingestion per question, and under <c>--haystack</c> that is ~490
    /// turns each, so a silently-widened default would multiply the cost of every existing invocation.</para>
    ///
    /// <para>Arms come from <see cref="FieldArms"/>, so a name means the same configuration here as it does
    /// on the LoCoMo bench — which is the whole point of running this ladder at all.</para></summary>
    /// <returns>The selected arms, or <c>null</c> when a name is not an arm — after saying so. A typo must
    /// not quietly measure fewer arms and still print a table that looks complete.</returns>
    private static FieldArm[]? SelectConfigs(string[] args, out bool cosine)
    {
        cosine = true;
        if (ArgValue(args, "--arms") is not { } wanted) return [FieldArms.Shipped()];

        var requested = wanted.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(a => a != VectorArm && !FieldArms.Has(a)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"--arms: unknown arm(s) {string.Join(", ", unknown)}. This mode runs: "
                + $"{string.Join(", ", FieldArms.All().Select(a => a.Name).Append(VectorArm))}");
            return null;
        }

        cosine = requested.Contains(VectorArm, StringComparer.Ordinal);
        return [.. FieldArms.All().Where(a => requested.Contains(a.Name, StringComparer.Ordinal))];
    }

    /// <summary>Narrows a SNAPSHOT mode's own arm names by <c>--arms</c>.
    ///
    /// <para><c>--shots</c> names steps of one walk rather than configurations, so this changes what is
    /// REPORTED and cannot change what is computed — unlike <see cref="SelectConfigs"/>, which also decides
    /// what is ingested. The asymmetry is the LoCoMo bench's and is kept deliberately; what is NOT kept is
    /// letting the flag be accepted and ignored, which is the silent no-op every other path here
    /// refuses.</para></summary>
    /// <returns>The names to report, or <c>null</c> after saying which name is not one of them.</returns>
    private static string[]? FilterArms(string[] args, string[] arms)
    {
        if (ArgValue(args, "--arms") is not { } wanted) return arms;

        var requested = wanted.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(a => !arms.Contains(a, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"--arms: unknown arm(s) {string.Join(", ", unknown)}. "
                + $"This mode runs: {string.Join(", ", arms)}");
            return null;
        }

        return [.. arms.Where(a => requested.Contains(a, StringComparer.Ordinal))];
    }

    /// <summary>The deep page a recovery probe asks for, well past any caller's limit. It is not a second
    /// opinion on ranking — it is how "the ranking put it below the cut" is told from "the entry is gone",
    /// which is the only distinction <c>docs/DECISIONS.md</c> D41 makes.</summary>
    private const int DeepLimit = 100;

    /// <summary>Does suppression DELETE? The measurement <c>docs/DECISIONS.md</c> D41 has never had.
    ///
    /// <para>Scored only over the questions where the arm actually suppressed the superseded fact, because
    /// those are the only ones where the question arises — an entry the recall returned was never buried,
    /// and counting it as "recovered" would inflate every row toward 100% by construction.</para>
    ///
    /// <para><b>The focused query is the entry's OWN WORDS with its marker stripped</b>, so recovery cannot
    /// come from matching the harness's <c>(sNtM)</c> tag, which is a token no real caller would type. It is
    /// deliberately the easiest honest query — this is a floor test for reachability, not a difficulty
    /// test.</para></summary>
    private static async Task<int> RunRecoveryAsync(IReadOnlyList<Question> sampled,
        SweepDoubles.CachingEmbedder embedder, string[] args)
    {
        var configs = SelectConfigs(args, out _);
        if (configs is null) return 1;

        var buried = new Dictionary<string, int>(StringComparer.Ordinal);
        var byQuery = new Dictionary<string, int>(StringComparer.Ordinal);
        var byWalk = new Dictionary<string, int>(StringComparer.Ordinal);
        var byEither = new Dictionary<string, int>(StringComparer.Ordinal);
        var atTop = new Dictionary<string, int>(StringComparer.Ordinal);
        var byDeep = new Dictionary<string, int>(StringComparer.Ordinal);
        var deepRankSum = new Dictionary<string, int>(StringComparer.Ordinal);

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            if (q.Stale.Count == 0) continue;

            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, embedder);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                var page = (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                if (FirstIndexOf(page, q.Stale) >= 0) continue;   // never buried; nothing to recover

                buried[arm.Name] = buried.GetValueOrDefault(arm.Name) + 1;

                // (a) A MORE FOCUSED QUERY — the buried entry's own content, marker removed.
                var stale = q.Stale[0];
                var focused = (await engine.RecallAsync(
                        new MemoryQuery(Task, Scope, stale.Content, Limit: RecallLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                var rank = FirstIndexOf(focused, q.Stale);
                if (rank >= 0)
                {
                    byQuery[arm.Name] = byQuery.GetValueOrDefault(arm.Name) + 1;
                    if (rank == 0) atTop[arm.Name] = atTop.GetValueOrDefault(arm.Name) + 1;
                }

                // (a2) THE SAME QUERY, A DEEPER PAGE — and this is the one that separates the two things
                // D41 is about. Missing from a page of ten says the RANKING put it there; missing from a
                // page of a hundred says the entry is gone. Without this column the table cannot tell
                // "buried" from "deleted", which is the only distinction it exists to make.
                var deep = (await engine.RecallAsync(
                        new MemoryQuery(Task, Scope, stale.Content, Limit: DeepLimit)))
                    .Items.Select(i => i.Content ?? i.Headline).ToList();
                var deepRank = FirstIndexOf(deep, q.Stale);
                if (deepRank >= 0)
                {
                    byDeep[arm.Name] = byDeep.GetValueOrDefault(arm.Name) + 1;
                    deepRankSum[arm.Name] = deepRankSum.GetValueOrDefault(arm.Name) + deepRank + 1;
                }

                // (b) A RELATED CUE — the walk, reaching it as a neighbour of what the question did return.
                var walked = false;
                await WalkAsync(engine, q.Text, (_, body, _) =>
                    walked |= q.Stale.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal))));
                if (walked) byWalk[arm.Name] = byWalk.GetValueOrDefault(arm.Name) + 1;

                if (rank >= 0 || walked) byEither[arm.Name] = byEither.GetValueOrDefault(arm.Name) + 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{"Arm",-16} {"buried",7} {$"page@{RecallLimit}",9} {"walk",7} "
            + $"{$"deep@{DeepLimit}",26} {"mean rank",10}");
        Console.WriteLine(new string('-', 82));
        foreach (var arm in configs.Select(c => c.Name))
        {
            var b = buried.GetValueOrDefault(arm);
            if (b == 0) { Console.WriteLine($"{arm,-16} {0,7}   (nothing was suppressed)"); continue; }

            var d = byDeep.GetValueOrDefault(arm);
            var (low, high) = BenchStats.Wilson(d, b);
            Console.WriteLine($"{arm,-16} {b,7} {$"{(double)byQuery.GetValueOrDefault(arm) / b:P1}",9} "
                + $"{$"{(double)byWalk.GetValueOrDefault(arm) / b:P1}",7} "
                + $"{$"{(double)d / b:P1} [{low:P0}, {high:P0}]",26} "
                + $"{(d == 0 ? "-" : $"{(double)deepRankSum.GetValueOrDefault(arm) / d:F1}"),10}");
        }

        Console.WriteLine();
        Console.WriteLine("  'buried' is the denominator and it is the point: only a suppressed entry can");
        Console.WriteLine("  test the invariant, so an arm that suppresses nothing reports nothing here.");
        Console.WriteLine();
        Console.WriteLine($"  THE COLUMN THAT DECIDES D41 IS `deep@{DeepLimit}`, not `page@{RecallLimit}`.");
        Console.WriteLine("  Absent from a ten-slot page means the RANKING put it there - which is what decay");
        Console.WriteLine("  is FOR. Absent from a hundred means the entry is unreachable, which is deletion");
        Console.WriteLine("  and which `IMemoryGraphStore.SeedAsync`'s 'faintness never excludes' forbids.");
        Console.WriteLine("  'mean rank' is where the focused query actually put it, so 'reachable' does not");
        Console.WriteLine("  quietly mean 'at position 99'.");
        return 0;
    }

    /// <summary>Each arm against the REFERENCE arm, paired question by question.
    ///
    /// <para><b>The reference is <c>vector</c> when it ran</b> — plain cosine is the "no mechanism at all"
    /// baseline, so a comparison against it is the one that answers whether this design does anything —
    /// otherwise the first arm, which makes an ablation ladder read against its own control.</para>
    ///
    /// <para><b>Only questions BOTH arms could decide are paired.</b> An arm that returned neither fact has
    /// no preference to compare, and counting it as a failure would score a retrieval miss as a preference
    /// error — the same conflation the <c>decidable</c> column exists to prevent.</para></summary>
    private static void PrintPairedComparison(
        IReadOnlyList<string> arms, Dictionary<string, Dictionary<int, bool>> perQuestion)
    {
        var reference = arms.Contains(VectorArm, StringComparer.Ordinal) ? VectorArm : arms.FirstOrDefault();
        if (reference is null || !perQuestion.TryGetValue(reference, out var baseline)) return;

        var others = arms.Where(a => !string.Equals(a, reference, StringComparison.Ordinal)).ToList();
        if (others.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"Paired against `{reference}`, McNemar exact over the questions both decided:");
        foreach (var arm in others)
        {
            if (!perQuestion.TryGetValue(arm, out var mine)) continue;

            int wins = 0, losses = 0, both = 0;
            foreach (var (question, preferred) in mine)
            {
                if (!baseline.TryGetValue(question, out var theirs)) continue;
                both++;
                if (preferred && !theirs) wins++;
                else if (!preferred && theirs) losses++;
            }

            var p = BenchStats.McNemarExact(wins, losses);
            Console.WriteLine($"  {arm,-24} {both,3} paired   +{wins,-3} -{losses,-3} "
                + $"net {wins - losses,+4}   {BenchStats.Format(p)}");
        }

        Console.WriteLine();
        Console.WriteLine("  '+' is questions this arm preferred the current fact on and the reference did");
        Console.WriteLine("  not; '-' the reverse. Only those DISAGREEMENTS carry information, so a small");
        Console.WriteLine("  net over few discordant pairs is not a result however large the percentage gap.");
    }

    /// <summary>One arm's engine over its own store, with the semantic channel registered when the arm asks
    /// for one. <b>Every arm gets its own store and its own vector store</b> — a recall reinforces what it
    /// returns, so arms sharing one would each mutate the decay state the next reads.</summary>
    private static GraphMemoryEngine EngineFor(FieldArm arm, MemoryPolicySweep.SweepDb db,
        SweepDoubles.CachingEmbedder embedder)
    {
        var vectors = new InMemoryVectorStore();
        IMemorySeedSource[]? seeds = arm.SemanticK is { } k
            ? [new LexicalSeedSource(), new SubjectSeedSource(),
                new SemanticSeedSource(embedder, vectors, new SemanticSeedOptions { K = k })]
            : null;

        return new GraphMemoryEngine(Task, new SqliteMemoryGraphStore(db.Factory), options: arm.Options,
            embedder: embedder, vectors: vectors, ranking: arm.Ranking, verification: arm.Verification,
            seedSources: seeds);
    }

    private sealed record Turn(int Session, int Index, string Role, string Content, bool HasAnswer)
    {
        /// <summary>The marker rides in the CONTENT so a hit is checkable with no model, exactly as the
        /// LoCoMo harness carries a dialogue id. Every arm reads the same text, so none is advantaged.</summary>
        public string Tag => $"(s{Session}t{Index})";

        public string Text => $"[{Role}] {Content}";
    }

    private sealed record Question(string Id, string Text, string Type, IReadOnlyList<Turn> Turns,
        IReadOnlyList<Turn> Current, IReadOnlyList<Turn> Stale, IReadOnlyList<Turn> Evidence);

    public static async Task<int> RunAsync(string[] args)
    {
        var haystack = args.Contains("--haystack");
        var (path, file, remote, variable) = haystack
            ? (Environment.GetEnvironmentVariable(HaystackVariable)
                ?? Path.Combine("devtools", "_bench-data", "lme_s.json"),
                "lme_s.json", "longmemeval_s_cleaned.json", HaystackVariable)
            : (Environment.GetEnvironmentVariable(DataVariable)
                ?? Path.Combine("devtools", "_bench-data", "lme_oracle.json"),
                "lme_oracle.json", "longmemeval_oracle.json", DataVariable);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-longmemeval: dataset not found at '{path}'.");
            Console.Error.WriteLine($"  Download it ({(haystack ? "277 MB" : "15 MB")}) and re-run:");
            Console.Error.WriteLine($"    curl -sSL -o devtools/_bench-data/{file} \\");
            Console.Error.WriteLine("      https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned/"
                + $"resolve/main/{remote}");
            Console.Error.WriteLine($"  Or point {variable} at a copy.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-longmemeval");
        if (embedder is null) return 1;

        // `--temporal` is the COUNTER-TEST to the knowledge-update class, not more of the same. Temporal
        // reasoning asks things like "what was the FIRST issue after the service", so the answer is often
        // the EARLIER fact and preferring the current one would be wrong. It needs BOTH facts present, which
        // is exactly what the suppression that wins knowledge-update should cost. Different question,
        // different metric: all-evidence recall rather than preference.
        // `--ranks` is a DIAGNOSTIC, not an arm: it asks WHERE in the candidate pool the two facts sit,
        // because the haystack table shows stale@k RISING (54.3 → 62.9) while twenty times more candidates
        // compete for the same ten slots. More competition should crowd the superseded fact out. It needs
        // the knowledge-update pair, so it overrides `--temporal` rather than combining with it.
        var ranks = args.Contains("--ranks");
        var temporal = args.Contains("--temporal") && !ranks;
        var questions = Load(path, temporal ? "temporal-reasoning" : "knowledge-update", temporal);
        if (questions.Count == 0)
        {
            Console.Error.WriteLine("memory-longmemeval: no question survived loading — a knowledge-update "
                + "needs two dated sessions with flagged answer turns; a temporal one needs any.");
            return 1;
        }

        var seed = ArgValue(args, "--seed") is { } s && int.TryParse(s, out var chosen) ? chosen : DefaultSeed;
        var take = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : questions.Count;
        var sampled = Sample(questions, take, seed);
        var turns = sampled.Sum(q => q.Turns.Count);

        var expandFloor = ArgValue(args, "--expand-floor") is { } f && double.TryParse(f, out var ef) ? ef : 0;

        if (temporal)
        {
            Console.WriteLine("=== LongMemEval temporal-reasoning: the COST side of the same bet ===");
            Console.WriteLine();
            Console.WriteLine("Knowledge-update rewards suppressing a superseded fact. This class does not:");
            Console.WriteLine("'what was the FIRST issue after the service' wants the EARLIER fact, and most");
            Console.WriteLine("questions need BOTH. So the metric is all-evidence recall, and the suppression");
            Console.WriteLine("that won the other class is expected to COST here. That is the trade, measured.");
            Console.WriteLine();
            if (args.Contains("--shots"))
            {
                Console.WriteLine("--shots asks what EXPANDING buys on that class. If a walk is worth anything");
                Console.WriteLine("on any workload it should be worth it here: the failure mode is a first load");
                Console.WriteLine("holding one flagged turn of two, which is exactly what a second shot is for.");
                Console.WriteLine();
            }
            Preamble(sampled, questions.Count, turns, haystack, seed, "temporal-reasoning");
            return args.Contains("--shots")
                ? await RunTemporalShotsAsync(sampled, embedder, expandFloor, args)
                : await RunTemporalAsync(sampled, embedder, args);
        }

        if (args.Contains("--recover"))
        {
            Console.WriteLine("=== BURIAL, NOT DELETION: is a decayed entry still reachable? ===");
            Console.WriteLine();
            Console.WriteLine("Every other table here scores what decay SUPPRESSES, and none of them can tell");
            Console.WriteLine("'ranked below the cut' from 'gone'. `docs/DECISIONS.md` D41 says the difference is");
            Console.WriteLine("the whole design - an entry is BURIED, never deleted - and that is a claim about");
            Console.WriteLine("the entries a recall did NOT return, which no ranking metric observes.");
            Console.WriteLine();
            Console.WriteLine("So: take the questions where the superseded fact was suppressed, and ask for it");
            Console.WriteLine("two ways - a FOCUSED query in the entry's own words, and a WALK that reaches it as");
            Console.WriteLine("a related neighbour. Recovery near 100% is the invariant holding; anything well");
            Console.WriteLine("below it means decay is deleting, and no gain elsewhere would justify that.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            return await RunRecoveryAsync(sampled, embedder, args);
        }

        if (args.Contains("--shots"))
        {
            Console.WriteLine("=== What each SHOT buys on the workload this design is FOR ===");
            Console.WriteLine();
            Console.WriteLine("LoCoMo asks for arbitrary old material and a perfect archive wins it by");
            Console.WriteLine("construction. This class asks the opposite: one fact was REVISED, so the context");
            Console.WriteLine("a reader gets should hold the current value and NOT the superseded one. That is");
            Console.WriteLine("what 'clean' scores, and it is the metric a smaller context is supposed to buy.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            return await RunShotsAsync(sampled, embedder, expandFloor, args);
        }

        // `--ranks` scores ONE probe-wrapped engine over a K ladder, so it has no arms to select. Rejected
        // rather than ignored: a flag that is accepted and does nothing is the silent no-op every other
        // path here refuses.
        if (ranks && ArgValue(args, "--arms") is not null)
        {
            Console.Error.WriteLine("--arms: not applicable with --ranks, which sweeps K over a single "
                + "configuration rather than comparing arms.");
            return 1;
        }

        if (ranks)
        {
            Console.WriteLine("=== Where the current and stale facts sit in the candidate pool ===");
            Console.WriteLine();
            Console.WriteLine("RRF scores a candidate as the sum over signals of w / (K + rank), K = 60. That");
            Console.WriteLine("curve is CONVEX in rank, so the same rank gap is worth far less deep in the list");
            Console.WriteLine("than near the top. If distractors push both facts down the retrievability order,");
            Console.WriteLine("the signal that separates them quietly stops paying - which is a prediction, and");
            Console.WriteLine("the contribution columns below are what tests it.");
            Console.WriteLine();
            Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");
            await RunRanksAsync(sampled, embedder);
            return 0;
        }

        Console.WriteLine("=== LongMemEval knowledge-update: does the memory PREFER the current fact? ===");
        Console.WriteLine();
        Console.WriteLine("Each question carries an earlier session stating a fact and a later one REVISING");
        Console.WriteLine("it. Both sit in the store and both are textually similar, so retrieving 'the");
        Console.WriteLine("answer' is not the test - preferring the CURRENT one over the superseded one is.");
        Console.WriteLine("That is the claim a decay model makes and a flat index cannot.");
        Console.WriteLine();
        Preamble(sampled, questions.Count, turns, haystack, seed, "knowledge-update");

        var configs = SelectConfigs(args, out var wantsCosine);
        if (configs is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        string[] arms = [.. configs.Select(c => c.Name), .. wantsCosine ? new[] { VectorArm } : []];
        var currentHit = new Dictionary<string, int>();
        var staleHit = new Dictionary<string, int>();
        var prefers = new Dictionary<string, int>();
        var decidable = new Dictionary<string, int>();

        // PER-QUESTION outcomes, because the arms answer the same questions and the comparison that follows
        // is therefore paired. Counts alone cannot support one: they lose which questions the arms disagreed
        // on, which is the whole of what a paired test reads.
        var perQuestion = new Dictionary<string, Dictionary<int, bool>>(StringComparer.Ordinal);

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            var results = new List<(string Arm, List<string> Got)>();

            // ONE PRISTINE STORE PER ARM. Only the configs a selected arm needs are ingested, which is the
            // whole cost of the run: under `--haystack` a question carries ~490 turns, so ingesting an arm
            // nobody scores is the most expensive way to measure nothing.
            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, embedder);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                results.Add((arm.Name,
                    (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList()));
            }

            if (wantsCosine)
            {
                // The cosine index is built from the same turn text every arm ingested, so the control reads
                // the same corpus rather than a parallel one.
                var index = new List<(string Text, float[] Vector)>();
                foreach (var t in q.Turns)
                {
                    var content = $"{t.Tag} {t.Text}";
                    index.Add((content, await embedder.EmbedAsync(content)));
                }
                results.Add((VectorArm, (await TopKAsync(embedder, index, q.Text, RecallLimit)).ToList()));
            }

            foreach (var (arm, got) in results)
            {
                var cur = FirstIndexOf(got, q.Current);
                var sta = FirstIndexOf(got, q.Stale);
                if (cur >= 0) currentHit[arm] = currentHit.GetValueOrDefault(arm) + 1;
                if (sta >= 0) staleHit[arm] = staleHit.GetValueOrDefault(arm) + 1;

                // DECIDABLE means the arm returned at least one of the two, so a preference exists to score.
                // Without this an arm that retrieves NEITHER would score a vacuous 100%.
                if (cur < 0 && sta < 0) continue;
                decidable[arm] = decidable.GetValueOrDefault(arm) + 1;
                var preferred = cur >= 0 && (sta < 0 || cur < sta);
                if (preferred) prefers[arm] = prefers.GetValueOrDefault(arm) + 1;

                // Keyed by the question's position in the sample, which is what makes two arms' outcomes
                // line up. An UNDECIDABLE question is absent rather than false — it is not a failure to
                // prefer the current fact, and folding it in would score a retrieval miss as a preference.
                if (!perQuestion.TryGetValue(arm, out var byQuestion))
                    perQuestion[arm] = byQuestion = [];
                byQuestion[done] = preferred;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"prefers current",18} {"current@k",12} {"stale@k",12} {"decidable",11}");
        Console.WriteLine(new string('-', 66));
        foreach (var arm in arms)
        {
            var d = decidable.GetValueOrDefault(arm);
            var p = prefers.GetValueOrDefault(arm);
            var (low, high) = BenchStats.Wilson(p, d);
            Console.WriteLine($"{arm,-10} {(d == 0 ? "-" : $"{(double)p / d:P1} ({p}/{d})"),18} "
                + $"{$"{(double)currentHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} "
                + $"{$"{(double)staleHit.GetValueOrDefault(arm) / sampled.Count:P1}",12} {d,11}"
                + (d == 0 ? "" : $"   95% CI [{low:P1}, {high:P1}]"));
        }

        PrintPairedComparison(arms, perQuestion);

        Console.WriteLine();
        Console.WriteLine("  'prefers current' is scored only over questions where the arm returned at least");
        Console.WriteLine("  one of the two facts — otherwise retrieving NEITHER would score a vacuous 100%.");
        Console.WriteLine("  'stale@k' is not a failure on its own: returning both is fine if the current one");
        Console.WriteLine("  ranks first. It is here so a preference win cannot hide a recall collapse.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>
    /// The per-shot curve on the class where suppression is supposed to pay. <b>'clean' is the column that
    /// matters</b>: the context holds the current fact and NOT the one it superseded, which is what a reader
    /// actually consumes — <c>current@k</c> alone rewards a context that also carries the wrong answer.
    /// <para>Every question gets a pristine store, so unlike the LoCoMo harness there is no cross-question
    /// reinforcement to confound the shots.</para>
    /// </summary>
    private static async Task<int> RunShotsAsync(List<Question> sampled,
        SweepDoubles.CachingEmbedder embedder, double expandFloor, string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var arms = FilterArms(args, ["shot-1", "shot-2", "shot-3", "vector", $"vector-{ShotBudget}"]);
        if (arms is null) return 1;
        var cur = new Dictionary<string, int>();
        var sta = new Dictionary<string, int>();
        var clean = new Dictionary<string, int>();
        var items = new Dictionary<string, int>();
        var chars = new Dictionary<string, long>();
        var millis = new Dictionary<string, double>();
        var done = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store,
                options: new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor },
                embedder: embedder, vectors: new InMemoryVectorStore());

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await embedder.EmbedAsync(content)));
            }
            await embedder.EmbedAsync(q.Text);   // warm the query embed so no arm pays for it alone

            void Score(string arm, IReadOnlyList<string> body, double ms)
            {
                var hasCurrent = q.Current.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                var hasStale = q.Stale.Any(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                if (hasCurrent) cur[arm] = cur.GetValueOrDefault(arm) + 1;
                if (hasStale) sta[arm] = sta.GetValueOrDefault(arm) + 1;
                if (hasCurrent && !hasStale) clean[arm] = clean.GetValueOrDefault(arm) + 1;
                items[arm] = items.GetValueOrDefault(arm) + body.Count;
                chars[arm] = chars.GetValueOrDefault(arm) + body.Sum(b => b.Length);
                millis[arm] = millis.GetValueOrDefault(arm) + ms;
            }

            await WalkAsync(engine, q.Text, (shot, body, ms) => Score($"shot-{shot}", body, ms));

            foreach (var (arm, k) in new[] { ("vector", RecallLimit), ($"vector-{ShotBudget}", ShotBudget) })
            {
                var vclock = Stopwatch.StartNew();
                var got = (await TopKAsync(embedder, index, q.Text, k)).ToList();
                Score(arm, got, vclock.Elapsed.TotalMilliseconds);
            }
        }

        var n = sampled.Count;
        Console.WriteLine($"{"Arm",-12} {"clean",10} {"current@k",11} {"stale@k",10} {"items/q",9} "
            + $"{"chars/q",9} {"ms/q",8}");
        Console.WriteLine(new string('-', 74));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-12} {(double)clean.GetValueOrDefault(arm) / n,10:P1} "
                + $"{(double)cur.GetValueOrDefault(arm) / n,11:P1} {(double)sta.GetValueOrDefault(arm) / n,10:P1} "
                + $"{(double)items.GetValueOrDefault(arm) / n,9:F1} {(double)chars.GetValueOrDefault(arm) / n,9:F0} "
                + $"{millis.GetValueOrDefault(arm) / n,8:F1}");

        Console.WriteLine();
        Console.WriteLine("  'clean' = the context holds the CURRENT fact and not the superseded one. It is the");
        Console.WriteLine("  one a reader's answer actually depends on: a context carrying both hands the model");
        Console.WriteLine("  the contradiction to resolve, which is the work this layer exists to do for it.");
        Console.WriteLine("  'ms/q' is memory-layer time only, and the vector arms are an in-memory brute force");
        Console.WriteLine("  with no persistence and no write-back - so that column compares a database against");
        Console.WriteLine("  an array, not two retrieval strategies.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>Scores the library's walk: <c>MemoryWalk.WalkAsync</c> is the loop, and this calls
    /// <paramref name="score"/> after each step with the cumulative context.
    /// <para><b>Shared by both shot curves on purpose</b>, so "shot 2" cannot quietly mean two things. The
    /// loop itself used to live here AND in <c>MemoryLocomoBench</c>, which is the duplication
    /// <c>TASKS.md</c> Part 116 cited when it asked for this surface.</para>
    /// <para>Shots 2 and 3 come from ONE walk because three is a strict superset of two, so the arms stay
    /// exactly nested and any difference between them is the extra shot and nothing else.</para>
    /// <para><b><c>MaxEntries</c> is passed EXPLICITLY.</b> The library derives twice what step 1 returned,
    /// which equals <c>ShotBudget</c> only when a question's recall fills its limit — leaving it null would
    /// shrink the bound on short recalls and move published figures for a reason that is not a defect. The
    /// expansion floor is the ENGINE's (<c>GraphMemoryOptions.ExpansionRetrievabilityFloor</c>), not this
    /// harness's.</para></summary>
    private static async Task WalkAsync(GraphMemoryEngine engine, string question,
        Action<int, IReadOnlyList<string>, double> score)
    {
        var clock = Stopwatch.StartNew();
        var options = new MemoryWalkOptions { SeedsPerStep = ExpandSeeds, Hops = 1, MaxItems = ShotBudget };

        await foreach (var step in engine.WalkAsync(
            new MemoryQuery(Task, Scope, question, Limit: RecallLimit), options))
        {
            score(step.Number, [.. step.Items.Select(i => i.Content ?? i.Headline)],
                clock.Elapsed.TotalMilliseconds);
            if (step.Number >= 3) break;   // this harness's curve is three shots, as published
        }
    }

    /// <summary>The temporal class's shot curve — the same walk as <see cref="RunShotsAsync"/> scored the
    /// other way round. Knowledge-update wants the superseded fact GONE; this class usually needs every
    /// flagged turn, so the question a shot answers here is whether expanding RECOVERS evidence the first
    /// load missed. That is the one workload where more shots should help most, and it had no curve at
    /// all.</summary>
    private static async Task<int> RunTemporalShotsAsync(List<Question> sampled,
        SweepDoubles.CachingEmbedder embedder, double expandFloor, string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var arms = FilterArms(args, ["shot-1", "shot-2", "shot-3", "vector", $"vector-{ShotBudget}"]);
        if (arms is null) return 1;
        var all = new Dictionary<string, int>();
        var any = new Dictionary<string, int>();
        var found = new Dictionary<string, int>();
        var items = new Dictionary<string, int>();
        var chars = new Dictionary<string, long>();
        var evidenceTotal = 0;
        var done = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var engine = new GraphMemoryEngine("lme", store,
                options: new GraphMemoryOptions { ExpansionRetrievabilityFloor = expandFloor },
                embedder: embedder, vectors: new InMemoryVectorStore());

            var index = new List<(string Text, float[] Vector)>();
            foreach (var t in q.Turns)
            {
                var content = $"{t.Tag} {t.Text}";
                await engine.RememberAsync(new MemoryWrite(Task, Scope, content));
                index.Add((content, await embedder.EmbedAsync(content)));
            }
            await embedder.EmbedAsync(q.Text);   // warm the query embed so no arm pays for it alone
            evidenceTotal += q.Evidence.Count;

            void Score(string arm, IReadOnlyList<string> body)
            {
                var hits = q.Evidence.Count(t => body.Any(b => b.Contains(t.Tag, StringComparison.Ordinal)));
                found[arm] = found.GetValueOrDefault(arm) + hits;
                if (hits == q.Evidence.Count) all[arm] = all.GetValueOrDefault(arm) + 1;
                if (hits > 0) any[arm] = any.GetValueOrDefault(arm) + 1;
                items[arm] = items.GetValueOrDefault(arm) + body.Count;
                chars[arm] = chars.GetValueOrDefault(arm) + body.Sum(b => b.Length);
            }

            await WalkAsync(engine, q.Text, (shot, body, _) => Score($"shot-{shot}", body));

            foreach (var (arm, k) in new[] { ("vector", RecallLimit), ($"vector-{ShotBudget}", ShotBudget) })
                Score(arm, (await TopKAsync(embedder, index, q.Text, k)).ToList());
        }

        var n = sampled.Count;
        Console.WriteLine($"{"Arm",-12} {"all evidence@k",16} {"any evidence@k",16} {"evidence turns",16} "
            + $"{"items/q",9} {"chars/q",9}");
        Console.WriteLine(new string('-', 84));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-12} "
                + $"{$"{(double)all.GetValueOrDefault(arm) / n:P1}",16} "
                + $"{$"{(double)any.GetValueOrDefault(arm) / n:P1}",16} "
                + $"{$"{(double)found.GetValueOrDefault(arm) / evidenceTotal:P1}",16} "
                + $"{(double)items.GetValueOrDefault(arm) / n,9:F1} "
                + $"{(double)chars.GetValueOrDefault(arm) / n,9:F0}");

        Console.WriteLine();
        Console.WriteLine("  'all evidence@k' is the one that matters: a temporal question usually needs every");
        Console.WriteLine("  flagged turn, so retrieving one of two answers nothing. If expansion is worth");
        Console.WriteLine("  anything on any workload it should be worth it HERE, where the first load missing");
        Console.WriteLine("  one turn of two is the whole failure mode.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>Reports where the pair lands on each ranking signal, and what that position is WORTH under
    /// RRF's own curve. The rank gap and the contribution gap are separate columns on purpose: a gap that
    /// holds while its contribution collapses is the whole hypothesis, and one number cannot show it.</summary>
    private static async Task RunRanksAsync(List<Question> sampled, SweepDoubles.CachingEmbedder embedder)
    {
        const double K = 60;   // ReciprocalRankFusionOptions.K's shipped default.
        var stopwatch = Stopwatch.StartNew();
        var rows = new List<(int Pool, int RelCur, int RelSta, int RetCur, int RetSta, double RCur, double RSta)>();
        var missing = 0;
        var done = 0;
        var curAtK = new int[RankLadder.K.Length];
        var staAtK = new int[RankLadder.K.Length];
        int agreed = 0, compared = 0;

        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            using var db = new MemoryPolicySweep.SweepDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            var probe = new RankProbe(new ReciprocalRankFusionPolicy()) { Current = q.Current, Stale = q.Stale };
            var engine = new GraphMemoryEngine("lme", store, embedder: embedder,
                vectors: new InMemoryVectorStore(), ranking: probe);

            foreach (var t in q.Turns)
                await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));
            await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit));

            if (probe.Seen.Count == 0) { missing++; continue; }
            rows.Add(probe.Seen[^1]);
            for (var k = 0; k < curAtK.Length; k++) { curAtK[k] += probe.CurrentAtK[k]; staAtK[k] += probe.StaleAtK[k]; }
            agreed += probe.Agreed;
            compared += probe.Compared;
        }

        Console.WriteLine($"{"",-22} {"current",12} {"stale",12} {"gap",12}");
        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"{"relevance rank",-22} {Med(rows, r => r.RelCur),12:F1} "
            + $"{Med(rows, r => r.RelSta),12:F1} {Med(rows, r => r.RelSta - r.RelCur),12:F1}");
        Console.WriteLine($"{"retrievability rank",-22} {Med(rows, r => r.RetCur),12:F1} "
            + $"{Med(rows, r => r.RetSta),12:F1} {Med(rows, r => r.RetSta - r.RetCur),12:F1}");
        Console.WriteLine($"{"retrievability value",-22} {Med(rows, r => r.RCur),12:F4} "
            + $"{Med(rows, r => r.RSta),12:F4} {Med(rows, r => r.RCur - r.RSta),12:F4}");
        Console.WriteLine();
        Console.WriteLine($"{"RRF contribution",-22} {"current",12} {"stale",12} {"gap",12}");
        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"{"  from relevance",-22} {Med(rows, r => 1 / (K + r.RelCur)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RelSta)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RelCur) - 1 / (K + r.RelSta)),12:F5}");
        Console.WriteLine($"{"  from retrievability",-22} {Med(rows, r => 1 / (K + r.RetCur)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RetSta)),12:F5} "
            + $"{Med(rows, r => 1 / (K + r.RetCur) - 1 / (K + r.RetSta)),12:F5}");
        Console.WriteLine();
        Console.WriteLine($"{"K",-8} {"current@k",12} {"stale@k",12}   (same pool, same ranks, scored offline)");
        Console.WriteLine(new string('-', 62));
        for (var k = 0; k < RankLadder.K.Length; k++)
            Console.WriteLine($"{RankLadder.K[k],-8:F0} {(double)curAtK[k] / rows.Count,12:P1} "
                + $"{(double)staAtK[k] / rows.Count,12:P1}{(RankLadder.K[k] == 60 ? "   <- shipped" : "")}");

        Console.WriteLine();
        Console.WriteLine($"  CONTROL: the K=60 row reproduces the SHIPPED policy's own top-{RecallLimit} on "
            + $"{agreed}/{compared} recalls.");
        Console.WriteLine("  Anything below 100% means the replica is not the formula this library runs, and");
        Console.WriteLine("  the ladder above is a table about something else.");
        Console.WriteLine();
        Console.WriteLine($"  Pool size (median): {Med(rows, r => r.Pool):F0}   scored on {rows.Count} "
            + $"question(s); {missing} had one of the pair outside the pool entirely.");
        Console.WriteLine("  A POSITIVE gap favours the current fact. 'rank' gaps count positions, so bigger");
        Console.WriteLine("  is better separation; 'contribution' gaps are what RRF actually adds up, and they");
        Console.WriteLine("  are the ones that decide the order.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
    }

    private static double Med<T>(List<T> rows, Func<T, double> of)
    {
        if (rows.Count == 0) return double.NaN;
        var sorted = rows.Select(of).OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>All-evidence recall. A temporal question usually needs EVERY flagged turn — "the first issue
    /// after the service" is unanswerable from the later fact alone — so partial recall is a miss, and the
    /// any-evidence column beside it shows how much of the gap is partial rather than total.</summary>
    private static async Task<int> RunTemporalAsync(List<Question> sampled,
        SweepDoubles.CachingEmbedder embedder, string[] args)
    {
        var configs = SelectConfigs(args, out var wantsCosine);
        if (configs is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        string[] arms = [.. configs.Select(c => c.Name), .. wantsCosine ? new[] { VectorArm } : []];
        var all = new Dictionary<string, int>();
        var any = new Dictionary<string, int>();
        var evidenceTotal = 0;
        var evidenceFound = new Dictionary<string, int>();

        var done = 0;
        foreach (var q in sampled)
        {
            Progress(++done, sampled.Count);
            var results = new List<(string Arm, List<string> Got)>();

            // One pristine store per arm, and only for arms this run scores — the same reasoning the
            // knowledge-update mode above states.
            foreach (var arm in configs)
            {
                using var db = new MemoryPolicySweep.SweepDb();
                var engine = EngineFor(arm, db, embedder);
                foreach (var t in q.Turns)
                    await engine.RememberAsync(new MemoryWrite(Task, Scope, $"{t.Tag} {t.Text}"));

                results.Add((arm.Name,
                    (await engine.RecallAsync(new MemoryQuery(Task, Scope, q.Text, Limit: RecallLimit)))
                        .Items.Select(i => i.Content ?? i.Headline).ToList()));
            }

            if (wantsCosine)
            {
                var index = new List<(string Text, float[] Vector)>();
                foreach (var t in q.Turns)
                {
                    var content = $"{t.Tag} {t.Text}";
                    index.Add((content, await embedder.EmbedAsync(content)));
                }
                results.Add((VectorArm, (await TopKAsync(embedder, index, q.Text, RecallLimit)).ToList()));
            }

            evidenceTotal += q.Evidence.Count;

            foreach (var (arm, got) in results)
            {
                var hits = q.Evidence.Count(t => got.Any(g => g.Contains(t.Tag, StringComparison.Ordinal)));
                evidenceFound[arm] = evidenceFound.GetValueOrDefault(arm) + hits;
                if (hits == q.Evidence.Count) all[arm] = all.GetValueOrDefault(arm) + 1;
                if (hits > 0) any[arm] = any.GetValueOrDefault(arm) + 1;
            }
        }

        Console.WriteLine($"{"Arm",-10} {"all evidence@k",16} {"any evidence@k",16} {"evidence turns",16}");
        Console.WriteLine(new string('-', 62));
        foreach (var arm in arms)
            Console.WriteLine($"{arm,-10} "
                + $"{$"{(double)all.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)any.GetValueOrDefault(arm) / sampled.Count:P1}",16} "
                + $"{$"{(double)evidenceFound.GetValueOrDefault(arm) / evidenceTotal:P1}",16}");

        Console.WriteLine();
        Console.WriteLine("  'all evidence@k' is the one that matters: a temporal question usually needs every");
        Console.WriteLine("  flagged turn, so retrieving one of two answers nothing. 'evidence turns' is the");
        Console.WriteLine("  per-turn rate, which separates 'missed one question badly' from 'missed a little");
        Console.WriteLine("  everywhere'.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s   "
            + $"embedder {embedder.Misses} call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    private static int FirstIndexOf(List<string> got, IReadOnlyList<Turn> wanted)
    {
        for (var i = 0; i < got.Count; i++)
            if (wanted.Any(t => got[i].Contains(t.Tag, StringComparison.Ordinal)))
                return i;
        return -1;
    }

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

    /// <summary>One class, and for knowledge-update only the questions whose evidence spans two dated
    /// sessions — the discriminating shape. Distractor sessions are loaded like any other: they are what the
    /// haystack variant is for.</summary>
    private static List<Question> Load(string path, string wantType, bool temporal)
    {
        // Bytes, not text: the haystack file is 277 MB, and reading it as a string doubles that before the
        // document is even built.
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path).AsMemory());
        var result = new List<Question>();

        foreach (var q in doc.RootElement.EnumerateArray())
        {
            if (q.GetProperty("question_type").GetString() != wantType) continue;

            var sessions = q.GetProperty("haystack_sessions");
            var dates = q.GetProperty("haystack_dates").EnumerateArray().Select(d => d.GetString() ?? "").ToList();
            if (sessions.GetArrayLength() < (temporal ? 1 : 2)) continue;

            // Order by the session's own date string. LongMemEval's format sorts lexicographically because
            // it is yyyy/MM/dd — which is why this needs no date parsing and no culture.
            var ordered = sessions.EnumerateArray()
                .Select((s, i) => (Session: s, Date: i < dates.Count ? dates[i] : "", Index: i))
                .OrderBy(x => x.Date, StringComparer.Ordinal)
                .ToList();

            var turns = new List<Turn>();
            foreach (var (session, _, si) in ordered)
                turns.AddRange(session.EnumerateArray().Select((t, ti) => new Turn(si, ti,
                    t.GetProperty("role").GetString() ?? "",
                    t.GetProperty("content").GetString() ?? "",
                    t.TryGetProperty("has_answer", out var h) && h.ValueKind == JsonValueKind.True)));

            // The current value sits in the latest-dated session that CARRIES a flagged turn, never simply in
            // the latest session. In the oracle the two coincide because every session is an evidence session;
            // in the haystack the last-dated session is a distractor nearly every time, so reading it would
            // find no current turn and drop the whole class. `turns` is already in date order.
            var evidence = turns.Where(t => t.HasAnswer).ToList();
            var lastEvidence = evidence.Count == 0 ? -1 : evidence[^1].Session;
            var current = evidence.Where(t => t.Session == lastEvidence).ToList();
            var stale = evidence.Where(t => t.Session != lastEvidence).ToList();
            if (temporal ? evidence.Count == 0 : current.Count == 0 || stale.Count == 0) continue;

            result.Add(new Question(q.GetProperty("question_id").GetString() ?? "",
                q.GetProperty("question").GetString() ?? "", wantType, turns, current, stale, evidence));
        }
        return result;
    }

    /// <summary>
    /// Observes one recall's candidate pool and delegates the actual ranking untouched — so the arm it runs
    /// in is the SHIPPED arm, and the numbers it prints describe the run that produced the table rather than
    /// a reconstruction of it.
    ///
    /// <para><b>Ranks are competition ranks</b> (<c>1 + |strictly better|</c>) because ties are the norm
    /// here, not the exception: every walked candidate reports <c>Relevance 0</c> with <c>Matched null</c>
    /// (<b>D97</b>), so a positional rank would depend on sort order among equals and mean nothing.</para>
    /// </summary>
    private sealed class RankProbe(IMemoryRankingPolicy inner) : IMemoryRankingPolicy
    {
        internal IReadOnlyList<Turn> Current { get; set; } = [];
        internal IReadOnlyList<Turn> Stale { get; set; } = [];
        internal List<(int Pool, int RelCur, int RelSta, int RetCur, int RetSta, double RCur, double RSta)>
            Seen { get; } = [];

        internal int[] CurrentAtK { get; } = new int[RankLadder.K.Length];
        internal int[] StaleAtK { get; } = new int[RankLadder.K.Length];
        internal int Agreed { get; private set; }
        internal int Compared { get; private set; }

        public IReadOnlyList<RankedMemory> Rank(
            IReadOnlyList<MemoryCandidate> candidates, in MemoryRankingContext context)
        {
            var result = inner.Rank(candidates, context);

            var ladder = new RankLadder(candidates);
            var pool = ladder.Pool;
            var cur = Find(pool, Current);
            var sta = Find(pool, Stale);
            if (cur is not { } c || sta is not { } s) return result;

            Seen.Add((pool.Count,
                RankLadder.RankOf(pool, x => x.Node.Relevance, c.Node.Relevance),
                RankLadder.RankOf(pool, x => x.Node.Relevance, s.Node.Relevance),
                RankLadder.RankOf(pool, x => x.Retrievability, c.Retrievability),
                RankLadder.RankOf(pool, x => x.Retrievability, s.Retrievability),
                c.Retrievability, s.Retrievability));

            for (var k = 0; k < RankLadder.K.Length; k++)
            {
                var top = ladder.TopAt(RankLadder.K[k], context.Limit);
                if (top.Any(x => Has(x, Current))) CurrentAtK[k]++;
                if (top.Any(x => Has(x, Stale))) StaleAtK[k]++;

                if (RankLadder.K[k] != RankLadder.Shipped) continue;
                Compared++;
                if (RankLadder.AgreesWithShipped(top, result, context.Limit)) Agreed++;
            }
            return result;
        }

        private static bool Has(MemoryCandidate c, IReadOnlyList<Turn> wanted)
            => wanted.Any(t => c.Node.Content.Contains(t.Tag, StringComparison.Ordinal));

        private static int Rank(IReadOnlyList<MemoryCandidate> all, Func<MemoryCandidate, double> of, double v)
            => 1 + all.Count(x => of(x) > v);

        private static MemoryCandidate? Find(IReadOnlyList<MemoryCandidate> all, IReadOnlyList<Turn> wanted)
        {
            foreach (var c in all)
                if (wanted.Any(t => c.Node.Content.Contains(t.Tag, StringComparison.Ordinal)))
                    return c;
            return null;
        }
    }

    /// <summary>What makes one run comparable to another: which variant ran, how much of the class was
    /// sampled, the seed, and a fingerprint of the sampled ids. Two runs printing the same digest asked the
    /// same questions, which is the whole basis for reading an oracle table against a haystack one.</summary>
    private static void Preamble(List<Question> sampled, int pool, int turns, bool haystack, int seed, string cls)
    {
        Console.WriteLine($"Variant:   {(haystack
            ? "haystack — the evidence sits among distractor sessions"
            : "oracle — evidence sessions only, nothing to bury")}");
        Console.WriteLine($"Questions: {sampled.Count} of {pool} {cls}   seed {seed}   sample {Digest(sampled)}");
        Console.WriteLine($"Ingested:  {turns} turns per arm, {(double)turns / sampled.Count:F0} per question   "
            + $"k = {RecallLimit}   embedder {SweepDoubles.Model}   model-free");
        Console.WriteLine();
    }

    /// <summary>A live count on stderr, because the haystack variant ingests ~490 turns per question and runs
    /// for the better part of an hour — long enough that a silent process is indistinguishable from a hung
    /// one. Suppressed when stderr is redirected, so a captured run's file holds the table and nothing
    /// else.</summary>
    private static void Progress(int done, int total)
    {
        if (Console.IsErrorRedirected) return;
        Console.Error.Write($"\r  ingesting: question {done}/{total}   ");
        if (done == total) Console.Error.WriteLine();
    }

    /// <summary>A stable fingerprint of the sampled ids, printed instead of the ids because the only question
    /// a reader has is whether two runs sampled the same set.</summary>
    private static string Digest(List<Question> sampled) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', sampled.Select(q => q.Id)))))[..12];

    /// <summary>A seeded sample of the class. The pool is sorted by question id first, and the ids are
    /// IDENTICAL in the oracle and haystack files — so one seed and count select the same questions from
    /// either, which is what lets the two tables be read against each other. Taking the whole class skips the
    /// shuffle rather than special-casing it.</summary>
    private static List<Question> Sample(List<Question> pool, int take, int seed)
    {
        var ordered = pool.OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
        if (take >= ordered.Count) return ordered;

        var rng = new Random(seed);
        for (var i = ordered.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }
        return ordered.Take(take).OrderBy(q => q.Id, StringComparer.Ordinal).ToList();
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
