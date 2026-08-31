using Lyntai.Memory;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;

namespace Lyntai.Tests.Memory;

public class ReciprocalRankFusionSourceTests
{
    private static GraphNode Node(long id, double relevance, bool? matched = true) => new(
        id, "e", "task", "scope", $"head{id}", $"content{id}", MemoryGrade.Associative,
        DateTimeOffset.UnixEpoch, 0, 1, 0, relevance, 0, null, Matched: matched);

    private static MemoryCandidate Candidate(long id, double relevance, double retrievability,
        params MemorySeedRank[] ranks) =>
        new MemoryCandidate(Node(id, relevance), retrievability, 0) with { Ranks = new(ranks) };

    private static readonly MemoryRankingContext Ctx = new(20, "test");

    // THE HEADLINE FACT — the one that would have caught this bug.
    // Same evidence reachable both ways. Node 2 is a semantic top hit carrying a COSINE of 0.74;
    // node 1 is a mediocre lexical hit carrying a RANK POSITION of 0.90. On the pooled field the
    // rank position wins on scale alone, whatever the semantics say. Per source, the semantic
    // top hit (rank 1) beats the lexical also-ran (rank 9).
    [Fact]
    public void A_semantic_top_hit_is_not_outranked_by_a_lexical_also_ran_on_SCALE_alone()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            RetrievabilityWeight = 0,
            HopWeight = 0,
        });

        var lexicalAlsoRan = Candidate(1, relevance: 0.90, retrievability: 0.5,
            new MemorySeedRank("lexical", 9));
        var semanticTopHit = Candidate(2, relevance: 0.74, retrievability: 0.5,
            new MemorySeedRank("semantic", 1));

        var ranked = policy.Rank([lexicalAlsoRan, semanticTopHit], Ctx);

        Assert.Equal(2, ranked[0].Candidate.Node.Id);
    }

    [Fact]
    public void A_candidate_two_sources_agree_on_outranks_one_only_a_single_source_found()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            RetrievabilityWeight = 0,
            HopWeight = 0,
        });

        var oneSource = Candidate(1, 0.5, 0.5, new MemorySeedRank("lexical", 1));
        var bothAgree = Candidate(2, 0.5, 0.5,
            new MemorySeedRank("lexical", 1), new MemorySeedRank("semantic", 1));

        var ranked = policy.Rank([oneSource, bothAgree], Ctx);

        Assert.Equal(2, ranked[0].Candidate.Node.Id);
    }

    // Empty ranks means "no relevance evidence", NOT "ranked worst". A hop neighbour beside a
    // ranked seed must contribute no relevance term rather than sorting to the bottom of one.
    // With relevance the only live signal, the seed leads and the neighbour still survives.
    [Fact]
    public void A_candidate_no_source_matched_contributes_no_relevance_term_and_is_not_dropped()
    {
        var policy = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
        {
            RetrievabilityWeight = 0,
            HopWeight = 0,
        });

        var hopNeighbour = Candidate(1, 0, 0.9);                                  // no ranks
        var seed = Candidate(2, 0.5, 0.1, new MemorySeedRank("lexical", 1));

        var ranked = policy.Rank([hopNeighbour, seed], Ctx);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(2, ranked[0].Candidate.Node.Id);
        Assert.Equal(1, ranked[1].Candidate.Node.Id);
    }

    // THE COMPATIBILITY CONTROL. Mutation-checked in step 4 — a guard that cannot observe the
    // thing it guards reads as coverage (pitfalls.md §Testing).
    [Fact]
    public void With_no_candidate_carrying_ranks_the_ordering_is_the_pooled_relevance_one()
    {
        var policy = new ReciprocalRankFusionPolicy();

        // Ordered so that ONLY the pooled Relevance field can produce this answer:
        // retrievability is deliberately inverted against it.
        var weak = new MemoryCandidate(Node(1, relevance: 0.20), 0.9, 0);
        var strong = new MemoryCandidate(Node(2, relevance: 0.95), 0.1, 0);

        var ranked = policy.Rank([weak, strong], Ctx);

        Assert.Equal(2, ranked[0].Candidate.Node.Id);
        Assert.Equal(1, ranked[1].Candidate.Node.Id);
    }
}
