using Lyntai.Storage;

namespace Lyntai.Tests.Core;

public class FtsQueryTests
{
    [Fact]
    public void Short_tokens_are_dropped()
    {
        Assert.Equal("\"hello\"", FtsQuery.Build("ab hello to"));
    }

    [Fact]
    public void Tokens_are_quoted_and_or_joined()
    {
        Assert.Equal("\"abc\" OR \"defg\"", FtsQuery.Build("abc defg"));
    }

    [Fact]
    public void Special_chars_are_neutralized_by_quoting()
    {
        Assert.Equal("\"he\"\"llo\"", FtsQuery.Build("he\"llo"));         // embedded quote doubled
        Assert.Equal("\"a-b*c\"", FtsQuery.Build("a-b*c"));               // FTS operators inert inside quotes
        Assert.Equal("\"col:umn\"", FtsQuery.Build("col:umn"));
    }

    [Fact]
    public void All_short_or_empty_returns_null_for_like_fallback()
    {
        Assert.Null(FtsQuery.Build("a bb c"));
        Assert.Null(FtsQuery.Build("   "));
        Assert.Null(FtsQuery.Build(null));
        Assert.Null(FtsQuery.Build("灵台")); // 2 CJK chars — below the trigram minimum
    }

    [Fact]
    public void Cjk_token_of_three_chars_is_kept()
    {
        Assert.Equal("\"灵台上\"", FtsQuery.Build("灵台上"));
    }

    /// <summary><b>A Chinese phrase must become an OR of trigrams, not one exact-substring phrase.</b>
    /// <para>This is the defect the whole CJK story turns on. Chinese has no spaces, so a whole sentence
    /// arrives as ONE whitespace token. Emitting it as a single quoted phrase means FTS matches only an entry
    /// containing that entire substring — while the identical English query, which DOES have spaces, becomes
    /// an OR of its words and matches on any one of them. Same store, same index, wildly different recall,
    /// decided purely by whether the language uses spaces.</para>
    /// <para>The index is <c>trigram</c>, so character trigrams are exactly the unit it stores: expanding a
    /// CJK run into its sliding trigrams gives Chinese the same partial-match behaviour English already had.
    /// </para></summary>
    [Fact]
    public void A_cjk_phrase_becomes_an_OR_of_trigrams_not_one_exact_phrase()
    {
        // 配偶是爱丽丝 — "the spouse is Alice"
        var built = FtsQuery.Build("配偶是爱丽丝");

        Assert.NotNull(built);
        Assert.Contains(" OR ", built);
        Assert.Contains("\"配偶是\"", built);   // leading trigram
        Assert.Contains("\"是爱丽\"", built);   // interior trigram
        Assert.Contains("\"爱丽丝\"", built);   // trailing trigram — the NAME, which is what has to be findable
    }

    /// <summary>The point of the trigram expansion, stated as the behaviour a consumer would notice: a query
    /// that overlaps a stored phrase only PARTIALLY still produces a term the stored text contains, so the
    /// entry is findable. Before this, the query had to contain the stored substring in full — the two
    /// sentences below share no such substring in either direction and would have matched nothing.</summary>
    [Fact]
    public void A_partially_overlapping_cjk_query_still_yields_a_term_the_stored_text_contains()
    {
        const string stored = "我的配偶是爱丽丝";        // "my spouse is Alice"
        var built = FtsQuery.Build("爱丽丝是谁");        // "who is Alice" — overlaps only on the name

        Assert.NotNull(built);
        Assert.False(stored.Contains("爱丽丝是谁", StringComparison.Ordinal));  // neither contains the other
        var terms = built.Split(" OR ").Select(t => t.Trim('"')).ToList();
        Assert.Contains(terms, t => stored.Contains(t, StringComparison.Ordinal));
    }

    /// <summary><b>The floor, pinned so it is not mistaken for a bug in this class.</b> A trigram index
    /// cannot match an overlap shorter than three characters — <c>配偶叫什么</c> and <c>配偶是爱丽丝</c> share
    /// only <c>配偶</c>, so every trigram of one is absent from the other. That is the tokenizer's property,
    /// not this builder's, and raising it would mean a different index.
    /// <para>Nothing is silently lost: a MATCH returning zero rows falls through to the caller's LIKE
    /// substring scan (see <c>SqliteMemoryStore.RecallAsync</c>), and a CJK query too short to yield any
    /// trigram at all returns null, which takes the same path.</para></summary>
    [Fact]
    public void A_two_character_cjk_overlap_is_below_the_trigram_floor()
    {
        const string stored = "我的配偶是爱丽丝";
        var built = FtsQuery.Build("配偶叫什么");

        Assert.NotNull(built);
        var terms = built.Split(" OR ").Select(t => t.Trim('"')).ToList();
        Assert.DoesNotContain(terms, t => stored.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>Mixed scripts: the ASCII half keeps whole-word matching (more precise than its trigrams),
    /// while the CJK half is expanded. Pinned because treating the whole query as one script would silently
    /// regress one of the two.</summary>
    [Fact]
    public void A_mixed_script_query_keeps_words_for_ascii_and_trigrams_for_cjk()
    {
        var built = FtsQuery.Build("deploy 部署管道");

        Assert.NotNull(built);
        Assert.Contains("\"deploy\"", built);   // whole word, not trigrams
        Assert.Contains("\"部署管\"", built);
        Assert.Contains("\"署管道\"", built);
    }
}
