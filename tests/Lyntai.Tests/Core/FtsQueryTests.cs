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
}
