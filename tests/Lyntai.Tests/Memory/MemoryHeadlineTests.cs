using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Where a derived headline is cut when no space in the budget's latter half offers a word boundary.
/// The engine and the judge each pin the ordinary case; these are the edges no caller's text reaches.</summary>
public class MemoryHeadlineTests
{
    [Fact]
    public void A_space_in_the_latter_half_is_still_the_cut() =>
        Assert.Equal("abcdef ghij…", MemoryHeadline.Derive("abcdef ghij klmnop", 12));

    [Fact]
    public void A_combined_character_is_not_split_at_the_budget() =>
        Assert.Equal("abcd…", MemoryHeadline.Derive("abcdéfgh", 5));

    [Fact]
    public void A_first_element_longer_than_the_budget_is_cut_at_a_code_point() =>
        Assert.Equal("é́…", MemoryHeadline.Derive("é́́́ tail", 3));

    [Fact]
    public void An_astral_first_character_outrunning_the_budget_is_kept_whole() =>
        Assert.Equal("😀…", MemoryHeadline.Derive("😀😀😀", 1));
}
