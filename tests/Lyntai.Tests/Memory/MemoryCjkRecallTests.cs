using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>The consumer's own scenario, end to end, in Chinese, Japanese, Korean AND English on identical
/// structure — a CLUSTER of facts about one person, stated once each, buried under many rounds of unrelated
/// conversation, then cued by the SUBJECT alone.</b>
///
/// <para>Four languages rather than two because the three CJK arms reach the fact by DIFFERENT routes, and a
/// single arm cannot tell them apart: Chinese through a spaceless run of Han characters, Japanese through a
/// spaceless run mixing kanji, hiragana and katakana (where kana's small inventory makes trigram collisions
/// likelier), and Korean — which WRITES SPACES — through agglutinative morphology, its particle riding along
/// on the stem. If trigram expansion ever costs more than it recovers, Korean is where that shows up first.
/// </para>
///
/// <para><b>Why this file exists.</b> Every recall-quality figure this repository had ever published was
/// measured on English, space-separated text. Nothing established that a language without spaces recalled at
/// all — and it did not: whitespace splitting handed back a whole Chinese sentence as ONE token, so a cue
/// could only match an entry containing that exact substring. English got OR-over-words, Chinese got
/// exact-phrase-or-nothing, and the difference was invisible because nothing measured it.
/// <see cref="Lyntai.Storage.SearchTerms"/> now expands a spaceless run into character trigrams, with no
/// configuration required (<c>docs/DECISIONS.md</c> D55).</para>
///
/// <para><b>The cluster, not a single fact, is the point.</b> The case as described: "my spouse is Alice —
/// even if I don't mention my spouse, this entire relationship of mine should stay relevant." So the cue
/// (<c>"我的配偶"</c> / <c>"my spouse"</c>) lexically reaches at most ONE cluster member; every other member
/// has to arrive through the graph's spreading activation. A test that wrote one fact and asked for it back
/// would be testing the index and calling it memory.</para>
///
/// <para><b>What this is and is not.</b> These are pass/fail guards on a fixed scenario — they answer "does
/// it work", not "how well". The quality question is measured, not asserted:
/// <c>node devtools/dev.mjs memory-language</c> replays the full corpus in both languages over 30 seeds and
/// 7 shapes and reports paired miss/pollution deltas. Both exist because a guard that also tried to be a
/// measurement would be re-baselined every time it drifted, which is how a measurement quietly becomes a
/// tautology.</para>
///
/// <para>Construction mirrors <see cref="MemoryDefaultRecallQualityTests"/>: SQLite, the DI-registered
/// ranking and forgetting defaults passed explicitly, and an undamped <see cref="PerWriteAgePolicy"/> so a
/// fast in-process replay does not sit inside <see cref="BurstDampenedAgePolicy"/>'s wall-clock burst window
/// and flatten the interference axis.</para>
/// </summary>
public class MemoryCjkRecallTests
{
    /// <summary>Rounds of unrelated conversation between the facts and the cue. Enough that recall is
    /// retrieval under real interference rather than a fresh lookup, while staying fast.</summary>
    private const int ChatterRounds = 12;

    /// <summary>One language's fixtures. <paramref name="Cluster"/> is the entity the cue names;
    /// <paramref name="Decoys"/> are facts about OTHER entities, present so "recall everything" cannot pass
    /// this test — the cue must select.</summary>
    public sealed record Scenario(
        string Language,
        string Subject,
        string Cue,
        IReadOnlyList<string> Cluster,
        IReadOnlyList<string> ClusterAnswers,
        IReadOnlyList<string> Decoys,
        IReadOnlyList<string> Chatter);

    private static readonly Scenario Chinese = new(
        "chinese",
        Subject: "配偶",
        Cue: "我的配偶",                                   // names the subject, never any answer
        Cluster:
        [
            "我的配偶是爱丽丝",                            // spouse is Alice
            "她在一家医院做麻醉师",                        // she is an anaesthetist at a hospital
            "我们是在京都的一次旅行中认识的",              // we met on a trip to Kyoto
            "结婚纪念日是十月十七号",                      // anniversary is 17 October
        ],
        ClusterAnswers: ["爱丽丝", "麻醉师", "京都", "十月十七号"],
        Decoys:
        [
            "部署密钥存放在保险库的第二层",                // the deploy key lives on vault tier two
            "主要客户是北方物流公司",                      // the main client is a northern logistics firm
        ],
        Chatter:
        [
            "今天的会议改到下午三点", "部署管道需要人工审批", "回滚必须通知值班人员",
            "季度报告已经提交给财务", "新的缓存策略下周上线", "测试环境的数据每晚重置",
            "监控告警的阈值调高了一点", "文档站点换了新的搜索后端",
        ]);

    private static readonly Scenario English = new(
        "english",
        Subject: "spouse",
        Cue: "my spouse",
        Cluster:
        [
            "my spouse is Alice",
            "she works as an anaesthetist at a hospital",
            "we met on a trip to Kyoto",
            "our anniversary is the seventeenth of October",
        ],
        ClusterAnswers: ["Alice", "anaesthetist", "Kyoto", "seventeenth"],
        Decoys:
        [
            "the deploy key lives on the second tier of the vault",
            "the main client is a northern logistics firm",
        ],
        Chatter:
        [
            "the meeting moved to three in the afternoon", "the deploy pipeline needs manual approval",
            "rollbacks must page the on-call", "the quarterly report went to finance",
            "the new caching strategy ships next week", "the test environment resets nightly",
            "the monitoring alert threshold went up a little", "the docs site changed search backend",
        ]);

    /// <summary>Keyed by NAME rather than by the record itself: xUnit serializes theory data to identify a
    /// case, and a complex type would either warn or produce unreadable case names in the runner output.</summary>
    /// <summary>"my spouse is Alice" in Japanese — kanji, hiragana and katakana inside one spaceless run,
    /// which is what normal Japanese looks like and what no other scenario here produces.</summary>
    private static readonly Scenario Japanese = new(
        "japanese",
        Subject: "配偶者",
        Cue: "私の配偶者",
        Cluster:
        [
            "私の配偶者はアリスです",                      // my spouse is Alice
            "彼女は病院で麻酔科医として働いている",        // she works as an anaesthetist at a hospital
            "私たちは京都への旅行で出会った",              // we met on a trip to Kyoto
            "結婚記念日は十月十七日です",                  // anniversary is 17 October
        ],
        ClusterAnswers: ["アリス", "麻酔科医", "京都", "十月十七日"],
        Decoys:
        [
            "デプロイ鍵は金庫の第二層に保管されている",
            "主要な取引先は北方物流会社です",
        ],
        Chatter:
        [
            "本日の会議は午後三時に変更された", "デプロイ管道は手動承認が必要だ",
            "巻き戻しは当番者に通知しなければならない", "四半期報告書は経理に提出済みだ",
            "新しいキャッシュ戦略は来週稼働する", "試験環境のデータは毎晩初期化される",
            "監視警報の閾値を少し上げた", "文書サイトの検索基盤を入れ替えた",
        ]);

    /// <summary>Korean — and it WRITES SPACES, so the cue reaches the fact through agglutinative morphology
    /// (배우자는 carrying its particle) rather than through a spaceless run. Kept distinct for exactly that
    /// reason; see <c>CorpusLanguage.Korean</c>.</summary>
    private static readonly Scenario Korean = new(
        "korean",
        Subject: "배우자",
        Cue: "나의 배우자는",
        Cluster:
        [
            "나의 배우자는 앨리스이다",                    // my spouse is Alice
            "그녀는 병원에서 마취과의사로 일한다",         // she works as an anaesthetist at a hospital
            "우리는 교토 여행에서 만났다",                 // we met on a trip to Kyoto
            "결혼기념일은 십월 십칠일이다",                // anniversary is 17 October
        ],
        ClusterAnswers: ["앨리스", "마취과의사", "교토", "십칠일"],
        Decoys:
        [
            "배포키는 금고 second 계층에 보관되어 있다",
            "주요 고객사는 북방 물류회사이다",
        ],
        Chatter:
        [
            "오늘 회의는 오후 세시로 변경되었다", "배포 파이프라인은 수동 승인이 필요하다",
            "롤백은 당직자에게 알려야 한다", "분기 보고서는 재무팀에 제출되었다",
            "새로운 캐시 전략은 다음 주에 배포된다", "테스트 환경 데이터는 매일 밤 초기화된다",
            "모니터링 경보 임계값을 조금 올렸다", "문서 사이트의 검색 엔진을 교체했다",
        ]);

    public static TheoryData<string> Scenarios() =>
        new() { "chinese", "japanese", "korean", "english" };

    private static Scenario Get(string language) => language switch
    {
        "chinese" => Chinese,
        "japanese" => Japanese,
        "korean" => Korean,
        _ => English,
    };

    private static GraphMemoryEngine NewEngine(TempDb db, string name) =>
        new(name, new SqliteMemoryGraphStore(db.Factory), agePolicies: [new PerWriteAgePolicy()],
            policy: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy());

    /// <summary>Writes the cluster and the decoys, then buries them under <see cref="ChatterRounds"/> rounds
    /// of unrelated material in the SAME language — so the trigram expansion competes against text that
    /// genuinely shares trigrams with it, which is the hard case and the one CJK actually lives in.</summary>
    private static async Task<GraphMemoryEngine> SeedAsync(
        TempDb db, Scenario s, MemoryGrade clusterGrade = MemoryGrade.Associative)
    {
        var engine = NewEngine(db, $"cluster-{s.Language}");

        foreach (var fact in s.Cluster)
            await engine.RememberAsync(new MemoryWrite("t", "s", fact, Grade: clusterGrade));
        // decoys stay ASSOCIATIVE whatever the cluster is: if everything were authoritative, "the cue selects"
        // would be satisfied by admitting the whole store, which is the opposite of selection
        foreach (var decoy in s.Decoys) await engine.RememberAsync(new MemoryWrite("t", "s", decoy));

        for (var round = 0; round < ChatterRounds; round++)
            foreach (var line in s.Chatter)
                await engine.RememberAsync(new MemoryWrite("t", "s", $"{line}{round}"));

        return engine;
    }

    private static async Task<List<string>> RecallTextsAsync(GraphMemoryEngine engine, string query, int limit) =>
        [.. (await engine.RecallAsync(new MemoryQuery("t", "s", query, Limit: limit))).Items
            .Select(i => i.Content ?? i.Headline)
            .Where(t => t is not null)
            .Select(t => t!)];

    /// <summary><b>The headline fact: a subject-only cue reaches the ANSWER it never contained.</b> In
    /// Chinese this fails outright without trigram expansion — mutation-checked by disabling
    /// <c>SearchTerms</c>'s spaceless-script detection, which fails this case and leaves English
    /// passing.</summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task A_subject_cue_recovers_the_answer_it_never_contained(string language)
    {
        var s = Get(language);
        using var db = new TempDb();
        var engine = await SeedAsync(db, s);

        var texts = await RecallTextsAsync(engine, s.Cue, 10);

        var answer = s.ClusterAnswers[0];
        Assert.Contains(texts, t => t.Contains(answer, StringComparison.Ordinal));
    }

    /// <summary><b>An ASSOCIATIVE cluster does NOT come back whole, and pinning that is the honest thing to
    /// do.</b> This assertion was written the other way round first — "more of the cluster returns than the
    /// cue could have matched" — and it failed IDENTICALLY in both languages, returning exactly the one
    /// lexically-matched fact. That is not a language defect and not a bug: this engine's edges come from
    /// vector similarity at write time (needs an embedder AND a vector store, neither supplied here) or from
    /// CO-ACTIVATION during recall (entries re-admitted together). Facts stated once, never re-mentioned and
    /// never co-recalled have no edges at all, so there is nothing for spreading activation to traverse.
    /// <para>Recorded as a fact rather than deleted, because the failing version of it is what makes the next
    /// test the ANSWER to the consumer's case rather than a decoration. See <c>TASKS.md</c> Part 65, which
    /// reached the same conclusion from the measurement side: "the case needs a GUARANTEE, and the
    /// associative path cannot give one."</para></summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task An_associative_cluster_returns_only_what_the_cue_lexically_reached(string language)
    {
        var s = Get(language);
        using var db = new TempDb();
        var engine = await SeedAsync(db, s, MemoryGrade.Associative);

        var texts = await RecallTextsAsync(engine, s.Cue, 10);
        var found = s.ClusterAnswers.Count(a => texts.Any(t => t.Contains(a, StringComparison.Ordinal)));

        // exactly the lexically-reachable member; no graph edges exist to carry the rest
        Assert.Equal(1, found);
    }

    /// <summary><b>An AUTHORITATIVE cluster DOES come back whole — in both languages. This is the answer to
    /// "even if I don't mention my spouse, this entire relationship should stay relevant."</b>
    /// <para>Authoritative material is admitted by <c>SeedAsync</c>'s grade carve-out whatever the query
    /// matched, and re-admitted by the engine, so it does not depend on a lexical hit, on an embedder, or on
    /// edges existing. That makes it the mechanism for a fact that must not be lost — and it means the answer
    /// to the consumer's case is a WRITE-side decision (grade the fact) rather than a retrieval-side hope.
    /// </para>
    /// <para>That it holds identically in Chinese is the point of running both: the grade path never touches
    /// the tokenizer, so a language cannot weaken the one guarantee the library actually makes.</para></summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task An_authoritative_cluster_returns_whole_from_a_subject_cue(string language)
    {
        var s = Get(language);
        using var db = new TempDb();
        var engine = await SeedAsync(db, s, MemoryGrade.Authoritative);

        var texts = await RecallTextsAsync(engine, s.Cue, 10);
        var missing = s.ClusterAnswers
            .Where(a => !texts.Any(t => t.Contains(a, StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0,
            $"[{s.Language}] authoritative cluster members missing for cue '{s.Cue}': " +
            $"{string.Join(", ", missing)}; got: {string.Join(" | ", texts)}");
    }

    /// <summary><b>The cue SELECTS — it does not merely return everything.</b> Without this, a recall that
    /// dumped the whole store would satisfy every assertion above. The decoys are facts about other entities,
    /// stated in the same style at the same time, so the only thing separating them from the cluster is
    /// whether the cue is about them.</summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_cue_prefers_its_own_cluster_over_facts_about_other_entities(string language)
    {
        var s = Get(language);
        using var db = new TempDb();
        var engine = await SeedAsync(db, s);

        var texts = await RecallTextsAsync(engine, s.Cue, 10);
        var cluster = s.ClusterAnswers.Count(a => texts.Any(t => t.Contains(a, StringComparison.Ordinal)));
        var decoys = s.Decoys.Count(d => texts.Any(t => t.Contains(d, StringComparison.Ordinal)));

        Assert.True(cluster > decoys,
            $"[{s.Language}] cluster hits {cluster} did not exceed decoy hits {decoys}; " +
            $"got: {string.Join(" | ", texts)}");
    }

    /// <summary>The fixtures really do withhold the answer from the cue — the property that makes every fact
    /// above a retrieval rather than an echo, and the one a careless fixture edit would silently destroy.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_cue_never_contains_any_answer(string language)
    {
        var s = Get(language);
        Assert.Contains(s.Subject, s.Cue, StringComparison.Ordinal);
        Assert.Equal(s.Cluster.Count, s.ClusterAnswers.Count);

        foreach (var answer in s.ClusterAnswers)
        {
            Assert.Contains(s.Cluster, f => f.Contains(answer, StringComparison.Ordinal));
            Assert.DoesNotContain(answer, s.Cue, StringComparison.Ordinal);
        }

        // and no chatter or decoy line smuggles an answer in
        foreach (var line in s.Chatter.Concat(s.Decoys))
            Assert.DoesNotContain(s.ClusterAnswers, a => line.Contains(a, StringComparison.Ordinal));
    }

    /// <summary><b>The SPACELESS fixtures really are spaceless</b>, so these tests exercise the trigram path
    /// rather than accidentally passing through whitespace splitting. Without it, someone adding a space to a
    /// fixture would silently convert the Chinese or Japanese case into the English one and nothing would
    /// fail.
    /// <para>Korean is deliberately absent: it WRITES spaces, so its cue reaches the fact through
    /// agglutinative morphology rather than through an unsplittable run. Asserting spacelessness of it would
    /// either fail or get relaxed until it checked nothing — see <c>CorpusLanguage.Korean</c>.</para></summary>
    [Theory]
    [InlineData("chinese")]
    [InlineData("japanese")]
    public void The_spaceless_fixtures_contain_no_whitespace(string language)
    {
        var s = Get(language);

        Assert.DoesNotContain(' ', s.Cue);
        Assert.All(s.Cluster, f => Assert.DoesNotContain(' ', f));
        Assert.All(s.Decoys, d => Assert.DoesNotContain(' ', d));
        Assert.All(s.Chatter, c => Assert.DoesNotContain(' ', c));
    }

    /// <summary>Korean's own version of the fact above: it writes spaces, so what must hold instead is that
    /// its cue still reaches the trigram expansion — a Hangul term of three syllables. Without this, Korean
    /// could quietly degrade to whole-token matching and still pass everything else.</summary>
    [Fact]
    public void The_korean_cue_still_reaches_the_trigram_expansion()
    {
        var terms = Lyntai.Storage.SearchTerms.Extract(Korean.Cue);

        Assert.Contains(terms, t => t.Length == 3 && t.All(c => c is >= '가' and <= '힯'));
    }
}
