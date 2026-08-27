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
/// <para><b>One fixture per (language, population).</b> This is a REFUTATION instrument, not a distribution
/// study — the question is whether the two populations separate at all, not how much noise surrounds them.
/// <c>--languages</c> turns each language into one more paired observation feeding the pooled AUC below,
/// rather than a repeated draw of the same one.</para>
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

    private sealed record Observation(CorpusLanguage Language, string Population, int SimilarCount);

    private sealed record PriorCheck(CorpusLanguage Language, string Population, int Expected, int Actual);

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
        var priorChecks = new ConcurrentBag<PriorCheck>();

        async ValueTask RunOneAsync(CorpusLanguage language, Fixture fixture)
        {
            var salience = new CapturingSalience();
            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine("density", new SqliteMemoryGraphStore(db.Factory),
                embedder: embedder, vectors: new InMemoryVectorStore(), saliencePolicies: [salience]);

            var written = 0;
            foreach (var prior in fixture.Prior)
            {
                await engine.RememberAsync(new MemoryWrite("t", "s", prior));
                written++;
            }
            priorChecks.Add(new PriorCheck(language, fixture.Population, fixture.Prior.Count, written));

            salience.Seen.Clear();
            await engine.RememberAsync(new MemoryWrite("t", "s", fixture.Probe));
            var context = salience.Seen.Single();
            observations.Add(new Observation(language, fixture.Population, context.SimilarCount));
        }

        var cells = languages.SelectMany(l => BuildFixtures(l).Select(f => (Language: l, Fixture: f))).ToList();
        await Parallel.ForEachAsync(cells,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.Language, item.Fixture));

        var allObservations = observations.ToList();
        var controlsOk = PrintControls([.. priorChecks], allObservations);
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

    /// <summary>C0: every fixture's priors actually landed — <c>SimilarCount</c> is meaningless read off an
    /// empty store. C1: the <c>novel</c> population reports 0 everywhere it ran — if it does not, the floor
    /// is not doing anything and the distribution table below measures nothing. Either failing withholds the
    /// AUC verdict, though the distribution table still prints.</summary>
    private static bool PrintControls(IReadOnlyList<PriorCheck> priorChecks, IReadOnlyList<Observation> observations)
    {
        Console.WriteLine("Controls");

        var c0 = priorChecks.Count > 0 && priorChecks.All(p => p.Actual == p.Expected);
        Console.WriteLine($"  C0 every fixture's priors were written: " +
            $"{priorChecks.Count(p => p.Actual == p.Expected)}/{priorChecks.Count}  {(c0 ? "ok" : "<== SUSPECT")}");

        var novel = observations.Where(o => o.Population == Novel).ToList();
        var c1 = novel.Count > 0 && novel.All(o => o.SimilarCount == 0);
        Console.WriteLine($"  C1 `novel` reports 0 everywhere: " +
            $"{novel.Count(o => o.SimilarCount == 0)}/{novel.Count}  {(c1 ? "ok" : "<== SUSPECT")}");

        if (!c0 || !c1)
        {
            Console.WriteLine();
            Console.WriteLine("  A control failed — see above. The distribution table still prints; the AUC");
            Console.WriteLine("  verdict below is withheld rather than computed over an untrustworthy floor.");
        }

        Console.WriteLine();
        return c0 && c1;
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
    /// own correction/recurrence pair into ONE AUC rather than reporting per-language, because a single
    /// fixture per (language, population) makes any one language's own comparison a coin flip on its own —
    /// see the class remarks. AUC 0.5 is chance; 1.0 is perfect separation. The threshold reported alongside
    /// is the <c>SimilarCount</c> cut that maximises sensitivity + specificity - 1 (Youden's J), read as
    /// "correction below, recurrence at or above".</summary>
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
        if (recurrence.Count == 0 || correction.Count == 0)
        {
            Console.WriteLine("  Verdict withheld — one population has no observations to compare.");
            Console.WriteLine();
            return;
        }

        var auc = RankSumAuc(recurrence, correction);
        var (threshold, sensitivity, specificity, j) = BestThreshold(recurrence, correction);

        Console.WriteLine($"  AUC = {auc:F3} over {recurrence.Count} recurrence / {correction.Count} correction " +
            "observation(s), pooled across every language this run touched.");
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
        Console.WriteLine("  - SimilarityK = 5 bounds the count and is unmeasured, so a ceiling effect is possible.");
        Console.WriteLine("  - Fixtures are authored, which caps what an absolute number is worth.");
        Console.WriteLine("  - Separability on authored fixtures is not separability on a real corpus.");
    }

    private static IReadOnlyList<Fixture> BuildFixtures(CorpusLanguage language) => language switch
    {
        CorpusLanguage.Chinese => ChineseFixtures,
        CorpusLanguage.Japanese => JapaneseFixtures,
        CorpusLanguage.Korean => KoreanFixtures,
        CorpusLanguage.ChineseMixed => ChineseMixedFixtures,
        _ => EnglishFixtures,
    };

    private static readonly Fixture[] EnglishFixtures =
    [
        new(Correction, Correction,
            ["the deploy key is stored in the alpha vault for the backend to read"],
            "actually the deploy key is stored in the beta vault, not the alpha vault"),
        new(Recurrence, Recurrence,
            [
                "checked the email inbox for new messages this morning",
                "checked the email inbox for new messages at noon",
                "checked the email inbox for new messages this afternoon",
                "checked the email inbox for new messages before the standup",
                "checked the email inbox for new messages after the standup",
                "checked the email inbox for new messages before lunch",
            ],
            "checked the email inbox for new messages before heading home"),
        new(Novel, Novel,
            [
                "the garden's tomato plants need staking before the storm arrives",
                "quarterly tax filings are due at the end of the month",
                "the hiking trail near the summit was closed after a storm felled trees",
                "the new espresso machine grinds beans more evenly than the old one",
            ],
            "the observatory's telescope mirror was recoated last winter"),
    ];

    private static readonly Fixture[] ChineseFixtures =
    [
        new(Correction, Correction,
            ["部署密钥保存在阿尔法保险库供后端服务读取"],
            "更正一下部署密钥其实保存在贝塔保险库而不是阿尔法"),
        new(Recurrence, Recurrence,
            [
                "今天早上检查了邮箱是否有新邮件",
                "今天中午检查了邮箱是否有新邮件",
                "今天下午检查了邮箱是否有新邮件",
                "站会之前检查了邮箱是否有新邮件",
                "站会之后检查了邮箱是否有新邮件",
                "午饭之前检查了邮箱是否有新邮件",
            ],
            "下班之前检查了邮箱是否有新邮件"),
        new(Novel, Novel,
            [
                "花园里的番茄植株需要在暴风雨来临前搭好支架",
                "季度纳税申报表必须在月底之前提交",
                "山顶附近的徒步小径因倒下的树木而关闭",
                "新买的意式咖啡机磨豆比旧的更均匀",
            ],
            "天文台的望远镜反射镜在去年冬天重新镀膜了"),
    ];

    private static readonly Fixture[] JapaneseFixtures =
    [
        new(Correction, Correction,
            ["デプロイ鍵はアルファ保管庫に保存されバックエンドが読み取る"],
            "訂正するとデプロイ鍵は実はベータ保管庫に保存されておりアルファではない"),
        new(Recurrence, Recurrence,
            [
                "今朝メールの受信箱を確認した",
                "昼にメールの受信箱を確認した",
                "午後にメールの受信箱を確認した",
                "スタンドアップの前にメールの受信箱を確認した",
                "スタンドアップの後にメールの受信箱を確認した",
                "昼食の前にメールの受信箱を確認した",
            ],
            "退勤の前にメールの受信箱を確認した"),
        new(Novel, Novel,
            [
                "庭のトマトの苗は嵐が来る前に支柱を立てる必要がある",
                "四半期の税務申告は月末までに提出しなければならない",
                "山頂近くの登山道は倒木のため閉鎖された",
                "新しいエスプレッソマシンは古いものより均等に豆を挽く",
            ],
            "天文台の望遠鏡の鏡は去年の冬に再蒸着された"),
    ];

    private static readonly Fixture[] KoreanFixtures =
    [
        new(Correction, Correction,
            ["배포키는 알파 보관소에 저장되어 백엔드가 읽어간다"],
            "정정하자면 배포키는 사실 베타 보관소에 저장되어 있고 알파가 아니다"),
        new(Recurrence, Recurrence,
            [
                "오늘 아침 이메일 수신함을 확인했다",
                "점심에 이메일 수신함을 확인했다",
                "오후에 이메일 수신함을 확인했다",
                "스탠드업 전에 이메일 수신함을 확인했다",
                "스탠드업 후에 이메일 수신함을 확인했다",
                "점심 전에 이메일 수신함을 확인했다",
            ],
            "퇴근 전에 이메일 수신함을 확인했다"),
        new(Novel, Novel,
            [
                "정원의 토마토 모종은 폭풍이 오기 전에 지지대를 세워야 한다",
                "분기별 세금 신고는 월말까지 제출해야 한다",
                "정상 근처의 등산로는 쓰러진 나무 때문에 폐쇄되었다",
                "새 에스프레소 머신은 예전 것보다 원두를 더 고르게 간다",
            ],
            "천문대의 망원경 거울은 작년 겨울에 재코팅되었다"),
    ];

    private static readonly Fixture[] ChineseMixedFixtures =
    [
        new(Correction, Correction,
            ["deploy密钥保存在alpha保险库供backend服务读取"],
            "更正一下deploy密钥其实保存在beta保险库而不是alpha"),
        new(Recurrence, Recurrence,
            [
                "今天早上检查了email收件箱有没有新消息",
                "今天中午检查了email收件箱有没有新消息",
                "今天下午检查了email收件箱有没有新消息",
                "standup之前检查了email收件箱有没有新消息",
                "standup之后检查了email收件箱有没有新消息",
                "午饭之前检查了email收件箱有没有新消息",
            ],
            "下班之前检查了email收件箱有没有新消息"),
        new(Novel, Novel,
            [
                "花园里的tomato植株需要在storm来临前搭好支架",
                "季度tax申报表必须在月底之前提交给finance",
                "山顶附近的hiking小径因倒下的树木而关闭",
                "新买的espresso咖啡机磨豆比旧的更均匀",
            ],
            "observatory的telescope反射镜在去年冬天重新镀膜了"),
    ];
}
