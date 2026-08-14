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
}
