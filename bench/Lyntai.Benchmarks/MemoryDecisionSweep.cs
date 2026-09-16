using System.Globalization;
using System.Text.Json;

namespace Lyntai.Benchmarks;

/// <summary>Which SHAPE wins a decision at 3-7 options, and at what model size.
///
/// <para>Every selective figure this repository holds stops at 20 candidates and measures an ENDORSE-A-SUBSET
/// task whose base rate is ~1.5%. A decision is a FORCED CHOICE over a short list, so neither the precision
/// nor the lift column transfers — a new regime rather than an extrapolation
/// (<c>docs/task-archive.md</c> Part 236).</para>
///
/// <para><b>The comparison is between two SHAPES, not two models.</b> One <c>select-from-list</c> call
/// showing N options, against N <c>score-a-pair</c> calls argmax'd — the second costs N calls and is the
/// shape with small-model evidence behind it. Both run on the same two instruct models, so shape and size
/// are separated.</para>
///
/// <para><b>Lift over chance is capped at N here</b>, so it is not comparable across list lengths: 3.0x at
/// N = 3 is a perfect score. Accuracy against the chance line is the primary reading.</para></summary>
internal static class MemoryDecisionSweep
{
    private const int Seed = 20260912;
    private const int DefaultTrials = 200;

    /// <summary>The list lengths a decision system actually shows. Nested: a trial holds gold plus
    /// <see cref="MaxDistractors"/> ranked distractors and N = 3 uses the first two, so ONE per-option
    /// scoring serves every length and the five cells share their distractors by construction.</summary>
    private static readonly int[] ListLengths = [3, 4, 5, 6, 7];

    private const int MaxDistractors = 6;

    /// <summary>Raw replies from the first <see cref="DumpTrials"/> trials, under <c>--dump</c>. A tie rate
    /// is a number about the harness's own arithmetic; what the model actually SAID is the only thing that
    /// says whether a flat score arm is the model, the scale it was asked for, or the parse.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> Dumped = [];

    private const int DumpTrials = 3;

    private static bool _dump;

    private static void Dump(string arm, int index, string reply) =>
        Dumped.Add(JsonSerializer.Serialize(new { arm, trial = index, reply }));

    /// <summary>How hard the wrong options are. <c>hard</c> takes the top non-gold turns by cosine — what a
    /// retriever actually hands a decision system — and <c>easy</c> draws uniformly from the same
    /// conversation. <c>easy</c> is the instrument check: an arm that cannot win there is broken rather than
    /// beaten, and one answer plus unrelated distractors is a fixture this repository has already measured a
    /// degraded scorer passing.</summary>
    private enum Difficulty { Hard, Easy }

    /// <summary>One question, its gold option, and the ranked distractor bench it draws from.
    /// <c>GoldRank</c> is gold's own cosine rank among the conversation's turns — printed, so a reader can
    /// see how hard the trials were rather than taking "hard" on trust.</summary>
    private sealed record Trial(
        string ConvId, string Question, string Gold, IReadOnlyList<string> Distractors, int GoldRank,
        int Category);

    /// <summary>What one arm did at one list length. <c>Fired</c> separates a seam that disagreed from one
    /// that never answered; <c>Ties</c> separates discrimination from a flat scorer, which reorders nothing
    /// and reads as a clean null. <c>Correct</c> is indexed BY TRIAL, not appended, so two arms stay paired
    /// under a parallel loop.</summary>
    private sealed record Cell(string Arm, int N, int Capacity)
    {
        internal int Trials, Fired, Hits, Ties, Calls, LenientHits;
        internal long PromptChars;
        internal readonly int[] HitsByPosition = new int[MaxDistractors + 2];
        internal readonly int[] ShownByPosition = new int[MaxDistractors + 2];
        internal readonly bool[] Correct = new bool[Capacity];
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var path = Environment.GetEnvironmentVariable("LYNTAI_LOCOMO_PATH")
            ?? Path.Combine("devtools", "_bench-data", "locomo10.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"memory-decision: dataset not found at '{path}'.");
            Console.Error.WriteLine("  Download it (2.8 MB, MIT) and re-run:");
            Console.Error.WriteLine("    curl -sSL -o devtools/_bench-data/locomo10.json \\");
            Console.Error.WriteLine("      https://raw.githubusercontent.com/snap-research/locomo/main/data/locomo10.json");
            return 1;
        }

        var want = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : DefaultTrials;
        var difficulty = ArgValue(args, "--difficulty") == "easy" ? Difficulty.Easy : Difficulty.Hard;
        var lanes = ArgValue(args, "--concurrency") is { } c && int.TryParse(c, out var l) ? Math.Max(1, l) : 4;
        _dump = args.Contains("--dump");

        // UseProxy=false is not decoration: proxy resolution against a local endpoint measured up to
        // 2,051 ms per call and is BIMODAL, so it reads as the model's own tail. At several thousand calls
        // it would also dominate the wall clock.
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-decision");
        if (embedder is null) return 1;
        var big = await SweepDoubles.TryRealChatAsync(http, "memory-decision");
        if (big is null) return 1;
        var small = TrySmallChat(http);

        var reranker = new CrossEncoderReranker(http, CrossEncoderReranker.BaseUrl, CrossEncoderReranker.Model);
        var rerankOk = await reranker.ReachableAsync();
        reranker.Reset();   // the probe's own two pairs must not count toward the measured region's audit

        var (conversations, questions) = MemoryLocomoBench.Load(path);
        var sampled = MemoryLocomoBench.Stratify(questions, want);
        PrintPreamble(path, big, small, rerankOk, difficulty, sampled.Count, lanes);

        var trials = await BuildTrialsAsync(conversations, sampled, embedder, difficulty);
        if (trials.Count == 0)
        {
            Console.Error.WriteLine("memory-decision: no trial could be built — every question lost its gold "
                + "turn. That is an instrument failure, not a result.");
            return 1;
        }
        PrintTrialProfile(trials, difficulty, sampled.Count);

        var cells = await RunArmsAsync(trials, big, small, rerankOk ? reranker : null, embedder, lanes);

        PrintAccuracyTable(cells, trials.Count);
        PrintControls(cells, reranker, rerankOk);
        PrintShapeComparison(cells);
        PrintPositionBias(cells);
        PrintNotSwept();
        await WriteDumpAsync();
        Console.WriteLine($"\nWall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    /// <summary>The SMALL chat model, on its own endpoint because a <c>llama-server</c> serves one model.
    /// Null is a SKIPPED arm with a printed reason, never a silent substitution of the big one.</summary>
    private static SweepDoubles.OpenAiCompatibleChat? TrySmallChat(HttpClient http)
    {
        var url = Environment.GetEnvironmentVariable("LYNTAI_LIVE_SMALL_URL");
        if (string.IsNullOrWhiteSpace(url)) return null;
        var model = Environment.GetEnvironmentVariable("LYNTAI_LIVE_SMALL_MODEL") ?? "small";
        return new SweepDoubles.OpenAiCompatibleChat(http, url, model);
    }

    // ── trial construction ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Gold plus a ranked distractor bench per question. The option text is the form
    /// <c>MemoryLocomoBench</c> ingests, so an option is what the engine would really hand a verifier — and
    /// gold is decidable from the <c>(dia_id)</c> marker riding in it, with no model at any layer.</summary>
    private static async Task<List<Trial>> BuildTrialsAsync(
        IReadOnlyList<(string Id, List<MemoryLocomoBench.Turn> Turns)> conversations,
        IReadOnlyList<MemoryLocomoBench.Question> questions,
        SweepDoubles.CachingEmbedder embedder,
        Difficulty difficulty)
    {
        var wanted = questions.Select(q => q.ConvId).ToHashSet(StringComparer.Ordinal);
        var trials = new List<Trial>();
        var rng = new Random(Seed);

        foreach (var (id, turns) in conversations.Where(c => wanted.Contains(c.Id)))
        {
            var texts = turns.Select(Render).ToList();
            var vectors = await embedder.EmbedAsync(texts);
            var byDiaId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < turns.Count; i++) byDiaId.TryAdd(turns[i].DiaId, i);

            foreach (var q in questions.Where(q => q.ConvId == id))
            {
                // EVERY evidence turn, never the first one found. 26.6% of LoCoMo's scored questions carry
                // more than one — 97.9% of the multi-hop class — and taking `Evidence[0]` as "the" answer
                // leaves the others in the distractor pool as options that are ALSO correct while the prompt
                // asserts exactly one is. A multi-hop question has no single answering turn by definition,
                // so it cannot pose a forced choice at all: both are dropped rather than scored.
                var evidence = q.Evidence.Where(byDiaId.ContainsKey).Select(e => byDiaId[e]).Distinct().ToList();
                if (evidence.Count != 1) continue;
                var gold = evidence[0];

                var query = (await embedder.EmbedAsync([q.Text]))[0];
                var ranked = Enumerable.Range(0, texts.Count)
                    .Select(i => (Index: i, Score: Cosine(query, vectors[i])))
                    .OrderByDescending(r => r.Score).ToList();

                // Belt and braces: exclude every id the dataset flags, not just the one chosen as gold, so a
                // question that slipped the filter still cannot seed a correct option into the distractors.
                var flagged = q.Evidence.Where(byDiaId.ContainsKey).Select(e => byDiaId[e]).ToHashSet();
                var pool = difficulty == Difficulty.Hard
                    ? ranked.Where(r => !flagged.Contains(r.Index)).Select(r => r.Index)
                    : Enumerable.Range(0, texts.Count).Where(i => !flagged.Contains(i)).OrderBy(_ => rng.Next());
                var distractors = pool.Take(MaxDistractors).Select(i => texts[i]).ToList();
                if (distractors.Count < MaxDistractors) continue;    // conversation too small to fill N = 7

                trials.Add(new Trial(id, q.Text, texts[gold], distractors,
                    ranked.FindIndex(r => r.Index == gold) + 1, q.Category));
            }
        }
        return trials;
    }

    /// <summary>The ingested form, identical to <c>MemoryLocomoBench</c>'s. A different rendering here would
    /// measure a text the engine never stores.</summary>
    private static string Render(MemoryLocomoBench.Turn t) =>
        $"[{t.Date}] ({t.DiaId}) {t.Speaker}: {t.Text}";

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length && i < b.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na <= 0 || nb <= 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>The N options as shown, by INDEX into the trial's option bench (0 = gold), with gold at a
    /// seeded slot. Indices rather than text because two turns can carry identical text, and a text lookup
    /// would then credit the wrong slot — silently, and only on the trials where it mattered.
    ///
    /// <para><b>The distractors are SHUFFLED before gold is inserted, not swapped into place.</b> Swapping
    /// slot 0 with gold's slot leaves every other distractor at its natural cosine rank, so the ARRANGEMENT
    /// of the competing options becomes a deterministic function of the gold slot — the hardest distractor
    /// sits next to gold at one slot and far from it at another. The competing set is identical either way;
    /// only a shuffle makes the position column measure position rather than arrangement.</para></summary>
    private static (List<int> Shown, int GoldSlot) Shown(int n, Random rng)
    {
        var shown = Enumerable.Range(1, n - 1).ToList();     // the first n-1 distractors, 0 = gold
        for (var i = shown.Count - 1; i > 0; i--)            // Fisher-Yates
        {
            var j = rng.Next(i + 1);
            (shown[i], shown[j]) = (shown[j], shown[i]);
        }
        var slot = rng.Next(n);
        shown.Insert(slot, 0);
        return (shown, slot + 1);
    }

    // ── the arms ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<List<Cell>> RunArmsAsync(
        IReadOnlyList<Trial> trials,
        SweepDoubles.OpenAiCompatibleChat big,
        SweepDoubles.OpenAiCompatibleChat? small,
        CrossEncoderReranker? reranker,
        SweepDoubles.CachingEmbedder embedder,
        int lanes)
    {
        var chats = new List<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)> { ("4b", big) };
        if (small is not null) chats.Add(("1b", small));

        // Every cell is materialised UP FRONT so one that never ran is distinguishable from one that ran and
        // scored zero — the same reason the fired counter exists a level down.
        var cells = new Dictionary<(string Arm, int N), Cell>();
        foreach (var n in ListLengths)
        {
            var names = new List<string> { "cosine", "random", "oracle" };
            foreach (var (label, _) in chats) { names.Add($"select-{label}"); names.Add($"score-{label}"); }
            if (reranker is not null) names.Add("rerank");
            foreach (var name in names) cells[(name, n)] = new Cell(name, n, trials.Count);
        }

        var done = 0;
        await Parallel.ForEachAsync(trials.Select((t, i) => (Trial: t, Index: i)),
            new ParallelOptions { MaxDegreeOfParallelism = lanes }, async (item, ct) =>
            {
                await RunOneTrialAsync(item.Trial, item.Index, chats, reranker, embedder, cells, ct);
                var seen = Interlocked.Increment(ref done);
                if (seen % 25 == 0) Console.WriteLine($"    {seen}/{trials.Count} trials");
            });

        return [.. cells.Values.OrderBy(c => c.Arm, StringComparer.Ordinal).ThenBy(c => c.N)];
    }

    private static async Task RunOneTrialAsync(
        Trial trial, int index,
        IReadOnlyList<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)> chats,
        CrossEncoderReranker? reranker,
        SweepDoubles.CachingEmbedder embedder,
        Dictionary<(string Arm, int N), Cell> cells,
        CancellationToken ct)
    {
        // One RNG per trial, seeded from its INDEX: the shown order must not depend on which thread ran it.
        var rng = new Random(Seed + index);
        var options = new List<string> { trial.Gold };
        options.AddRange(trial.Distractors);

        // Per-option scores are computed ONCE over gold plus every distractor and argmax'd inside each N's
        // subset. They do not depend on the list, which is exactly what makes score-a-pair a different shape
        // from select-from-list — and what lets seven scorings serve five list lengths.
        var scores = new Dictionary<string, double?[]>(StringComparer.Ordinal);
        foreach (var (label, chat) in chats)
        {
            var row = new double?[options.Count];
            for (var i = 0; i < options.Count; i++)
            {
                var reply = await ScoreOneAsync(chat, trial.Question, options[i], ct);
                if (_dump && index < DumpTrials) Dump($"score-{label}", index, reply ?? "(null)");
                row[i] = ReadDouble(reply, "score");
            }
            scores[$"score-{label}"] = row;
        }

        if (reranker is not null)
        {
            var row = new double?[options.Count];
            if (await reranker.RankAsync(trial.Question, options, ct) is { } ranked)
                foreach (var (i, score) in ranked) if (i >= 0 && i < row.Length) row[i] = score;
            scores["rerank"] = row;
        }

        var query = (await embedder.EmbedAsync([trial.Question], ct))[0];
        var vectors = await embedder.EmbedAsync(options, ct);
        scores["cosine"] = [.. vectors.Select(v => (double?)Cosine(query, v))];

        foreach (var n in ListLengths)
        {
            var (shown, goldSlot) = Shown(n, rng);
            foreach (var (label, chat) in chats)
                await SelectArmAsync(cells[($"select-{label}", n)], chat, trial, shown, options, goldSlot,
                    index, ct);
            foreach (var (arm, row) in scores) ArgmaxArm(cells[(arm, n)], row, shown, goldSlot, index, rng);
            Record(cells[("random", n)], index, fired: true, rng.Next(n) + 1 == goldSlot, goldSlot);

            // `oracle` goes through the SAME Shown -> ArgmaxArm path every real arm does, scoring 1 for the
            // gold option and 0 for the rest. Hardcoding `correct: true` made it a control that could only
            // fail on an empty run: an off-by-one in the slot arithmetic would collapse every real arm while
            // this one still printed 200/200. Now it exercises the plumbing it claims to prove.
            var truth = new double?[options.Count];
            for (var i = 0; i < truth.Length; i++) truth[i] = i == 0 ? 1.0 : 0.0;
            ArgmaxArm(cells[("oracle", n)], truth, shown, goldSlot, index, rng);
        }
    }

    /// <summary>One <c>select-from-list</c> call. A <c>0</c> choice, an unparseable reply or a dead endpoint
    /// is NOT FIRED rather than wrong — a fail-open seam that declined must never be scored as one that
    /// chose badly.</summary>
    private static async Task SelectArmAsync(Cell cell, SweepDoubles.OpenAiCompatibleChat chat, Trial trial,
        IReadOnlyList<int> shown, IReadOnlyList<string> options, int goldSlot, int index, CancellationToken ct)
    {
        var prompt = SelectPrompt(trial.Question, [.. shown.Select(i => options[i])]);
        var reply = await chat.AskAsync(prompt, ct, maxTokens: 16);
        if (_dump && index < DumpTrials) Dump($"{cell.Arm}@{cell.N}", index, reply ?? "(null)");
        var choice = ReadInt(reply, "choice");
        lock (cell) { cell.Calls++; cell.PromptChars += prompt.Length; }
        var fired = choice is > 0 && choice <= shown.Count;
        Record(cell, index, fired, fired && choice == goldSlot, goldSlot);
    }

    /// <summary>Argmax over the options shown at this N.
    ///
    /// <para><b>A tie at the maximum is a DECLINE, symmetrically with the select shape.</b> A scorer that
    /// cannot separate its top options has not chosen one, exactly as a model replying <c>0</c> has not —
    /// and scoring it as a coin flip would put an arm's accuracy half under the harness's own control
    /// rather than under the model. So a tied argmax is `fired: false` and counted; the coin-broken outcome
    /// is kept ALONGSIDE in <c>LenientHits</c>, because "it declined this often" and "it would have scored
    /// this much guessing" are both worth reading and neither substitutes for the other.</para></summary>
    private static void ArgmaxArm(Cell cell, double?[] scores, IReadOnlyList<int> shown, int goldSlot,
        int index, Random rng)
    {
        var seen = shown.Select(i => i >= 0 && i < scores.Length ? scores[i] : null).ToList();
        lock (cell) cell.Calls += seen.Count;
        if (seen.Any(s => s is null)) { Record(cell, index, fired: false, correct: false, goldSlot); return; }

        var best = seen.Max()!.Value;
        var winners = Enumerable.Range(0, seen.Count).Where(i => seen[i]!.Value >= best).ToList();
        var coin = winners[rng.Next(winners.Count)] + 1 == goldSlot;
        lock (cell) { if (winners.Count > 1) cell.Ties++; if (coin) cell.LenientHits++; }
        Record(cell, index, fired: winners.Count == 1, winners.Count == 1 && coin, goldSlot);
    }

    private static void Record(Cell cell, int index, bool fired, bool correct, int goldSlot)
    {
        lock (cell)
        {
            cell.Trials++;
            if (fired) cell.Fired++;
            if (correct) { cell.Hits++; cell.HitsByPosition[goldSlot]++; cell.Correct[index] = true; }
            cell.ShownByPosition[goldSlot]++;
        }
    }

    // ── prompts ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The forced-choice prompt. It states that exactly one option answers, because otherwise the
    /// "none" escape confounds the fired rate with accuracy — and it still offers <c>0</c>, so a model that
    /// cannot tell declines rather than guessing. Bench-local on purpose: the shipped judge prompt endorses
    /// a SUBSET and cannot express a forced choice at all.</summary>
    private static string SelectPrompt(string question, IReadOnlyList<string> options)
    {
        var numbered = options.Select((o, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {o}"));
        return $$"""
            You are given a question and a numbered list of candidate notes.
            EXACTLY ONE of the notes answers the question.

            Reply with ONLY a JSON object: {"choice": <number>}
            - <number> is the number of the single note that answers the question.
            - Use 0 only if you genuinely cannot tell which one it is.
            - Judge meaning, never spelling. The question and the notes may be in different languages.

            Question:
            {{question}}

            Notes:
            {{string.Join("\n", numbered)}}
            """;
    }

    /// <summary>The score-a-pair prompt. It mirrors the shipped <c>LlmScorerBase</c> shape — one pair, JSON
    /// only, no reason field — with one deliberate difference: the scale is an integer 0-100, not
    /// <c>LlmScorerBase</c>'s 0..1.
    ///
    /// <para><b>Measured, not chosen.</b> On the 0..1 scale both models replied <c>{"score": 0}</c> or
    /// <c>{"score": 1}</c> and NOTHING between, across every captured reply. A binary score cannot rank 7
    /// options, so the tie rate — and with it the whole score arm — would have been an artifact of the
    /// scale this prompt asked for rather than a property of the shape under test. A finer scale is the
    /// cheap input-shaping fix; whether the model USES it is then a real result.</para></summary>
    private static Task<string?> ScoreOneAsync(SweepDoubles.OpenAiCompatibleChat chat, string question,
        string note, CancellationToken ct) =>
        chat.AskAsync($$"""
            SCORING TASK: judge how well the note answers the question, 0 (does not answer it at all) to
            100 (fully answers it). Use the whole range — a note that is merely on the same topic scores
            low, and only a note that actually answers scores high.

            Reply with ONLY a JSON object: {"score": <integer between 0 and 100>}

            [question]
            {{question}}

            [note]
            {{note}}
            """, ct, maxTokens: 16);

    // ── parsing ───────────────────────────────────────────────────────────────────────────────────────────

    private static JsonElement? Body(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var open = text.IndexOf('{', StringComparison.Ordinal);
        var close = text.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        try { return JsonDocument.Parse(text[open..(close + 1)]).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static int? ReadInt(string? text, string name) =>
        Body(text) is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var i) ? i : null;

    private static double? ReadDouble(string? text, string name) =>
        Body(text) is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ── reporting ─────────────────────────────────────────────────────────────────────────────────────────

    private static void PrintPreamble(string path, SweepDoubles.OpenAiCompatibleChat big,
        SweepDoubles.OpenAiCompatibleChat? small, bool rerankOk, Difficulty difficulty, int sampled, int lanes)
    {
        Console.WriteLine("\nmemory-decision — which SHAPE wins a decision at 3-7 options, and at what size\n");
        Console.WriteLine($"  corpus     : {path}, {sampled} question(s) sampled");
        Console.WriteLine($"  lengths    : N = {string.Join(", ", ListLengths)}   (chance = 1/N)");
        Console.WriteLine($"  difficulty : {difficulty.ToString().ToLowerInvariant()}"
            + (difficulty == Difficulty.Hard
                ? "  — distractors are the top non-gold turns by cosine, what a retriever hands you"
                : "  — distractors drawn uniformly; this is the INSTRUMENT CHECK, not a result"));
        Console.WriteLine($"  big        : {big.Model}");
        Console.WriteLine(small is null
            ? "  small      : SKIPPED — set LYNTAI_LIVE_SMALL_URL to add the small-model arms"
            : $"  small      : {small.Model}");
        Console.WriteLine(rerankOk
            ? $"  rerank     : {CrossEncoderReranker.Model} at {CrossEncoderReranker.BaseUrl}"
            : "  rerank     : SKIPPED — nothing usable answered /v1/rerank");
        Console.WriteLine($"  concurrency: {lanes} in-flight request(s)\n");
        Console.WriteLine("  How to read this. `random` must land on 1/N and `oracle` on 100% — the structural");
        Console.WriteLine("  null and the can-it-move control, and a run where either misses is VOID rather");
        Console.WriteLine("  than surprising. `fired` separates a seam that disagreed from one that never");
        Console.WriteLine("  answered; accuracy is reported over FIRED trials.");
    }

    /// <summary>How hard the trials actually were, printed rather than asserted. Gold's own cosine rank is
    /// the honest difficulty column: under <c>hard</c> the distractors are the top ranks minus gold, so a
    /// gold sitting deep is a trial where the retriever was already wrong.</summary>
    private static void PrintTrialProfile(IReadOnlyList<Trial> trials, Difficulty difficulty, int sampled)
    {
        var ranks = trials.Select(t => (double)t.GoldRank).ToList();
        var top1 = trials.Count(t => t.GoldRank == 1);
        var names = new[] { "", "multi-hop", "temporal", "open-domain", "single-hop" };
        Console.WriteLine($"\n  trials built: {trials.Count} of {sampled} sampled "
            + $"({sampled - trials.Count} dropped: no evidence id, MORE THAN ONE evidence turn, or a "
            + "conversation too small for N = 7)");
        Console.WriteLine("  category mix: " + string.Join(", ", trials.GroupBy(t => t.Category)
            .OrderBy(g => g.Key).Select(g => $"{names[g.Key]} {g.Count()}"))
            + "   — multi-hop is expected to be NEAR ZERO: 97.9% of it carries several evidence turns, so it "
            + "cannot pose a forced choice");
        Console.WriteLine($"  gold cosine rank: median {BenchStats.Percentile(ranks, 0.5):F0}, "
            + $"p90 {BenchStats.Percentile(ranks, 0.9):F0}, max {ranks.Max():F0}; "
            + $"top-1 in {top1} of {trials.Count} ({(double)top1 / trials.Count:P1})");
        if (difficulty == Difficulty.Hard)
            Console.WriteLine("  under `hard`, `cosine` can only be right when gold IS rank 1 — that cell is a "
                + "difficulty readout, not a competitive arm.");
    }

    private static void PrintAccuracyTable(IReadOnlyList<Cell> cells, int trials)
    {
        Console.WriteLine($"\nACCURACY over FIRED trials, n = {trials} per cell\n");
        Console.WriteLine($"{"arm",-12} {"N",2} {"chance",7} {"fired",12} {"accuracy",9} {"95% CI",18} {"lift",7}");
        Console.WriteLine(new string('-', 80));
        foreach (var group in cells.GroupBy(c => c.Arm))
        {
            foreach (var cell in group.OrderBy(c => c.N))
            {
                var chance = 1.0 / cell.N;
                var accuracy = cell.Fired == 0 ? 0 : (double)cell.Hits / cell.Fired;
                var (low, high) = BenchStats.Wilson(cell.Hits, cell.Fired);
                var firedRate = cell.Trials == 0 ? 0 : (double)cell.Fired / cell.Trials;
                Console.WriteLine($"{group.Key,-12} {cell.N,2} {chance,7:P0} {$"{cell.Fired}/{cell.Trials}",12} "
                    + $"{accuracy,9:P1} {$"[{low:P0}, {high:P0}]",18} {accuracy / chance,6:F2}x"
                    + (firedRate < 0.9 ? "   ← FIRED BELOW 90%: accuracy UNREADABLE" : ""));
            }
            Console.WriteLine();
        }
        Console.WriteLine("  lift is capped at N (accuracy <= 100%), so it is NOT comparable across rows of");
        Console.WriteLine("  different N: 3.00x at N = 3 is a perfect score. Read accuracy against chance.");
    }

    /// <summary>The controls, in one block, so a reader can void the run without reading the result table
    /// first.</summary>
    private static void PrintControls(IReadOnlyList<Cell> cells, CrossEncoderReranker reranker, bool rerankOk)
    {
        Console.WriteLine("\nCONTROLS — a miss here VOIDS the run\n");
        var voided = false;
        foreach (var cell in cells.Where(c => c.Arm == "random").OrderBy(c => c.N))
        {
            var (low, high) = BenchStats.Wilson(cell.Hits, cell.Fired);
            var chance = 1.0 / cell.N;
            var ok = chance >= low && chance <= high;
            voided |= !ok;
            Console.WriteLine($"  random  N={cell.N}: {(double)cell.Hits / Math.Max(1, cell.Fired),6:P1} "
                + $"[{low:P0}, {high:P0}] vs 1/N = {chance:P0}   {(ok ? "ok" : "*** OUTSIDE ***")}");
        }
        foreach (var cell in cells.Where(c => c.Arm == "oracle").OrderBy(c => c.N))
        {
            var ok = cell.Hits == cell.Trials && cell.Trials > 0;
            voided |= !ok;
            Console.WriteLine($"  oracle  N={cell.N}: {cell.Hits}/{cell.Trials}   "
                + (ok ? "ok" : "*** NOT 100% — the gold labelling or the argmax is wrong ***"));
        }

        Console.WriteLine("\n  select prompt size must RISE with N, or the list length never reached the model:");
        foreach (var group in cells.Where(c => c.Arm.StartsWith("select-", StringComparison.Ordinal))
                     .GroupBy(c => c.Arm))
        {
            var sizes = group.OrderBy(c => c.N)
                .Select(c => c.Calls == 0 ? 0 : (double)c.PromptChars / c.Calls).ToList();
            var rising = sizes.Zip(sizes.Skip(1), (a, b) => b > a).All(x => x);
            voided |= !rising;
            Console.WriteLine($"    {group.Key,-12} {string.Join(" -> ", sizes.Select(s => $"{s:F0}"))} chars   "
                + (rising ? "ok" : "*** NOT MONOTONE ***"));
        }

        Console.WriteLine("\n  ties — a scorer that cannot separate its top options has DECLINED, so these are");
        Console.WriteLine("  counted as not-fired above. `coin` is what the arm would score guessing among the");
        Console.WriteLine("  tied winners, over ALL trials — the lenient reading, never the headline one:");
        if (cells.All(c => c.Ties == 0)) Console.WriteLine("    none on any arm");
        foreach (var group in cells.Where(c => c.Ties > 0).GroupBy(c => c.Arm))
            Console.WriteLine($"    {group.Key,-12} " + string.Join("  ", group.OrderBy(c => c.N)
                .Select(c => $"N={c.N}: {c.Ties}/{c.Trials} tied, coin {(double)c.LenientHits / Math.Max(1, c.Trials):P1}")));

        if (rerankOk)
        {
            var (calls, scored, distinct) = reranker.Audit;
            Console.WriteLine($"\n  rerank audit: {calls} call(s), {scored} pair(s) scored, {distinct} distinct "
                + "score(s) — a flat cross-encoder is a broken conversion, not a weak model");
        }
        if (voided) Console.WriteLine("\n  *** AT LEAST ONE CONTROL FAILED — this run is not a result. ***");
    }

    /// <summary>The question the item actually asks: one call over N options, or N calls argmax'd. Paired by
    /// construction — both shapes answered the same trials with the same options — so the test is McNemar
    /// over the trials they disagreed on.</summary>
    private static void PrintShapeComparison(IReadOnlyList<Cell> cells)
    {
        Console.WriteLine("\nSHAPE: select-from-list (1 call) vs score-a-pair (N calls), paired\n");
        Console.WriteLine($"{"model",-6} {"N",2} {"select",8} {"score",8} {"net",6} {"discordant",11} {"p",10}");
        Console.WriteLine(new string('-', 58));
        foreach (var label in new[] { "4b", "1b" })
        {
            foreach (var n in ListLengths)
            {
                var select = cells.FirstOrDefault(c => c.Arm == $"select-{label}" && c.N == n);
                var score = cells.FirstOrDefault(c => c.Arm == $"score-{label}" && c.N == n);
                if (select is null || score is null) continue;

                var onlySelect = select.Correct.Where((v, i) => v && !score.Correct[i]).Count();
                var onlyScore = score.Correct.Where((v, i) => v && !select.Correct[i]).Count();
                var net = onlySelect - onlyScore;
                Console.WriteLine($"{label,-6} {n,2} "
                    + $"{(double)select.Hits / Math.Max(1, select.Trials),8:P1} "
                    + $"{(double)score.Hits / Math.Max(1, score.Trials),8:P1} "
                    + $"{(net > 0 ? $"+{net}" : net.ToString(CultureInfo.InvariantCulture)),6} "
                    + $"{onlySelect + onlyScore,11} "
                    + $"{BenchStats.Format(BenchStats.McNemarExact(onlySelect, onlyScore)),10}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("  Only the DISCORDANT trials carry information — both shapes getting a trial right");
        Console.WriteLine("  says nothing about which shape is better. `net` is select minus score.");
    }

    /// <summary>Position bias, AT ONE N rather than pooled over them.
    ///
    /// <para><b>Pooling confounds slot with list length</b> and the first version of this table did: slot 7
    /// occurs only at N = 7, slot 1 at every N, and accuracy falls with N — so a pooled row shows a
    /// downward slope on an arm with no position dependence at all. Reading it at the largest N, where
    /// every slot exists and every cell shares one list length, removes the confound outright.</para>
    ///
    /// <para>The argmax arms are the control: an argmax over per-option scores CANNOT depend on the order
    /// they were shown in, so whatever slope they carry is sampling noise and a select arm's excess over it
    /// is the bias.</para></summary>
    private static void PrintPositionBias(IReadOnlyList<Cell> cells)
    {
        var n = ListLengths[^1];
        Console.WriteLine($"\nPOSITION of the gold option — accuracy by slot, at N = {n} ONLY\n");
        Console.WriteLine("  arm            slot 1  slot 2  slot 3  slot 4  slot 5  slot 6  slot 7");
        foreach (var cell in cells.Where(c => c.N == n && c.Arm != "random" && c.Arm != "oracle")
                     .OrderBy(c => c.Arm, StringComparer.Ordinal))
            Console.WriteLine($"  {cell.Arm,-12} " + string.Join(" ", Enumerable.Range(1, n)
                .Select(s => cell.ShownByPosition[s] == 0 ? "     -"
                    : $"{(double)cell.HitsByPosition[s] / cell.ShownByPosition[s],6:P0}")));

        // `ShownByPosition[s]` is ALREADY the per-slot trial count — an earlier version divided it by N
        // again and published "about 5 trial(s)" for cells holding roughly 37.
        var perSlot = cells.Where(c => c.N == n).Select(c => c.ShownByPosition.Skip(1).Take(n).Max()).Max();
        var (lo, hi) = BenchStats.Wilson(perSlot / 2, perSlot);
        Console.WriteLine($"\n  Each cell holds about {perSlot} trial(s) — a 95% interval on a mid-range "
            + $"rate there spans roughly {(hi - lo):P0}, so only a LARGE gap is readable and the rest is noise.");
        Console.WriteLine("  Every cell is one list length, so a slope is not the list getting longer. The");
        Console.WriteLine("  argmax arms cannot depend on the order shown, so they are the noise floor.");
    }

    /// <summary>Writes the raw replies, or says why it wrote nothing — a flag parsed and read by nothing
    /// compiles silently, and an empty file would read as "the models said nothing".</summary>
    private static async Task WriteDumpAsync()
    {
        if (!_dump) return;
        if (Dumped.IsEmpty)
        {
            Console.WriteLine("\n  --dump: nothing recorded — no model arm ran on the first trials.");
            return;
        }
        var to = Path.Combine("devtools", $"_decision-dump-{DateTime.Now:HHmmss}.jsonl");
        await File.WriteAllLinesAsync(to, Dumped.OrderBy(l => l, StringComparer.Ordinal));
        Console.WriteLine($"\n  --dump: {Dumped.Count} raw reply(ies) → {to}");
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - ONE corpus (LoCoMo), one language, one embedder choosing the distractors.");
        Console.WriteLine("  - ONE prompt per shape. Both are bench-local: the shipped judge prompt endorses a");
        Console.WriteLine("    SUBSET and cannot express a forced choice, so it could not have been reused.");
        Console.WriteLine("  - Temperature 0 throughout; no sampling-variance arm, and requests run");
        Console.WriteLine("    concurrently, so batch composition can move a near-tie token.");
        Console.WriteLine("  - Accuracy is the metric, so a busy device costs WALL CLOCK and not validity —");
        Console.WriteLine("    every cell shares one backend. No latency figure here is comparable elsewhere.");
        Console.WriteLine("  - NO affordance-shaped arm (given these tools, what do you want). That is Part");
        Console.WriteLine("    178's third item and is deliberately untouched.");
    }
}
