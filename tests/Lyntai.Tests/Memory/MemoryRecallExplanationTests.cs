using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A recall can EXPLAIN itself</b> (<c>MemoryQuery.Explain</c>, <c>MemoryRecall.Candidates</c>): every
/// candidate the engine considered, which channel found it at what rank, its hop, its score and why it was
/// cut — so "never a candidate" is told apart from "ranked out of the limit".
/// <para>On SQLite, because the in-memory store reports a flat relevance and so no channel rank to observe
/// (<c>pitfalls.md</c> §Storage). Each arm runs on its own database: a recall mutates.</para>
/// </summary>
public sealed class MemoryRecallExplanationTests : IDisposable
{
    private const string Query = "deployment note";

    private static readonly string[] Facts =
    [
        "deployment note one covers response caching",
        "deployment note two covers index rebuilds",
        "deployment note three covers schema migrations",
        "deployment note four covers rollback drills",
    ];

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static GraphMemoryEngine Engine(TempDb db, string name = "explain", GraphMemoryOptions? options = null,
        IMemoryVerificationPolicy? verifier = null, IMemoryRankingPolicy? ranking = null) =>
        new(name, new SqliteMemoryGraphStore(db.Factory), options: options, seams: new GraphMemorySeams
        {
            AgePolicies = [new PerWriteAgePolicy()],
            Retrievability = new DsrRetrievability(),
            Ranking = ranking ?? new ReciprocalRankFusionPolicy(),
            Verification = verifier,
        });

    private static async Task<GraphMemoryEngine> SeededAsync(TempDb db, string name = "explain",
        GraphMemoryOptions? options = null, IMemoryVerificationPolicy? verifier = null,
        IMemoryRankingPolicy? ranking = null)
    {
        var engine = Engine(db, name, options, verifier, ranking);
        foreach (var fact in Facts) await engine.RememberAsync(new MemoryWrite("t", "s", fact));
        return engine;
    }

    /// <summary>Drops the candidate carrying one headline and ranks the rest as the default does.</summary>
    private sealed class DropsHeadline(string headline) : IMemoryRankingPolicy
    {
        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context) =>
            new ReciprocalRankFusionPolicy().Rank(
                [.. candidates.Where(c => !string.Equals(c.Node.Headline, headline, StringComparison.Ordinal))],
                context);
    }

    [Fact]
    public async Task Off_by_default_and_nothing_is_attached()
    {
        var engine = await SeededAsync(_db);

        var query = new MemoryQuery("t", "s", Query, Limit: 2);
        var recall = await engine.RecallAsync(query);

        Assert.False(query.Explain);
        Assert.NotEmpty(recall.Items);
        Assert.Null(recall.Candidates);
    }

    [Fact]
    public async Task Explaining_does_not_change_what_a_recall_returns()
    {
        using var other = new TempDb();
        var plain = await (await SeededAsync(_db)).RecallAsync(new MemoryQuery("t", "s", Query, Limit: 2));
        var explained = await (await SeededAsync(other))
            .RecallAsync(new MemoryQuery("t", "s", Query, Limit: 2) { Explain = true });

        Assert.Equal(plain.Items.Select(i => i.Headline), explained.Items.Select(i => i.Headline));
        Assert.Equal(plain.Ran, explained.Ran);
        Assert.Equal(plain.Answered, explained.Answered);
    }

    [Fact]
    public async Task A_candidate_ranked_out_of_the_limit_is_reported_cut_by_the_limit()
    {
        var engine = await SeededAsync(_db);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 2) { Explain = true });

        var candidates = Assert.IsAssignableFrom<IReadOnlyList<MemoryRecallCandidate>>(recall.Candidates);
        Assert.Equal(Facts.Length, candidates.Count);
        Assert.Equal(2, candidates.Count(c => c.Outcome == MemoryCandidateOutcome.Returned));

        var cut = candidates.Where(c => c.Outcome == MemoryCandidateOutcome.CutByLimit).ToList();
        Assert.Equal(2, cut.Count);
        Assert.Equal([3, 4], cut.Select(c => c.Position));
        Assert.All(cut, c => Assert.True(c.Ranks.TryGet("lexical", out _)));   // FOUND, then outranked
        Assert.All(cut, c => Assert.DoesNotContain(recall.Items, i => i.Reference == c.Reference));
    }

    [Fact]
    public async Task A_returned_item_carries_its_channel_rank_hop_score_and_position()
    {
        var engine = await SeededAsync(_db);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 2) { Explain = true });

        var returned = recall.Candidates!.Where(c => c.Outcome == MemoryCandidateOutcome.Returned).ToList();
        Assert.Equal(recall.Items.Select(i => i.Reference), returned.Select(c => c.Reference));
        Assert.Equal([1, 2], returned.Select(c => c.Position));
        Assert.All(returned, c =>
        {
            Assert.True(c.Ranks.TryGet("lexical", out var rank));
            Assert.True(rank >= 1);
            Assert.Equal(0, c.Hop);
            Assert.NotNull(c.Score);
            Assert.Null(c.Verified);   // no verifier registered
        });
    }

    [Fact]
    public async Task A_hop_neighbour_reports_its_hop_and_no_channel()
    {
        var engine = await SeededAsync(_db);
        var stranger = await engine.RememberAsync(new MemoryWrite("t", "s", "an unrelated remark about lunch"));
        var anchor = (await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 1))).Items[0].Reference;
        await engine.LinkAsync(anchor, stranger.Reference, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 10) { Explain = true });

        var neighbour = Assert.Single(recall.Candidates!, c => c.Reference == stranger.Reference);
        Assert.Equal(1, neighbour.Hop);
        Assert.True(neighbour.Ranks.IsEmpty);
    }

    [Fact]
    public async Task A_candidate_the_policy_floored_is_reported_floored_with_no_score()
    {
        var engine = await SeededAsync(_db, ranking: new DropsHeadline(Facts[0]));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 10) { Explain = true });

        var floored = Assert.Single(recall.Candidates!, c => c.Outcome == MemoryCandidateOutcome.Floored);
        Assert.Null(floored.Score);
        Assert.Null(floored.Position);
        Assert.DoesNotContain(recall.Items, i => i.Reference == floored.Reference);
    }

    [Fact]
    public async Task A_verifier_removal_and_its_verdicts_are_reported()
    {
        var engine = await SeededAsync(_db, options: new GraphMemoryOptions { VerificationFilters = true },
            verifier: new VerdictOrderingFixture.Endorses(Facts[2]));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 10) { Explain = true });

        var endorsed = Assert.Single(recall.Candidates!, c => c.Verified == true);
        Assert.Equal(MemoryCandidateOutcome.Returned, endorsed.Outcome);
        var refused = recall.Candidates!.Where(c => c.Verified == false).ToList();
        Assert.Equal(Facts.Length - 1, refused.Count);
        Assert.All(refused, c => Assert.Equal(MemoryCandidateOutcome.FilteredByVerifier, c.Outcome));
    }

    [Fact]
    public async Task A_candidate_the_character_budget_cut_is_reported_cut_by_budget()
    {
        var engine = await SeededAsync(_db);

        // the first item is always kept; every headline here is longer than what the budget leaves after it
        var recall = await engine.RecallAsync(
            new MemoryQuery("t", "s", Query, Limit: 10, CharBudget: 50) { Explain = true });

        Assert.Single(recall.Items);
        Assert.Equal(Facts.Length - 1,
            recall.Candidates!.Count(c => c.Outcome == MemoryCandidateOutcome.CutByBudget));
    }

    [Fact]
    public async Task An_empty_recall_still_explains_itself_as_an_empty_list()
    {
        var engine = Engine(_db);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query) { Explain = true });

        Assert.Empty(recall.Items);
        Assert.NotNull(recall.Candidates);
        Assert.Empty(recall.Candidates);
    }

    [Fact]
    public async Task A_blend_carries_its_members_candidates_and_reports_its_own_cut()
    {
        var a = await SeededAsync(_db, "blend/a");
        var b = await SeededAsync(_db, "blend/b");
        var blend = new CompositeMemoryEngine("blend", [a, b]);

        var recall = await blend.RecallAsync(new MemoryQuery("t", "s", Query, Limit: 2) { Explain = true });

        var candidates = recall.Candidates!;
        Assert.Equal(2 * Facts.Length, candidates.Count);
        Assert.Equal(Facts.Length, candidates.Count(c => c.Reference.Engine == "blend/a"));
        Assert.Equal(Facts.Length, candidates.Count(c => c.Reference.Engine == "blend/b"));

        // each member returned two, the blend kept two: the other two are the BLEND's limit cut
        var returned = candidates.Where(c => c.Outcome == MemoryCandidateOutcome.Returned).ToList();
        Assert.Equal(recall.Items.Select(i => i.Reference).OrderBy(r => r.ToString()),
            returned.Select(c => c.Reference).OrderBy(r => r.ToString()));
        Assert.Equal(2 * Facts.Length - 2, candidates.Count(c => c.Outcome == MemoryCandidateOutcome.CutByLimit));
    }

    [Fact]
    public async Task A_blend_whose_members_cannot_explain_attaches_nothing()
    {
        var item = new MemoryItem(new MemoryRef("blend/x", "1"), "h", "h", MemoryGrade.Associative, 1, 1, 0);
        var blend = new CompositeMemoryEngine("blend", [new StaticEngine("blend/x", [item])]);

        var recall = await blend.RecallAsync(new MemoryQuery("t", Query: "h") { Explain = true });

        Assert.Single(recall.Items);
        Assert.Null(recall.Candidates);
    }
}
