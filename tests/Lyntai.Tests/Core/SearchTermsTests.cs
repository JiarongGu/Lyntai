using Lyntai.Storage;

namespace Lyntai.Tests.Core;

/// <summary>
/// <see cref="SearchTerms"/>'s own facts, separate from <see cref="FtsQueryTests"/> because the split is now
/// shared by every backend rather than owned by the FTS path — so a defect here is a defect in all six
/// callers at once, not in one query builder.
/// <para>Covers the claims made in <c>docs/DECISIONS.md</c> D55 that nothing else exercises: that all three
/// CJK scripts are treated alike (asserted, not assumed — the decision says so and only Chinese has a
/// corpus), that the expansion is bounded, and that <see cref="SearchTerms.LikeClause"/> escapes and falls
/// back the way the substring backends rely on.</para>
/// </summary>
public class SearchTermsTests
{
    // ---- spaceless scripts: the decision claims all three behave alike; only Chinese was ever measured ----

    /// <summary>Japanese, both syllabaries. <b>The sharper case than Chinese, and worth stating why:</b> kana
    /// words are frequently two characters and hiragana's character inventory is small, so trigram collisions
    /// are likelier than in Chinese. This pins the SPLIT only — that Japanese reaches the trigram path at all
    /// — never that its recall quality matches, which is unmeasured (`TASKS.md`).</summary>
    [Theory]
    [InlineData("ひらがなのぶんしょう")]   // hiragana
    [InlineData("カタカナノブンショウ")]   // katakana
    public void A_single_script_japanese_run_is_expanded_into_trigrams(string text)
    {
        var terms = SearchTerms.Extract(text);

        Assert.Equal(text.Length - 2, terms.Count);          // a full sliding window, nothing dropped
        Assert.Equal(text[..3], terms[0]);
        Assert.All(terms, t => Assert.Equal(3, t.Length));
    }

    /// <summary><b>Ordinary Japanese mixes kanji and kana, and segmenting on that boundary is a cheap
    /// approximation of word segmentation</b> — kanji runs carry the content words, kana runs carry grammar.
    /// <c>日本語の文章です</c> yields <c>日本語</c> and nothing else: the sliding windows that used to span the
    /// boundary (<c>語の文</c>, <c>の文章</c>) were never words, and they are the kind of low-information term
    /// that made Japanese pollute more than any other language measured.
    /// <para><b>The cost is real and stated:</b> <c>文章</c> is a genuine two-character word and is lost from
    /// the INDEX path, because two characters are below what a trigram index can match. The substring path
    /// still carries it as a short gram. Whether that trade is worth it is a measurement, not an argument —
    /// see <c>TASKS.md</c>.</para></summary>
    [Fact]
    public void A_mixed_kanji_kana_token_is_segmented_on_the_script_boundary()
    {
        var terms = SearchTerms.Extract("日本語の文章です");

        Assert.Equal(["日本語"], terms);
        Assert.Contains("文章", SearchTerms.SubstringTerms("日本語の文章です"));   // the substring path keeps it
    }

    /// <summary><b>A query whose tokens are ALL below the index floor still yields its short grams.</b>
    ///
    /// <para>The regression this pins: <see cref="SearchTerms.SubstringTerms"/> short-circuited on
    /// <c>Extract</c> returning empty and never consulted the short grams at all, so a multi-word CJK query
    /// like <c>"配偶 客户"</c> — two ordinary two-character words, which this file's own docs call the COMMON
    /// case for Chinese — produced NO terms. Every substring backend then fell back to matching the whole
    /// trimmed query as one literal (<c>LikeClause</c>'s own <c>terms.Count == 0</c> fallback), i.e.
    /// <c>%配偶 客户%</c>, which requires that exact phrase INCLUDING the space and so matches nothing that
    /// ordinary prose contains.</para>
    ///
    /// <para><b>The tell was the asymmetry, not the empty list.</b> The identical token <c>配偶</c> DID
    /// survive when a longer word accompanied it (<c>"配偶 叫什么名字"</c>), because the long token made
    /// <c>Extract</c> non-empty and the short grams were appended. So the same word was kept or dropped
    /// depending on its NEIGHBOURS — which no design would choose.</para>
    ///
    /// <para>The single-token case (<c>"配偶"</c> alone) was never broken and stays covered below: it
    /// produced no terms and fell through to the whole-query scan, which for one token is the SAME pattern
    /// (<c>%配偶%</c>). That coincidence is precisely why this went unnoticed.</para></summary>
    [Fact]
    public void Short_grams_survive_when_every_token_is_below_the_index_floor()
    {
        // two-character Han words: below the 3-gram index floor, at exactly the substring floor
        Assert.Empty(SearchTerms.Extract("配偶 客户"));
        Assert.True(SearchTerms.HasShortSpacelessTerms("配偶 客户"));

        Assert.Equal(["配偶", "客户"], SearchTerms.SubstringTerms("配偶 客户"));

        // and the asymmetry is gone: the same token survives with or without a long neighbour
        Assert.Contains("配偶", SearchTerms.SubstringTerms("配偶 叫什么名字"));
        Assert.Contains("配偶", SearchTerms.SubstringTerms("配偶 客户"));
    }

    /// <summary>The single-token case, unchanged — kept beside the fact above so the two cannot drift apart.
    /// One short token yields exactly itself, which matches what the whole-query fallback would have
    /// produced anyway.</summary>
    [Fact]
    public void A_single_below_floor_token_yields_itself()
    {
        Assert.Equal(["配偶"], SearchTerms.SubstringTerms("配偶"));
        Assert.Empty(SearchTerms.Extract("配偶"));
    }

    /// <summary>An all-ASCII query is untouched by the fix: <see cref="ScriptProfile.Spaced"/> does not
    /// expand, so there are no short grams to add and a two-letter word stays out (it would match almost
    /// every row). Pinned because the obvious over-broad fix — always unioning — must not start emitting
    /// ASCII fragments.</summary>
    [Fact]
    public void An_ascii_query_gains_no_short_grams()
    {
        Assert.False(SearchTerms.HasShortSpacelessTerms("what is the spouse called"));
        Assert.Equal(SearchTerms.Extract("what is the spouse called"),
            SearchTerms.SubstringTerms("what is the spouse called"));
    }

    /// <summary>Korean Hangul syllables. Same contract, and it is a genuinely different Unicode block from
    /// the CJK ideographs — a range check that covered only the ideograph block would fail here while every
    /// Chinese test still passed.</summary>
    [Fact]
    public void Korean_is_expanded_into_trigrams()
    {
        var terms = SearchTerms.Extract("한국어문장");

        Assert.Equal(["한국어", "국어문", "어문장"], terms);
    }

    /// <summary>An ASCII word is NOT expanded — deliberately, since a whole word is more precise than its
    /// trigrams. Pinned because the cheap implementation (expand everything) passes every CJK fact above
    /// while quietly making <c>"cat"</c> match <c>"concatenate"</c>.</summary>
    [Fact]
    public void An_ascii_word_is_not_expanded()
    {
        Assert.Equal(["concatenate"], SearchTerms.Extract("concatenate"));
        Assert.Equal(["deploy", "pipeline"], SearchTerms.Extract("deploy pipeline"));
    }

    /// <summary><b>A token mixing scripts is segmented into script RUNS, and each run is analysed under its
    /// own rules.</b>
    /// <para>CJK text without spaces around embedded Latin is the normal case, not a corner —
    /// <c>我今天deploy了</c>, <c>部署key</c>. Sliding one window across the whole token instead shreds the Latin
    /// word into fragments (<c>dep</c>, <c>epl</c>, <c>plo</c>) that are not words in any language, while
    /// never emitting <c>deploy</c> itself. The fragments then match arbitrary unrelated text.</para>
    /// <para>This test asserted the shredding behaviour until 2026-08-13 — it pinned what the code did rather
    /// than what it should do, which is why the defect survived being tested.</para></summary>
    [Fact]
    public void A_token_mixing_scripts_is_split_into_script_runs()
    {
        // the Latin word survives WHOLE; the two-character Han run is below the index floor and drops
        Assert.Equal(["key"], SearchTerms.Extract("部署key"));

        // and the CJK run is still expanded on its own terms, with no gram straddling the boundary
        var mixed = SearchTerms.Extract("我今天deploy了");
        Assert.Contains("我今天", mixed);
        Assert.Contains("deploy", mixed);
        Assert.DoesNotContain(mixed, t => t is "天de" or "dep" or "epl");
    }

    // ---- the script-profile seam ----

    /// <summary>The profile of a token's FIRST script run — which for a single-script token is simply its
    /// profile, and that is the case a consumer asks about.
    /// <para>A mixed token deliberately has no single answer: analysis segments it into runs and treats each
    /// under its own rules, which is strictly better than picking one set of rules for both halves. An
    /// earlier version returned "the most demanding script present" precisely because it had to choose;
    /// segmentation removed the need to.</para></summary>
    [Theory]
    [InlineData("deploy", "spaced")]
    [InlineData("配偶是爱丽丝", "han")]
    [InlineData("ひらがな", "kana")]
    [InlineData("배우자는", "hangul")]
    [InlineData("私の配偶者", "han")]         // first run is the kanji 私; の and 配偶者 are their own runs
    public void ProfileOf_returns_the_first_runs_profile(string token, string expected) =>
        Assert.Equal(expected, SearchTerms.ProfileOf(token).Name);

    /// <summary><b>Thai is spaceless too, and gets the same treatment.</b> It was outside the script ranges
    /// until 2026-08-13, so a Thai sentence was handed back as ONE whitespace token and could only match an
    /// entry containing that exact substring — precisely the defect fixed for CJK, still live for a script
    /// nobody had looked at.
    /// <para>Thai has no case, and its words are frequently two or three characters, so it takes the same
    /// profile shape as Han. <b>Unmeasured</b>, deliberately said out loud: Japanese is the standing warning
    /// that adding a range without measuring it proves nothing, and Thai has no corpus. This makes it
    /// tokenize like a spaceless script rather than like one long word — a strict improvement over matching
    /// nothing — and claims no more than that.</para></summary>
    [Theory]
    [InlineData("ภาษาไทย", "thai")]        // Thai
    [InlineData("ພາສາລາວ", "lao")]         // Lao
    [InlineData("ភាសាខ្មែរ", "khmer")]        // Khmer
    [InlineData("မြန်မာဘာသာ", "myanmar")]    // Burmese
    [InlineData("བོད་སྐད་", "tibetan")]        // Tibetan — the tsheg separates syllables, not words
    public void A_spaceless_script_outside_cjk_is_expanded_into_trigrams(string text, string profile)
    {
        Assert.Equal(profile, SearchTerms.ProfileOf(text).Name);

        var terms = SearchTerms.Extract(text);

        Assert.NotEmpty(terms);
        Assert.All(terms, t => Assert.Equal(3, t.Length));
        // and NOT the whole sentence as one term, which is the defect this closes
        Assert.DoesNotContain(text, terms);
    }

    /// <summary>Digits and punctuation are script-NEUTRAL and must never split a run. Without this,
    /// <c>第3轮</c> and <c>重复0</c> break into single characters and yield NO terms at all — a regression on
    /// perfectly ordinary CJK, and the reason run segmentation needed a refinement rather than being applied
    /// naively.</summary>
    [Fact]
    public void A_digit_does_not_split_a_run()
    {
        Assert.Equal(["第3轮"], SearchTerms.Extract("第3轮"));
        Assert.Equal(["重复0"], SearchTerms.Extract("重复0"));
        Assert.Equal(["attribute0"], SearchTerms.Extract("attribute0"));
    }

    /// <summary><b>No shipped profile may ask the INDEX for a term shorter than it can match.</b> The FTS5
    /// trigram tokenizer indexes three-character sequences, so a two-character index term matches nothing and
    /// would silently delete recall for that script. The reverse is deliberately allowed and is the point of
    /// the seam: a query may be LONGER than three, matched as consecutive trigrams, so a script whose
    /// 3-grams collide can be made more selective with no migration.</summary>
    [Fact]
    public void Every_profile_asks_the_index_for_at_least_a_trigram()
    {
        Assert.NotEmpty(SearchTerms.Profiles);
        Assert.All(SearchTerms.Profiles,
            p => Assert.True(p.IndexGramLength >= SearchTerms.MinimumTermLength,
                $"profile '{p.Name}' asks the index for {p.IndexGramLength} characters, below the " +
                $"{SearchTerms.MinimumTermLength} it can match — that deletes recall for the script"));
    }

    /// <summary>The substring gram must stay SHORTER than the index gram, or it adds nothing while costing a
    /// sequential scan on Postgres. Zero means "this script contributes none", which is right for a spaced
    /// script where a two-letter fragment would match nearly every row.</summary>
    [Fact]
    public void A_substring_gram_is_shorter_than_the_index_gram_or_absent()
    {
        Assert.All(SearchTerms.Profiles, p => Assert.True(
            p.SubstringGramLength == 0 || p.SubstringGramLength < p.IndexGramLength,
            $"profile '{p.Name}' has substring gram {p.SubstringGramLength} against index gram " +
            $"{p.IndexGramLength} — it would buy nothing and still force a scan"));
    }

    /// <summary>A spaced script is matched WHOLE. Pinned separately because it is the property that keeps
    /// <c>"cat"</c> from matching <c>"concatenate"</c>, and it is one boolean away from being lost.</summary>
    [Fact]
    public void A_spaced_script_is_not_expanded()
    {
        Assert.False(ScriptProfile.Spaced.ExpandsIntoGrams);
        Assert.Equal(ScriptProfile.Spaced, SearchTerms.ProfileOf("concatenate"));
    }

    // ---- bounds and hygiene ----

    /// <summary>The expansion is bounded: the input is raw user text and the output sizes a SQL expression.
    /// Asserted against the constant rather than a literal, so raising the cap cannot leave this passing for
    /// the wrong reason.
    /// <para>The run is built from DISTINCT characters on purpose. A first draft used repeated ones and
    /// yielded 7 terms rather than the cap — because de-duplication collapsed them long before the bound was
    /// reached, so the test would have passed the day the cap was deleted.</para></summary>
    [Fact]
    public void A_long_spaceless_run_is_capped()
    {
        // 200 consecutive CJK ideographs — every sliding window is a distinct trigram
        var run = string.Concat(Enumerable.Range(0, 200).Select(i => (char)('一' + i)));

        var terms = SearchTerms.Extract(run);

        Assert.Equal(SearchTerms.MaxTermsPerToken, terms.Count);
        Assert.Equal(terms.Count, terms.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Repeats collapse — a doubled word or a repeated character sequence must not inflate the
    /// expression, and on the substring backends it would otherwise inflate the matched-term COUNT that now
    /// leads their ordering, letting a query outrank itself by repeating a word.</summary>
    [Fact]
    public void Terms_are_de_duplicated_case_insensitively()
    {
        Assert.Equal(["deploy"], SearchTerms.Extract("deploy Deploy DEPLOY"));
        Assert.Equal(["一一一"], SearchTerms.Extract("一一一一一"));   // every window is the same trigram
    }

    /// <summary>Below the floor in any script: no terms, which is the caller's signal to fall back to a
    /// whole-query substring scan. Two CJK characters is the case that matters — most Chinese content words
    /// are exactly two — and it is why the fallback exists rather than being a rounding error.</summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("配偶")]
    [InlineData("  ")]
    [InlineData("")]
    [InlineData(null)]
    public void Nothing_above_the_floor_yields_no_terms(string? raw) =>
        Assert.Empty(SearchTerms.Extract(raw));

    // ---- LikeClause: what the substring backends actually run ----

    /// <summary>One predicate and one score term per extracted term, with the values parameterized. The
    /// COUNT expression is what the non-FTS backends now order by, so a query matching more of the user's
    /// words outranks one matching less — the coarse stand-in for the bm25 SQLite gets for free.</summary>
    [Fact]
    public void LikeClause_emits_one_predicate_and_one_score_per_term()
    {
        var clause = SearchTerms.LikeClause("deploy pipeline", "content");

        Assert.Equal(2, clause.TermCount);
        Assert.Equal(2, clause.Parameters.Count);
        Assert.Contains(" OR ", clause.Predicate);
        Assert.Contains(" + ", clause.MatchCount);
        Assert.Contains("content LIKE @kw0", clause.Predicate);
        Assert.Contains("content LIKE @kw1", clause.Predicate);
        Assert.Equal("%deploy%", clause.Parameters["kw0"]);
        Assert.Equal("%pipeline%", clause.Parameters["kw1"]);
    }

    /// <summary>The operator and the parameter prefix are caller-controlled, so two clauses can coexist in
    /// one statement without colliding — and Postgres gets <c>ILIKE</c> where SQLite gets <c>LIKE</c>.</summary>
    [Fact]
    public void LikeClause_honours_the_operator_and_prefix()
    {
        var clause = SearchTerms.LikeClause("deploy", "n.content", "ILIKE", "q");

        Assert.Contains("n.content ILIKE @q0", clause.Predicate);
        Assert.True(clause.Parameters.ContainsKey("q0"));
    }

    /// <summary><b>Wildcards in user text are escaped</b>, so a query containing <c>%</c> matches a literal
    /// percent rather than everything. The clause emits <c>ESCAPE '\'</c>, which both dialects honour.</summary>
    [Fact]
    public void LikeClause_escapes_wildcards_in_user_text()
    {
        var clause = SearchTerms.LikeClause("100%_off", "content");

        Assert.Equal("%100\\%\\_off%", Assert.Single(clause.Parameters).Value);
        Assert.Contains("ESCAPE '\\'", clause.Predicate);
    }

    /// <summary>A query too short to yield a term falls back to matching the WHOLE query as one substring —
    /// the behaviour a short query always had, and what makes a two-character CJK word findable at all.
    /// </summary>
    [Fact]
    public void LikeClause_falls_back_to_the_whole_query_when_nothing_clears_the_floor()
    {
        var clause = SearchTerms.LikeClause("配偶", "content");

        Assert.Equal(1, clause.TermCount);
        Assert.Equal("%配偶%", Assert.Single(clause.Parameters).Value);
    }

    /// <summary><b>The SUBSTRING path takes two-character terms for spaceless scripts, where the FTS path
    /// cannot.</b>
    /// <para>Most Chinese content words are exactly two characters (配偶 spouse, 客户 client), and a trigram
    /// index cannot match one — glue it to anything and the trigrams straddle the boundary. The short-query
    /// fallback does NOT rescue this: a query that is only <c>配偶</c> yields no term and falls through, but a
    /// LONGER query whose only overlap is that word DOES yield trigrams, so FTS runs, matches nothing, and
    /// the fallback then re-tries the whole query — which also fails. That is the gap.</para>
    /// <para><c>LIKE</c>/<c>ILIKE</c> has no index-imposed minimum, so the substring path can carry the
    /// bigrams the index cannot. They are emitted ALONGSIDE the trigrams, never instead: the trigrams are
    /// more selective, and the matched-term count that orders these backends still rewards a row matching
    /// more of them.</para>
    /// <para>ASCII is deliberately excluded — a two-letter English term ("is", "of") is near-useless and
    /// would match almost everything, whereas a two-character CJK word is a real, reasonably selective
    /// word.</para></summary>
    [Fact]
    public void LikeClause_carries_two_character_terms_for_a_spaceless_script()
    {
        var clause = SearchTerms.LikeClause("我的配偶叫什么名字", "content");
        var patterns = clause.Parameters.Values.Cast<string>().ToList();

        Assert.Contains("%配偶%", patterns);          // the two-character word the trigram index cannot reach
        Assert.Contains("%我的配%", patterns);        // trigrams are still emitted
    }

    /// <summary>The bigram widening is CJK-only. An English query must not start matching on two-letter
    /// fragments, which would match nearly every row and destroy the precision the whole-word rule exists to
    /// protect.</summary>
    [Fact]
    public void LikeClause_does_not_add_two_character_terms_for_ascii()
    {
        var clause = SearchTerms.LikeClause("deploy pipeline", "content");
        var patterns = clause.Parameters.Values.Cast<string>().ToList();

        Assert.Equal(["%deploy%", "%pipeline%"], patterns);
    }

    // ---- does a 3-gram DISCRIMINATE in an abugida? (`TASKS.md` Part 65) ---------------------------------

    /// <summary>Distinct everyday words per script, chosen to share no meaning and, as far as possible, no
    /// characters — so any measured overlap is the TOKENIZER's, not the vocabulary's.
    /// <para><b>Long enough to produce grams at all.</b> A first draft used two-character Han words and
    /// measured nothing: below <see cref="SearchTerms.MinimumTermLength"/> the extractor deliberately
    /// returns EMPTY, which is its documented signal for the caller to fall back to a whole-query substring
    /// scan. Comparing scripts on words that all tokenize to nothing would have compared nothing.</para></summary>
    public static TheoryData<string, string[]> ScriptWords() => new()
    {
        // Han — the arm with a corpus behind it, so it is the reference the others are read against.
        { "han", ["人民医院", "电子邮件", "高速公路", "手机号码", "春夏秋冬", "东南西北", "学习方法", "交通规则"] },
        // Thai — market, book, school, train, food, hospital, river, mountain.
        { "thai", ["ตลาด", "หนังสือ", "โรงเรียน", "รถไฟ", "อาหาร", "โรงพยาบาล", "แม่น้ำ", "ภูเขา"] },
        // Lao — likewise.
        { "lao", ["ຕະຫຼາດ", "ປຶ້ມ", "ໂຮງຮຽນ", "ລົດໄຟ", "ອາຫານ", "ໂຮງໝໍ", "ແມ່ນ້ຳ", "ພູເຂົາ"] },
        // Khmer.
        { "khmer", ["ផ្សារ", "សៀវភៅ", "សាលារៀន", "រថភ្លើង", "អាហារ", "មន្ទីរពេទ្យ", "ទន្លេ", "ភ្នំ"] },
        // Burmese.
        { "myanmar", ["ဈေး", "စာအုပ်", "ကျောင်း", "ရထား", "အစား", "ဆေးရုံ", "မြစ်", "တောင်"] },
        // Tibetan. Included because it is one of the five shipped abugida profiles and leaving it out would
        // make "the abugidas were measured" false for a fifth of them. Its tsheg (U+0F0B) sits INSIDE the
        // Tibetan range, so it does not break the run — a syllable separator the tokenizer treats as
        // content, which is precisely the sub-word-fragment risk under test.
        // <b>Orthography not independently verified.</b> For a DISCRIMINATION measurement what has to be
        // true is that these are distinct, realistic character sequences in the script's own range, which
        // they are; the semantic glosses are incidental and are not what the assertion rests on.
        { "tibetan", ["དཔེ་ཆ", "སློབ་གྲྭ", "མེ་འཁོར", "ཁ་ལག", "སྨན་ཁང", "གཙང་པོ", "རི་བོ", "ཚོང་ཁང"] },
    };

    /// <summary><b>Whether a 3-gram discriminates at all in an abugida — Part 65's standing doubt, measured
    /// for the first time.</b>
    ///
    /// <para><b>The doubt, precisely.</b> Thai, Lao, Khmer, Burmese and Tibetan were given Han's profile
    /// because it was the safe default, not because anything measured them. In these scripts a written
    /// syllable is a base consonant plus stacked vowel and tone marks, each its own code point — so three
    /// UTF-16 chars can be LESS than one syllable, and the grams may be sub-syllabic fragments that many
    /// unrelated words share. That is the same mismatch that made kana trigrams weak discriminators, and
    /// kana's numbers were asserted to behave like Chinese while being half a metric wrong.</para>
    ///
    /// <para><b>What this measures, and what it does not.</b> It measures the tokenizer's DISCRIMINATION on
    /// isolated, unrelated words: the share of word PAIRS that share any term at all. That is the property
    /// the doubt is about, and it needs no corpus — which is why it is done here rather than deferred behind
    /// authoring five corpora in scripts nobody here can proof-read. It is NOT end-to-end recall quality,
    /// and a script passing this is not thereby measured the way Chinese is.</para>
    ///
    /// <para>Read against <c>han</c> in the same run rather than against a fixed constant, so the bar moves
    /// with the tokenizer instead of pinning a number that a legitimate change would have to edit.</para></summary>
    [Theory]
    [MemberData(nameof(ScriptWords))]
    public void A_three_gram_discriminates_between_unrelated_words_in_each_spaceless_script(
        string script, string[] words)
    {
        var sets = words.Select(w => SearchTerms.Extract(w).ToHashSet(StringComparer.Ordinal))
            .Where(s => s.Count > 0)   // below the floor the extractor returns empty BY DESIGN (substring fallback)
            .ToList();

        // ...but if a whole script's everyday words all fell back, the profile would be indexing nothing and
        // the collision figure below would be measuring an empty set rather than a good tokenizer.
        Assert.True(sets.Count >= words.Length - 1,
            $"[{script}] {words.Length - sets.Count} of {words.Length} everyday words produced NO index " +
            "terms — the profile's gram length is longer than the vocabulary it has to index");

        var pairs = 0;
        var colliding = 0;
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
            {
                pairs++;
                if (sets[i].Overlaps(sets[j])) colliding++;
            }

        var collisionRate = (double)colliding / pairs;

        // Unrelated everyday words should mostly NOT collide. A rate above half would mean the grams are
        // sub-word fragments shared across the vocabulary — the failure the doubt predicted, and the point
        // at which the profile would need a different gram length rather than Han's.
        Assert.True(collisionRate <= 0.5,
            $"[{script}] {colliding}/{pairs} unrelated word pairs share a term ({collisionRate:P0}) — a " +
            "3-gram is not discriminating in this script, which is what Part 65 doubted");
    }
}
