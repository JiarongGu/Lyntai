using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class MemoryContentKeyTests
{
    [Fact]
    public void A_lone_surrogate_and_the_replacement_character_share_a_key()
    {
        // the hash is over UTF-8, and UTF-8 has no spelling for a lone surrogate but U+FFFD, which is also what
        // a store holds for it; built from the code point so the source carries no replacement character
        var replacement = char.ConvertFromUtf32(0xFFFD);

        Assert.Equal(MemoryContentKey.Of("a" + replacement), MemoryContentKey.Of("a\uD800"));
        Assert.Equal(MemoryContentKey.Of("a\uD800"), MemoryContentKey.Of("a\uDC00"));
    }

    [Fact]
    public void Content_differing_only_in_whitespace_or_case_keeps_its_own_key()
    {
        Assert.NotEqual(MemoryContentKey.Of("a"), MemoryContentKey.Of("a "));
        Assert.NotEqual(MemoryContentKey.Of("a"), MemoryContentKey.Of("A"));
    }

    [Fact]
    public void A_surrogate_pair_is_one_character_and_keeps_its_own_key()
    {
        Assert.NotEqual(MemoryContentKey.Of("a\U0001F600"), MemoryContentKey.Of("a\uD83D"));
    }
}
