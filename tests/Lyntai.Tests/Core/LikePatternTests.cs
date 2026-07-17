using Lyntai.Storage;

namespace Lyntai.Tests.Core;

public class LikePatternTests
{
    [Fact]
    public void Contains_wraps_and_trims()
    {
        Assert.Equal("%hello%", LikePattern.Contains("  hello  "));
    }

    [Theory]
    [InlineData("50%", "50\\%")]           // percent escaped
    [InlineData("a_b", "a\\_b")]           // underscore escaped
    [InlineData("c:\\path", "c:\\\\path")] // backslash escaped first
    [InlineData("plain", "plain")]
    public void Escape_neutralizes_wildcards(string input, string expected)
    {
        Assert.Equal(expected, LikePattern.Escape(input));
    }

    [Fact]
    public void Contains_escapes_within_the_wildcards()
    {
        // a literal % in the term must not become a wildcard
        Assert.Equal("%100\\% sure%", LikePattern.Contains("100% sure"));
    }
}
