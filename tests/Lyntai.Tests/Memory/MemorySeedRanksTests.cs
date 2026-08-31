using Lyntai.Memory.Seeding;

namespace Lyntai.Tests.Memory;

public class MemorySeedRanksTests
{
    [Fact]
    public void Default_is_empty_and_equals_Empty()
    {
        MemorySeedRanks fromDefault = default;

        Assert.Equal(0, fromDefault.Count);
        Assert.True(fromDefault.IsEmpty);
        Assert.Equal(MemorySeedRanks.Empty, fromDefault);
        Assert.Equal(0, fromDefault.Span.Length);
    }

    [Fact]
    public void TryGet_finds_a_source_by_name_and_reports_absence()
    {
        var ranks = new MemorySeedRanks([new MemorySeedRank("lexical", 3), new MemorySeedRank("semantic", 1)]);

        Assert.True(ranks.TryGet("semantic", out var semantic));
        Assert.Equal(1, semantic);
        Assert.True(ranks.TryGet("lexical", out var lexical));
        Assert.Equal(3, lexical);
        Assert.False(ranks.TryGet("subject", out var missing));
        Assert.Equal(0, missing);
        Assert.Equal(2, ranks.Count);
    }

    // The load-bearing one. A struct over an array gets REFERENCE equality by default, and
    // MemoryCandidate is a record struct whose generated Equals would inherit it — so two
    // candidates identical in every value would compare unequal. Nothing else in the subsystem
    // would report that; it would surface as a mysteriously failing existing test.
    [Fact]
    public void Equality_is_by_CONTENT_not_by_array_reference()
    {
        var a = new MemorySeedRanks([new MemorySeedRank("lexical", 3), new MemorySeedRank("semantic", 1)]);
        var b = new MemorySeedRanks([new MemorySeedRank("lexical", 3), new MemorySeedRank("semantic", 1)]);
        var c = new MemorySeedRanks([new MemorySeedRank("lexical", 4), new MemorySeedRank("semantic", 1)]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void A_null_or_empty_sequence_yields_Empty_rather_than_throwing()
    {
        Assert.Equal(MemorySeedRanks.Empty, new MemorySeedRanks(null!));
        Assert.Equal(MemorySeedRanks.Empty, new MemorySeedRanks([]));
    }
}
