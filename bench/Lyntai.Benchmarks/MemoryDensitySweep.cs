using System.Collections.Concurrent;
using System.Diagnostics;

using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Salience;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>memory-density</c> — does a CORRECTION separate from a RECURRENCE on
/// <see cref="SalienceContext.SimilarCount"/> at all? The cheapest possible refutation of the gist tier's
/// promotion rule (<c>local/superpowers/specs/2026-08-27-gist-tier-design.md</c> §5): a correction resembles
/// exactly one stored entry, a recurrence resembles many, and if the two do not separate here — authored
/// fixtures, a real embedder, no corpus and no tier — nothing downstream can work.
/// </summary>
/// <remarks>
/// <para><b>Fixtures, never <see cref="MemoryCorpus"/>.</b> The corpus has no CORRECTION class — the gist
/// design's piece (2) adds a missing class of its own, and this is a DIFFERENT missing one. A separability
/// test needs only "which population is this write from", never a per-query ground truth, so hand-authored
/// fixtures are sufficient and independent of piece (2). Do not wire this to the corpus.</para>
///
/// <para><b>It refuses to run without a real embedder</b>, for the reason <c>memory-enrichment</c> and
/// <c>memory-importance</c> already established: a correction shares nearly every word with the fact it
/// corrects, so a bag-of-words fake rates it maximally similar to its target — and a recurrence shares words
/// with many. The fake would produce a plausible table measuring word overlap, the exact defect that withdrew
/// an earlier set of numbers (<c>TASKS.md</c> Part 69).</para>
///
/// <para><b>Pooling across <c>--languages</c> is NOT independent evidence.</b> Every non-English fixture is
/// a TRANSLATION of the same fixture-pair, and the language axis is built from structurally identical
/// corpora on purpose (<c>docs/DECISIONS.md</c> D55) — so pooling is one comparison measured several ways,
/// never independent draws; <see cref="PrintAuc"/> prints the effective-n caveat at run time.</para>
/// </remarks>
internal static class MemoryDensitySweep
{
    private const string Correction = "correction";
    private const string Recurrence = "recurrence";
    private const string Novel = "novel";

    private static readonly string[] Populations = [Correction, Recurrence, Novel];

    // The rank-sum AUC bar below which a signal is not worth building a promotion rule on. A judgement call
    // named in the brief this sweep implements, never a value read off any run.
    private const double SignalFloor = 0.65;

    // Below this many observations per population, AUC can only take the values {0, 0.5, 1} and would read
    // as a real signal when it is actually just "was the one recurrence value larger than the one correction
    // value". Two is the smallest n where the rank-sum statistic can differ from a coin flip. Fix round 1,
    // Important 4 — the single-language default run (n=1 per population) is exactly what this refuses.
    private const int MinimumPairedObservations = 2;

    // Every fixture's Prior list is padded to exactly this many entries (fix round 2, Critical 1): SearchAsync
    // requests SimilarityK + 1 = 6 neighbours, so any store short of that lets the STORE, not the embedder,
    // set SimilarCount's ceiling. 10 rather than the bare minimum of 6 because recurrence already sat there
    // (6 near-identical priors + 4 distractors) — matching it, rather than computing a per-population minimum,
    // makes every store the SAME size, not merely each one individually "enough".
    private const int CommonStoreSize = 10;

    /// <summary>One population of writes: a label, the entries written BEFORE the probe, and the probe
    /// itself. <see cref="Population"/> is the statistical class (for AUC/grouping); <see cref="Label"/> is
    /// what a table prints — identical today, kept separate so a future fixture can vary within one
    /// class without renaming the class.</summary>
    private sealed record Fixture(string Label, string Population, IReadOnlyList<string> Prior, string Probe);

    private sealed class CapturingSalience : IMemorySaliencePolicy
    {
        public List<SalienceContext> Seen { get; } = [];

        // A running policy must declare its own bit; None means "nothing computed this" and the engine
        // refuses the registration. 32-62 is the consumer range.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            Seen.Add(context);
            return MemorySignals.Empty;
        }
    }

    /// <summary><see cref="ComparableCount"/> rides alongside <see cref="SimilarCount"/> for two controls, not
    /// one: C0 (fix round 1, Critical 2) checks it is nonzero, and C1 (fix round 2, Critical 1) checks it is
    /// EQUAL across every population and language — the one that actually controls store size. See
    /// <see cref="PrintControls"/>.</summary>
    private sealed record Observation(CorpusLanguage Language, string Population, int SimilarCount, int ComparableCount);

    public static async Task<int> RunAsync(bool acrossLanguages = false)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var embedder = await SweepDoubles.TryRealEmbedderAsync(http, "memory-density");
        if (embedder is null) return 1;

        var stopwatch = Stopwatch.StartNew();

        // English FIRST, so a difference in its cells against a single-language run is an instrument bug
        // rather than a finding about language.
        CorpusLanguage[] languages = acrossLanguages
            ? [.. Enum.GetValues<CorpusLanguage>().OrderBy(l => l == CorpusLanguage.English ? 0 : 1)]
            : [CorpusLanguage.English];

        PrintPreamble(languages);

        var observations = new ConcurrentBag<Observation>();

        async ValueTask RunOneAsync(CorpusLanguage language, Fixture fixture)
        {
            var salience = new CapturingSalience();
            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine("density", new SqliteMemoryGraphStore(db.Factory),
                embedder: embedder, vectors: new InMemoryVectorStore(), saliencePolicies: [salience]);

            foreach (var prior in fixture.Prior)
                await engine.RememberAsync(new MemoryWrite("t", "s", prior));

            salience.Seen.Clear();
            await engine.RememberAsync(new MemoryWrite("t", "s", fixture.Probe));
            var context = salience.Seen.Single();
            observations.Add(new Observation(language, fixture.Population, context.SimilarCount, context.ComparableCount));
        }

        var cells = languages.SelectMany(l => BuildFixtures(l).Select(f => (Language: l, Fixture: f))).ToList();
        await Parallel.ForEachAsync(cells,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.Language, item.Fixture));

        var allObservations = observations.ToList();
        var controlsOk = PrintControls(allObservations);
        PrintDistribution(allObservations, languages);
        PrintAuc(allObservations, controlsOk);
        PrintNotSwept();

        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s over {languages.Length} language(s); " +
            $"{embedder.Misses} embed call(s), {embedder.Hits} cache hit(s).");
        return 0;
    }

    private static void PrintPreamble(IReadOnlyList<CorpusLanguage> languages)
    {
        Console.WriteLine();
        Console.WriteLine("memory-density — does a CORRECTION separate from a RECURRENCE on SimilarCount?");
        Console.WriteLine();
        Console.WriteLine($"  populations  {string.Join(" / ", Populations)}");
        Console.WriteLine($"  languages    {string.Join(" / ", languages)}");
        Console.WriteLine("  one authored fixture per (language, population); no MemoryCorpus — see the class remarks.");
        Console.WriteLine();
    }

    /// <summary>Three controls, and the split between C0 and C1 is deliberate — fix round 2 found C0 alone
    /// insufficient and a doc CLAIMING it covered the gap while nothing in the code did (its own defect, now
    /// corrected here rather than repeated).
    /// <para><b>C0</b>: every probe found SOME stored material (<c>ComparableCount &gt; 0</c>) — replaces a
    /// check that compared a loop counter against the length of the loop that incremented it and could never
    /// fail (fix round 1, Critical 2). This is a FLOOR OF ONE: it passes on a single-entry store, so on its
    /// own it does NOT control for store size.</para>
    /// <para><b>C1</b> is what does: every observation's <c>ComparableCount</c> is the SAME value, across
    /// every population and every language (fix round 2, Critical 1). <c>SearchAsync</c> requests
    /// <c>SimilarityK + 1</c> neighbours, so a store short of that count lets the STORE, not the embedder, set
    /// <c>SimilarCount</c>'s ceiling — this is the control that would have caught <c>correction</c> sitting at
    /// 5 stored entries while <c>recurrence</c> sat at 10, the exact confound fix round 1 left open.</para>
    /// <para><b>C2</b>: the <c>novel</c> population reports 0 everywhere it ran — if it does not, the floor is
    /// not doing anything and the distribution table measures nothing.</para>
    /// Any control failing withholds the AUC verdict, though the distribution table still prints.</summary>
    private static bool PrintControls(IReadOnlyList<Observation> observations)
    {
        Console.WriteLine("Controls");

        var c0 = observations.Count > 0 && observations.All(o => o.ComparableCount > 0);
        Console.WriteLine($"  C0 every probe found stored material to compare against (ComparableCount > 0): " +
            $"{observations.Count(o => o.ComparableCount > 0)}/{observations.Count}  {(c0 ? "ok" : "<== SUSPECT")}");

        var comparableValues = observations.Select(o => o.ComparableCount).Distinct().OrderBy(v => v).ToList();
        var c1 = comparableValues.Count == 1;
        Console.WriteLine($"  C1 ComparableCount is equal across every population and language: " +
            (c1
                ? $"{comparableValues[0]} everywhere  ok"
                : $"{comparableValues.Count} distinct value(s) ({string.Join(", ", comparableValues)})  <== SUSPECT"));

        var novel = observations.Where(o => o.Population == Novel).ToList();
        var c2 = novel.Count > 0 && novel.All(o => o.SimilarCount == 0);
        Console.WriteLine($"  C2 `novel` reports 0 everywhere: " +
            $"{novel.Count(o => o.SimilarCount == 0)}/{novel.Count}  {(c2 ? "ok" : "<== SUSPECT")}");

        if (!c0 || !c1 || !c2)
        {
            Console.WriteLine();
            Console.WriteLine("  A control failed — see above. The distribution table still prints; the AUC");
            Console.WriteLine("  verdict below is withheld rather than computed over an untrustworthy floor.");
        }

        Console.WriteLine();
        return c0 && c1 && c2;
    }

    private static void PrintDistribution(IReadOnlyList<Observation> observations, IReadOnlyList<CorpusLanguage> languages)
    {
        Console.WriteLine("Distribution: SimilarCount by language and population (mean, min/max)");
        Console.WriteLine($"  {"language",-14} {"population",-12} {"mean",6} {"min",5} {"max",5}");

        foreach (var language in languages)
        {
            foreach (var population in Populations)
            {
                var cell = observations.Where(o => o.Language == language && o.Population == population)
                    .Select(o => o.SimilarCount).ToList();
                if (cell.Count == 0)
                {
                    Console.WriteLine($"  {language,-14} {population,-12} {"-",6} {"-",5} {"-",5}");
                    continue;
                }
                Console.WriteLine($"  {language,-14} {population,-12} {cell.Average(),6:F2} {cell.Min(),5} {cell.Max(),5}");
            }
        }
        Console.WriteLine();
    }

    /// <summary>The separability question, by the rank-sum (Mann-Whitney U) identity: pools every language's
    /// own correction/recurrence pair into ONE AUC. AUC 0.5 is chance; 1.0 is perfect separation. Withheld
    /// below <see cref="MinimumPairedObservations"/> per population (fix round 1, Important 4 — n=1 can only
    /// read 0, 0.5 or 1) and captioned, when it runs on more than one language, with the reminder that those
    /// languages are TRANSLATIONS rather than independent draws (Important 3; see the class remarks). The
    /// threshold reported alongside is the <c>SimilarCount</c> cut that maximises sensitivity + specificity -
    /// 1 (Youden's J), read as "correction below, recurrence at or above".</summary>
    private static void PrintAuc(IReadOnlyList<Observation> observations, bool controlsOk)
    {
        Console.WriteLine("Separability: recurrence vs correction (SimilarCount)");

        var recurrence = observations.Where(o => o.Population == Recurrence).Select(o => o.SimilarCount).ToList();
        var correction = observations.Where(o => o.Population == Correction).Select(o => o.SimilarCount).ToList();

        if (!controlsOk)
        {
            Console.WriteLine("  Verdict withheld — see Controls above.");
            Console.WriteLine();
            return;
        }
        if (recurrence.Count < MinimumPairedObservations || correction.Count < MinimumPairedObservations)
        {
            Console.WriteLine($"  Verdict withheld — {recurrence.Count} recurrence / {correction.Count} correction " +
                $"observation(s): below {MinimumPairedObservations} per population, AUC can only read 0, 0.5 or 1 " +
                "and would not be a real signal. Run with --languages for more.");
            Console.WriteLine();
            return;
        }

        var auc = RankSumAuc(recurrence, correction);
        var (threshold, sensitivity, specificity, j) = BestThreshold(recurrence, correction);

        Console.WriteLine($"  AUC = {auc:F3} over {recurrence.Count} recurrence / {correction.Count} correction " +
            "observation(s), pooled across every language this run touched.");
        if (recurrence.Count > 1)
        {
            Console.WriteLine("  These are TRANSLATIONS of one fixture-pair, not independent draws (D55) — read");
            Console.WriteLine($"  this as a robustness check across {recurrence.Count} scripts with an EFFECTIVE");
            Console.WriteLine($"  n of 1, never as {recurrence.Count} times the evidence.");
        }
        Console.WriteLine($"  Best threshold: SimilarCount >= {threshold} predicts recurrence " +
            $"(sensitivity {sensitivity:F2}, specificity {specificity:F2}, J = {j:F2}).");
        Console.WriteLine();
        Console.WriteLine(auc >= SignalFloor
            ? $"  Signal: separable (AUC >= {SignalFloor:F2}) — worth building the gist tier's promotion rule on."
            : $"  Signal: NOT separable enough (AUC < {SignalFloor:F2}) — the promotion rule has nothing to read.");
        Console.WriteLine();
    }

    /// <summary>AUC via the rank-sum identity: rank the pooled, sorted sample (ties get the average rank),
    /// then <c>(positiveRankSum - n(n+1)/2) / (n * m)</c> is exactly the probability a random positive
    /// outranks a random negative — the same statistic as Mann-Whitney U without enumerating every pair.
    /// </summary>
    private static double RankSumAuc(IReadOnlyList<int> positives, IReadOnlyList<int> negatives)
    {
        var combined = positives.Select(v => (Value: v, IsPositive: true))
            .Concat(negatives.Select(v => (Value: v, IsPositive: false)))
            .OrderBy(x => x.Value)
            .ToList();

        var ranks = new double[combined.Count];
        var i = 0;
        while (i < combined.Count)
        {
            var j = i;
            while (j < combined.Count && combined[j].Value == combined[i].Value) j++;
            var averageRank = (i + 1 + j) / 2.0; // 1-based ranks i+1..j, averaged for the tied block
            for (var k = i; k < j; k++) ranks[k] = averageRank;
            i = j;
        }

        var positiveRankSum = combined.Select((x, idx) => (x.IsPositive, Rank: ranks[idx]))
            .Where(x => x.IsPositive).Sum(x => x.Rank);
        var n = positives.Count;
        var m = negatives.Count;
        return (positiveRankSum - (n * (n + 1) / 2.0)) / (n * (double)m);
    }

    /// <summary>The <c>SimilarCount</c> cut maximising sensitivity + specificity - 1 (Youden's J) — the
    /// single threshold a <c>PromotionSupport</c> option would be set to if this fixture set were the whole
    /// answer.</summary>
    private static (int Threshold, double Sensitivity, double Specificity, double J) BestThreshold(
        IReadOnlyList<int> positives, IReadOnlyList<int> negatives)
    {
        var candidates = positives.Concat(negatives).Distinct().OrderBy(v => v).ToList();
        var above = (candidates.Count == 0 ? 0 : candidates[^1]) + 1;
        var best = (Threshold: above, Sensitivity: 0.0, Specificity: 0.0, J: double.NegativeInfinity);

        foreach (var t in candidates.Append(above))
        {
            var sensitivity = positives.Count(v => v >= t) / (double)positives.Count;
            var specificity = negatives.Count(v => v < t) / (double)negatives.Count;
            var j = sensitivity + specificity - 1;
            if (j > best.J) best = (t, sensitivity, specificity, j);
        }
        return best;
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("What this does NOT settle");
        Console.WriteLine("  - SimilarityK = 5 and MinSimilarity = 0.6 bound and define the count and are both");
        Console.WriteLine("    unmeasured (\"a starting point, not a tuned value\" — GraphMemoryOptions' own doc),");
        Console.WriteLine("    so a ceiling effect and a floor effect are both possible.");
        Console.WriteLine("  - Fixtures are authored, which caps what an absolute number is worth.");
        Console.WriteLine("  - Separability on authored fixtures is not separability on a real corpus.");
        Console.WriteLine("  - Every non-English fixture is this author's own best-effort text, unreviewed by a");
        Console.WriteLine("    native speaker — and four of the five --languages arms feed the pooled AUC.");
    }

    private static IReadOnlyList<Fixture> BuildFixtures(CorpusLanguage language) => language switch
    {
        CorpusLanguage.English => EnglishFixtures,
        CorpusLanguage.Chinese => ChineseFixtures,
        CorpusLanguage.Japanese => JapaneseFixtures,
        CorpusLanguage.Korean => KoreanFixtures,
        CorpusLanguage.ChineseMixed => ChineseMixedFixtures,
        // Throws rather than defaulting to English text under a foreign label — CorpusLanguage has grown
        // before (four arms to five; fix round 1, Important 6), and a silent fallback would pool a row
        // LABELLED with the new language whose cells were actually measured on English prose.
        _ => throw new InvalidOperationException(
            $"{nameof(MemoryDensitySweep)} has no authored fixtures for {language}. Add a Fixtures array for " +
            $"it and a {nameof(BuildFixtures)} arm — do not let it fall through to English."),
    };

    /// <summary>Builds one language's three fixtures, padding every population's store to the SAME total
    /// (<see cref="CommonStoreSize"/>) from one shared distractor pool — fix round 2, Critical 1. Fix round
    /// 1 shared the pool but did not equalise totals: <c>correction</c> landed at 5 entries, <c>recurrence</c>
    /// at 10 (its distractors, added on top of 6 already-saturating priors, were a no-op), and
    /// <c>SimilarCount</c>'s ceiling was still set by the STORE rather than the embedder — exactly the
    /// confound this exists to remove. <c>novel</c>'s own priors ARE the pool; <c>correction</c> and
    /// <c>recurrence</c> draw only as much of it as their own signal-bearing priors leave short of the
    /// common total, so the SAME material backs every population's padding.</summary>
    private static Fixture[] BuildLanguageFixtures(string correctionFact, string correctionProbe,
        IReadOnlyList<string> recurrencePriors, string recurrenceProbe,
        IReadOnlyList<string> distractorPool, string novelProbe)
    {
        if (distractorPool.Count != CommonStoreSize)
            throw new ArgumentException(
                $"the distractor pool must have exactly {CommonStoreSize} entries (had {distractorPool.Count}) " +
                $"— it doubles as `novel`'s own priors, so a short pool would leave `novel` below {nameof(CommonStoreSize)} too.",
                nameof(distractorPool));

        return
        [
            new(Correction, Correction,
                [correctionFact, .. distractorPool.Take(CommonStoreSize - 1)], correctionProbe),
            new(Recurrence, Recurrence,
                [.. recurrencePriors, .. distractorPool.Take(CommonStoreSize - recurrencePriors.Count)], recurrenceProbe),
            new(Novel, Novel, distractorPool, novelProbe),
        ];
    }

    private static readonly Fixture[] EnglishFixtures = BuildLanguageFixtures(
        "the deploy key is stored in the alpha vault for the backend to read",
        "actually the deploy key is stored in the beta vault, not the alpha vault",
        [
            "checked the email inbox for new messages this morning",
            "checked the email inbox for new messages at noon",
            "checked the email inbox for new messages this afternoon",
            "checked the email inbox for new messages before the standup",
            "checked the email inbox for new messages after the standup",
            "checked the email inbox for new messages before lunch",
        ],
        "checked the email inbox for new messages before heading home",
        [
            "the garden's tomato plants need staking before the storm arrives",
            "quarterly tax filings are due at the end of the month",
            "the hiking trail near the summit was closed after a storm felled trees",
            "the new espresso machine grinds beans more evenly than the old one",
            "the neighbour's cat keeps sleeping on the porch furniture",
            "the museum's east wing reopens after the roof repair",
            "a violin string snapped during the second movement",
            "the bakery started selling sourdough on weekends only",
            "the ferry schedule changes twice a year for winter",
            "the library extended its late fees grace period",
        ],
        "the observatory's telescope mirror was recoated last winter");

    private static readonly Fixture[] ChineseFixtures = BuildLanguageFixtures(
        "部署密钥保存在阿尔法保险库供后端服务读取",
        "更正一下部署密钥其实保存在贝塔保险库而不是阿尔法",
        [
            "今天早上检查了邮箱是否有新邮件",
            "今天中午检查了邮箱是否有新邮件",
            "今天下午检查了邮箱是否有新邮件",
            "站会之前检查了邮箱是否有新邮件",
            "站会之后检查了邮箱是否有新邮件",
            "午饭之前检查了邮箱是否有新邮件",
        ],
        "下班之前检查了邮箱是否有新邮件",
        [
            "花园里的番茄植株需要在暴风雨来临前搭好支架",
            "季度纳税申报表必须在月底之前提交",
            "山顶附近的徒步小径因倒下的树木而关闭",
            "新买的意式咖啡机磨豆比旧的更均匀",
            "邻居的猫总是睡在门廊的家具上",
            "博物馆东翼在屋顶修缮后重新开放",
            "小提琴的一根弦在第二乐章断了",
            "面包店只在周末才卖酸面包",
            "渡轮时刻表每年冬天都会调整两次",
            "图书馆延长了逾期还书的宽限期",
        ],
        "天文台的望远镜反射镜在去年冬天重新镀膜了");

    private static readonly Fixture[] JapaneseFixtures = BuildLanguageFixtures(
        "デプロイ鍵はアルファ保管庫に保存されバックエンドが読み取る",
        "訂正するとデプロイ鍵は実はベータ保管庫に保存されておりアルファではない",
        [
            "今朝メールの受信箱を確認した",
            "昼にメールの受信箱を確認した",
            "午後にメールの受信箱を確認した",
            "スタンドアップの前にメールの受信箱を確認した",
            "スタンドアップの後にメールの受信箱を確認した",
            "昼食の前にメールの受信箱を確認した",
        ],
        "退勤の前にメールの受信箱を確認した",
        [
            "庭のトマトの苗は嵐が来る前に支柱を立てる必要がある",
            "四半期の税務申告は月末までに提出しなければならない",
            "山頂近くの登山道は倒木のため閉鎖された",
            "新しいエスプレッソマシンは古いものより均等に豆を挽く",
            "隣の猫はいつも玄関の家具の上で眠っている",
            "博物館の東棟は屋根の修理後に再開した",
            "第二楽章でバイオリンの弦が切れた",
            "パン屋は週末だけサワードウを販売している",
            "フェリーの時刻表は冬に年二回変更される",
            "図書館は延滞料金の猶予期間を延長した",
        ],
        "天文台の望遠鏡の鏡は去年の冬に再蒸着された");

    private static readonly Fixture[] KoreanFixtures = BuildLanguageFixtures(
        "배포키는 알파 보관소에 저장되어 백엔드가 읽어간다",
        "정정하자면 배포키는 사실 베타 보관소에 저장되어 있고 알파가 아니다",
        [
            "오늘 아침 이메일 수신함을 확인했다",
            "점심에 이메일 수신함을 확인했다",
            "오후에 이메일 수신함을 확인했다",
            "스탠드업 전에 이메일 수신함을 확인했다",
            "스탠드업 후에 이메일 수신함을 확인했다",
            "점심 전에 이메일 수신함을 확인했다",
        ],
        "퇴근 전에 이메일 수신함을 확인했다",
        [
            "정원의 토마토 모종은 폭풍이 오기 전에 지지대를 세워야 한다",
            "분기별 세금 신고는 월말까지 제출해야 한다",
            "정상 근처의 등산로는 쓰러진 나무 때문에 폐쇄되었다",
            "새 에스프레소 머신은 예전 것보다 원두를 더 고르게 간다",
            "이웃집 고양이는 항상 현관 가구 위에서 잔다",
            "박물관 동쪽 별관은 지붕 수리 후 다시 문을 열었다",
            "이악장 도중 바이올린 줄이 끊어졌다",
            "그 빵집은 주말에만 사워도우를 판매한다",
            "여객선 시간표는 겨울마다 두 번 바뀐다",
            "도서관은 연체료 유예 기간을 연장했다",
        ],
        "천문대의 망원경 거울은 작년 겨울에 재코팅되었다");

    private static readonly Fixture[] ChineseMixedFixtures = BuildLanguageFixtures(
        "deploy密钥保存在alpha保险库供backend服务读取",
        "更正一下deploy密钥其实保存在beta保险库而不是alpha",
        [
            "今天早上检查了email收件箱有没有新消息",
            "今天中午检查了email收件箱有没有新消息",
            "今天下午检查了email收件箱有没有新消息",
            "standup之前检查了email收件箱有没有新消息",
            "standup之后检查了email收件箱有没有新消息",
            "午饭之前检查了email收件箱有没有新消息",
        ],
        "下班之前检查了email收件箱有没有新消息",
        [
            "花园里的tomato植株需要在storm来临前搭好支架",
            "季度tax申报表必须在月底之前提交给finance",
            "山顶附近的hiking小径因倒下的树木而关闭",
            "新买的espresso咖啡机磨豆比旧的更均匀",
            "邻居的cat总是睡在porch的家具上",
            "museum东翼在roof修缮后重新开放",
            "violin的一根弦在第二movement断了",
            "bakery只在周末才卖sourdough",
            "ferry时刻表每年winter都会调整两次",
            "library延长了逾期还书的grace period",
        ],
        "observatory的telescope反射镜在去年冬天重新镀膜了");
}
