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

    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
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
            scores.Add(await ScoreAsync("PERFECT (self-check)", name, fixture, new PerfectAnnotator(perfect)));

            scores.Add(await ScoreAsync(label, name, fixture,
                new LlmMemoryAnnotationPolicy(new SweepDoubles.BenchClientFactory(chat))));
        }

        PrintTable(scores);
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    /// <summary>One language: replay every cluster through the annotator with <c>Known</c> accumulating
    /// exactly as the engine accumulates it, then count.</summary>
    private static async Task<Score> ScoreAsync(
        string model, string language, IReadOnlyList<Cluster> clusters, IMemoryAnnotationPolicy annotator)
    {
        // Handle → how many facts used it, which is both the `Known` ordering the contract promises
        // ("most-used first") and the collapse control's raw material.
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        int eligible = 0, drifted = 0, empty = 0;

        foreach (var cluster in clusters)
        {
            var recent = new List<string>();
            HashSet<string>? anchor = null;          // what the cluster's FIRST answered fact was labelled

            foreach (var fact in cluster.Facts)
            {
                var known = uses.OrderByDescending(k => k.Value).Select(k => k.Key).ToList();
                var annotation = await annotator.AnnotateAsync(
                    new MemoryAnnotationRequest(new MemoryWrite("drift", "session", fact), recent, known));

                var subjects = annotation.Subjects
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (subjects.Count == 0) empty++;
                else if (anchor is null) anchor = subjects;       // the handle later facts should reuse
                else
                {
                    // ELIGIBLE means the anchor was on offer: it is in `known`, so reusing it was available
                    // and choosing otherwise is drift rather than ignorance.
                    eligible++;
                    if (!subjects.Overlaps(anchor)) drifted++;
                }

                foreach (var s in subjects)
                {
                    uses[s] = uses.GetValueOrDefault(s) + 1;
                    (owners.TryGetValue(s, out var set) ? set : owners[s] = new(StringComparer.Ordinal))
                        .Add(cluster.Key);
                }

                recent.Insert(0, fact);
            }
        }

        // COLLAPSE: a handle spanning clusters that share no entity. This is the control that makes the
        // drift column readable — "owner" for everything scores a perfect 0% drift and is worthless.
        var collapsed = owners.Count(o => o.Value.Count > 1);
        return new Score(model, language, eligible, drifted, collapsed, empty, uses.Count, clusters.Count);
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
        Console.WriteLine($"{"model",-26} {"lang",-9} {"drift",-18} {"collapse",-20} {"empty",-7}");
        Console.WriteLine(new string('-', 86));
        foreach (var s in scores)
        {
            var drift = s.Eligible > 0
                ? $"{100.0 * s.Drifted / s.Eligible:F1}% ({s.Drifted}/{s.Eligible})"
                : "n/a";
            var collapse = $"{s.Collapsed} of {s.DistinctHandles} handles";
            Console.WriteLine($"{s.Model,-26} {s.Language,-9} {drift,-18} {collapse,-20} {s.Empty,-7}");
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
