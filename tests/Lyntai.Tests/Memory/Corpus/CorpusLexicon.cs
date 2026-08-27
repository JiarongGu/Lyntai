namespace Lyntai.Tests.Memory.Corpus;

/// <summary>The language a corpus's text is generated in. <see cref="English"/> is the default and every
/// existing measurement is pinned against it (<c>MemoryCorpusGoldenTests</c>).</summary>
public enum CorpusLanguage
{
    /// <summary>Space-separated English — what every published recall-quality figure was measured on.</summary>
    English,

    /// <summary><b>Chinese, written with NO spaces inside a sentence</b>, which is the whole point of having
    /// this axis: whitespace splitting cannot break such a sentence up, so retrieval has to go through
    /// <c>SearchTerms</c>'s character-trigram expansion. Every recall number this repository published before
    /// 2026-08-12 was measured under the friendliest tokenization the library supports; this is the
    /// unfriendly one, and it is the condition much of this library's audience actually deploys in.</summary>
    Chinese,

    /// <summary><b>Japanese — also spaceless, and the sharper test of the two.</b> Same structural condition
    /// as Chinese (whitespace splits nothing), but kana words are frequently two characters and hiragana's
    /// character inventory is small, so trigram COLLISIONS are far likelier than in Chinese: a trigram of
    /// three common kana can appear in unrelated sentences by chance. Mixing kanji, hiragana and katakana in
    /// one sentence — which normal Japanese does — is also a script mix inside a single spaceless run, a
    /// shape no other language here produces.</summary>
    Japanese,

    /// <summary><b>Korean — and it WRITES SPACES, which is why it is a different case from the two
    /// above.</b> Whitespace does separate Korean 어절, so the "whitespace splits nothing" argument for
    /// trigram expansion does NOT apply. <c>SearchTerms</c> expands it anyway, because Hangul syllables sit
    /// in its spaceless-script range — and that turns out to be defensible for a different reason: Korean is
    /// agglutinative, so particles attach to the stem (배우자는, 배우자의, 배우자에게 are one stem with three
    /// endings) and whole-token matching would miss the stem every time the particle differed. Trigram
    /// expansion recovers it.
    /// <para>Kept as a distinct arm precisely because the JUSTIFICATION differs from Chinese and Japanese.
    /// If the expansion ever turns out to cost more than it recovers, this is the language it would show up
    /// in first — and a measurement that lumped Korean in with the spaceless pair could not see it.</para>
    /// </summary>
    Korean,

    /// <summary><b>Chinese technical prose with English terms embedded WITHOUT spaces around them</b> —
    /// <c>部署pipeline</c>, <c>client客户</c> — which is how the language is actually written in software
    /// contexts and the single most common real-world shape this library had never measured.
    /// <para>It exists because mixed script was a real defect, not a hypothetical: a Latin word inside a CJK
    /// run used to be shredded into fragments that are words in no language (<c>dep</c>, <c>epl</c>) while
    /// never being emitted whole. Script-run segmentation fixed it, and every other arm here is monolingual
    /// prose plus ASCII ids — which exercises the boundary only at a token edge, never inside one. This arm
    /// puts the boundary in the middle of a run, where the defect lived.</para></summary>
    ChineseMixed,
}

/// <summary>
/// Every piece of text a <see cref="MemoryCorpus"/> emits, plus the readers that parse it back — so a
/// language is swapped in ONE place and the corpus's own guarantees keep being checked in whichever
/// language is generated.
///
/// <para><b>Why the READERS live here too, and not in the tests.</b> The corpus's invariants are asserted by
/// parsing generated content positionally: the attribute value is "the word after <c>is</c>", a cue is
/// "the query ending in <c>recallcue</c>". Those readers are English grammar wearing the costume of a
/// helper. Left in the test file, a Chinese corpus would either crash them or — far worse — return
/// something plausible and wrong, and the variant that most needs its guarantees checked would be the one
/// running unchecked. Putting them beside the templates that produce the text means adding a language is
/// one class and the compiler names every hole.</para>
///
/// <para><b>The ID stays an ASCII token in both languages, and that is deliberate rather than a
/// compromise.</b> Ids (<c>topic3</c>, <c>critical0</c>) are the corpus's addressing scheme, not its prose:
/// consumers read them back with <c>content.Split(' ')[1]</c>, and every measurement's ground truth is
/// expressed in them. Translating them would change what is being measured from "can a cue retrieve this
/// entry" to "can this harness parse Chinese". Real Chinese text carries ASCII identifiers constantly, so
/// the mix is realistic; what matters is that everything AROUND the id is a single spaceless run, which is
/// what forces the trigram path.</para>
///
/// <para><b>The routine class's non-English text is this author's own best-effort translation, unreviewed
/// by a native speaker</b> — the same disclosure <c>MemoryDensitySweep</c> carries for its own
/// author-written fixtures.</para>
/// </summary>
internal abstract class CorpusLexicon
{
    public static CorpusLexicon For(CorpusLanguage language) => language switch
    {
        CorpusLanguage.Chinese => ChineseLexicon.Instance,
        CorpusLanguage.ChineseMixed => ChineseMixedLexicon.Instance,
        CorpusLanguage.Japanese => JapaneseLexicon.Instance,
        CorpusLanguage.Korean => KoreanLexicon.Instance,
        _ => EnglishLexicon.Instance,
    };

    /// <summary>Whether this language separates words with spaces in the generated content.
    /// <para>Not cosmetic: it decides which shape the corpus's own invariants can assert. Chinese and
    /// Japanese content is ONE spaceless run apart from its ASCII id, so a test can demand exactly three
    /// space-delimited parts. English and Korean write spaces, so that assertion is meaningless for them and
    /// asserting it anyway would either fail or — worse — be quietly relaxed until it checked nothing.</para>
    /// <para>Korean writing spaces while still being trigram-expanded is the fact this property exists to
    /// keep visible; see <see cref="CorpusLanguage.Korean"/>.</para></summary>
    public abstract bool WritesWordSpaces { get; }

    /// <summary>The token every QUERIED entry starts with, so a broad recall makes them genuinely compete.
    /// Filler deliberately does not share it — see <see cref="MemoryCorpus"/>'s own class doc.</summary>
    public abstract string ItemToken { get; }

    /// <summary>Filler adverbs, drawn once per entry from the seeded PRNG. The ONLY seed-dependent part of
    /// an entry's content.</summary>
    public abstract IReadOnlyList<string> Fillers { get; }

    /// <summary>The three attribute subjects — a person, a credential, a party. Single-token in English;
    /// in Chinese a short word that is still a clean cue.</summary>
    public abstract IReadOnlyList<string> AttributeSubjects { get; }

    /// <summary>The values a subject takes — the detail stated once that a cue must recover.</summary>
    public abstract IReadOnlyList<string> AttributeValues { get; }

    // ---- writes ----

    /// <summary>An AUTHORITATIVE fact — the corpus's only graded material, and the only class with no
    /// acceptable failure rate (design §5.7.0 objective (1)).
    /// <para>Its wording must share nothing with <see cref="UnrelatedProbe"/>, because the whole measurement
    /// is "can the grade carve-out return this when NOTHING lexical can". If the two overlap, the class
    /// silently becomes an ordinary keyword recall and reports the engine's promise as kept when it was
    /// never tested.</para></summary>
    public abstract string Authoritative(string id, string filler);

    public abstract string Attribute(string id, string subject, string value, string filler);
    public abstract string Critical(string id, string filler);
    public abstract string Topical(string id, string filler);
    public abstract string Hot(string id, string filler, int round);
    public abstract string Noise(string id, string filler);
    public abstract string Padding(string id, string filler);

    /// <summary>One instance of a recurring routine. <paramref name="regime"/> selects WHICH routine — the
    /// two must be mutually distinguishable, because the corpus asks which one is current.</summary>
    public abstract string Routine(string id, int regime, string filler);

    /// <summary>The single word <see cref="Routine"/> embeds for <paramref name="regime"/> — English:
    /// <c>"noodles"</c> / <c>"salad"</c> — exposed on its own so a test can assert
    /// <see cref="RoutineQuery"/> names neither, without re-deriving the word from written content.</summary>
    public abstract string RoutineToken(int regime);

    /// <summary>Content words with nothing to do with any other class, drawn from to build TEXTUALLY DIVERSE
    /// junk. Deliberately large and mutually unrelated: the point is that two noise entries built from it
    /// share as little as possible, which is exactly what <see cref="Noise"/> cannot do.</summary>
    public abstract IReadOnlyList<string> NoiseVocabulary { get; }

    /// <summary>Junk with NO shared skeleton — the adversarial case for a novelty-driven salience policy.
    ///
    /// <para><b>Why this exists as a separate method rather than a wordier <see cref="Noise"/>.</b>
    /// <c>StructuralSaliencePolicy</c> is monotone in "how unlike anything already stored", so the concern
    /// it raises is that maximally-unlike material — random junk — is preferentially preserved AND granted
    /// admission priority, which is <c>PollutionRate</c> by definition. <see cref="Noise"/> cannot present
    /// that case: it shares a fixed skeleton with every other noise entry, so under bag-of-words novelty the
    /// second one onward reads as FAMILIAR. The corpus models noise as *semantically irrelevant*; the
    /// hypothesis is about *textually diverse*. Those are different things and only one of them was
    /// buildable before this method.</para>
    ///
    /// <para><b>It is deliberately near-skeletonless</b> — the id plus drawn words, and nothing else. A
    /// template with any fixed phrasing would re-introduce the very familiarity being controlled for, at
    /// whatever strength the phrasing happened to have. The strongest form of the hypothesis is the honest
    /// thing to measure.</para>
    ///
    /// <para>Spacing follows <see cref="WritesWordSpaces"/>, so a spaceless script gets a spaceless run and
    /// is tokenized by the same n-gram path the rest of its corpus is. Shared rather than per-language for
    /// exactly that reason: the only thing that varies is the vocabulary and the separator, and duplicating
    /// it five times would invite five slightly different skeletons — the defect this method exists to
    /// remove.</para></summary>
    public string DiverseNoise(string id, IReadOnlyList<string> words) =>
        WritesWordSpaces
            ? $"{id} {string.Join(' ', words)}"
            : $"{id}{string.Concat(words)}";

    // ---- queries ----
    public abstract string TopicalRepeat(string id, int k);
    public abstract string HotRepeat(string id, int k);
    public abstract string HotStale(string id);
    public abstract string CriticalLookup(string id);
    public abstract string CriticalRecall(string id);

    /// <summary>The frequency question — "what do I usually …". It must name the ACTIVITY and never either
    /// routine's own distinguishing token, or the cue would contain its own answer and the class would
    /// collapse into an ordinary lookup.</summary>
    public abstract string RoutineQuery();

    /// <summary>Statement/cue pairs that mean the same thing and share NO index term.
    ///
    /// <para><b>Why the corpus needs them.</b> Every other class here defines relevance LEXICALLY — the
    /// query names the id or shares a word — so a semantic neighbour is wrong BY CONSTRUCTION, and an
    /// embedder can only ever be measured costing slots it never earns back (`TASKS.md` Part 69, where
    /// enabling one raised the miss rate from 0.5357 to 0.8357). This is the one class that asks a question
    /// the lexical path cannot answer at all, so it is the only place the enrichment can show an upside.</para>
    ///
    /// <para><b>A bag-of-words test double cannot serve this.</b> <c>FakeEmbedder</c> is a feature-hashed
    /// bag of WORDS, so "semantic similarity" in it IS word overlap — and a cue sharing no words is exactly
    /// as invisible to it as to the lexical path. This class therefore only means anything against a REAL
    /// embedding model, which is a limit of the instrument rather than of the idea, and is stated here so
    /// nobody measures it with the double and concludes the embedder does not help.</para>
    ///
    /// <para>The no-shared-term property is asserted in <c>MemoryCorpusTests</c> rather than trusted: if a
    /// pair ever overlaps, the class silently becomes an ordinary keyword recall and would report semantic
    /// retrieval working when nothing semantic happened.</para></summary>
    public abstract IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs { get; }

    /// <summary>A query about something else entirely, sharing NO term with any
    /// <see cref="Authoritative"/> entry — so the only thing that can return one is the grade carve-out.
    /// Pinned by <c>MemoryCorpusTests.The_authoritative_probe_shares_no_term_with_the_facts_it_must_return</c>,
    /// because an overlap here would turn objective (1)'s measurement into an ordinary keyword recall that
    /// passes for the wrong reason.</summary>
    public abstract string UnrelatedProbe();

    /// <summary>A term that appears ONLY in an authored headline and in nothing else the corpus writes —
    /// the instrument for headline search.
    ///
    /// <para><b>Why this class exists.</b> Every other entry lets the engine DERIVE its headline from the
    /// content, so headline words are a subset of content words and a query matching the headline matches
    /// the content too. That made headline search unobservable: the 3.0 review narrowed it and then widened
    /// it back (<c>docs/FIXES.md</c>) and <c>memory-sweep</c> could not see either direction — the
    /// <c>pitfalls.md</c> "a measurement that cannot observe a change reports nothing moved" shape, live in
    /// the instrument rather than in the library.</para>
    ///
    /// <para>Must share NO term with any other class, for the same reason
    /// <see cref="UnrelatedProbe"/> must: an overlap turns the measurement into an ordinary content recall
    /// that passes for the wrong reason. Pinned by
    /// <c>MemoryCorpusTests.A_headline_only_entry_hides_its_marker_from_its_own_content</c>.</para></summary>
    public abstract string HeadlineMarker { get; }

    /// <summary>The cue whose only live term is the subject — reaches exactly one cluster member, so the
    /// rest can arrive only through the graph.</summary>
    public abstract string DiscriminativeCue(string subject);

    /// <summary>The cue that also carries ordinary words appearing in unrelated entries, so the cluster
    /// competes against incidental matches.</summary>
    public abstract string CommonTokenCue(string subject);

    // ---- readers: how the corpus's own invariants parse what the templates produced ----

    /// <summary>Whether a query is one of the two attribute cues. Used by the corpus's tests to find the
    /// cues without re-encoding their wording.</summary>
    public abstract bool IsAttributeCue(string queryText);

    /// <summary>The VALUE out of an attribute entry's content — the token the cue must make the engine
    /// recover, and which the cue itself must never contain.</summary>
    public abstract string AttributeValueOf(string content);

    /// <summary>The searchable terms of a query, at or above the trigram floor — what the store would
    /// actually match on. English splits into words; Chinese expands into character trigrams, so a test
    /// asking "does any live term of this cue reach outside the cluster" asks the right question in both.
    /// Delegates to the library's own <see cref="Lyntai.Storage.SearchTerms"/> rather than re-deriving it:
    /// a corpus invariant checked against a DIFFERENT split than the store uses is checking nothing.</summary>
    public IReadOnlyList<string> LiveTermsOf(string queryText) => Lyntai.Storage.SearchTerms.Extract(queryText);

    private sealed class EnglishLexicon : CorpusLexicon
    {
        public static readonly EnglishLexicon Instance = new();

        public override bool WritesWordSpaces => true;

        public override string ItemToken => "item";

        public override IReadOnlyList<string> Fillers =>
        [
            "quietly", "eventually", "briefly", "plainly", "somehow",
            "apparently", "unusually", "notably", "curiously", "obviously",
        ];

        public override IReadOnlyList<string> AttributeSubjects => ["spouse", "deploykey", "client"];
        public override IReadOnlyList<string> AttributeValues => ["alpha", "beta", "gamma"];

        public override string Authoritative(string id, string filler) =>
            $"item {id} the passport number is {filler} XK4419 and must survive everything";

        public override string UnrelatedProbe() => "item repeat focus material";

        public override string HeadlineMarker => "beacon";

        public override string Attribute(string id, string subject, string value, string filler) =>
            $"item {id} the {subject} is {value} {filler} stated once and never restated";

        public override string Critical(string id, string filler) =>
            $"item {id} is a fact {filler} mentioned exactly once and must never be lost";

        public override string Topical(string id, string filler) =>
            $"item {id} covers {filler} ordinary material queried on its own terms";

        public override string Hot(string id, string filler, int round) =>
            $"item {id} is the {filler} current focus of round {round}";

        public override string Noise(string id, string filler) =>
            $"item {id} was {filler} mentioned once and never again";

        public override string Padding(string id, string filler) =>
            $"padding {id} was {filler} written only to interpose age and is never queried";

        public override string Routine(string id, int regime, string filler) =>
            regime == 0
                ? $"item {id} had noodles for lunch again, {filler}"
                : $"item {id} had a salad for lunch again, {filler}";

        public override string RoutineToken(int regime) => regime == 0 ? "noodles" : "salad";

        /// <summary>English is the easy case: whole-word terms, so two synonyms share nothing at all.</summary>
        public override IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs =>
        [
            ("the meeting was postponed until next week", "conference delayed"),
            ("the server crashed during the night", "machine failed overnight"),
            ("the budget was approved by the committee", "funding authorised council"),
        ];

        public override IReadOnlyList<string> NoiseVocabulary =>
        [
            "harbour", "lantern", "vellum", "basalt", "monsoon", "tessera", "quarry", "orchid",
            "kettle", "granite", "meadow", "cobalt", "trellis", "saffron", "pumice", "anvil",
            "willow", "cinder", "marlin", "juniper", "flint", "cypress", "amber", "thistle",
            "quartz", "bramble", "heron", "opal", "sable", "fennel", "birch", "coral",
            "gypsum", "linnet", "myrrh", "onyx", "pewter", "ripple", "sorrel", "tundra",
            "umber", "verbena", "walnut", "xenon", "yarrow", "zephyr", "alcove", "bellows",
            "cistern", "dovetail", "ember", "furrow", "gantry", "hollow", "ingot", "jetty",
            "kiln", "lintel", "mortise", "nutmeg",
        ];

        public override string TopicalRepeat(string id, int k) => $"item {id} repeat{k}";
        public override string HotRepeat(string id, int k) => $"item {id} focus repeat{k}";
        public override string HotStale(string id) => $"item {id} focus stale";
        public override string CriticalLookup(string id) => $"item {id} lookup";
        public override string CriticalRecall(string id) => $"what happened to {id}";

        public override string RoutineQuery() => "what do I usually have for lunch";

        public override string DiscriminativeCue(string subject) => $"{subject} recallcue";
        public override string CommonTokenCue(string subject) => $"remind me about the {subject} recallcue";

        public override bool IsAttributeCue(string queryText) =>
            queryText.EndsWith(" recallcue", StringComparison.Ordinal);

        public override string AttributeValueOf(string content)
        {
            // "item {id} the {subject} is {value} …" — the word after "is"
            var parts = content.Split(' ');
            return parts[Array.IndexOf(parts, "is") + 1];
        }
    }

    /// <summary><b>Every sentence below is ONE spaceless run apart from the ASCII id</b>, which is what makes
    /// this variant exercise the trigram path rather than accidentally re-testing whitespace splitting.
    /// Pinned by <c>MemoryCorpusTests.Chinese_corpus_content_is_spaceless_apart_from_the_id</c>.</summary>
    private sealed class ChineseLexicon : CorpusLexicon
    {
        public static readonly ChineseLexicon Instance = new();

        public override bool WritesWordSpaces => false;

        /// <summary>"record entry" — the shared leading token, the Chinese counterpart of "item".
        /// <para><b>FOUR characters, and the length is load-bearing rather than stylistic.</b> The first
        /// draft used 条目, which is TWO — below <see cref="Lyntai.Storage.SearchTerms.MinimumTermLength"/>,
        /// so it yields no trigram and was silently dropped from every query. That deleted the corpus's
        /// central design property (every queried entry shares a token, so a broad recall makes them genuinely
        /// COMPETE) and produced a measurably EASIER corpus: the first Chinese sweep reported `topical` miss
        /// AND pollution of exactly 0.0000 on almost every shape, which is not a language finding but an
        /// instrument that had stopped measuring. Any replacement must clear the floor.</para>
        /// <para>This is the same failure the English corpus already paid for from the other direction — see
        /// <see cref="MemoryCorpus"/>'s note on filler that used to begin "item filler{n}". A shared token
        /// that competes when it should not, and one that does not compete when it should, are one bug.</para>
        /// </summary>
        public override string ItemToken => "记录条目";

        /// <summary>Adverbs, matching the English list's role exactly: one drawn per entry, varying content
        /// with the seed and nothing else.</summary>
        public override IReadOnlyList<string> Fillers =>
        [
            "悄悄地", "最终", "简短地", "明确地", "不知怎么",
            "显然", "异常", "特别", "奇怪地", "当然",
        ];

        /// <summary>配偶 spouse, 部署密钥 deploy key, 客户 client — the same three kinds of subject (a person,
        /// a credential, a party) the English list carries.</summary>
        public override IReadOnlyList<string> AttributeSubjects => ["配偶", "部署密钥", "客户"];

        /// <summary>爱丽丝 Alice, 贝拉 Bella, 卡罗尔 Carol — personal names rather than English letter-words,
        /// so the value is the kind of token a real cue has to recover.</summary>
        public override IReadOnlyList<string> AttributeValues => ["爱丽丝", "贝拉", "卡罗尔"];

        public override string Authoritative(string id, string filler) =>
            $"记录条目 {id} 护照号码是{filler}XK4419必须永远保留";

        public override string UnrelatedProbe() => "记录条目 重复 关注材料";

        public override string HeadlineMarker => "灯塔";

        public override string Attribute(string id, string subject, string value, string filler) =>
            $"记录条目 {id} 我的{subject}是{value}{filler}只说过一次而且从未重复";

        public override string Critical(string id, string filler) =>
            $"记录条目 {id} 这是一个{filler}只提到过一次的事实绝对不能丢失";

        public override string Topical(string id, string filler) =>
            $"记录条目 {id} 涵盖了{filler}按自身主题被查询的日常材料";

        public override string Hot(string id, string filler, int round) =>
            $"记录条目 {id} 是第{round}轮{filler}当前的关注重点";

        public override string Noise(string id, string filler) =>
            $"记录条目 {id} 曾{filler}被提到一次此后再也没有出现";

        public override string Padding(string id, string filler) =>
            $"填充 {id} 仅{filler}用于插入间隔从来不会被查询";

        /// <summary>面条 noodles (regime 0) / 沙拉 salad (regime 1) — two food words sharing no character
        /// with each other or with <see cref="RoutineQuery"/>, so the query cannot match either one even by
        /// trigram accident.</summary>
        public override string Routine(string id, int regime, string filler) =>
            regime == 0
                ? $"记录条目 {id} 又{filler}吃了面条当作午餐"
                : $"记录条目 {id} 又{filler}吃了沙拉当作午餐";

        public override string RoutineToken(int regime) => regime == 0 ? "面条" : "沙拉";

        /// <summary>Harder than English: Chinese expands into trigrams, so a synonym pair must share no
        /// three-character window either. Written with disjoint characters throughout for that reason.</summary>
        public override IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs =>
        [
            ("会议被推迟到下个星期", "研讨延后"),
            ("服务器在夜里崩溃了", "主机凌晨故障"),
            ("预算已经通过委员批准", "经费获得理事同意"),
        ];

        /// <summary>Two-character words with no relation to any other class here, and no character shared
        /// between entries — so two drawn samples collide on a TRIGRAM only by crossing a word boundary,
        /// which is the spaceless-script counterpart of the English list's non-overlap.</summary>
        public override IReadOnlyList<string> NoiseVocabulary =>
        [
            "港湾", "灯笼", "羊皮", "玄武", "季风", "镶嵌", "采石", "兰花",
            "水壶", "花岗", "草甸", "钴蓝", "棚架", "藏红", "浮石", "铁砧",
            "柳树", "煤渣", "旗鱼", "杜松", "燧石", "柏木", "琥珀", "蓟草",
            "石英", "荆棘", "苍鹭", "蛋白", "貂皮", "茴香", "桦木", "珊瑚",
            "石膏", "红雀", "没药", "缟玛", "白镴", "涟漪", "酸模", "苔原",
            "赭色", "马鞭", "胡桃", "氙气", "蓍草", "西风", "壁龛", "风箱",
            "水槽", "鸠尾", "余烬", "犁沟", "构台", "凹陷", "锭铁", "浮桥",
            "窑炉", "过梁", "榫眼", "肉豆",
        ];

        // Every query carries the shared leading token, exactly as the English ones carry "item": that is what
        // makes a broad recall put these entries in competition instead of handing each query its own private
        // answer. Dropping it (or using a token below the trigram floor) makes the corpus easier and the
        // numbers meaningless — see ItemToken's own note.
        public override string TopicalRepeat(string id, int k) => $"记录条目 {id} 重复{k}";
        public override string HotRepeat(string id, int k) => $"记录条目 {id} 关注重复{k}";
        public override string HotStale(string id) => $"记录条目 {id} 关注已过期";
        public override string CriticalLookup(string id) => $"记录条目 {id} 查询";
        // The id is its OWN space-delimited token, never embedded in the Chinese run: inside a spaceless run
        // it would be swallowed by the trigram expansion and stop being matchable as a whole id, which would
        // quietly turn this from "look this entry up" into "look up some overlapping characters".
        public override string CriticalRecall(string id) => $"{id} 后来怎么样了";

        /// <summary>Shares no character with 面条 (noodles) or 沙拉 (salad) — see <see cref="Routine"/>.
        /// </summary>
        public override string RoutineQuery() => "我通常午餐吃什么";

        /// <summary>Subject plus a marker that appears nowhere else — the Chinese counterpart of
        /// "{subject} recallcue".
        /// <para><b>The leading 我的 is load-bearing, and the first draft ("{subject}回忆线索") reached ZERO
        /// cluster members.</b> English gets to name a subject as a whole word token: "spouse" is a term and
        /// matches. Chinese has no such token — 配偶 is TWO characters, below the trigram floor — so a bare
        /// subject glued to the marker yields only boundary-straddling trigrams (配偶回, 偶回忆, …) that
        /// appear in no entry at all. The arm reported <c>attribute</c> miss ≈ 0.889 with pollution 0.000
        /// against English's 0.299, which reads as a language finding and was two different experiments.</para>
        /// <para>The content says 我的{subject}是, so 我的配 and 的配偶 are trigrams the cue and the content
        /// genuinely share — and it is how the phrase is actually said. Pinned by
        /// <c>MemoryCorpusTests.A_discriminative_cue_reaches_exactly_one_cluster_member</c>, which did not
        /// exist until this bug: every prior fact pinned the cue's UPPER bound (shares nothing outside the
        /// cluster) and none pinned the lower one, so a cue matching nothing passed them all.</para>
        /// <para><b>The underlying limitation is real and is NOT a corpus artifact:</b> a two-character CJK
        /// term cannot be matched by a trigram index, so a Chinese query whose only overlap with the stored
        /// text is one two-character word finds nothing through FTS. It reaches the caller's substring
        /// fallback only when the query yields no usable term at all. See <c>docs/DECISIONS.md</c> D55.</para>
        /// </summary>
        public override string DiscriminativeCue(string subject) => $"我的{subject}回忆线索";

        /// <summary>The same cue wrapped in ordinary conversational words that DO appear in unrelated
        /// entries. In Chinese this is not a wording choice one could avoid: under trigram matching there is
        /// no stopword to strip, so contention with incidental material is the normal condition.
        /// <para><b>The overlap is deliberate and load-bearing, and the first draft did not have it.</b>
        /// "提醒我一下关于我的{subject}回忆线索" reads perfectly naturally and shares NO trigram with any
        /// non-cluster entry, which would have made this cue kind identical to the discriminative one and any
        /// measured gap between them pure noise. The phrase 提到过一次 is carried on purpose: it appears in
        /// both the critical (只提到过一次的事实) and noise (被提到一次) templates, so the trigrams 提到过 /
        /// 到过一 / 过一次 genuinely reach outside the cluster. Pinned by
        /// <c>MemoryCorpusTests.An_overlapping_attribute_query_shares_a_token_with_material_outside_its_cluster</c>,
        /// which is what caught the first draft.</para></summary>
        public override string CommonTokenCue(string subject) => $"关于我的{subject}我记得提到过一次回忆线索";

        public override bool IsAttributeCue(string queryText) =>
            queryText.EndsWith("回忆线索", StringComparison.Ordinal);

        public override string AttributeValueOf(string content)
        {
            // "记录条目 {id} 我的{subject}是{value}{filler}只说过一次…" — between 是 and the fixed tail
            var body = content.Split(' ')[2];
            var start = body.IndexOf('是', StringComparison.Ordinal) + 1;
            var end = body.IndexOf("只说过一次", StringComparison.Ordinal);
            var span = body[start..end];
            // the filler is appended directly after the value, with no separator to split on, so strip it
            foreach (var filler in Instance.Fillers)
                if (span.EndsWith(filler, StringComparison.Ordinal))
                    return span[..^filler.Length];
            return span;
        }
    }

    /// <summary><b>Chinese technical prose with English embedded inside the run</b> — the shape that made
    /// script-run segmentation necessary, and the one every other arm misses because they put ASCII only at
    /// token edges (the id) rather than in the middle of a word run.
    /// <para>Subjects and values deliberately span both kinds: <c>配偶</c> is pure Chinese, <c>deploy密钥</c>
    /// and <c>client客户</c> put a script boundary inside a single token, and the values mix likewise. If
    /// segmentation regressed, THESE are the entries that would stop being findable while the monolingual
    /// arms carried on passing.</para></summary>
    private sealed class ChineseMixedLexicon : CorpusLexicon
    {
        public static readonly ChineseMixedLexicon Instance = new();

        public override bool WritesWordSpaces => false;

        public override string ItemToken => "记录条目";

        /// <summary>Latin INSIDE the CJK run, like the rest of this arm — the marker has to cross the same
        /// script boundary the entries do, or the one arm that exists to test mixed script would test it
        /// everywhere except in its own instrument.</summary>
        public override string HeadlineMarker => "灯塔beacon";

        public override IReadOnlyList<string> Fillers => ChineseLexicon.Instance.Fillers;

        /// <summary>One pure-Chinese subject and two that straddle a script boundary mid-token.</summary>
        public override IReadOnlyList<string> AttributeSubjects => ["配偶", "deploy密钥", "client客户"];

        /// <summary>Likewise: a Chinese name, and two values with Latin inside the run.</summary>
        public override IReadOnlyList<string> AttributeValues => ["爱丽丝", "beta版本", "gamma集群"];

        public override string Authoritative(string id, string filler) =>
            $"记录条目 {id} passport号码是{filler}XK4419必须永远保留";

        public override string UnrelatedProbe() => "记录条目 重复 关注材料";

        public override string Attribute(string id, string subject, string value, string filler) =>
            $"记录条目 {id} 我的{subject}是{value}{filler}只说过一次而且从未重复";

        public override string Critical(string id, string filler) =>
            $"记录条目 {id} 这是一个{filler}只提到过一次的critical事实绝对不能丢失";

        public override string Topical(string id, string filler) =>
            $"记录条目 {id} 涵盖了{filler}按自身主题被查询的pipeline日常材料";

        public override string Hot(string id, string filler, int round) =>
            $"记录条目 {id} 是第{round}轮{filler}当前的focus关注重点";

        public override string Noise(string id, string filler) =>
            $"记录条目 {id} 曾{filler}被提到一次此后再也没有出现在log里";

        public override string Padding(string id, string filler) =>
            $"填充 {id} 仅{filler}用于插入间隔从来不会被查询";

        /// <summary>The distinguishing tokens are the untranslated English loanwords "noodles"/"salad",
        /// matching the rest of this lexicon's habit of leaving a technical/foreign term in Latin script —
        /// and, like <see cref="ChineseLexicon.Routine"/>, sharing no character with
        /// <see cref="RoutineQuery"/>.</summary>
        public override string Routine(string id, int regime, string filler) =>
            regime == 0
                ? $"记录条目 {id} 又{filler}吃了noodles当作lunch"
                : $"记录条目 {id} 又{filler}吃了salad当作lunch";

        public override string RoutineToken(int regime) => regime == 0 ? "noodles" : "salad";

        /// <summary>Mixed like the rest of this lexicon — the statement carries Latin inside the Han run,
        /// so the pair also exercises run segmentation rather than only the Han path.</summary>
        public override IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs =>
        [
            ("会议被推迟到下个星期meeting", "研讨延后"),
            ("服务器在夜里崩溃了server", "主机凌晨故障"),
            ("预算已经通过委员批准budget", "经费获得理事同意"),
        ];

        /// <summary>Mixed by construction, like everything else in this lexicon: each entry straddles the
        /// script boundary mid-token, so diverse junk here also exercises the run-segmentation path rather
        /// than only the Han one.</summary>
        public override IReadOnlyList<string> NoiseVocabulary =>
        [
            "港湾dock", "灯笼lamp", "羊皮skin", "玄武rock", "季风wind", "镶嵌tile", "采石pit", "兰花bloom",
            "水壶pot", "花岗stone", "草甸field", "钴蓝blue", "棚架rack", "藏红dye", "浮石foam", "铁砧iron",
            "柳树tree", "煤渣ash", "旗鱼fish", "杜松cone", "燧石spark", "柏木wood", "琥珀resin", "蓟草weed",
            "石英glass", "荆棘thorn", "苍鹭bird", "蛋白white", "貂皮fur", "茴香seed", "桦木bark", "珊瑚reef",
            "石膏chalk", "红雀finch", "没药balm", "缟玛band", "白镴alloy", "涟漪wave", "酸模herb", "苔原plain",
            "赭色ochre", "马鞭vine", "胡桃shell", "氙气gas", "蓍草stalk", "西风gale", "壁龛nook", "风箱pump",
            "水槽basin", "鸠尾joint", "余烬coal", "犁沟groove", "构台frame", "凹陷dent", "锭铁bar", "浮桥span",
            "窑炉oven", "过梁beam", "榫眼slot", "肉豆spice",
        ];

        public override string TopicalRepeat(string id, int k) => $"记录条目 {id} 重复{k}";
        public override string HotRepeat(string id, int k) => $"记录条目 {id} 关注重复{k}";
        public override string HotStale(string id) => $"记录条目 {id} 关注已过期";
        public override string CriticalLookup(string id) => $"记录条目 {id} 查询";
        public override string CriticalRecall(string id) => $"{id} 后来怎么样了";

        /// <summary>Carries the English "lunch" like the rest of this lexicon, but neither "noodles" nor
        /// "salad" — see <see cref="Routine"/>.</summary>
        public override string RoutineQuery() => "我通常lunch吃什么";

        public override string DiscriminativeCue(string subject) => $"我的{subject}回忆线索";

        /// <summary>Carries 提到过一次, which appears in the critical and noise templates, so this cue kind
        /// genuinely contends with material outside the cluster.</summary>
        public override string CommonTokenCue(string subject) => $"关于我的{subject}我记得提到过一次回忆线索";

        public override bool IsAttributeCue(string queryText) =>
            queryText.EndsWith("回忆线索", StringComparison.Ordinal);

        public override string AttributeValueOf(string content)
        {
            var body = content.Split(' ')[2];
            var start = body.IndexOf('是', StringComparison.Ordinal) + 1;
            var end = body.IndexOf("只说过一次", StringComparison.Ordinal);
            var span = body[start..end];
            foreach (var filler in Instance.Fillers)
                if (span.EndsWith(filler, StringComparison.Ordinal))
                    return span[..^filler.Length];
            return span;
        }
    }

    /// <summary><b>Japanese: spaceless like Chinese, and the harder case.</b> Kana words are often two
    /// characters and hiragana's inventory is small, so three common kana can collide across unrelated
    /// sentences — a trigram is a weaker discriminator here than in Chinese. Every sentence below also mixes
    /// kanji, hiragana and katakana inside ONE spaceless run, which normal Japanese does and no other arm
    /// here produces.</summary>
    private sealed class JapaneseLexicon : CorpusLexicon
    {
        public static readonly JapaneseLexicon Instance = new();

        public override bool WritesWordSpaces => false;

        /// <summary>"record item" — four characters, so it clears the trigram floor and the shared token
        /// actually makes entries compete. The Chinese arm shipped a TWO-character token first and silently
        /// stopped measuring; see <see cref="ChineseLexicon"/>'s note.</summary>
        public override string ItemToken => "記録項目";

        public override IReadOnlyList<string> Fillers =>
        [
            "そっと", "最終的に", "手短に", "はっきりと", "どうやら",
            "明らかに", "異常に", "とりわけ", "不思議にも", "もちろん",
        ];

        /// <summary>配偶者 spouse, デプロイ鍵 deploy key, 取引先 client — a person, a credential, a party, the
        /// same three kinds every arm carries. All three or more characters, and deliberately sharing no
        /// trigram with one another.</summary>
        public override IReadOnlyList<string> AttributeSubjects => ["配偶者", "デプロイ鍵", "取引先"];

        /// <summary>Katakana personal names — the kind of token a real cue has to recover.</summary>
        public override IReadOnlyList<string> AttributeValues => ["アリス", "ベラ", "キャロル"];

        public override string Authoritative(string id, string filler) =>
            $"記録項目 {id} 旅券番号は{filler}XK4419であり永久に保持されねばならない";

        public override string UnrelatedProbe() => "記録項目 反復 関心資料";

        public override string HeadlineMarker => "灯台標識";

        public override string Attribute(string id, string subject, string value, string filler) =>
            $"記録項目 {id} 私の{subject}は{value}{filler}一度だけ述べられ二度と繰り返されない";

        public override string Critical(string id, string filler) =>
            $"記録項目 {id} これは{filler}一度だけ言及された事実で決して失われてはならない";

        public override string Topical(string id, string filler) =>
            $"記録項目 {id} は{filler}それ自体の主題で照会される通常の資料を扱う";

        public override string Hot(string id, string filler, int round) =>
            $"記録項目 {id} は第{round}巡の{filler}現在の関心事である";

        public override string Noise(string id, string filler) =>
            $"記録項目 {id} は{filler}一度だけ言及されその後二度と現れない";

        public override string Padding(string id, string filler) =>
            $"詰め物 {id} は{filler}間隔を挿入するためだけに書かれ照会されない";

        /// <summary>蕎麦 soba/noodles (regime 0, kanji) and サラダ salad (regime 1, katakana loanword) share
        /// no character with each other or with <see cref="RoutineQuery"/>.</summary>
        public override string Routine(string id, int regime, string filler) =>
            regime == 0
                ? $"記録項目 {id} は{filler}昼食に蕎麦を食べた"
                : $"記録項目 {id} は{filler}昼食にサラダを食べた";

        public override string RoutineToken(int regime) => regime == 0 ? "蕎麦" : "サラダ";

        /// <summary>Kanji-led for the same reason the junk list is: a kana-heavy pair would collide on
        /// kana's small inventory and measure the tokenizer's known weak spot rather than the retrieval
        /// question.</summary>
        public override IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs =>
        [
            ("会議は来週まで延期された", "研討先送り"),
            ("夜中にサーバが停止した", "主機早朝故障"),
            ("予算は委員会で承認された", "経費理事同意"),
        ];

        /// <summary>Kanji-led words rather than kana ones, on purpose: kana's small inventory is exactly
        /// what makes its trigrams weak discriminators, so a kana-heavy junk list would measure the
        /// tokenizer's known weak spot rather than the salience hypothesis.</summary>
        public override IReadOnlyList<string> NoiseVocabulary =>
        [
            "港湾", "灯籠", "羊皮", "玄武", "季節", "象嵌", "採石", "蘭花",
            "水壺", "花崗", "草原", "紺青", "棚架", "紅花", "軽石", "鉄床",
            "柳樹", "石炭", "旗魚", "杜松", "燧石", "檜木", "琥珀", "薊草",
            "石英", "茨木", "蒼鷺", "卵白", "貂皮", "茴香", "樺木", "珊瑚",
            "石膏", "紅雀", "没薬", "縞瑪", "白鑞", "漣漪", "酸模", "凍土",
            "赭色", "馬鞭", "胡桃", "硝子", "蓍草", "西風", "壁龕", "風箱",
            "水槽", "鳩尾", "余燼", "犂溝", "構台", "凹陷", "錠鉄", "浮橋",
            "窯炉", "過梁", "枘穴", "肉荳",
        ];

        public override string TopicalRepeat(string id, int k) => $"記録項目 {id} 反復{k}";
        public override string HotRepeat(string id, int k) => $"記録項目 {id} 関心反復{k}";
        public override string HotStale(string id) => $"記録項目 {id} 関心期限切れ";
        public override string CriticalLookup(string id) => $"記録項目 {id} 照会";
        // the id stays its own space-delimited token — inside a spaceless run the trigram expansion would
        // swallow it and it would stop being matchable as a whole id
        public override string CriticalRecall(string id) => $"{id} はその後どうなったか";

        /// <summary>Shares no character with 蕎麦 (soba/noodles) or サラダ (salad) — see
        /// <see cref="Routine"/>.</summary>
        public override string RoutineQuery() => "私はいつも昼食に何を食べますか";

        /// <summary>The cue carries 私の{subject}, for the same reason the Chinese one does: the content says
        /// 私の{subject}は, so 私の配 / の配偶 / 配偶者 are trigrams the query and the content genuinely share.
        /// A bare subject glued to the marker would yield only boundary-straddling trigrams and reach nothing.
        /// </summary>
        public override string DiscriminativeCue(string subject) => $"私の{subject}想起手掛かり";

        /// <summary>Wrapped in ordinary words that DO appear elsewhere. 一度だけ言及 is carried on purpose: it
        /// is in both the critical and noise templates, so the trigrams 一度だ / 度だけ / だけ言 / け言及 reach
        /// outside the cluster and the two cue kinds measure genuinely different conditions.</summary>
        public override string CommonTokenCue(string subject) =>
            $"私の{subject}について一度だけ言及されたのを覚えている想起手掛かり";

        public override bool IsAttributeCue(string queryText) =>
            queryText.EndsWith("想起手掛かり", StringComparison.Ordinal);

        public override string AttributeValueOf(string content)
        {
            // "記録項目 {id} 私の{subject}は{value}{filler}一度だけ述べられ…" — between the first は and the tail
            var body = content.Split(' ')[2];
            var start = body.IndexOf('は', StringComparison.Ordinal) + 1;
            var end = body.IndexOf("一度だけ述べられ", StringComparison.Ordinal);
            var span = body[start..end];
            foreach (var filler in Instance.Fillers)
                if (span.EndsWith(filler, StringComparison.Ordinal))
                    return span[..^filler.Length];
            return span;
        }
    }

    /// <summary><b>Korean, and it WRITES SPACES</b> — so whitespace really does separate its 어절 and the
    /// "whitespace splits nothing" argument for trigram expansion does not apply here. <c>SearchTerms</c>
    /// expands it regardless, because Hangul sits in its spaceless-script range, and that is defensible for a
    /// different reason: Korean is agglutinative, so 배우자는 / 배우자의 / 배우자에게 are one stem with three
    /// endings and whole-token matching would miss the stem whenever the particle differed.
    /// <para>This arm exists to keep that difference in JUSTIFICATION measurable. If the expansion ever costs
    /// more than it recovers, Korean is where it shows up first — and an arm that lumped it in with the
    /// spaceless pair could not see it. See <see cref="CorpusLanguage.Korean"/>.</para></summary>
    private sealed class KoreanLexicon : CorpusLexicon
    {
        public static readonly KoreanLexicon Instance = new();

        public override bool WritesWordSpaces => true;

        /// <summary>"record item" — four syllables, clearing the trigram floor so the shared token competes.
        /// </summary>
        public override string ItemToken => "기록항목";

        public override string HeadlineMarker => "등대표지";

        /// <summary>Single tokens, never phrases: the value reader below finds the value POSITIONALLY among
        /// space-delimited tokens, so a filler containing a space would shift every index after it.</summary>
        public override IReadOnlyList<string> Fillers =>
        [
            "조용히", "결국", "간단히", "분명히", "어쩐지",
            "확실히", "이례적으로", "특히", "이상하게", "물론",
        ];

        /// <summary>배우자 spouse, 배포키 deploy key, 고객사 client — a person, a credential, a party. Three
        /// syllables each so every one clears the floor, and no two share a trigram.</summary>
        public override IReadOnlyList<string> AttributeSubjects => ["배우자", "배포키", "고객사"];

        public override IReadOnlyList<string> AttributeValues => ["앨리스", "벨라", "캐럴"];

        public override string Authoritative(string id, string filler) =>
            $"기록항목 {id} 여권번호는 {filler} XK4419이며 영구히 보존되어야 한다";

        public override string UnrelatedProbe() => "기록항목 반복 관심자료";

        public override string Attribute(string id, string subject, string value, string filler) =>
            $"기록항목 {id} 나의 {subject}는 {value}{filler} 한 번만 언급되고 다시 반복되지 않는다";

        public override string Critical(string id, string filler) =>
            $"기록항목 {id} 이것은 {filler} 한 번만 언급된 사실이며 절대 잃어버려서는 안 된다";

        public override string Topical(string id, string filler) =>
            $"기록항목 {id} 는 {filler} 자체 주제로 조회되는 일상적인 자료를 다룬다";

        public override string Hot(string id, string filler, int round) =>
            $"기록항목 {id} 는 제{round}회차의 {filler} 현재 관심사이다";

        public override string Noise(string id, string filler) =>
            $"기록항목 {id} 는 {filler} 한 번만 언급되고 그 후 다시 나타나지 않는다";

        public override string Padding(string id, string filler) =>
            $"채움 {id} 는 {filler} 간격을 넣기 위해서만 기록되며 조회되지 않는다";

        /// <summary>국수 noodles (regime 0) and 샐러드 salad (regime 1) share no syllable with each other or
        /// with <see cref="RoutineQuery"/>.</summary>
        public override string Routine(string id, int regime, string filler) =>
            regime == 0
                ? $"기록항목 {id} 는 {filler} 또 점심으로 국수를 먹었다"
                : $"기록항목 {id} 는 {filler} 또 점심으로 샐러드를 먹었다";

        public override string RoutineToken(int regime) => regime == 0 ? "국수" : "샐러드";

        /// <summary>Korean writes spaces, but Hangul is still expanded into grams, so a pair must avoid a
        /// shared three-syllable window as well as a shared word.</summary>
        public override IReadOnlyList<(string Statement, string Cue)> ParaphrasePairs =>
        [
            // Every cue needs at least one THREE-syllable word: Hangul is expanded into 3-grams, so a cue
            // built only from two-syllable words falls below the floor and extracts to nothing — which is
            // indistinguishable from "the lexical path cannot answer it" for the wrong reason.
            ("회의가 다음 주까지 연기되었다", "토론 미뤄짐"),
            ("서버가 밤사이 멈추었다", "야간 장비 정지됨"),
            ("예산이 위원회에서 승인되었다", "경비 이사회 동의"),
        ];

        /// <summary>Korean WRITES spaces, so <see cref="DiverseNoise"/> joins these with them and each word
        /// is its own token — the arm where diverse junk is least likely to collide incidentally, and
        /// therefore the cleanest read on the hypothesis.</summary>
        public override IReadOnlyList<string> NoiseVocabulary =>
        [
            "항구", "등불", "양피", "현무", "계절", "상감", "채석", "난초",
            "주전", "화강", "초원", "감청", "선반", "홍화", "경석", "모루",
            "버들", "숯재", "청새", "노간", "부싯", "편백", "호박", "엉겅",
            "석영", "가시", "왜가", "달걀", "담비", "회향", "자작", "산호",
            "석고", "홍방", "몰약", "마노", "백랍", "잔물", "수영", "동토",
            "황토", "마편", "호두", "유리", "톱풀", "서풍", "벽감", "풀무",
            "물통", "비둘", "잉걸", "고랑", "구조", "함몰", "주철", "부교",
            "가마", "인방", "장부", "육두",
        ];

        public override string TopicalRepeat(string id, int k) => $"기록항목 {id} 반복{k}";
        public override string HotRepeat(string id, int k) => $"기록항목 {id} 관심반복{k}";
        public override string HotStale(string id) => $"기록항목 {id} 관심만료";
        public override string CriticalLookup(string id) => $"기록항목 {id} 조회";
        public override string CriticalRecall(string id) => $"{id} 는 그 후 어떻게 되었나";

        /// <summary>Shares no syllable with 국수 (noodles) or 샐러드 (salad) — see <see cref="Routine"/>.
        /// </summary>
        public override string RoutineQuery() => "나는 점심에 보통 무엇을 먹나요";

        /// <summary>The subject carries its topic particle exactly as the content does (배우자는), so the
        /// shared trigrams are 배우자 and 우자는 — the agglutinative case this arm exists to exercise.</summary>
        public override string DiscriminativeCue(string subject) => $"나의 {subject}는 회상단서";

        /// <summary>언급된 is carried on purpose: it appears in the critical template, so this cue genuinely
        /// contends with material outside the cluster while the discriminative form does not.</summary>
        public override string CommonTokenCue(string subject) =>
            $"나의 {subject}는 한 번만 언급된 것으로 기억한다 회상단서";

        public override bool IsAttributeCue(string queryText) =>
            queryText.EndsWith("회상단서", StringComparison.Ordinal);

        public override string AttributeValueOf(string content)
        {
            // "기록항목 {id} 나의 {subject}는 {value}{filler} 한 번만 …" — value+filler is the fifth token.
            // Positional, and safe only because Korean fillers are single tokens (see Fillers).
            var span = content.Split(' ')[4];
            foreach (var filler in Instance.Fillers)
                if (span.EndsWith(filler, StringComparison.Ordinal))
                    return span[..^filler.Length];
            return span;
        }
    }
}
