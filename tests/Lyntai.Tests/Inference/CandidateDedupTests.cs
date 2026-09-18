using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

public class CandidateDedupTests
{
    [Fact]
    public void Duplicate_primary_is_stripped_first_wins()
    {
        var deduped = CandidateDedup.Dedup([
            new ProviderCandidate("a", "m1"),
            new ProviderCandidate("b"),
            new ProviderCandidate("a", "m1"),
        ]);
        Assert.Equal([new ProviderCandidate("a", "m1"), new ProviderCandidate("b")], deduped);
    }

    [Fact]
    public void Same_provider_different_model_is_kept_and_order_preserved()
    {
        var deduped = CandidateDedup.Dedup([
            new ProviderCandidate("a", "m1"),
            new ProviderCandidate("a", "m2"),
            new ProviderCandidate("a"),
        ]);
        Assert.Equal(3, deduped.Count);
        Assert.Equal("m1", deduped[0].Model);
        Assert.Equal("m2", deduped[1].Model);
        Assert.Null(deduped[2].Model);
    }

    [Fact]
    public void Empty_in_empty_out()
    {
        Assert.Empty(CandidateDedup.Dedup([]));
    }
}
