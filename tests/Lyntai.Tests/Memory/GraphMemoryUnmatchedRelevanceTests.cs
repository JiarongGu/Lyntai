using System.Globalization;

using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A candidate the query never matched must not claim a relevance it did not earn, and must not be
/// annihilated for lacking one.</b> Both halves are needed, and each one alone is a defect this repository
/// has actually shipped or nearly shipped.
///
/// <para><b>Half one, the shipped defect.</b> The row projections materialized every node with
/// <c>Relevance = 1</c> — the MAXIMUM — and only <c>SeedAsync</c> overwrote it. So a graph-walk neighbour,
/// or any node fetched by id for a semantic or subject seed, outranked every candidate that had actually
/// been scored. Measured on LoCoMo (<c>docs/memory.md</c> §5): evidence-hit@20 of 11.0% against 80.5% for
/// plain cosine, and <c>SemanticSeedK</c> worth exactly 0.0 points because a real 0.785 cosine could never
/// beat a fabricated 1.000.</para>
///
/// <para><b>Half two, the fix that was tried and refused.</b> Setting the literal to <c>0</c> nearly tripled
/// the LoCoMo figure and broke traversal instead: <see cref="MultiplicativeRankingPolicy"/> scores a PRODUCT,
/// so a zero annihilates a candidate rather than ranking it low, and the walked entries VANISHED from the
/// result. That is why <see cref="GraphNode.Matched"/> exists — a policy has to be able to tell "scored
/// zero" from "never asked", which no single <see cref="double"/> can express.</para>
///
/// <para>Runs against <see cref="InMemoryMemoryGraphStore"/> deliberately: the defect was identical in both
/// row projections, and the store contract holds the SQL twin to the same rule.</para>
/// </summary>
public sealed class GraphMemoryUnmatchedRelevanceTests
{
    private const string Task = "t";
    private const string Scope = "s";

    private static async Task<(InMemoryMemoryGraphStore Store, GraphMemoryEngine Engine, long Walked)>
        LinkedPairAsync(IMemoryRankingPolicy? ranking = null)
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store, ranking: ranking);

        await engine.RememberAsync(new MemoryWrite(Task, Scope, "alpha migration rollout begins"));
        await engine.RememberAsync(new MemoryWrite(Task, Scope, "unrelated note about the kitchen roster"));

        // The second entry is reachable ONLY by the walk: its text shares no term with the query below.
        var seeded = await store.SeedAsync("e", Task, Scope, query: null, limit: 10);
        var anchor = seeded.Single(n => n.Content.Contains("alpha", StringComparison.Ordinal)).Id;
        var walked = seeded.Single(n => n.Content.Contains("kitchen", StringComparison.Ordinal)).Id;
        await store.LinkAsync("e", anchor, walked, "similar", 1, symmetric: true);

        return (store, engine, walked);
    }

    /// <summary>The store must say "never asked" rather than inventing an answer. <c>null</c> is the third
    /// state <see cref="MemoryRelevance.ByRankPosition"/> already reads that way, and the one a walk needs.
    /// </summary>
    [Fact]
    public async Task A_node_reached_by_the_walk_reports_Matched_null_and_no_fabricated_relevance()
    {
        var (store, _, walked) = await LinkedPairAsync();

        var neighbours = await store.NeighboursAsync("e", Task,
            [(await store.SeedAsync("e", Task, Scope, "alpha", 10)).First().Id], 10);

        var node = neighbours.Select(n => n.Node).Single(n => n.Id == walked);
        Assert.Null(node.Matched);
        Assert.Equal(0, node.Relevance);
    }

    /// <summary>A matched candidate outranks an unmatched one. This is the half that was broken: the walked
    /// entry claimed <c>1</c> and won.</summary>
    [Fact]
    public async Task A_matched_entry_outranks_one_the_query_never_matched()
    {
        var (_, engine, walked) = await LinkedPairAsync();

        var recall = await engine.RecallAsync(new MemoryQuery(Task, Scope, "alpha", Limit: 10));

        Assert.NotEmpty(recall.Items);
        var order = recall.Items.Select(i => i.Reference.Id).ToList();
        var walkedAt = order.IndexOf(walked.ToString(CultureInfo.InvariantCulture));
        Assert.True(walkedAt != 0,
            "the entry the query never matched came back FIRST — it is claiming a relevance it did not earn");
    }

    /// <summary><b>The regression that the obvious fix would have introduced.</b> Under a multiplicative
    /// policy a relevance of 0 zeroes the whole product, so an unmatched candidate is not ranked low, it is
    /// deleted. Traversal is the feature that dies, and no ranking assertion would notice — only its
    /// ABSENCE from the result does.</summary>
    [Fact]
    public async Task A_walked_entry_survives_a_MULTIPLICATIVE_policy_rather_than_being_annihilated()
    {
        var (_, engine, walked) = await LinkedPairAsync(new MultiplicativeRankingPolicy());

        var recall = await engine.RecallAsync(new MemoryQuery(Task, Scope, "alpha", Limit: 10));

        Assert.Contains(walked.ToString(CultureInfo.InvariantCulture),
            recall.Items.Select(i => i.Reference.Id));
    }
}
