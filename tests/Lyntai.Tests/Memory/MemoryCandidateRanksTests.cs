using Lyntai.Memory;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;

namespace Lyntai.Tests.Memory;

public class MemoryCandidateRanksTests
{
    private static GraphNode Node(long id) => new(
        id, "e", "task", "scope", "head", "content", MemoryGrade.Associative,
        DateTimeOffset.UnixEpoch, 0, 1, 0, 0.5, 0, null);

    // The whole point of declaring Ranks outside the primary constructor: every existing
    // caller keeps compiling and keeps deconstructing into three.
    [Fact]
    public void The_three_argument_shape_is_unchanged_and_defaults_to_no_ranks()
    {
        var candidate = new MemoryCandidate(Node(1), 0.5, 0);
        var (node, retrievability, hop) = candidate;

        Assert.Equal(1, node.Id);
        Assert.Equal(0.5, retrievability);
        Assert.Equal(0, hop);
        Assert.True(candidate.Ranks.IsEmpty);
    }

    [Fact]
    public void Ranks_is_carried_through_a_with_expression()
    {
        var ranks = new MemorySeedRanks([new MemorySeedRank("lexical", 2)]);
        var candidate = new MemoryCandidate(Node(1), 0.5, 0) with { Ranks = ranks };

        Assert.Equal(ranks, candidate.Ranks);
        Assert.True(candidate.Ranks.TryGet("lexical", out var rank));
        Assert.Equal(2, rank);
    }

    // Guards Task 1's content equality from the consumer's side: without it these compare
    // unequal on the array reference and every candidate comparison in the suite shifts.
    [Fact]
    public void Two_candidates_with_equal_ranks_are_equal()
    {
        var a = new MemoryCandidate(Node(1), 0.5, 0) with { Ranks = new([new MemorySeedRank("lexical", 2)]) };
        var b = new MemoryCandidate(Node(1), 0.5, 0) with { Ranks = new([new MemorySeedRank("lexical", 2)]) };

        Assert.Equal(a, b);
    }
}
