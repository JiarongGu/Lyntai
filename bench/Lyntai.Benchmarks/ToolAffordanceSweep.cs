using Lyntai.Lifecycle;
using System.Globalization;
using System.Text.Json;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Benchmarks;

/// <summary>What a small model does with a roster of 3-7 tools, through the PROMPT protocol
/// <see cref="ToolLoop"/> authors itself.
///
/// <para><b>The shape has no evidence at any size</b> (<c>docs/model-tasks.md</c> §1), and it is the one
/// this library does not bound — "per model tool call, unbounded by this library". The native transport is
/// blocked on a positive control this machine does not hold, so the prompt half is measured: it needs no
/// template support, it is the path a gemma-class deployment actually gets, and it is a prompt this
/// repository owns.</para>
///
/// <para><b>Every loop arm runs the REAL <see cref="ToolLoop"/> over a real <see cref="ToolRegistry"/>.</b>
/// Only the <see cref="ILlmClient"/> is the bench's, which is what puts the loop on its prompt path
/// (<c>SupportsToolCalls</c> defaults to false) and what lets the prompt the transport actually sent be
/// counted rather than reconstructed.</para>
///
/// <para><b>The cross-shape arm is the point.</b> The same trials posed as a plain <c>select-from-list</c>
/// separate the cost of the TOOL TRANSPORT from the cost of choosing — without it, a bad number cannot be
/// attributed to either. <c>docs/task-archive.md</c> Part 236.</para></summary>
internal static class ToolAffordanceSweep
{
    private const int Seed = 20260912;

    /// <summary>The tool-capable model's arm label. One constant, because it names three arms and a
    /// mismatch between them would leave a cell that never ran looking like one that scored zero.</summary>
    private const string NativeLabel = "tool";

    /// <summary>Roster sizes. Nested exactly as <c>memory-decision</c>'s: a trial holds gold plus
    /// <see cref="MaxDistractors"/> ordered distractors and N = 3 uses the first two, so the cells share
    /// their distractors by construction and stay paired.
    ///
    /// <para><b>Settable by <c>--roster</c>, and the DEFAULT is the published ladder</b> — a run that
    /// passes nothing reproduces every table already in the record. The ceiling is a property of the
    /// FIXTURE and differs by difficulty: <c>hard</c> draws distractors from the gold tool's own family, so
    /// it cannot exceed <see cref="ToolAffordanceCorpus.FamilySize"/> and that is a design choice rather
    /// than an oversight — the distractors are meant to be the hardest available. <c>easy</c> draws from
    /// every OTHER family, so its ceiling is the rest of the corpus. <see cref="RosterCeiling"/> is where
    /// that is enforced, because an over-large roster silently yields ZERO trials otherwise.</para></summary>
    private static int[] RosterSizes { get; set; } = [3, 4, 5, 6, 7];

    /// <summary>Distractors a trial carries, derived from the largest roster asked for so the cells stay
    /// nested however the ladder is set.</summary>
    private static int MaxDistractors => RosterSizes[^1] - 1;

    /// <summary>The largest roster this fixture can fill at <paramref name="difficulty"/>, and the reason
    /// the two differ. Enforced rather than documented because the failure is SILENT: the trial builder
    /// skips any gold whose pool is short, so a roster one past the ceiling produces an empty table rather
    /// than an error.</summary>
    private static int RosterCeiling(Difficulty difficulty) => difficulty == Difficulty.Hard
        ? ToolAffordanceCorpus.FamilySize
        : 1 + ToolAffordanceCorpus.Tools.Count - ToolAffordanceCorpus.FamilySize;

    /// <summary>Iterations a loop arm is given. TWO, not one: the first turn is the choice this sweep
    /// scores, and the second is what makes the run a real loop — the model receives the observation and is
    /// expected to finish. A budget of one would record the step and then report non-convergence on every
    /// trial, which prices the budget rather than the model.</summary>
    private const int LoopIterations = 2;

    /// <summary>Output cap for a loop turn. Generous on purpose: a truncated <c>{"tool":…}</c> object is
    /// unparseable, which sends <c>CompleteJsonAsync</c> into its repair round and then reads as a model
    /// that would not call a tool — the cap would be published as the finding.</summary>
    private const int LoopMaxTokens = 192;

    /// <summary>The candidate instruction half, run as a PAIRED arm against the shipped one rather than
    /// swapped in and compared across runs — this grid's run-to-run noise floor is unmeasured, so a
    /// cross-run delta could not be told from it.
    ///
    /// <para><b>The ESCAPE comes first, because that is where the shipped prompt actually fails.</b> It
    /// opens "You can use tools to answer the request", which presupposes a tool answers it and never says
    /// what to do when none fits — and a 4B then calls one on <b>90-95%</b> of requests no tool can serve.
    /// The 1B fails the opposite way (0-5% false calls, 81-93% no-tool where a tool DOES fit), so the last
    /// clause keeps the push and targets its captured failure: <c>{"final": "…Please wait a moment while I
    /// retrieve the data."}</c>, a model that has decided to use a tool and cannot express it.</para>
    ///
    /// <para><b>A first candidate pushed HARDER toward tool use and was refuted</b> — it took the 4B's false
    /// calls from 95% to 100% for a gain significant in 1 of 10 cells. That direction is recorded so it is
    /// not retried: the diagnosis was read off the 1B's no-tool rate alone, and the negative trials are what
    /// caught it.</para></summary>
    internal const string CandidatePreamble =
        "You may use tools to answer the request, but ONLY when one of them genuinely fits it.\n"
        + "If no tool below fits, answer the request directly. Do NOT call a tool that is merely related to "
        + "the same topic — calling the wrong tool is worse than answering without one.\n"
        + "When a tool does fit, call it instead of answering from your own knowledge, and never reply "
        + "saying you are about to look something up.\n"
        + "On each turn reply with EXACTLY ONE JSON object and nothing else, in one of these two forms:\n"
        + "  to call a tool:      {\"tool\": \"<name>\", \"arguments\": { ... }}\n"
        + "  for the final answer: {\"final\": \"<answer>\"}\n"
        + "After a tool call you receive its result, then continue. Only call tools listed below.\n";

    /// <summary>What a loop arm's client must be able to tell the harness, so the two transports report
    /// through one path. Extracted when the native arm landed: the reporting code cast to the prompt
    /// client's concrete type, so a second client would have silently contributed zero calls, zero prompt
    /// characters and — worst — zero ERRORS, which is the counter that keeps a fail-open arm honest.</summary>
    private interface ICountedLoopClient
    {
        int Calls { get; }

        long FirstPromptChars { get; }

        string? FirstReply { get; }

        /// <summary>Calls the endpoint could not answer. A trial with any of these is never scored as the
        /// model declining — that conflation would report a stalled socket as an affordance decision.</summary>
        int Errors { get; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<string> Dumped = [];

    private const int DumpTrials = 3;

    private static bool _dump;

    private static void Dump(string arm, int index, string reply) =>
        Dumped.Add(JsonSerializer.Serialize(new { arm, trial = index, reply }));

    /// <summary>How confusable the wrong tools are. <c>hard</c> shows the gold's own family — seven tools
    /// authored to be adjacent — ordered by cosine against the request. <c>easy</c> draws from OTHER
    /// families, and is the instrument check: an arm that cannot win there is broken rather than beaten.
    /// </summary>
    private enum Difficulty { Hard, Easy }

    /// <summary>One request, the single tool that serves it, and the distractor bench it draws from — both
    /// as indices into <see cref="ToolAffordanceCorpus.Tools"/>. <c>GoldRank</c> is the gold's own cosine
    /// rank across all 42 declarations, printed so a reader can see how much surface leakage the requests
    /// left rather than taking "does not echo the label" on trust.</summary>
    private sealed record Trial(
        string Request, int Gold, IReadOnlyList<int> Distractors, string Family, int GoldRank);

    /// <summary>What one arm did at one roster size.
    ///
    /// <para><c>Fired</c> is the count that keeps a fail-open transport honest: a loop that never invoked a
    /// tool has DECLINED, and scoring it as a wrong choice would flatter every other arm. The three failure
    /// modes a forced choice cannot express are counted beside it — <c>NoTool</c>, <c>Hallucinated</c> (a
    /// name that resolved to nothing) and <c>Malformed</c> (the right tool, unusable arguments).</para>
    ///
    /// <para><c>Correct</c> is indexed BY TRIAL rather than appended, so two arms stay paired under a
    /// parallel loop and McNemar has something to test.</para></summary>
    private sealed record Cell(string Arm, int N, int Capacity)
    {
        internal int Trials, Fired, Hits, Ties, Calls;
        internal int NoTool, Hallucinated, Malformed, Converged, Recovered, Failed, Truncated;
        internal long FirstPromptChars;
        internal readonly int[] HitsByPosition = new int[MaxDistractors + 2];
        internal readonly int[] ShownByPosition = new int[MaxDistractors + 2];
        internal readonly bool[] Correct = new bool[Capacity];

        internal bool IsLoop => Arm.StartsWith("loop-", StringComparison.Ordinal);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var problems = ToolAffordanceCorpus.Audit();
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("tool-affordance: the corpus is structurally unmeasurable —");
            foreach (var p in problems) Console.Error.WriteLine($"  - {p}");
            Console.Error.WriteLine("  Refusing to run: a duplicate name makes a gold unreachable through the");
            Console.Error.WriteLine("  registry, and a short family cannot fill a roster of seven.");
            return 1;
        }

        var difficulty = ArgValue(args, "--difficulty") == "easy" ? Difficulty.Easy : Difficulty.Hard;

        // `--roster 3,7,14,...` takes the ladder to CATALOGUE scale, which is where bounding a tool roster
        // would actually matter — the published cells stop at seven and a deployment with a catalogue is the
        // case the selector question was asked about (docs/task-archive.md Part 236).
        //
        // Validated against the fixture rather than trusted: `hard` draws its distractors from the gold
        // tool's own family, so it cannot go past FamilySize, and asking for more makes the trial builder
        // skip EVERY gold and print an empty table instead of failing.
        if (ArgValue(args, "--roster") is { } spec)
        {
            var wanted = new List<int>();
            foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var size) || size < 2)
                {
                    Console.Error.WriteLine($"--roster: '{part}' is not a roster size of at least 2.");
                    return 1;
                }

                wanted.Add(size);
            }

            var ceiling = RosterCeiling(difficulty);
            var over = wanted.Where(n => n > ceiling).ToList();
            if (over.Count > 0)
            {
                Console.Error.WriteLine($"--roster: {string.Join(", ", over)} exceed(s) what the "
                    + $"{(difficulty == Difficulty.Hard ? "hard" : "easy")} fixture can fill (ceiling {ceiling}). "
                    + (difficulty == Difficulty.Hard
                        ? "`hard` draws distractors from the gold tool's OWN family, which holds "
                          + $"{ToolAffordanceCorpus.FamilySize}; that is the design, not a limit to raise. "
                          + "Use --difficulty easy for a catalogue-scale roster."
                        : $"The corpus holds {ToolAffordanceCorpus.Tools.Count} tools and a gold's own family "
                          + "is excluded."));
                return 1;
            }

            RosterSizes = [.. wanted.Distinct().Order()];
        }

        var lanes = ArgValue(args, "--concurrency") is { } c && int.TryParse(c, out var l) ? Math.Max(1, l) : 4;
        var cap = ArgValue(args, "--n") is { } n && int.TryParse(n, out var parsed) ? parsed : int.MaxValue;
        _dump = args.Contains("--dump");

        // `--scorers-only` drops every arm that calls a chat model — the loop arms, the cross-shape arms
        // and the whole negative corpus — leaving the model-free scorers plus the two scripted controls.
        // It exists because the EMBEDDER axis is a survey (`docs/task-archive.md` Part 196): a sub-100 MB
        // candidate is one more `cosine-*` column, and paying ~2 hours of generation per candidate would
        // make the axis unaffordable. The dropped arms are NAMED in the output, never silently absent.
        var scorersOnly = args.Contains("--scorers-only");

        // `--skip-baseline` drops the 4B and 1B arms while keeping everything else. Their figures are
        // already published and they cost ~2 hours; the NATIVE transport question needs neither, because
        // neither model can take that path at all — gemma-3's template carries no tool section.
        var skipBaseline = args.Contains("--skip-baseline");

        // UseProxy=false is not decoration: proxy resolution against a local endpoint measured up to
        // 2,051 ms per call and is BIMODAL, so it reads as the model's own tail.
        //
        // TWO MINUTES, not the five every other sweep here takes, and the difference was paid for: a single
        // call stalled past 300 s on a saturated device and its unhandled exception discarded 90 minutes of
        // a completed run. A call that slow is unusable either way, so the cheaper failure is the right one
        // — and it is COUNTED rather than absorbed (see `Cell.Failed`), because a silently dropped trial is
        // the one thing worse than a dropped run.
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };

        var vectorProvider = await SweepDoubles.TryRealVectorProviderAsync(http, "tool-affordance");
        if (vectorProvider is null) return 1;
        var extra = await SweepDoubles.TryExtraVectorProvidersAsync(http, "tool-affordance");
        if (extra is null) return 1;
        // Not resolved at all under `--scorers-only`: that mode calls no chat model, and DEMANDING one
        // meant the orchestrator had to serve ~3.3 GB of weights it never touched — which is what made the
        // cheap mode refuse to start on a machine whose GPU was merely busy. A mode that exists to be
        // affordable must not pay the full bill.
        var big = scorersOnly ? null : await SweepDoubles.TryRealChatAsync(http, "tool-affordance");
        if (!scorersOnly && big is null) return 1;
        var small = TrySmallChat(http);
        var nativeChat = TryNativeChat(http);

        var reranker = new CrossEncoderReranker(http, CrossEncoderReranker.BaseUrl, CrossEncoderReranker.Model);
        var rerankOk = await reranker.ReachableAsync();
        reranker.Reset();   // the probe's own two pairs must not count toward the measured region's audit

        var trials = await BuildTrialsAsync(vectorProvider, difficulty, cap);
        PrintPreamble(big, small, rerankOk, difficulty, trials.Count, lanes, extra, scorersOnly,
            scorersOnly ? null : nativeChat, skipBaseline);
        PrintTrialProfile(trials, difficulty);

        // `cosine` is the PRIMARY vector backend — the one that also built the trials — so its name is kept and
        // its figure stays comparable to every published table. The extras vary only the scoring.
        var scorers = new List<(string Arm, SweepDoubles.CachingVectorProvider VectorProvider)> { ("cosine", vectorProvider) };
        scorers.AddRange(extra.Select(e => ($"cosine-{e.Label}", e.VectorProvider)));

        // CENTERING, as a paired arm on EVERY vector backend rather than a rescue for the weak one. An embedding
        // space is anisotropic — every vector shares a large common component — so cosine measures that
        // shared direction as much as the text. Subtracting the corpus centroid removes it, and the reason
        // it is run on the incumbent too is that a lever which only ever gets tried on a small model can
        // never be shown to transfer. The centroid is over all 42 declarations, computed ONCE per vector backend.
        var centroids = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var (arm, scorer) in scorers)
            centroids[arm] = Centroid(await scorer.EmbedAsync(
                [.. ToolAffordanceCorpus.Tools.Select(ToolAffordanceCorpus.Declaration)]));

        var baseline = !scorersOnly && !skipBaseline;
        var cells = await RunArmsAsync(trials, baseline ? big : null, baseline ? small : null,
            scorersOnly ? null : nativeChat, rerankOk ? reranker : null, scorers, centroids, lanes);
        var negativeNatives = scorersOnly || nativeChat is null
            ? []
            : new[] { (NativeLabel, nativeChat) };
        var falseCalls = await RunNegativeTrialsAsync(
            baseline ? big : null, baseline ? small : null, negativeNatives, lanes);

        PrintAccuracyTable(cells, trials.Count);
        PrintFalseCalls(falseCalls);
        PrintControls(cells, reranker, rerankOk);
        PrintTransportFailures(cells);
        PrintWhereFreeArmFails(cells);
        PrintPreambleComparison(cells);
        PrintShapeComparison(cells);
        PrintTransportComparison(cells);
        PrintPositionBias(cells);
        PrintNotSwept();
        await WriteOutcomesAsync();
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

    /// <summary>The TOOL-CAPABLE model, if one is served — the only arm that can take the loop's native
    /// path. Absent is a SKIPPED arm with a printed reason, never a silent substitution: a model whose
    /// chat template has no tool section returns HTTP 200 with <c>tool_calls: null</c> and answers anyway,
    /// so a native arm quietly backed by the wrong model would read as the transport failing.</summary>
    private static SweepDoubles.OpenAiCompatibleChat? TryNativeChat(HttpClient http)
    {
        var url = Environment.GetEnvironmentVariable("LYNTAI_LIVE_NATIVE_URL");
        if (string.IsNullOrWhiteSpace(url)) return null;
        var model = Environment.GetEnvironmentVariable("LYNTAI_LIVE_NATIVE_MODEL") ?? "native";
        return new SweepDoubles.OpenAiCompatibleChat(http, url, model);
    }

    // ── trial construction ────────────────────────────────────────────────────────────────────────────────

    /// <summary>One trial per authored request. The distractor bench is ORDERED by cosine against the
    /// request so the nested roster sizes show the most confusable siblings first — which is what makes
    /// N = 3 a harder cell per option than N = 7, not merely a shorter one.</summary>
    private static async Task<List<Trial>> BuildTrialsAsync(
        SweepDoubles.CachingVectorProvider vectorProvider, Difficulty difficulty, int cap)
    {
        var tools = ToolAffordanceCorpus.Tools;
        var declarations = await vectorProvider.EmbedAsync(
            [.. tools.Select(ToolAffordanceCorpus.Declaration)]);

        var rng = new Random(Seed);
        var trials = new List<Trial>();

        for (var gold = 0; gold < tools.Count; gold++)
        {
            foreach (var request in tools[gold].Requests)
            {
                var query = (await vectorProvider.EmbedAsync([request]))[0];
                var ranked = Enumerable.Range(0, tools.Count)
                    .Select(i => (Index: i, Score: Cosine(query, declarations[i])))
                    .OrderByDescending(r => r.Score).ToList();

                var pool = difficulty == Difficulty.Hard
                    ? ranked.Where(r => r.Index != gold && tools[r.Index].Family == tools[gold].Family)
                        .Select(r => r.Index)
                    : Enumerable.Range(0, tools.Count)
                        .Where(i => tools[i].Family != tools[gold].Family)
                        .OrderBy(_ => rng.Next());

                var distractors = pool.Take(MaxDistractors).ToList();
                if (distractors.Count < MaxDistractors) continue;   // unreachable while Audit() passes

                trials.Add(new Trial(request, gold, distractors, tools[gold].Family,
                    ranked.FindIndex(r => r.Index == gold) + 1));
            }
        }

        // Capped by STRIDE rather than by truncation, so a smoke run keeps every family and every tool in
        // proportion — taking the first k would measure the weather family alone.
        if (cap >= trials.Count) return trials;
        var stride = (double)trials.Count / cap;
        return [.. Enumerable.Range(0, cap).Select(i => trials[(int)(i * stride)])];
    }

    /// <summary>The mean vector of a set — the direction every embedding in this corpus shares.</summary>
    private static float[] Centroid(IReadOnlyList<float[]> vectors)
    {
        var mean = new float[vectors[0].Length];
        foreach (var v in vectors)
            for (var i = 0; i < mean.Length && i < v.Length; i++) mean[i] += v[i];
        for (var i = 0; i < mean.Length; i++) mean[i] /= vectors.Count;
        return mean;
    }

    private static float[] Subtract(float[] v, float[] mean)
    {
        var result = new float[v.Length];
        for (var i = 0; i < v.Length; i++) result[i] = v[i] - (i < mean.Length ? mean[i] : 0f);
        return result;
    }

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

    /// <summary>The N tools as shown, by INDEX into the trial's option bench (0 = gold), with gold at a
    /// seeded slot. The distractors are SHUFFLED before gold is inserted rather than swapped into place:
    /// swapping leaves every other distractor at its natural cosine rank, so the ARRANGEMENT becomes a
    /// deterministic function of the gold slot and the position column stops measuring position.</summary>
    private static (List<int> Shown, int GoldSlot) Shown(int n, Random rng)
    {
        var shown = Enumerable.Range(1, n - 1).ToList();
        for (var i = shown.Count - 1; i > 0; i--)
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
        SweepDoubles.OpenAiCompatibleChat? big,
        SweepDoubles.OpenAiCompatibleChat? small,
        SweepDoubles.OpenAiCompatibleChat? nativeChat,
        CrossEncoderReranker? reranker,
        IReadOnlyList<(string Arm, SweepDoubles.CachingVectorProvider VectorProvider)> scorers,
        IReadOnlyDictionary<string, float[]> centroids,
        int lanes)
    {
        // Empty under `--scorers-only`, which is what drops every model arm — the arm NAMES are built
        // from this list, so a skipped arm does not appear as a cell that ran and scored zero.
        var chats = new List<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)>();
        if (big is not null) chats.Add(("4b", big));
        if (small is not null) chats.Add(("1b", small));
        // The tool-capable model is its own list: it gets a NATIVE arm the others cannot take, and it does
        // not get the preamble arm, whose question is settled and belongs to the prompt protocol.
        var natives = new List<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)>();
        if (nativeChat is not null) natives.Add((NativeLabel, nativeChat));

        // Materialised UP FRONT so a cell that never ran is distinguishable from one that ran and scored
        // zero — the same reason the fired counter exists a level down.
        var cells = new Dictionary<(string Arm, int N), Cell>();
        foreach (var n in RosterSizes)
        {
            var names = new List<string>(scorers.SelectMany(s => new[] { s.Arm, $"{s.Arm}-centered" }))
                { "loop-random", "loop-oracle" };
            foreach (var (label, _) in chats)
            {
                names.Add($"loop-{label}");
                names.Add($"loop-{label}-v2");   // the candidate preamble, PAIRED on the same trials
                names.Add($"select-{label}");
            }
            foreach (var (label, _) in natives)
            {
                names.Add($"loop-{label}");          // the SAME model on the prompt protocol
                names.Add($"loop-{label}-native");   // …and on native function-calling, same trials
                names.Add($"select-{label}");
            }
            if (reranker is not null) names.Add("rerank");
            foreach (var name in names) cells[(name, n)] = new Cell(name, n, trials.Count);
        }

        var done = 0;
        await Parallel.ForEachAsync(trials.Select((t, i) => (Trial: t, Index: i)),
            new ParallelOptions { MaxDegreeOfParallelism = lanes }, async (item, ct) =>
            {
                await RunOneTrialAsync(item.Trial, item.Index, chats, natives, reranker, scorers, centroids, cells, ct);
                var seen = Interlocked.Increment(ref done);
                // Every ten, not every twenty-five: a redirected stdout is block-buffered, so a sparse
                // progress line leaves a multi-hour run looking hung for its first half.
                if (seen % 10 == 0) Console.WriteLine($"    {seen}/{trials.Count} trials");
            });

        return [.. cells.Values.OrderBy(c => c.Arm, StringComparer.Ordinal).ThenBy(c => c.N)];
    }

    private static async Task RunOneTrialAsync(
        Trial trial, int index,
        IReadOnlyList<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)> chats,
        IReadOnlyList<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)> natives,
        CrossEncoderReranker? reranker,
        IReadOnlyList<(string Arm, SweepDoubles.CachingVectorProvider VectorProvider)> scorers,
        IReadOnlyDictionary<string, float[]> centroids,
        Dictionary<(string Arm, int N), Cell> cells,
        CancellationToken ct)
    {
        // One RNG per trial, seeded from its INDEX: the shown order must not depend on which thread ran it.
        var rng = new Random(Seed + index);
        var tools = ToolAffordanceCorpus.Tools;
        var options = new List<int> { trial.Gold };
        options.AddRange(trial.Distractors);

        // The two scoring arms do not depend on the roster size — one pass over gold plus every distractor
        // serves all five cells, which is exactly what makes them a different shape from the transport.
        var scores = new Dictionary<string, double?[]>(StringComparer.Ordinal);
        var declarations = options.Select(i => ToolAffordanceCorpus.Declaration(tools[i])).ToList();

        // One row per vector backend arm. Every arm sees the SAME roster, because the trials were built by the
        // primary vector backend before any of them ran — so a difference here is the scorer and nothing else.
        foreach (var (arm, scorer) in scorers)
        {
            var query = (await scorer.EmbedAsync([trial.Request], ct))[0];
            var vectors = await scorer.EmbedAsync(declarations, ct);
            scores[arm] = [.. vectors.Select(v => (double?)Cosine(query, v))];

            // The SAME vectors, scored after removing the corpus centroid — paired by construction, so the
            // two arms differ in nothing but that subtraction and no second embedding call is spent.
            if (centroids.TryGetValue(arm, out var mean))
            {
                var q = Subtract(query, mean);
                scores[$"{arm}-centered"] = [.. vectors.Select(v => (double?)Cosine(q, Subtract(v, mean)))];
            }
        }

        if (reranker is not null)
        {
            var row = new double?[options.Count];
            if (await reranker.RankAsync(trial.Request, declarations, ct) is { } ranked)
                foreach (var (i, score) in ranked) if (i >= 0 && i < row.Length) row[i] = score;
            scores["rerank"] = row;
        }

        foreach (var n in RosterSizes)
        {
            var (shown, goldSlot) = Shown(n, rng);
            var roster = shown.Select(i => tools[options[i]]).ToList();

            foreach (var (label, chat) in chats)
            {
                await LoopArmAsync(cells[($"loop-{label}", n)], new BenchLoopClient(chat), roster, trial,
                    goldSlot, index, ct);
                await LoopArmAsync(cells[($"loop-{label}-v2", n)], new BenchLoopClient(chat), roster, trial,
                    goldSlot, index, ct, CandidatePreamble);
                await SelectArmAsync(cells[($"select-{label}", n)], chat, roster, trial, goldSlot, index, ct);
            }

            // The transport comparison, PAIRED on one model and one trial: the same roster reaches the
            // same weights twice, once as a prompt protocol this library authors and once as native
            // function-calling. Two models on two transports would confound the two.
            foreach (var (label, chat) in natives)
            {
                await LoopArmAsync(cells[($"loop-{label}", n)], new BenchLoopClient(chat), roster, trial,
                    goldSlot, index, ct);
                await LoopArmAsync(cells[($"loop-{label}-native", n)], new NativeBenchLoopClient(chat),
                    roster, trial, goldSlot, index, ct);
                await SelectArmAsync(cells[($"select-{label}", n)], chat, roster, trial, goldSlot, index, ct);
            }

            foreach (var (arm, row) in scores) ArgmaxArm(cells[(arm, n)], trial, row, shown, goldSlot, index);

            // Both controls go through the REAL loop, driven by a scripted client. An argmax control would
            // prove the scoring arithmetic and nothing about the transport — and the transport is what this
            // sweep is about, so a break in it must fail a control rather than be published as a result.
            await LoopArmAsync(cells[("loop-oracle", n)],
                new ScriptedToolClient(roster[goldSlot - 1]), roster, trial, goldSlot, index, ct);
            await LoopArmAsync(cells[("loop-random", n)],
                new ScriptedToolClient(roster[rng.Next(n)]), roster, trial, goldSlot, index, ct);
        }
    }

    /// <summary>The requests no tool serves, put to every loop arm. A tool call here is a FALSE POSITIVE —
    /// the model fabricating an action because the prompt pushed it to.
    ///
    /// <para><b>This is the only arm that can refute a preamble change</b>, and it is why the candidate is
    /// not simply "try harder to call a tool". Reported per (arm, N) as a rate over the negative set.</para>
    /// </summary>
    private static async Task<Dictionary<(string Arm, int N), (int Trials, int Called, int Failed)>>
        RunNegativeTrialsAsync(SweepDoubles.OpenAiCompatibleChat? big,
            SweepDoubles.OpenAiCompatibleChat? small,
            IReadOnlyList<(string Label, SweepDoubles.OpenAiCompatibleChat Chat)> natives, int lanes)
    {
        // One flat list of (arm, how to build its client, which preamble), because the arms are no longer
        // one grid: the baseline models vary the PREAMBLE and the tool-capable one varies the TRANSPORT.
        // Nesting two loops produced the cross product of both, which is three arms nobody asked for.
        var arms = new List<(string Name, Func<ILlmClient> Client, string? Preamble)>();
        foreach (var (label, chat) in new[] { ("4b", big), ("1b", small) }.Where(p => p.Item2 is not null))
        {
            arms.Add(($"loop-{label}", () => new BenchLoopClient(chat!), null));
            arms.Add(($"loop-{label}-v2", () => new BenchLoopClient(chat!), CandidatePreamble));
        }
        foreach (var (label, chat) in natives)
        {
            arms.Add(($"loop-{label}", () => new BenchLoopClient(chat), null));
            // The native branch composes no protocol prompt at all, so a preamble would be inert here —
            // passing one would imply the wording was under test when it cannot reach this path.
            arms.Add(($"loop-{label}-native", () => new NativeBenchLoopClient(chat), null));
        }

        var result = new Dictionary<(string Arm, int N), (int Trials, int Called, int Failed)>();
        foreach (var n in RosterSizes)
            foreach (var (name, _, _) in arms) result[(name, n)] = (0, 0, 0);
        if (arms.Count == 0) return result;

        var tools = ToolAffordanceCorpus.Tools;
        var requests = ToolAffordanceCorpus.NoToolRequests;
        var gate = new SemaphoreSlim(lanes);

        await Parallel.ForEachAsync(requests.Select((r, i) => (Request: r, Index: i)),
            new ParallelOptions { MaxDegreeOfParallelism = lanes }, async (item, ct) =>
            {
                // Seeded from the index so the roster a negative request sees does not depend on the thread.
                var rng = new Random(Seed + 90_000 + item.Index);
                var pool = Enumerable.Range(0, tools.Count).OrderBy(_ => rng.Next()).Take(MaxDistractors + 1)
                    .ToList();

                foreach (var n in RosterSizes)
                {
                    var roster = pool.Take(n).Select(i => tools[i]).ToList();
                    var registry = new ToolRegistry(
                        roster.Select(t => new ToolAffordanceCorpus.SyntheticTool(t)));

                    foreach (var (name, factory, preamble) in arms)
                    {
                        await gate.WaitAsync(ct);
                        ToolLoopResult run;
                        var client = factory();
                        try
                        {
                            var loop = new ToolLoop(client, registry,
                                new LyntaiOptions { ToolProtocolPreamble = preamble });
                            run = await loop.RunAsync(
                                new LlmRequest { Messages = [LlmMessage.User(item.Request)] },
                                LoopIterations, ct);
                        }
                        finally { gate.Release(); }

                        // A trial the ENDPOINT could not answer is not the model declining, and this
                        // counter is what keeps the two apart. Without it a stalled socket reads as
                        // perfect restraint — which is the direction that flatters an arm silently.
                        var failed = client is ICountedLoopClient { Errors: > 0 };
                        lock (result)
                        {
                            var (trials, called, bad) = result[(name, n)];
                            result[(name, n)] = failed
                                ? (trials, called, bad + 1)
                                : (trials + 1, called + (run.Steps.Count > 0 ? 1 : 0), bad);
                        }

                        // WHICH tool it invented, not just how often. A false-call RATE is a counter and
                        // a counter cannot say whether the model picked something defensibly adjacent or
                        // something absurd — and the rate is a claim about shipped behaviour.
                        if (_dump && item.Index < DumpTrials * 2)
                            Dump($"negative:{name}@{n}", item.Index, run.Steps.Count > 0
                                ? $"CALLED {run.Steps[0].Tool} {run.Steps[0].ArgumentsJson} :: {item.Request}"
                                : $"(answered directly) :: {item.Request}");
                    }
                }
            });

        return result;
    }

    /// <summary>False-call rate on the requests no tool serves — the regression column for any preamble
    /// change. Read it BESIDE the accuracy table: a candidate that lifts choice accuracy and lifts this has
    /// bought one at the price of the other, and only the pair says whether that is a win.</summary>
    private static void PrintFalseCalls(
        Dictionary<(string Arm, int N), (int Trials, int Called, int Failed)> falseCalls)
    {
        if (falseCalls.Count == 0)
        {
            Console.WriteLine("\nFALSE CALLS: NOT RUN — no arm was asked for. The positive trials cannot see");
            Console.WriteLine("  this failure, so nothing in this run bounds a fabricated tool call.");
            return;
        }

        Console.WriteLine($"\nFALSE CALLS on {ToolAffordanceCorpus.NoToolRequests.Count} requests NO tool "
            + "serves — lower is better, and a preamble change must not raise it\n");
        Console.WriteLine($"{"arm",-18} " + string.Join(" ", RosterSizes.Select(n => $"{$"N={n}",9}")));
        Console.WriteLine(new string('-', 18 + RosterSizes.Length * 10));
        foreach (var arm in falseCalls.Keys.Select(k => k.Arm).Distinct().OrderBy(a => a, StringComparer.Ordinal))
        {
            var cellsByN = RosterSizes.Select(n =>
            {
                var (trials, called, _) = falseCalls[(arm, n)];
                return trials == 0 ? "        -" : $"{(double)called / trials,9:P1}";
            });
            Console.WriteLine($"{arm,-18} {string.Join(" ", cellsByN)}");
        }

        // Excluded from every rate above, so the count has to be printed: an unreported 10% would shrink
        // each denominator while the table still looked complete.
        var failed = falseCalls.Values.Sum(v => v.Failed);
        var attempts = falseCalls.Values.Sum(v => v.Trials) + failed;
        Console.WriteLine($"\n  unanswered by the endpoint: {failed} of {attempts} "
            + $"({(attempts == 0 ? 0 : (double)failed / attempts):P2}) — excluded from every rate above, "
            + "because a\n  dead socket is not a model showing restraint");
        Console.WriteLine("\n  A tool call here is the model fabricating an action. The corpus's positive trials");
        Console.WriteLine("  CANNOT see this failure — every one of them has a right tool — so without this");
        Console.WriteLine("  table a preamble that simply pushes harder scores as a clean win.");
        Console.WriteLine("\n  READ IT WITH THE ACCURACY TABLE. An arm that declines everything scores a perfect");
        Console.WriteLine("  0% here and is useless; this column only bounds the cost of the other one.");
    }

    /// <summary>One run of the REAL <see cref="ToolLoop"/> over a roster of N tools.
    ///
    /// <para>The FIRST step is the affordance choice. No step at all is a DECLINE (the model answered from
    /// its own knowledge), and a name that resolved to nothing is a decline too — both are counted, neither
    /// is scored as a wrong choice. A step whose tool is in the roster is FIRED, right or wrong.</para></summary>
    private static async Task LoopArmAsync(Cell cell, ILlmClient client,
        IReadOnlyList<ToolAffordanceCorpus.ToolSpec> roster, Trial trial, int goldSlot, int index,
        CancellationToken ct, string? preamble = null)
    {
        var registry = new ToolRegistry(roster.Select(t => new ToolAffordanceCorpus.SyntheticTool(t)));
        var loop = new ToolLoop(client, registry, new LyntaiOptions { ToolProtocolPreamble = preamble });
        var request = new LlmRequest { Messages = [LlmMessage.User(trial.Request)] };

        var result = await loop.RunAsync(request, LoopIterations, ct);
        var first = result.Steps.Count > 0 ? result.Steps[0] : null;
        var chose = first is null ? -1 : roster.ToList().FindIndex(
            t => string.Equals(t.Name, first.Tool, StringComparison.OrdinalIgnoreCase));

        var hallucinated = first is not null && chose < 0;
        var malformed = first is not null
            && first.Result.StartsWith(ToolAffordanceCorpus.SyntheticTool.MalformedPrefix, StringComparison.Ordinal);
        var recovered = hallucinated && result.Steps.Count > 1 && roster.Any(
            t => string.Equals(t.Name, result.Steps[1].Tool, StringComparison.OrdinalIgnoreCase));

        // A trial the endpoint could not answer is NOT the model declining, and conflating the two would
        // report a stalled socket as "it chose not to call a tool" — the exact reading this arm exists to
        // make. It is excluded from every failure-mode rate and counted on its own.
        var failed = client is ICountedLoopClient { Errors: > 0 };

        lock (cell)
        {
            if (failed) cell.Failed++;
            else
            {
                if (first is null) cell.NoTool++;
                if (hallucinated) cell.Hallucinated++;
                if (malformed) cell.Malformed++;
                if (recovered) cell.Recovered++;
                if (result.Ok) cell.Converged++;
            }
            if (client is ICountedLoopClient counted)
            {
                cell.Calls += counted.Calls;
                cell.FirstPromptChars += counted.FirstPromptChars;
            }
            if (client is NativeBenchLoopClient native) cell.Truncated += native.Truncated;
        }

        if (_dump && index < DumpTrials && client is ICountedLoopClient dumpable && dumpable.FirstReply is { } text)
            Dump($"{cell.Arm}@{cell.N}", index, text);

        var hit = chose >= 0 && chose + 1 == goldSlot;
        var mode = failed ? "unanswered" : first is null ? "no-tool" : hallucinated ? "bad-name"
            : hit ? "hit" : "miss";
        Record(cell, trial, index, fired: chose >= 0 && !failed, correct: hit && !failed, goldSlot, mode);
    }

    /// <summary>The same trial posed as a plain <c>select-from-list</c>. A <c>0</c> choice, an unparseable
    /// reply or a dead endpoint is NOT FIRED rather than wrong — symmetrically with a loop that invoked
    /// nothing.</summary>
    private static async Task SelectArmAsync(Cell cell, SweepDoubles.OpenAiCompatibleChat chat,
        IReadOnlyList<ToolAffordanceCorpus.ToolSpec> roster, Trial trial, int goldSlot, int index,
        CancellationToken ct)
    {
        var prompt = SelectPrompt(trial.Request, roster);
        string? reply;
        var failed = false;
        try
        {
            reply = await chat.AskAsync(prompt, ct, maxTokens: 16);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
        {
            reply = null;
            failed = true;
        }

        if (_dump && index < DumpTrials) Dump($"{cell.Arm}@{cell.N}", index, reply ?? "(null)");
        var choice = ReadInt(reply, "choice");
        lock (cell) { cell.Calls++; cell.FirstPromptChars += prompt.Length; if (failed) cell.Failed++; }
        var fired = choice is > 0 && choice <= roster.Count && !failed;
        var hit = fired && choice == goldSlot;
        Record(cell, trial, index, fired, hit, goldSlot,
            failed ? "unanswered" : !fired ? "declined" : hit ? "hit" : "miss");
    }

    /// <summary>Argmax over the tools shown at this roster size. A tie at the maximum is a DECLINE,
    /// symmetrically with a model that invoked nothing — a scorer that cannot separate its top options has
    /// not chosen one, and breaking the tie with a coin would put the arm's accuracy half under the
    /// harness's control rather than under the model.</summary>
    private static void ArgmaxArm(Cell cell, Trial trial, double?[] scores, IReadOnlyList<int> shown,
        int goldSlot, int index)
    {
        var seen = shown.Select(i => i >= 0 && i < scores.Length ? scores[i] : null).ToList();
        lock (cell) cell.Calls += seen.Count;
        if (seen.Any(s => s is null))
        {
            Record(cell, trial, index, fired: false, correct: false, goldSlot, "unscored");
            return;
        }

        var best = seen.Max()!.Value;
        var winners = Enumerable.Range(0, seen.Count).Where(i => seen[i]!.Value >= best).ToList();
        lock (cell) { if (winners.Count > 1) cell.Ties++; }
        var fired = winners.Count == 1;
        var hit = fired && winners[0] + 1 == goldSlot;
        Record(cell, trial, index, fired, hit, goldSlot, !fired ? "tie" : hit ? "hit" : "miss");
    }

    /// <summary>One arm's outcome on one trial at one roster size — the row every breakdown is computed
    /// from, and the reason no future question about this grid costs another run.
    ///
    /// <para><b>Written because its absence cost one.</b> Asking "does the model beat the free arm where the
    /// free arm is WRONG" needed a per-trial join that a table of totals cannot express, so a completed
    /// 168-trial grid would have had to be re-run to answer it. Emitting the rows makes that question, and
    /// the ones nobody has thought of yet, post-hoc.</para></summary>
    private sealed record Outcome(string Arm, int N, int Trial, bool Fired, bool Correct, int GoldSlot,
        int GoldRank, string Family, string Mode);

    private static readonly System.Collections.Concurrent.ConcurrentBag<Outcome> Outcomes = [];

    private static void Record(Cell cell, Trial trial, int index, bool fired, bool correct, int goldSlot,
        string mode)
    {
        lock (cell)
        {
            cell.Trials++;
            if (fired) cell.Fired++;
            if (correct) { cell.Hits++; cell.HitsByPosition[goldSlot]++; cell.Correct[index] = true; }
            cell.ShownByPosition[goldSlot]++;
        }
        Outcomes.Add(new Outcome(cell.Arm, cell.N, index, fired, correct, goldSlot, trial.GoldRank,
            trial.Family, mode));
    }

    // ── the clients ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The loop's model, and the instrument that counts what the transport actually sent.
    ///
    /// <para><b><c>SupportsToolCalls</c> is left at its interface default of false</b>, which is what puts
    /// <see cref="ToolLoop"/> on its prompt path. That is the measurement, not a limitation: the native path
    /// is silently inert on both models this machine holds.</para>
    ///
    /// <para>One instance per loop run, so no locking is needed — a run is sequential. The prompt is counted
    /// from the REQUEST rather than composed here, so a change to the protocol's own system prompt moves
    /// this number instead of going unnoticed.</para></summary>
    private sealed class BenchLoopClient(SweepDoubles.OpenAiCompatibleChat chat) : ILlmClient, ICountedLoopClient
    {
        public int Calls { get; private set; }

        public long FirstPromptChars { get; private set; }

        public string? FirstReply { get; private set; }

        /// <summary>Calls the endpoint could not answer — a stall, a dropped socket, a non-200. Counted
        /// rather than thrown, so one bad call costs a TRIAL and never the run; a trial with any of these
        /// is reported separately and is never scored as the model declining.</summary>
        public int Errors { get; private set; }

        public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            Calls++;
            if (Calls == 1) FirstPromptChars = req.Messages.Sum(m => (long)m.Content.Length);

            var turns = req.Messages.Select(m => (m.Role, m.Content)).ToList();
            string? text;
            try
            {
                text = await chat.AskTurnsAsync(turns, ct, LoopMaxTokens).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                           && !ct.IsCancellationRequested)
            {
                // Never swallow the CALLER's cancellation — that belongs to whoever asked to stop.
                Errors++;
                text = null;
            }
            if (Calls == 1) FirstReply = text ?? "(null)";

            return text is null
                ? new LlmReply("", ProviderVerdict.Failed, Detail: "bench chat returned nothing")
                : new LlmReply(text, ProviderVerdict.Ok);
        }

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("the affordance arm drives the loop's completion path");
    }

    /// <summary>The same loop over the NATIVE function-calling transport: declarations go on the request and
    /// the model's structured <c>tool_calls</c> come back, so <see cref="ToolLoop"/> takes its native branch
    /// instead of authoring a prompt protocol.
    ///
    /// <para><b>This is the arm `docs/task-archive.md` Part 236 could not run</b>, and it needs a model
    /// whose template carries a tool section — gemma-3's does not, and the array is silently discarded. Pair
    /// it against the SAME model's prompt arm or the comparison confounds the transport with the model.</para>
    ///
    /// <para><c>SupportsStreamingToolCalls</c> stays false: the streaming half would deliver the same
    /// choice through a second code path, and guessing wrong there fails SILENTLY — no call chunk arrives
    /// and the turn's prose reads as a final answer.</para></summary>
    private sealed class NativeBenchLoopClient(SweepDoubles.OpenAiCompatibleChat chat) : ILlmClient, ICountedLoopClient
    {
        public int Calls { get; private set; }

        public long FirstPromptChars { get; private set; }

        public string? FirstReply { get; private set; }

        public int Errors { get; private set; }

        /// <summary>Turns the OUTPUT CAP cut off while no tool call had been emitted. Separates a model
        /// that chose not to call a tool from one that never got to — the pair a bare empty
        /// <c>tool_calls</c> cannot tell apart, and the difference between a finding and an artifact.</summary>
        internal int Truncated { get; private set; }

        public bool SupportsToolCalls(LlmRequest req) => true;

        public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            Calls++;
            // The DECLARATIONS are counted here and not just the messages, because on this transport the
            // roster travels in `req.Tools` rather than in message content. Counting messages alone would
            // make this arm's prompt size CONSTANT in N, and the control that asserts "the roster grew with
            // the roster" would void the run — reporting a correct native arm as a broken one.
            if (Calls == 1)
                FirstPromptChars = req.Messages.Sum(m => (long)m.Content.Length)
                    + (req.Tools?.Sum(t => (long)(t.Name.Length + (t.Description?.Length ?? 0)
                        + (t.ParametersJsonSchema?.Length ?? 0))) ?? 0);

            try
            {
                var (content, calls, finish) = await chat
                    .AskWithToolsAsync(req.Messages, req.Tools ?? [], ct, LoopMaxTokens).ConfigureAwait(false);
                // A turn the CAP cut off is not a model that declined, and an empty `tool_calls` reports
                // the two identically. Counted so a decline rate can be read as one.
                if (finish == "length" && (calls is null || calls.Count == 0)) Truncated++;
                // NULL calls is the transport failing; an EMPTY list is the model declining. Only the first
                // is an error, and scoring the second as one would flatter every arm that cannot decline.
                if (calls is null)
                {
                    Errors++;
                    if (Calls == 1) FirstReply = "(no answer)";
                    return new LlmReply("", ProviderVerdict.Failed, Detail: "bench native chat returned nothing");
                }
                if (Calls == 1)
                    FirstReply = calls.Count > 0
                        ? string.Join("; ", calls.Select(c => $"{c.Name} {c.ArgumentsJson}"))
                        : content ?? "";
                return new LlmReply(content ?? "", ProviderVerdict.Ok) { ToolCalls = calls };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                           && !ct.IsCancellationRequested)
            {
                // Never swallow the CALLER's cancellation — that belongs to whoever asked to stop.
                Errors++;
                if (Calls == 1) FirstReply = "(no answer)";
                return new LlmReply("", ProviderVerdict.Failed, Detail: "bench native chat returned nothing");
            }
        }

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("the affordance arm drives the loop's completion path");
    }

    /// <summary>A model that always names one chosen tool, then finishes — the control that proves the
    /// transport CAN carry a choice.
    ///
    /// <para>It emits the protocol's own <c>{"tool":…}</c> turn with arguments built from the tool's declared
    /// schema, so a break anywhere in the path — the protocol grammar, the registry lookup, the argument
    /// validator, the step recording, the slot arithmetic — fails <c>loop-oracle</c> loudly instead of
    /// collapsing every real arm while a hardcoded control still printed 100%.</para></summary>
    private sealed class ScriptedToolClient(ToolAffordanceCorpus.ToolSpec choice) : ILlmClient
    {
        private int _calls;

        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            var text = _calls++ == 0
                ? $"{{\"tool\":\"{choice.Name}\",\"arguments\":{SampleArguments(choice)}}}"
                : "{\"final\":\"done\"}";
            return Task.FromResult(new LlmReply(text, ProviderVerdict.Ok));
        }

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException("the scripted control drives the loop's completion path");
    }

    /// <summary>A well-formed arguments object for a tool, read off its own schema. The control must not
    /// trip the malformed-argument counter it exists to validate.</summary>
    private static string SampleArguments(ToolAffordanceCorpus.ToolSpec spec)
    {
        using var doc = JsonDocument.Parse(spec.Schema);
        var properties = doc.RootElement.GetProperty("properties");
        var pairs = doc.RootElement.GetProperty("required").EnumerateArray()
            .Select(r => r.GetString()!)
            .Select(name => properties.GetProperty(name).GetProperty("type").GetString() == "integer"
                ? $"\"{name}\":1"
                : $"\"{name}\":\"x\"");
        return $"{{{string.Join(",", pairs)}}}";
    }

    // ── prompts ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The cross-shape prompt. It shows the SAME declaration text the loop's system prompt shows —
    /// name, description and schema — so the only difference between the two arms is the transport: the
    /// protocol's two-key grammar, its system turn, and a tool that actually executes.
    ///
    /// <para>It states that exactly one tool fits, because otherwise the "none" escape confounds the fired
    /// rate with accuracy, and it still offers <c>0</c> so a model that cannot tell declines rather than
    /// guessing. Bench-local: no shipped prompt poses a forced choice over a roster.</para></summary>
    private static string SelectPrompt(string request, IReadOnlyList<ToolAffordanceCorpus.ToolSpec> roster)
    {
        var numbered = roster.Select((t, i) => string.Create(CultureInfo.InvariantCulture,
            $"{i + 1}. {ToolAffordanceCorpus.Declare(t)}"));
        return $$"""
            You are given a user request and a numbered list of tools.
            EXACTLY ONE of the tools is the right one to call for this request.

            Reply with ONLY a JSON object: {"choice": <number>}
            - <number> is the number of the single tool that serves the request.
            - Use 0 only if you genuinely cannot tell which one it is.

            Request:
            {{request}}

            Tools:
            {{string.Join("\n", numbered)}}
            """;
    }

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

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ── reporting ─────────────────────────────────────────────────────────────────────────────────────────

    private static void PrintPreamble(SweepDoubles.OpenAiCompatibleChat big,
        SweepDoubles.OpenAiCompatibleChat? small, bool rerankOk, Difficulty difficulty, int trials, int lanes,
        IReadOnlyList<(string Label, SweepDoubles.CachingVectorProvider VectorProvider)> extra, bool scorersOnly,
        SweepDoubles.OpenAiCompatibleChat? nativeChat, bool skipBaseline)
    {
        Console.WriteLine("\ntool-affordance — what a small model does with a roster of 3-7 tools, through the");
        Console.WriteLine("                  PROMPT protocol ToolLoop authors itself\n");
        Console.WriteLine($"  corpus     : SYNTHETIC — {ToolAffordanceCorpus.Tools.Count} authored tools in "
            + $"{ToolAffordanceCorpus.Tools.Select(t => t.Family).Distinct().Count()} families of "
            + $"{ToolAffordanceCorpus.FamilySize}, {trials} trial(s)");
        Console.WriteLine("               a fixture built to be measured, which is the weakest evidence tier");
        Console.WriteLine("               here and must be said wherever a figure from this run is quoted");
        Console.WriteLine($"  roster     : N = {string.Join(", ", RosterSizes)}   (chance = 1/N)");
        Console.WriteLine($"  difficulty : {difficulty.ToString().ToLowerInvariant()}"
            + (difficulty == Difficulty.Hard
                ? "  — the gold's own family, seven tools authored to be adjacent"
                : "  — tools from OTHER families; this is the INSTRUMENT CHECK, not a result"));
        Console.WriteLine(scorersOnly
            ? "  big        : NOT RUN — --scorers-only"
            : $"  big        : {big!.Model}");
        Console.WriteLine(scorersOnly ? "  small      : NOT RUN — --scorers-only"
            : small is null
                ? "  small      : SKIPPED — set LYNTAI_LIVE_SMALL_URL to add the small-model arms"
                : $"  small      : {small.Model}");
        if (nativeChat is null)
        {
            Console.WriteLine("  native     : SKIPPED — set LYNTAI_LIVE_NATIVE_URL to a model whose chat");
            Console.WriteLine("               template carries a tool section, and the NATIVE path is measured");
        }
        else
        {
            Console.WriteLine($"  native     : {nativeChat.Model}  — the SAME model runs `loop-{NativeLabel}`");
            Console.WriteLine($"               (prompt) and `loop-{NativeLabel}-native`, paired on identical");
            Console.WriteLine("               trials, so the gap is the TRANSPORT and not the model");
        }
        if (skipBaseline)
        {
            Console.WriteLine("\n  *** --skip-baseline: the 4B and 1B arms and the negative corpus are absent");
            Console.WriteLine("      from this run. Neither can take the native path at all, so they answer");
            Console.WriteLine("      nothing it asks, and their figures are already published. ***");
        }
        if (scorersOnly)
        {
            Console.WriteLine("\n  *** --scorers-only: every arm that calls a CHAT model is absent from this run —");
            Console.WriteLine("      loop-4b, loop-4b-v2, select-4b, loop-1b, loop-1b-v2, select-1b, and the whole");
            Console.WriteLine("      negative (false-call) corpus. This run prices the MODEL-FREE scorers only.");
            Console.WriteLine("      The scripted transport controls still run, and `cosine` reproducing its");
            Console.WriteLine("      published figure is what makes these cells comparable to a full run's. ***");
        }
        Console.WriteLine(rerankOk
            ? $"  rerank     : {CrossEncoderReranker.Model} at {CrossEncoderReranker.BaseUrl}"
            : "  rerank     : SKIPPED — nothing usable answered /v1/rerank");
        Console.WriteLine($"  concurrency: {lanes} in-flight request(s)");
        Console.WriteLine($"  vector backend   : {SweepDoubles.ServedOrRequestedModel}  — builds the trials AND "
            + "scores the `cosine` arm");
        Console.WriteLine(extra.Count == 0
            ? "  embed arms : none — set LYNTAI_LIVE_EMBED_ARMS=label=url,… to add `cosine-<label>` arms"
            : $"  embed arms : {string.Join(", ", extra.Select(e => $"cosine-{e.Label}"))}");
        if (extra.Count > 0)
        {
            Console.WriteLine("               Trial construction is PINNED to the primary vector backend above, so");
            Console.WriteLine("               every arm sees the same roster and only the SCORING varies. It");
            Console.WriteLine("               also means `cosine` faces distractors selected to be hardest for");
            Console.WriteLine("               itself, which biases against it rather than for it.\n");
        }
        else Console.WriteLine();
        Console.WriteLine("  How to read this. `loop-*` runs the REAL ToolLoop on its prompt path; `select-*`");
        Console.WriteLine("  poses the same trial as a forced choice over the same text, so the gap between");
        Console.WriteLine("  them is the TRANSPORT and not the choosing. `loop-random` must land on 1/N and");
        Console.WriteLine("  `loop-oracle` on 100% — both through the real loop, so a run missing either is");
        Console.WriteLine("  VOID rather than surprising.");
    }

    /// <summary>How much surface leakage the authored requests left, printed rather than asserted. A request
    /// that echoes its tool's name or description makes the whole grid measure label-following, and the
    /// gold's own cosine rank across all 42 declarations is the readout — a median of 1 would mean the
    /// corpus answers itself and the `cosine` arm is the finding.</summary>
    private static void PrintTrialProfile(IReadOnlyList<Trial> trials, Difficulty difficulty)
    {
        var ranks = trials.Select(t => (double)t.GoldRank).ToList();
        var top1 = trials.Count(t => t.GoldRank == 1);
        Console.WriteLine($"\n  family mix: " + string.Join(", ", trials.GroupBy(t => t.Family)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")));
        Console.WriteLine($"  gold cosine rank over all {ToolAffordanceCorpus.Tools.Count} declarations: "
            + $"median {BenchStats.Percentile(ranks, 0.5):F0}, p90 {BenchStats.Percentile(ranks, 0.9):F0}, "
            + $"max {ranks.Max():F0}; top-1 in {top1} of {trials.Count} ({(double)top1 / trials.Count:P1})");
        Console.WriteLine("  A LOW median here is label leakage, not difficulty: the requests are written not");
        Console.WriteLine("  to echo their tool's name or description, and `cosine` is kept in the table as");
        Console.WriteLine("  the readout of how much of that discipline survived.");
        if (difficulty == Difficulty.Easy)
            Console.WriteLine("  Under `easy` the distractors come from other families, so a cross-family "
                + "near-miss is possible and makes this a CONSERVATIVE check rather than a ceiling.");
    }

    private static void PrintAccuracyTable(IReadOnlyList<Cell> cells, int trials)
    {
        Console.WriteLine($"\nACCURACY over FIRED trials, n = {trials} per cell\n");
        Console.WriteLine($"{"arm",-13} {"N",2} {"chance",7} {"fired",12} {"accuracy",9} {"95% CI",18} {"lift",7}");
        Console.WriteLine(new string('-', 81));
        foreach (var group in cells.GroupBy(c => c.Arm))
        {
            foreach (var cell in group.OrderBy(c => c.N))
            {
                var chance = 1.0 / cell.N;
                var accuracy = cell.Fired == 0 ? 0 : (double)cell.Hits / cell.Fired;
                var (low, high) = BenchStats.Wilson(cell.Hits, cell.Fired);
                var firedRate = cell.Trials == 0 ? 0 : (double)cell.Fired / cell.Trials;
                Console.WriteLine($"{group.Key,-13} {cell.N,2} {chance,7:P0} {$"{cell.Fired}/{cell.Trials}",12} "
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
        foreach (var cell in cells.Where(c => c.Arm == "loop-random").OrderBy(c => c.N))
        {
            var (low, high) = BenchStats.Wilson(cell.Hits, cell.Fired);
            var chance = 1.0 / cell.N;
            var ok = chance >= low && chance <= high && cell.Fired == cell.Trials;
            voided |= !ok;
            Console.WriteLine($"  loop-random  N={cell.N}: {(double)cell.Hits / Math.Max(1, cell.Fired),6:P1} "
                + $"[{low:P0}, {high:P0}] vs 1/N = {chance:P0}, fired {cell.Fired}/{cell.Trials}   "
                + (ok ? "ok" : "*** OUTSIDE ***"));
        }
        foreach (var cell in cells.Where(c => c.Arm == "loop-oracle").OrderBy(c => c.N))
        {
            var clean = cell.Hits == cell.Trials && cell.Trials > 0 && cell.Malformed == 0
                && cell.Hallucinated == 0 && cell.Converged == cell.Trials;
            voided |= !clean;
            Console.WriteLine($"  loop-oracle  N={cell.N}: {cell.Hits}/{cell.Trials} chosen, "
                + $"{cell.Converged}/{cell.Trials} converged, {cell.Malformed} malformed   "
                + (clean ? "ok" : "*** THE TRANSPORT ITSELF IS BROKEN — not a model result ***"));
        }

        Console.WriteLine("\n  prompt size must RISE with N on both shapes, or the roster never reached the");
        Console.WriteLine("  model. Read off the REQUEST the transport sent, not off a prompt composed here:");
        foreach (var group in cells.Where(c => c.Arm.StartsWith("select-", StringComparison.Ordinal)
                         || (c.IsLoop && c.Arm is not ("loop-random" or "loop-oracle")))
                     .GroupBy(c => c.Arm).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // Both shapes send exactly one first-turn prompt per trial, so Trials is the right denominator.
            var sizes = group.OrderBy(c => c.N)
                .Select(c => c.Trials == 0 ? 0 : (double)c.FirstPromptChars / c.Trials).ToList();
            var rising = sizes.Zip(sizes.Skip(1), (a, b) => b > a).All(x => x);
            voided |= !rising;
            Console.WriteLine($"    {group.Key,-13} {string.Join(" -> ", sizes.Select(s => $"{s:F0}"))} chars   "
                + (rising ? "ok" : "*** NOT MONOTONE ***"));
        }

        Console.WriteLine("\n  ties — a scorer that cannot separate its top options has DECLINED, so these are");
        Console.WriteLine("  counted as not-fired above:");
        if (cells.All(c => c.Ties == 0)) Console.WriteLine("    none on any arm");
        foreach (var group in cells.Where(c => c.Ties > 0).GroupBy(c => c.Arm))
            Console.WriteLine($"    {group.Key,-13} " + string.Join("  ", group.OrderBy(c => c.N)
                .Select(c => $"N={c.N}: {c.Ties}/{c.Trials} tied")));

        // A call the endpoint could not answer is excluded from every rate above, so the run degrades
        // gracefully — which is exactly why the count has to be printed. An unreported 10% would silently
        // shrink every denominator while the table still looked complete.
        var failed = cells.Sum(c => c.Failed);
        var attempts = cells.Where(c => c.IsLoop || c.Arm.StartsWith("select-", StringComparison.Ordinal))
            .Sum(c => c.Trials);
        var failRate = attempts == 0 ? 0 : (double)failed / attempts;
        voided |= failRate > 0.02;
        Console.WriteLine($"\n  endpoint failures: {failed} of {attempts} model trials ({failRate:P2})   "
            + (failRate > 0.02 ? "*** ABOVE 2% — the device, not the models ***" : "ok"));

        if (rerankOk)
        {
            var (calls, scored, distinct) = reranker.Audit;
            Console.WriteLine($"  rerank audit: {calls} call(s), {scored} pair(s) scored, {distinct} distinct "
                + "score(s) — a flat cross-encoder is a broken conversion, not a weak model");
        }
        if (voided) Console.WriteLine("\n  *** AT LEAST ONE CONTROL FAILED — this run is not a result. ***");
    }

    /// <summary>The three outcomes a forced choice cannot express, which is the whole reason the transport is
    /// measured separately from the choosing: nothing invoked, a name that resolved to nothing, and the right
    /// tool called with arguments it could not use.</summary>
    private static void PrintTransportFailures(IReadOnlyList<Cell> cells)
    {
        Console.WriteLine("\nTRANSPORT FAILURES — outcomes a forced choice has no way to express\n");
        Console.WriteLine($"{"arm",-13} {"N",2} {"no tool",9} {"bad name",9} {"recovered",10} "
            + $"{"bad args",9} {"converged",10} {"calls/run",10} {"unanswered",11}");
        Console.WriteLine(new string('-', 90));
        foreach (var group in cells.Where(c => c.IsLoop && c.Arm is not ("loop-random" or "loop-oracle"))
                     .GroupBy(c => c.Arm).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            foreach (var cell in group.OrderBy(c => c.N))
            {
                // Every rate is over the trials the ENDPOINT answered. A stall is not a model behaviour.
                var t = Math.Max(1, cell.Trials - cell.Failed);
                Console.WriteLine($"{group.Key,-13} {cell.N,2} {(double)cell.NoTool / t,9:P1} "
                    + $"{(double)cell.Hallucinated / t,9:P1} {$"{cell.Recovered}/{cell.Hallucinated}",10} "
                    + $"{(double)cell.Malformed / t,9:P1} {(double)cell.Converged / t,10:P1} "
                    + $"{(double)cell.Calls / Math.Max(1, cell.Trials),10:F2} "
                    + $"{$"{cell.Failed}/{cell.Trials}",11}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("  `no tool` answered from its own knowledge — the failure the native transport also");
        Console.WriteLine("  shows, and the one a deployment mistakes for an answer. `bad name` is counted as");
        Console.WriteLine("  NOT FIRED, with `recovered` the share that reached a real tool on the next turn");
        Console.WriteLine("  after the loop fed back its unknown-tool error. `bad args` is scored against ALL");
        Console.WriteLine("  trials, so it is not conditional on the choice being right.");
        Console.WriteLine("  `calls/run` above 1 on a converging run is the second turn; above 2 is");
        Console.WriteLine("  CompleteJsonAsync's repair round, which is a real second billed call.");
    }

    /// <summary>The question the item actually asks: does routing a choice through the TOOL transport cost
    /// anything the same choice posed flat does not? Paired by construction — both shapes answered the same
    /// trials over the same roster — so the test is McNemar over the trials they disagreed on.</summary>
    private static void PrintShapeComparison(IReadOnlyList<Cell> cells)
    {
        Console.WriteLine("\nSHAPE: affordance through the loop vs the same choice posed flat, paired\n");
        Console.WriteLine($"{"model",-6} {"N",2} {"loop",8} {"select",8} {"net",6} {"discordant",11} {"p",10}");
        Console.WriteLine(new string('-', 58));
        foreach (var label in new[] { "4b", "1b" })
        {
            foreach (var n in RosterSizes)
            {
                var loop = cells.FirstOrDefault(c => c.Arm == $"loop-{label}" && c.N == n);
                var select = cells.FirstOrDefault(c => c.Arm == $"select-{label}" && c.N == n);
                if (loop is null || select is null) continue;

                var onlyLoop = loop.Correct.Where((v, i) => v && !select.Correct[i]).Count();
                var onlySelect = select.Correct.Where((v, i) => v && !loop.Correct[i]).Count();
                var net = onlyLoop - onlySelect;
                Console.WriteLine($"{label,-6} {n,2} "
                    + $"{(double)loop.Hits / Math.Max(1, loop.Trials),8:P1} "
                    + $"{(double)select.Hits / Math.Max(1, select.Trials),8:P1} "
                    + $"{(net > 0 ? $"+{net}" : net.ToString(CultureInfo.InvariantCulture)),6} "
                    + $"{onlyLoop + onlySelect,11} "
                    + $"{BenchStats.Format(BenchStats.McNemarExact(onlyLoop, onlySelect)),10}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("  Both columns are accuracy over ALL trials, not over fired ones, because a loop");
        Console.WriteLine("  that invoked nothing is a real outcome of the transport and dropping it would");
        Console.WriteLine("  compare the two shapes on different denominators. `net` is loop minus select.");
    }

    /// <summary>The shipped protocol preamble against the candidate, paired on identical trials — the test
    /// that decides whether the library's own constant changes.
    ///
    /// <para>Paired McNemar, because both arms answered the same trial with the same roster: only the
    /// trials they DISAGREED on carry information, and a cross-run comparison would need a run-to-run noise
    /// floor this grid has not measured.</para></summary>
    private static void PrintPreambleComparison(IReadOnlyList<Cell> cells)
    {
        Console.WriteLine("\nPREAMBLE: the shipped instruction vs the candidate, paired on the same trials\n");
        Console.WriteLine($"{"model",-6} {"N",2} {"shipped",9} {"candidate",10} {"net",6} {"discordant",11} "
            + $"{"p",10} {"no-tool v1→v2",15}");
        Console.WriteLine(new string('-', 74));
        foreach (var label in new[] { "4b", "1b" })
        {
            foreach (var n in RosterSizes)
            {
                var v1 = cells.FirstOrDefault(c => c.Arm == $"loop-{label}" && c.N == n);
                var v2 = cells.FirstOrDefault(c => c.Arm == $"loop-{label}-v2" && c.N == n);
                if (v1 is null || v2 is null) continue;

                var onlyV2 = v2.Correct.Where((v, i) => v && !v1.Correct[i]).Count();
                var onlyV1 = v1.Correct.Where((v, i) => v && !v2.Correct[i]).Count();
                var net = onlyV2 - onlyV1;
                var t1 = Math.Max(1, v1.Trials - v1.Failed);
                var t2 = Math.Max(1, v2.Trials - v2.Failed);
                Console.WriteLine($"{label,-6} {n,2} "
                    + $"{(double)v1.Hits / Math.Max(1, v1.Trials),9:P1} "
                    + $"{(double)v2.Hits / Math.Max(1, v2.Trials),10:P1} "
                    + $"{(net > 0 ? $"+{net}" : net.ToString(CultureInfo.InvariantCulture)),6} "
                    + $"{onlyV1 + onlyV2,11} "
                    + $"{BenchStats.Format(BenchStats.McNemarExact(onlyV2, onlyV1)),10} "
                    + $"{$"{(double)v1.NoTool / t1,0:P0}→{(double)v2.NoTool / t2,0:P0}",15}");
            }
            Console.WriteLine();
        }
        Console.WriteLine("  `net` is candidate minus shipped. Read it WITH the false-call table: a candidate");
        Console.WriteLine("  that wins here and raises false calls has moved the failure, not removed it.");
    }

    /// <summary>The question `docs/task-archive.md` Part 236 asks: does routing a choice through NATIVE
    /// function-calling beat the prompt protocol this library authors, on one model that can do both?
    ///
    /// <para>Paired McNemar on identical trials with an identical roster, so only the trials the two
    /// transports DISAGREED on carry information. Two models on two transports would confound them, which
    /// is why the native model runs both arms rather than being compared against the published 4B.</para>
    ///
    /// <para>`no tool` is printed beside the accuracy because the two transports fail differently: the
    /// prompt path can emit unparseable JSON, and the native path can return an empty `tool_calls` while
    /// answering from parametric knowledge. Both are DECLINES, and accuracy alone hides which.</para>
    /// </summary>
    private static void PrintTransportComparison(IReadOnlyList<Cell> cells)
    {
        var prompt = cells.Where(c => c.Arm == $"loop-{NativeLabel}").ToList();
        var native = cells.Where(c => c.Arm == $"loop-{NativeLabel}-native").ToList();
        if (prompt.Count == 0 || native.Count == 0) return;

        Console.WriteLine("\nTRANSPORT: the same model on the PROMPT protocol vs NATIVE function-calling\n");
        Console.WriteLine($"{"N",2} {"prompt",8} {"native",8} {"net",6} {"discordant",11} {"p",10} "
            + $"{"no-tool p→n",14}");
        Console.WriteLine(new string('-', 62));
        foreach (var n in RosterSizes)
        {
            var p = prompt.FirstOrDefault(c => c.N == n);
            var v = native.FirstOrDefault(c => c.N == n);
            if (p is null || v is null) continue;

            var onlyNative = v.Correct.Where((x, i) => x && !p.Correct[i]).Count();
            var onlyPrompt = p.Correct.Where((x, i) => x && !v.Correct[i]).Count();
            var net = onlyNative - onlyPrompt;
            var tp = Math.Max(1, p.Trials - p.Failed);
            var tv = Math.Max(1, v.Trials - v.Failed);
            Console.WriteLine($"{n,2} {(double)p.Hits / Math.Max(1, p.Trials),8:P1} "
                + $"{(double)v.Hits / Math.Max(1, v.Trials),8:P1} "
                + $"{(net > 0 ? $"+{net}" : net.ToString(CultureInfo.InvariantCulture)),6} "
                + $"{onlyPrompt + onlyNative,11} "
                + $"{BenchStats.Format(BenchStats.McNemarExact(onlyNative, onlyPrompt)),10} "
                + $"{$"{(double)p.NoTool / tp,0:P0}→{(double)v.NoTool / tv,0:P0}",14}");
        }
        Console.WriteLine("\n  `net` is native minus prompt. Accuracy is over ALL trials, not fired ones: a loop");
        Console.WriteLine("  that invoked nothing is a real outcome of the transport, and dropping it would");
        Console.WriteLine("  compare the two on different denominators.");

        // The native path's no-tool rate is the headline of this table, so the one artifact that could
        // manufacture it gets its own line. A turn the CAP cut off before any call was emitted reports
        // as an empty `tool_calls`, which is byte-identical to a model that decided not to call one.
        var truncated = native.Sum(c => c.Truncated);
        var turns = native.Sum(c => c.Calls);
        Console.WriteLine($"\n  native turns cut off by the {LoopMaxTokens}-token cap with no tool call: "
            + $"{truncated} of {turns} ({(turns == 0 ? 0 : (double)truncated / turns):P2})");
        Console.WriteLine(truncated == 0
            ? "  ZERO — so every no-tool above is the model DECLINING, not the harness truncating it."
            : "  NON-ZERO — subtract these before reading the no-tool column as a decline rate.");
    }

    /// <summary>Position bias, AT ONE roster size rather than pooled over them. Pooling confounds slot with
    /// length — slot 7 occurs only at N = 7 while slot 1 occurs at every N, and accuracy falls with N, so a
    /// pooled row shows a slope on an arm with no position dependence at all.
    /// <para>The argmax arms are the noise floor: an argmax over per-tool scores cannot know what order the
    /// roster was shown in, so whatever slope they carry is sampling noise.</para></summary>
    private static void PrintPositionBias(IReadOnlyList<Cell> cells)
    {
        var n = RosterSizes[^1];
        Console.WriteLine($"\nPOSITION of the right tool in the roster — accuracy by slot, at N = {n} ONLY\n");

        // DERIVED from the roster, never a fixed seven. It was a literal until `--roster` existed, and the
        // first catalogue-scale run printed a 7-slot header over 35 columns — a table nobody could read.
        Console.WriteLine("  arm          " + string.Join(" ", Enumerable.Range(1, n).Select(s => $"{$"slot {s}",6}")));
        foreach (var cell in cells.Where(c => c.N == n && c.Arm is not ("loop-random" or "loop-oracle"))
                     .OrderBy(c => c.Arm, StringComparer.Ordinal))
            Console.WriteLine($"  {cell.Arm,-13} " + string.Join(" ", Enumerable.Range(1, n)
                .Select(s => cell.ShownByPosition[s] == 0 ? "     -"
                    : $"{(double)cell.HitsByPosition[s] / cell.ShownByPosition[s],6:P0}")));

        var perSlot = cells.Where(c => c.N == n).Select(c => c.ShownByPosition.Skip(1).Take(n).Max()).Max();
        var (lo, hi) = BenchStats.Wilson(perSlot / 2, perSlot);
        Console.WriteLine($"\n  Each cell holds about {perSlot} trial(s) — a 95% interval on a mid-range rate "
            + $"there spans roughly {hi - lo:P0}, so only a LARGE gap is readable and the rest is noise.");
        Console.WriteLine("  In the loop arms the slot IS the order the tools appear in the protocol's own");
        Console.WriteLine("  system prompt, so a slope here is a property of that prompt.");
    }

    /// <summary>Every arm restricted to the trials the MODEL-FREE arm got wrong — the only region where a
    /// model can change an outcome, and therefore the only region that prices one.
    ///
    /// <para><b>The headline table cannot answer this and reads as though it does.</b> On a corpus where
    /// cosine already picks the right tool, an arm scoring 75% overall may be winning nothing a 333 MB
    /// vector backend was not already winning for free. Splitting on the free arm separates "it agreed with
    /// cosine" from "it beat cosine", and only the second is worth bytes.</para>
    ///
    /// <para>The subset is SMALL by construction — it is exactly the corpus's own difficulty — so the
    /// interval is printed beside it. A wide one is a statement about the fixture, not about the models.</para>
    /// </summary>
    private static void PrintWhereFreeArmFails(IReadOnlyList<Cell> cells)
    {
        var rows = Outcomes.ToList();
        var wrong = rows.Where(o => o.Arm == "cosine" && !o.Correct)
            .Select(o => (o.N, o.Trial)).ToHashSet();

        Console.WriteLine("\nWHERE THE MODEL-FREE ARM IS WRONG — the only region a model can pay for\n");
        Console.WriteLine("  Accuracy restricted to the trials `cosine` got wrong, reported WITHIN each roster");
        Console.WriteLine("  size. Not pooled: the same trial appears at all five sizes, so a pooled rate would");
        Console.WriteLine("  count it five times and print an interval five times too narrow.\n");

        var header = string.Join(" ", RosterSizes.Select(n => $"{$"N={n}",8}"));
        Console.WriteLine($"{"arm",-13} {header}   (subset size per N below)");
        Console.WriteLine(new string('-', 13 + header.Length + 26));

        foreach (var arm in cells.Select(c => c.Arm).Distinct().Where(a => a != "cosine")
                     .OrderBy(a => a, StringComparer.Ordinal))
        {
            var cellsByN = RosterSizes.Select(n =>
            {
                var hard = rows.Where(o => o.Arm == arm && o.N == n && wrong.Contains((o.N, o.Trial))).ToList();
                return hard.Count == 0 ? "       -"
                    : $"{(double)hard.Count(o => o.Correct) / hard.Count,8:P1}";
            });
            Console.WriteLine($"{arm,-13} {string.Join(" ", cellsByN)}");
        }

        var sizes = RosterSizes.Select(n => $"N={n}: {wrong.Count(w => w.N == n)}");
        Console.WriteLine($"\n  subset size   {string.Join("  ", sizes)}   of {rows.Count(o => o.Arm == "cosine") / RosterSizes.Length} trials");
        Console.WriteLine("\n  Read this table, not the overall one. An arm that is strong overall and near zero");
        Console.WriteLine("  here is TRACKING THE EMBEDDER rather than reasoning, and is not worth its bytes on");
        Console.WriteLine("  this corpus. `loop-oracle` must still read 100% — it is unaffected by difficulty.");
        Console.WriteLine("  A small subset is a fact about the FIXTURE: it is how much of this corpus needs a");
        Console.WriteLine("  model at all, and widening it means writing more oblique requests, not more trials.");
    }

    /// <summary>Every arm's outcome on every trial, one JSON object per line. The grid's own raw data, so a
    /// question asked later is a query rather than another run of the servers.</summary>
    private static async Task WriteOutcomesAsync()
    {
        var to = Path.Combine("devtools", "_affordance", $"outcomes-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        await File.WriteAllLinesAsync(to, Outcomes
            .OrderBy(o => o.Arm, StringComparer.Ordinal).ThenBy(o => o.N).ThenBy(o => o.Trial)
            .Select(o => JsonSerializer.Serialize(o)));
        Console.WriteLine($"\n  per-trial outcomes: {Outcomes.Count} row(s) → {to}");
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
        var to = Path.Combine("devtools", $"_affordance-dump-{DateTime.Now:HHmmss}.jsonl");
        await File.WriteAllLinesAsync(to, Dumped.OrderBy(l => l, StringComparer.Ordinal));
        Console.WriteLine($"\n  --dump: {Dumped.Count} raw reply(ies) → {to}");
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The corpus is SYNTHETIC and English-only. No corpus of real tool rosters exists");
        Console.WriteLine("    here, so nothing about absolute difficulty transfers off this fixture.");
        Console.WriteLine("  - MODEL CAPABILITY, on most of this corpus. A tool description says what the tool");
        Console.WriteLine("    does, so a request for it is usually its nearest neighbour and a vector backend gets");
        Console.WriteLine("    it free — see the gold-rank line above. That makes this a clean measurement of");
        Console.WriteLine("    the TRANSPORT, which is what docs/task-archive.md Part 236 asked for, and a");
        Console.WriteLine("    weak one of affordance REASONING, which it did not. The free-arm-wrong table");
        Console.WriteLine("    is the only part that prices a model, and its subset size is the honest");
        Console.WriteLine("    ceiling on that claim.");
        Console.WriteLine("  - The NATIVE transport. It is silently inert on both models this machine holds");
        Console.WriteLine("    and blocked on a positive control — docs/task-archive.md Part 236,");
        Console.WriteLine("    pitfalls.md.");
        Console.WriteLine("  - ONE tool per request. A roster task that needs two calls, or none, is a");
        Console.WriteLine("    different question and this fixture cannot pose it.");
        Console.WriteLine("  - ONE protocol prompt: the shipped one. It is a compile-time constant and not a");
        Console.WriteLine("    knob a deployment has, which is exactly why it is the thing worth measuring.");
        Console.WriteLine("  - NO GUARD RAIL and NO tool that throws. Both change the loop's path and neither");
        Console.WriteLine("    is exercised here.");
        Console.WriteLine($"  - ONE output cap, {LoopMaxTokens} tokens. It cannot truncate a compliant tool");
        Console.WriteLine("    call, but it CAN truncate a rambling final answer into unparseable JSON — so");
        Console.WriteLine("    read `converged` as a floor, and the choice columns as unaffected by it.");
        Console.WriteLine("  - Temperature 0 throughout, requests run concurrently, and accuracy is the metric");
        Console.WriteLine("    — so a busy device costs WALL CLOCK and not validity.");
    }
}
