using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;

namespace Lyntai.Benchmarks;

/// <summary>
/// How often does a REAL annotator invent a new handle past a perfectly good existing one?
///
/// <para><b>The gap this fills.</b> <c>memory-annotation</c> measures the mechanism's CEILING with a
/// perfect annotator, and <c>LlmAnnotationLiveTests</c> asserts the PROPERTY that some handle is shared by
/// two of three facts. Neither is a RATE, and a rate is what decides whether the published ceiling is
/// something a deployment gets or something it only approaches (`TASKS.md` Part 65).</para>
///
/// <para><b>It needs no engine, no store and no recall</b>, which is what keeps it small and sharp: drift
/// is a property of <see cref="IMemoryAnnotationPolicy"/> alone. Wiring the graph engine would measure
/// recall quality — a different question that <c>memory-annotation</c> already owns.</para>
///
/// <para><b>Three numbers, because DRIFT ALONE IS VACUOUS.</b> A model answering one handle for every fact
/// in the corpus drifts 0% and links everything to everything; a model answering nothing drifts 0% and
/// links nothing. So COLLAPSE (one handle spanning unrelated clusters) and EMPTY are reported beside it,
/// and a model is only good when all three are low.</para>
///
/// <para><b>The SHIPPED policy, never a bench-local one</b> — the prompt, the parsing and the fail-open
/// behaviour are what a consumer inherits, so an arm that reimplemented them would price something nobody
/// gets.</para></summary>
internal static class MemoryAnnotationDriftSweep
{
    /// <summary>One entity, and facts about it that a later one can only resolve through the earlier ones —
    /// which is the case the seam exists for and the case a pronoun makes hard.</summary>
    private sealed record Cluster(string Key, IReadOnlyList<string> Facts);

    private sealed record Score(string Model, string Language, int Eligible, int Drifted, int Collapsed,
        int Empty, int DistinctHandles, int Clusters);

    /// <summary>One fact's answer, kept so the reconciler ladder can be scored on IDENTICAL model output —
    /// re-asking the model per rung would price its sampling noise as a reconciler difference.</summary>
    private sealed record Answer(string Cluster, int Index, string Fact, IReadOnlyList<string> Subjects);

    /// <summary>Set by <c>--dump</c>. What the model ACTUALLY answered, per cluster — the only way to tell a
    /// drift that CODE could reconcile ("spouse" against "my spouse") from one it could not ("Alice"
    /// against "Kyoto"), which is the question that decides whether a normalizer is worth building.</summary>
    private static bool _dump;

    /// <summary>Eight entities per language, four facts each. The first fact NAMES the entity and the rest
    /// refer to it obliquely, so reuse cannot be had from surface tokens — the whole argument for a model
    /// here is that the judgement is semantic and language-independent.</summary>
    private static IReadOnlyList<Cluster> Fixture(bool chinese) => chinese
        ?
        [
            new("spouse", ["我的配偶是爱丽丝", "她在一家医院做麻醉师", "我们是在京都的一次旅行中认识的", "她下个月过生日"]),
            new("car", ["我买了一辆二手的蓝色轿车", "它的里程表显示八万公里", "上周它的刹车需要更换", "我通常把它停在车库里"]),
            new("manager", ["我的经理叫陈先生", "他每周一主持例会", "他以前在一家银行工作", "他更喜欢用邮件沟通"]),
            new("apartment", ["我住在城南的一套公寓里", "它在六楼而且没有电梯", "租约在明年三月到期", "阳台朝南采光很好"]),
            new("dog", ["我们养了一只叫豆豆的狗", "它每天早上要出去散步", "兽医说它有点超重", "它怕打雷"]),
            new("thesis", ["我在写一篇关于水文学的论文", "第三章还差一节没写完", "导师下周要看初稿", "它的截止日期是六月"]),
            new("guitar", ["我有一把旧的原声吉他", "它的第六弦上个月断了", "这是我父亲传给我的", "琴身上有一道划痕"]),
            new("clinic", ["附近新开了一家牙科诊所", "它周六也营业", "前台的人很客气", "停车位不太够"]),
        ]
        :
        [
            new("spouse", ["my spouse is Alice", "she works as an anaesthetist at a hospital",
                "we met on a trip to Kyoto", "her birthday is next month"]),
            new("car", ["I bought a used blue saloon", "the odometer reads eighty thousand kilometres",
                "its brakes needed replacing last week", "I usually park it in the garage"]),
            new("manager", ["my manager is called Mr Chen", "he runs the standup every Monday",
                "he used to work at a bank", "he prefers to be reached by email"]),
            new("apartment", ["I live in a flat on the south side of town", "it is on the sixth floor with no lift",
                "the lease runs out next March", "the balcony faces south and gets good light"]),
            new("dog", ["we have a dog called Biscuit", "he needs a walk every morning",
                "the vet says he is slightly overweight", "he is frightened of thunder"]),
            new("thesis", ["I am writing a thesis on hydrology", "one section of chapter three is still unwritten",
                "my supervisor wants the draft next week", "the deadline is in June"]),
            new("guitar", ["I own an old acoustic guitar", "its sixth string snapped last month",
                "it was handed down from my father", "there is a scratch across the body"]),
            new("clinic", ["a new dental clinic opened nearby", "it is open on Saturdays",
                "the person on reception is very polite", "there is not enough parking"]),
        ];

    /// <summary>Answers the cluster's own key for every fact in it — the SELF-CHECK arm.
    ///
    /// <para><b>Without it the drift column is unreadable</b>, because "models drift" and "the scorer is
    /// broken" produce the same number. A perfect annotator must score exactly 0% drift, 0 collapse and 0
    /// empty through the same scorer; anything else condemns the instrument rather than the model. It runs
    /// on every invocation for the same reason the reranker's distinct-score audit does.</para></summary>
    private sealed class PerfectAnnotator(IReadOnlyDictionary<string, string> keyByFact) : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(keyByFact.TryGetValue(request.Write.Content, out var key)
                ? new MemoryAnnotation([key])
                : MemoryAnnotation.None);
    }

    /// <summary>The CANDIDATE shape: annotation as `select-from-list` rather than `extract`.
    ///
    /// <para><b>Bench-local ON PURPOSE, which is the opposite of the rule elsewhere here.</b> Every other
    /// arm drives the SHIPPED policy because it prices what a consumer inherits; this one prices a PROPOSED
    /// change to that policy, so it has to be written here before it can earn its way in.</para>
    ///
    /// <para><b>Why the shape and not the wording.</b> The shipped prompt already says "REUSE an existing
    /// subject … Copy it EXACTLY", and every model drifts 58-90% regardless. Selection makes invention
    /// STRUCTURALLY impossible instead of discouraged — which is the fix `docs/model-tasks.md` §1
    /// prescribes for a generative task that must reuse. The <c>new</c> escape is required for a genuinely
    /// new entity and is also the hole drift can return through, which is the thing being measured.</para></summary>
    private sealed class SelectingAnnotator(SweepDoubles.OpenAiCompatibleChat chat, int listLimit)
        : IMemoryAnnotationPolicy
    {
        public async Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            var known = request.Known.Take(listLimit).ToList();
            var numbered = string.Join("\n", known.Select((k, i) => $"{i + 1}. {k}"));
            var recent = request.Recent.Count == 0
                ? ""
                : "\nEarlier facts, newest first:\n" + string.Join("\n", request.Recent.Select(r => "- " + r));

            // With NOTHING in use yet there is no list to select from, and offering one is an ABSORBING
            // STATE: the model answers {"pick": 1} against an empty list, the pick is out of range and
            // dropped, so no handle is ever recorded and the list can never grow. Measured 2026-09-15 as
            // 32 of 32 facts unlabelled. A selective seam has to bootstrap generatively.
            var prompt = known.Count == 0
                ? $$"""
                    A memory system connects facts that are about the same thing.

                    Fact: {{request.Write.Content}}
                    {{recent}}
                    Give this fact a SHORT, STABLE handle for the one entity or topic it is about — the same
                    handle you would produce again for any other fact about that same thing. Prefer the
                    enduring thing over the passing detail: a fact about a person's job is about the PERSON.
                    Write it in the fact's own language. Answer with ONLY: {"new": "<handle>"}
                    """
                : $$"""
                    A memory system connects facts that are about the same thing.

                    Fact: {{request.Write.Content}}
                    {{recent}}
                    Subjects already in use:
                    {{numbered}}

                    Which ONE of the numbered subjects is this fact about? Answer with ONLY a JSON object.
                    If one of them is the thing this fact concerns: {"pick": <number>}
                    If none of them is: {"new": "<a short stable handle, in the fact's own language>"}
                    Prefer the enduring thing over the passing detail: a fact about a person's job is about
                    the PERSON. Resolve pronouns using the earlier facts.
                    """;

            var reply = await chat.AskAsync(prompt, ct, maxTokens: 64).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply))
            {
                if (_dump) Console.WriteLine("    select: NULL/EMPTY reply from the endpoint");
                return MemoryAnnotation.None;
            }

            var pick = System.Text.RegularExpressions.Regex.Match(reply, "\"pick\"\\s*:\\s*(\\d+)");
            if (pick.Success && int.TryParse(pick.Groups[1].Value, out var n) && n >= 1 && n <= known.Count)
                return new MemoryAnnotation([known[n - 1]]);

            var fresh = System.Text.RegularExpressions.Regex.Match(reply, "\"new\"\\s*:\\s*\"([^\"]+)\"");
            if (fresh.Success) return new MemoryAnnotation([fresh.Groups[1].Value.Trim()]);

            if (_dump) Console.WriteLine($"    select: UNPARSED {reply.Replace("\n", "\\n")}");
            return MemoryAnnotation.None;
        }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        _dump = args.Contains("--dump");
        using var http = new HttpClient();

        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-annotation-drift");
        if (chat is null)
        {
            // REFUSES rather than substituting: this arm measures what a MODEL does, so a scripted stand-in
            // would measure the stand-in. The same posture every model-priced arm here takes.
            Console.Error.WriteLine("memory-annotation-drift: this measures a MODEL's drift, so it will not "
                + "run without one. Point LYNTAI_LIVE_CHAT_URL (or LYNTAI_LIVE_MODEL_URL) at an "
                + "OpenAI-compatible endpoint and name it with LYNTAI_LIVE_CHAT_MODEL.");
            return 1;
        }

        var label = SweepDoubles.ChatModel;
        // The GAP ladder. 0 is the consecutive fixture every figure before 2026-09-15 was taken on; 7 is a
        // full round-robin over the eight clusters, which is what a real interleaved stream looks like.
        var gaps = ArgValue(args, "--gap") is { } g && int.TryParse(g, out var one)
            ? [one]
            : new[] { 0, 1, 7 };
        var languages = args.Contains("--english-only")
            ? new[] { (Name: "english", Chinese: false) }
            : [(Name: "english", Chinese: false), (Name: "chinese", Chinese: true)];

        PrintPreamble(label, languages.Length);

        var scores = new List<Score>();
        foreach (var (name, chinese) in languages)
        {
            var fixture = Fixture(chinese);

            // The self-check FIRST, so a broken scorer is seen before any model time is spent on it.
            var perfect = fixture.SelectMany(c => c.Facts.Select(f => (f, c.Key)))
                .ToDictionary(x => x.f, x => x.Key, StringComparer.Ordinal);
            scores.Add(ScoreAnswers("PERFECT (self-check)", name,
                await AskAsync(fixture, new PerfectAnnotator(perfect), 0), Reconciler.Exact));

            foreach (var gap in gaps)
            {
                // ONE model pass per GAP, scored by every reconciler — the reconcilers price CODE, so the
                // model's answers must be the same bytes under each. Re-asking would price sampling noise.
                var answers = await AskAsync(fixture,
                    new LlmMemoryAnnotationPolicy(new SweepDoubles.BenchClientFactory(chat)), gap);
                foreach (var mode in new[]
                         { Reconciler.Exact, Reconciler.Fragment, Reconciler.Recency })
                    scores.Add(ScoreAnswers($"{label} gap{gap} extract +{mode}", name, answers, mode));

                // The SHAPE arm, at the shipped list length and a short one — §2's selective measurements
                // degrade hard with length, so a single length could not tell the shape from the list.
                foreach (var limit in new[] { KnownWindow, 8 })
                {
                    var picked = await AskAsync(fixture, new SelectingAnnotator(chat, limit), gap);
                    scores.Add(ScoreAnswers(
                        $"{label} gap{gap} select@{limit}", name, picked, Reconciler.Exact));
                }
            }
        }

        PrintTable(scores);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    /// <summary>The engine's own context bounds (`GraphMemoryOptions`), so the fixture asks the model what
    /// a deployment would ask it. <b>Getting these wrong flatters the model</b>: `Recent` scoped per cluster
    /// and unbounded hands it a cleaner pronoun context than it will ever have.</summary>
    private const int RecentWindow = 8;

    private const int KnownWindow = 24;

    /// <summary>The write ORDER. <paramref name="gap"/> is how many other clusters' facts fall between two
    /// consecutive facts of one cluster: 0 reproduces the consecutive fixture, and a full round-robin over
    /// eight clusters is 7.
    ///
    /// <para><b>The consecutive order is the unrealistic one</b>, and it is the one every number before
    /// 2026-09-15 was taken on. A real stream interleaves, which both makes the model's pronoun harder and
    /// is the only condition under which a RECENCY rule can be honestly priced — at gap 0 an adjacency rule
    /// is handed the answer.</para></summary>
    private static IReadOnlyList<(string Cluster, int Index, string Fact)> Order(
        IReadOnlyList<Cluster> clusters, int gap)
    {
        var order = new List<(string, int, string)>();
        var size = Math.Max(1, gap + 1);
        for (var start = 0; start < clusters.Count; start += size)
        {
            var group = clusters.Skip(start).Take(size).ToList();
            for (var i = 0; i < group.Max(c => c.Facts.Count); i++)
                foreach (var c in group)
                    if (i < c.Facts.Count) order.Add((c.Key, i, c.Facts[i]));
        }

        return order;
    }

    /// <summary>One model pass, in the given write order. Kept SEPARATE from scoring so the reconciler
    /// ladder is scored on identical answers — re-asking per rung would price the model's sampling noise as
    /// a code difference.
    ///
    /// <para><b>`Known` accumulates the RAW answers</b>, because that is what the engine stores today: the
    /// reconciler is being evaluated as a change to LINKING, not to what the model is shown.</para></summary>
    private static async Task<IReadOnlyList<Answer>> AskAsync(
        IReadOnlyList<Cluster> clusters, IMemoryAnnotationPolicy annotator, int gap)
    {
        var answers = new List<Answer>();
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var recent = new List<string>();                  // GLOBAL to the task/scope, as the engine's is

        foreach (var (cluster, index, fact) in Order(clusters, gap))
        {
            var known = uses.OrderByDescending(k => k.Value).Take(KnownWindow).Select(k => k.Key).ToList();
            var annotation = await annotator.AnnotateAsync(new MemoryAnnotationRequest(
                new MemoryWrite("drift", "session", fact), recent.Take(RecentWindow).ToList(), known));

            var subjects = annotation.Subjects
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            answers.Add(new Answer(cluster, index, fact, subjects));
            foreach (var s in subjects) uses[s] = uses.GetValueOrDefault(s) + 1;
            recent.Insert(0, fact);
        }

        return answers;
    }

    /// <summary>Score one model pass under one reconciler.</summary>
    private static Score ScoreAnswers(
        string model, string language, IReadOnlyList<Answer> answers, Reconciler mode)
    {
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var anchors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var clusters = new HashSet<string>(StringComparer.Ordinal);
        int eligible = 0, drifted = 0, empty = 0;
        IReadOnlyCollection<string> previous = [];        // the PRIOR write's subjects, whatever cluster

        foreach (var answer in answers)
        {
            clusters.Add(answer.Cluster);
            var known = uses.OrderByDescending(k => k.Value).Take(KnownWindow).Select(k => k.Key).ToList();
            var subjects = Reconcile(
                answer.Subjects.ToHashSet(StringComparer.OrdinalIgnoreCase), known, mode,
                answer.Fact, previous);
            previous = subjects;
            var anchor = anchors.GetValueOrDefault(answer.Cluster);

            if (subjects.Count == 0)
            {
                empty++;
                if (_dump) Console.WriteLine($"    empty   [{answer.Cluster}]");
            }
            else if (anchor is null)
            {
                anchors[answer.Cluster] = subjects;               // the handle later facts should reuse
                if (_dump) Console.WriteLine($"    anchor  [{answer.Cluster}] {{{Join(subjects)}}}");
            }
            else
            {
                // ELIGIBLE means the anchor was on offer: it is in `known`, so reusing it was available
                // and choosing otherwise is drift rather than ignorance.
                eligible++;
                var drift = !subjects.Overlaps(anchor);
                if (drift) drifted++;
                if (_dump)
                    Console.WriteLine($"    {(drift ? "DRIFT " : "reuse ")} [{answer.Cluster}] "
                        + $"anchor={{{Join(anchor)}}} got={{{Join(subjects)}}}");
            }

            foreach (var s in subjects)
            {
                uses[s] = uses.GetValueOrDefault(s) + 1;
                (owners.TryGetValue(s, out var set) ? set : owners[s] = new(StringComparer.Ordinal))
                    .Add(answer.Cluster);
            }
        }

        // COLLAPSE: a handle spanning clusters that share no entity. This is the control that makes the
        // drift column readable — "owner" for everything scores a perfect 0% drift and is worthless.
        var collapsed = owners.Count(o => o.Value.Count > 1);
        return new Score(model, language, eligible, drifted, collapsed, empty, uses.Count, clusters.Count);
    }

    private static string Join(IEnumerable<string> handles) => string.Join(" | ", handles);

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>How hard CODE tries to reconcile a handle the model invented against one already in use.
    /// A ladder, because each rung links strictly more than the one above it and the question is where the
    /// gain stops paying for the over-linking.</summary>
    private enum Reconciler
    {
        /// <summary>What the engine does today: equality on the normalized handle.</summary>
        Exact,

        /// <summary>…plus CONTAINMENT either way, by the script-aware rule
        /// <see cref="MemorySubject.Matches"/> already applies on the RECALL side — a spaceless script
        /// matches as a plain substring, a spaced one only on a word boundary.</summary>
        Containment,

        /// <summary>…plus a shared FRAGMENT where neither contains the other — a shared word in a spaced
        /// script, a shared 2-gram or longer in a spaceless one.</summary>
        Fragment,

        /// <summary>A DIFFERENT axis, not a rung on the three above: Exact plus carrying the PREVIOUS
        /// write's subjects onto a fact that refers to its entity only by pronoun and tied to nothing
        /// already known. The last of the three signals entity resolution uses, and the only pure-code one
        /// the dumped answers do not already rule out.
        ///
        /// <para><b>Scored against `--gap` or it is meaningless.</b> At gap 0 the previous write is always
        /// the same cluster, so this is handed the answer; the question is what it does once a real stream
        /// puts other entities in between, and the price is paid in COLLAPSE.</para></summary>
        Recency,
    }

    /// <summary>Third-person and possessive references, the closed vocabulary that makes a fact's subject
    /// unrecoverable from its own words. Deliberately small and language-specific — this is the cheap,
    /// inspectable half of coreference, not a resolver.
    ///
    /// <para><b>Chinese is PRO-DROP</b>, so a Chinese fact often names no pronoun at all and this signal
    /// simply is not there to read — a limitation of the language, not of the list.</para></summary>
    private static readonly string[] EnglishPronouns =
        ["he", "she", "it", "him", "her", "his", "hers", "its", "they", "them", "their"];

    private static readonly string[] ChinesePronouns = ["他", "她", "它", "其"];

    private static bool RefersByPronoun(string fact)
    {
        var text = MemorySubject.Normalize(fact);
        if (ChinesePronouns.Any(p => text.Contains(p, StringComparison.Ordinal))) return true;

        var words = text.Split([' ', ',', '.', ';', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(w => EnglishPronouns.Contains(w, StringComparer.Ordinal));
    }

    /// <summary>The handles a fact should be indexed under once code has done what it can: what the model
    /// returned, PLUS any known handle this reconciler can tie one of them to.
    ///
    /// <para><b>Additive, never replacing.</b> The invented handle is usually a true statement about the
    /// fact ("parking", "reception") and deleting it would lose a real subject; what is missing is the LINK,
    /// so the reconciler only ever adds one.</para></summary>
    private static HashSet<string> Reconcile(
        HashSet<string> subjects, IReadOnlyCollection<string> known, Reconciler mode,
        string fact, IReadOnlyCollection<string> previous)
    {
        if (mode == Reconciler.Recency)
        {
            // Carry only when the fact cannot name its own subject AND the model tied it to nothing already
            // in use — otherwise a fact that linked perfectly well would also inherit its neighbour.
            if (subjects.Count == 0 || previous.Count == 0) return subjects;
            if (subjects.Any(known.Contains) || !RefersByPronoun(fact)) return subjects;
            return [.. subjects, .. previous];
        }

        if (mode == Reconciler.Exact || known.Count == 0) return subjects;

        var result = new HashSet<string>(subjects, StringComparer.OrdinalIgnoreCase);
        foreach (var handle in subjects)
            foreach (var candidate in known)
            {
                if (result.Contains(candidate)) continue;
                if (Ties(handle, candidate, mode)) result.Add(candidate);
            }

        return result;
    }

    private static bool Ties(string handle, string candidate, Reconciler mode)
    {
        // The recall side's own rule, asked in both directions: "水文学论文" contains the known "水文学",
        // and a known "父亲的原声吉他" contains the handle "原声吉他".
        if (MemorySubject.Matches(handle, candidate) || MemorySubject.Matches(candidate, handle)) return true;
        if (mode != Reconciler.Fragment) return false;

        var a = MemorySubject.Normalize(handle);
        var b = MemorySubject.Normalize(candidate);
        if (Lyntai.Storage.SearchTerms.ProfileOf(a).ExpandsIntoGrams)
        {
            // A spaceless script: any shared run of 2+ characters. One character is a radical-level
            // coincidence and would tie almost anything to anything.
            for (var i = 0; i + 2 <= a.Length; i++)
                if (b.Contains(a.AsSpan(i, 2), StringComparison.Ordinal)) return true;
            return false;
        }

        var words = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length > 2 && words.Contains(w));
    }

    private static void PrintPreamble(string model, int languages)
    {
        Console.WriteLine("=== annotator DRIFT: how often a model invents past a handle it was shown ===");
        Console.WriteLine();
        Console.WriteLine("A fact links to an earlier one because their SUBJECTS MATCH, so an annotator that");
        Console.WriteLine("phrases the same entity differently each write connects nothing and looks exactly");
        Console.WriteLine("like having no annotator at all. memory-annotation measures the mechanism's ceiling");
        Console.WriteLine("with a PERFECT annotator; this measures how much of that a real model reaches.");
        Console.WriteLine();
        Console.WriteLine("  drift    of facts whose anchor handle was ON OFFER and not reused — lower is better");
        Console.WriteLine("  collapse handles spanning unrelated clusters — the control, because a model that");
        Console.WriteLine("           answers one handle for everything drifts 0% and links everything");
        Console.WriteLine("  empty    facts it declined to label — drifts 0% and links nothing");
        Console.WriteLine();
        Console.WriteLine($"Model: {model}   languages: {languages}   8 clusters x 4 facts each");
        Console.WriteLine("SYNTHETIC fixture — the weakest evidence tier here; read the directions, not the");
        Console.WriteLine("magnitudes, and never carry one into a deployment (model-decoupling.md).");
        Console.WriteLine();
    }

    private static void PrintTable(IReadOnlyList<Score> scores)
    {
        Console.WriteLine($"{"model / reconciler",-40} {"lang",-9} {"drift",-18} {"collapse",-20} {"empty",-7}");
        Console.WriteLine(new string('-', 100));
        foreach (var s in scores)
        {
            var drift = s.Eligible > 0
                ? $"{100.0 * s.Drifted / s.Eligible:F1}% ({s.Drifted}/{s.Eligible})"
                : "n/a";
            var collapse = $"{s.Collapsed} of {s.DistinctHandles} handles";
            Console.WriteLine($"{s.Model,-40} {s.Language,-9} {drift,-18} {collapse,-20} {s.Empty,-7}");
        }

        Console.WriteLine();
        // The self-check is a GATE on the table above, not a row to read: a perfect annotator that does not
        // score 0/0/0 means the scorer is wrong and every model number beside it is noise.
        var broken = scores.Where(s => s.Model.StartsWith("PERFECT", StringComparison.Ordinal))
            .Where(s => s.Drifted != 0 || s.Collapsed != 0 || s.Empty != 0 || s.Eligible == 0)
            .ToList();
        if (broken.Count > 0)
        {
            Console.WriteLine("  ! THE SELF-CHECK FAILED — a perfect annotator did not score 0% drift, 0");
            Console.WriteLine("    collapse, 0 empty. The instrument is wrong; ignore the model rows.");
            foreach (var s in broken)
                Console.WriteLine($"      {s.Language}: drift {s.Drifted}/{s.Eligible}, collapse "
                    + $"{s.Collapsed}, empty {s.Empty}");
        }

        // Both controls print their own failure, because a 0% drift has two worthless causes and neither
        // is distinguishable from the good one by the drift column alone.
        if (scores.All(s => s.Eligible == 0))
        {
            Console.WriteLine("  ! NOTHING was eligible — the model labelled at most one fact per cluster, so");
            Console.WriteLine("    an anchor was never on offer. That is a broken arm, not a 0% drift.");
        }
        else if (scores.Sum(s => s.Empty) > scores.Sum(s => s.Eligible))
        {
            Console.WriteLine("  ! More facts went UNLABELLED than were scored — read drift as a property of");
            Console.WriteLine("    the few it answered, not of the model.");
        }
    }
}
