using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The ONE subject normalization, tested where every clause is observable.
/// <para><see cref="MemoryGraphStoreContract.Subjects_match_case_insensitively"/> pins the clauses a STORE
/// can be seen to honour (case, padding); de-duplication is invisible through that contract on every
/// shipped backend, so it is pinned here on the rule itself rather than faked into a store assertion that
/// could not fail.</para></summary>
public class MemorySubjectTests
{
    [Theory]
    [InlineData("Spouse", "spouse")]
    [InlineData("  Client  ", "client")]
    [InlineData("\tNORTHERN Logistics\n", "northern logistics")]
    [InlineData("配偶", "配偶")]           // no casing to fold; must survive unchanged
    public void Normalize_trims_and_lowercases_invariantly(string input, string expected) =>
        Assert.Equal(expected, MemorySubject.Normalize(input));

    /// <summary><b>INVARIANT casing, not the current culture's.</b> Under a Turkish culture
    /// <c>"I".ToLower()</c> is <c>"ı"</c> (dotless), so a backend reaching for the culture-sensitive
    /// overload would record a handle on one machine that a lookup on another could never match — a linking
    /// failure reproducing only under a locale nobody tests with. This is the fact that makes that
    /// unshippable rather than merely discouraged.</summary>
    [Fact]
    public void Normalize_folds_the_same_way_under_a_turkish_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal("istanbul client", MemorySubject.Normalize("ISTANBUL Client"));
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unusable_handle_normalizes_to_empty_and_reports_unusable(string? input)
    {
        Assert.False(MemorySubject.IsUsable(input));
        Assert.Equal(string.Empty, MemorySubject.Normalize(input));
    }

    [Fact]
    public void IsUsable_accepts_anything_with_content() => Assert.True(MemorySubject.IsUsable(" x "));

    /// <summary>Canonicalize drops the unusable, normalizes the rest, and collapses what normalization made
    /// identical — in FIRST-SEEN order, so a store's persisted set is a function of the annotation rather
    /// than of hash ordering.</summary>
    [Fact]
    public void Canonicalize_drops_blanks_normalizes_and_collapses_duplicates()
    {
        var result = MemorySubject.Canonicalize(["Owner", "  owner ", "", "  ", null!, "Client", "OWNER"]);

        Assert.Equal(["owner", "client"], result);
    }

    [Fact]
    public void Canonicalize_of_null_is_empty() => Assert.Empty(MemorySubject.Canonicalize(null));

    /// <summary><b>A handle in a space-writing script needs a word BOUNDARY.</b> <c>pairbond</c> sits inside
    /// <c>repairbonded</c>, and a bare substring test would seed a whole entity cluster off a query that
    /// mentions neither the entity nor anything like it — the one failure mode that cannot be undone by
    /// ranking, because the entries are genuinely about something else.</summary>
    [Theory]
    [InlineData("what does my spouse do", "spouse", true)]
    [InlineData("SPOUSE?", "spouse", true)]                   // both sides fold through Normalize
    [InlineData("spouse", "  Spouse  ", true)]                // …including the handle's own padding
    [InlineData("the repairbonded seam", "pairbond", false)]  // inside a longer word
    [InlineData("espouse the idea", "spouse", false)]         // …and at the front of one
    [InlineData("ask northern logistics", "northern logistics", true)]  // a multi-word handle
    public void A_spaced_handle_matches_only_on_a_word_boundary(string query, string subject, bool expected) =>
        Assert.Equal(expected, MemorySubject.Matches(query, subject));

    /// <summary><b>A handle in a spaceless script matches as a plain SUBSTRING</b>, because there is no
    /// boundary to anchor to: Chinese writes 配偶 straight against whatever precedes and follows it, so
    /// demanding a boundary would reject every real query. Read from
    /// <c>ScriptProfile.ExpandsIntoGrams</c> rather than from an is-this-ASCII test, so Thai and Khmer get
    /// the same treatment and Cyrillic — which writes spaces — does not.</summary>
    [Theory]
    [InlineData("配偶是谁", "配偶", true)]
    [InlineData("我的太太", "配偶", false)]                   // a real miss is still a miss
    [InlineData("배우자는 어디", "배우자", true)]              // Hangul writes spaces and is expanded anyway
    public void A_spaceless_handle_matches_as_a_substring(string query, string subject, bool expected) =>
        Assert.Equal(expected, MemorySubject.Matches(query, subject));

    [Theory]
    [InlineData(null, "spouse")]
    [InlineData("   ", "spouse")]
    [InlineData("what does my spouse do", null)]
    [InlineData("what does my spouse do", "  ")]
    public void Nothing_matches_an_empty_query_or_an_unusable_handle(string? query, string? subject) =>
        Assert.False(MemorySubject.Matches(query, subject));
}
