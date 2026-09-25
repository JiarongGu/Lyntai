using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>The review log end to end through <see cref="GraphMemoryEngine"/>: one row per reinforcement,
/// bounded by default, opt-out rather than opt-in, best-effort at a STRICTER grain than the reinforcement it
/// logs, and — the property this file argues hardest for — provably inert data.
/// <para>SQLite by default, because the no-feedback fact compares RANKED results and only a relational store
/// ranks (<c>.claude/knowledge/pitfalls.md</c>); the best-effort fact needs a hostile in-process double and a
/// query-less recall.</para></summary>
public class GraphMemoryReviewLogTests
{
    /// <summary>An undamped per-write age policy, matching every other recall-quality fact in this tree, so
    /// ages advance deterministically by counting rather than by wall-clock burst damping.</summary>
    private static GraphMemoryEngine Engine(IMemoryGraphStore store, GraphMemoryOptions? options = null,
        IMemoryRetrievabilityPolicy? retrievability = null) =>
        new("e", store, options, seams: new GraphMemorySeams
            {
                Retrievability = retrievability,
                AgePolicies = [new PerWriteAgePolicy()],
            });

    /// <summary>Make everything already stored older by writing unrelated material — the only thing that
    /// ages a memory in this model.</summary>
    private static async Task Crowd(GraphMemoryEngine engine, int writes)
    {
        for (var i = 0; i < writes; i++)
            await engine.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler number {i}"));
    }

    [Fact]
    public async Task Reviews_are_logged_by_default()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store);
        await engine.RememberAsync(new MemoryWrite("t", "s", "logged by default"));
        await Crowd(engine, 5);

        await engine.RecallAsync(new MemoryQuery("t", "s", "default"));

        Assert.NotEmpty(await store.ReviewsAsync("e"));
    }

    /// <summary>Opt-out, not opt-in (design spec §3): setting <see cref="GraphMemoryOptions.LogReviews"/> to
    /// false must skip the write entirely, not merely discard it afterward.</summary>
    [Fact]
    public async Task No_reviews_are_logged_when_opted_out()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store, new GraphMemoryOptions { LogReviews = false });
        await engine.RememberAsync(new MemoryWrite("t", "s", "never logged"));
        await Crowd(engine, 5);

        await engine.RecallAsync(new MemoryQuery("t", "s", "logged"));

        Assert.Empty(await store.ReviewsAsync("e"));
    }

    /// <summary>The grade recorded is the one Reinforce ACTUALLY used, computed independently here from the
    /// raw formula (<c>g = 2 + 2·r</c>, documented on <see cref="DsrRetrievability"/>'s own class doc) against
    /// the pre-reinforcement state — never by calling <see cref="IMemoryRetrievabilityPolicy.DerivedGrade"/>
    /// itself, so this test does not just check that production code agrees with itself.
    /// <para><b>Growth is switched on</b> (<c>ReinforceGain = 2.0</c>): at the shipped gain of 0 the reinforced
    /// state differs from <c>pre</c> only in difficulty, which the grade does not read, so logging
    /// <c>DerivedGrade(reinforced)</c> instead of <c>DerivedGrade(pre)</c> would be undetectable.</para></summary>
    [Fact]
    public async Task Recall_logs_the_grade_Reinforce_actually_used_from_the_pre_reinforcement_state()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var policy = new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 });
        var engine = Engine(store, retrievability: policy);
        var reference = (await engine.RememberAsync(new MemoryWrite("t", "s", "graded on recall"))).Reference;
        const int crowd = 12;
        await Crowd(engine, crowd);

        await engine.RecallAsync(new MemoryQuery("t", "s", "graded"));

        var row = Assert.Single(await store.ReviewsAsync("e"));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);
        Assert.Equal(id, row.NodeId);
        Assert.Equal(crowd, row.PreAge, precision: 6);
        Assert.Equal(20, row.PreStability, precision: 6); // DsrOptions.InitialStability's own default
        Assert.Equal(5, row.PreDifficulty, precision: 6); // neutral mid-point — never judged

        // independently: retrievability at the PRE-reinforcement state, then the documented g = 2 + 2r
        var preState = new MemoryDecayState(Age: crowd, RecallCount: 0, Stability: 20);
        var expectedGrade = 2 + 2 * policy.Retrievability(preState);

        Assert.NotNull(row.ReviewGrade);
        Assert.Equal(expectedGrade, row.ReviewGrade!.Value, precision: 6);

        // and the post columns are the state Reinforce actually returned — grown, so the two states differ
        var expectedPost = policy.Reinforce(preState);
        Assert.True(expectedPost.Stability > preState.Stability, "growth must be on, or pre and post coincide");
        Assert.Equal(expectedPost.Stability, row.PostStability, precision: 6);
        Assert.Equal(expectedPost.Difficulty, row.PostDifficulty, precision: 6);
    }

    /// <summary>A same-position review (no intervening write — an immediate re-recall) is the one case
    /// <see cref="DsrRetrievability.Reinforce"/> itself skips the grade-driven update for (the Δt=0 branch).
    /// The log must say so honestly: <c>ReviewGrade</c> null, not a synthetic "Easy" value a naive
    /// re-derivation from <c>r=1</c> would produce.</summary>
    [Fact]
    public async Task A_session_burst_with_no_intervening_write_logs_a_null_grade()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store);
        await engine.RememberAsync(new MemoryWrite("t", "s", "recalled twice in a row"));

        // the SAME position both times — nothing writes in between
        await engine.RecallAsync(new MemoryQuery("t", "s", "twice"));
        await engine.RecallAsync(new MemoryQuery("t", "s", "twice"));

        var rows = await store.ReviewsAsync("e");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.ReviewGrade));
    }

    /// <summary>The review log is DATA, not a decision. Two otherwise-identical databases, one with wildly
    /// divergent rows sitting in <c>lyntai_memory_review</c> before anything that matters runs — if anything
    /// in <see cref="GraphMemoryEngine"/>'s recall, ranking, retrievability OR PRUNING path read that table,
    /// these numbers would have to move the result. They cannot move it: nothing reads it.
    /// <para><b>Pruning gets its own target and its own comparison.</b> A defect written into
    /// <c>PruneAsync</c>'s own body (its predicate, its doomed-id selection) would not reach the recall
    /// half, and pruning is the one path here that DELETES. "a fact worth pruning" is never recalled, because
    /// a recall resets the age of what it returns and would make it un-prunable. Exactly one of the three
    /// entries is doomed in both runs, so "nothing ever gets pruned" cannot pass.</para></summary>
    [Fact]
    public async Task The_review_log_never_feeds_recall_ranking_or_pruning()
    {
        async Task<(MemoryRecall Recall, int Pruned, List<string> Survivors)> RunAsync(bool pollute)
        {
            using var db = new TempDb();
            var store = new SqliteMemoryGraphStore(db.Factory);
            // PerWriteAgePolicy is Derivable (IMemoryAgePolicy.Kind), so PruneAsync takes its derivable-age
            // branch — the one this fact is about — rather than the cheap store-side ratio path.
            var engine = Engine(store);
            var reference = (await engine.RememberAsync(new MemoryWrite("t", "s", "a stable fact"))).Reference;
            await Crowd(engine, 30);

            // a SEPARATE entry for the pruning half, deliberately never recalled: RecallAsync reinforces
            // whatever it returns, which would reset THIS entry's age to zero if it were the same one the
            // recall query below touches — the two halves need independent targets or the recall proof
            // would silently erase the prune proof's own setup.
            var pruneTarget = (await engine.RememberAsync(new MemoryWrite("t", "s", "a fact worth pruning"))).Reference;
            await Crowd(engine, 200); // ages it well below the 0.3 floor used below
            await engine.RememberAsync(new MemoryWrite("t", "s", "a fact just written")); // fresh, stays above it

            if (pollute)
            {
                // wildly divergent from anything either entry's real state could ever produce — if recall
                // or pruning read this table at all, these numbers would have to move the result below
                MemoryReviewWrite Bogus(long id) => new(id, Guid.NewGuid(), PreAge: 999_999,
                    PreStability: 0.0001, PreDifficulty: 10, PreStrength: 500, PreStrengthAge: 500, ReviewGrade: 2,
                    PostStability: 0.0001, PostDifficulty: 10);
                var refId = long.Parse(reference.Id, CultureInfo.InvariantCulture);
                var targetId = long.Parse(pruneTarget.Id, CultureInfo.InvariantCulture);
                await store.RecordReviewsAsync("e", [Bogus(refId), Bogus(targetId), Bogus(refId)], cap: 1000);
            }

            // touches ONLY "a stable fact" (the query matches nothing else) — the ranking/retrievability half
            var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "stable"));
            // scans the WHOLE scope — "a stable fact" is now reinforced (retrievability 1, never doomed),
            // "a fact worth pruning" never was (still faint), "a fact just written" is simply fresh
            var pruned = await engine.PruneAsync("t", "s", minRetrievability: 0.3);
            var survivors = (await store.SeedAsync("e", "t", "s", null, 100))
                .Select(n => n.Content).OrderBy(c => c, StringComparer.Ordinal).ToList();

            return (recall, pruned, survivors);
        }

        var clean = await RunAsync(pollute: false);
        var polluted = await RunAsync(pollute: true);

        var cleanItem = Assert.Single(clean.Recall.Items);
        var pollutedItem = Assert.Single(polluted.Recall.Items);
        Assert.Equal(clean.Recall.Ran, polluted.Recall.Ran);
        Assert.Equal(cleanItem.Headline, pollutedItem.Headline);
        Assert.Equal(cleanItem.Grade, pollutedItem.Grade);
        Assert.Equal(cleanItem.Relevance, pollutedItem.Relevance, precision: 12);
        Assert.Equal(cleanItem.Retrievability, pollutedItem.Retrievability, precision: 12);
        Assert.Equal(cleanItem.Degree, pollutedItem.Degree);

        // pruning: the same proof, for the path that DELETES rather than merely ranks
        Assert.Equal(1, clean.Pruned); // sanity: the comparison below is meaningless if nothing was ever doomed
        Assert.Equal(clean.Pruned, polluted.Pruned);
        Assert.Equal(["a fact just written", "a stable fact"], clean.Survivors);
        Assert.Equal(clean.Survivors, polluted.Survivors);
    }

    /// <summary>One recall reinforcing several candidates at once shares a single <c>BatchId</c> across
    /// every row it logs — <see cref="GraphMemoryEngine.RecallAsync"/>'s own remarks on why a fitter may
    /// care that these co-occurred.</summary>
    [Fact]
    public async Task A_single_recalls_reinforcements_share_one_batch_id()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store);
        await engine.RememberAsync(new MemoryWrite("t", "s", "shared batch alpha"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "shared batch beta"));
        await Crowd(engine, 5);

        // no query text: takes the most-recent branch, which returns both candidates and reinforces both
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10));
        Assert.Equal(2, recall.Items.Count);

        var rows = await store.ReviewsAsync("e");
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].BatchId, rows[1].BatchId);
    }

    /// <summary>Best-effort at a STRICTER grain than reinforcement itself: a broken review log must cost
    /// neither the caller's hits, nor the touch, nor the co-activation edges. It holds because
    /// <see cref="IMemoryGraphStore.WriteBackAsync"/> writes the review log LAST (D101), so a reordering that
    /// logged first fails the age and degree assertions below. A query-less recall, so the in-process store
    /// needs no term matching.
    /// <para>The entries are crowded BEFORE the first recall: at the shipped gain of 0 the touch's whole effect
    /// is the age reset, and a just-written entry has no age to reset.</para></summary>
    [Fact]
    public async Task A_broken_review_log_costs_neither_the_hits_the_learning_nor_co_activation()
    {
        var engine = new GraphMemoryEngine("project/graph", new ReviewLogHostileGraphStore(), seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced despite a broken log alpha"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced despite a broken log beta"));
        await Crowd(engine, 30);

        // no query text: both come back together, so the co-activation loop actually has a pair to link
        var first = await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10));
        Assert.Equal(2, first.Items.Count); // the hits, despite a log write that always throws
        Assert.All(first.Items, item => Assert.True(item.Retrievability < 0.5,
            $"the entries must have aged before the touch, or the reset below proves nothing (r={item.Retrievability})"));

        var afterItems = (await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10))).Items;
        Assert.Equal(2, afterItems.Count);

        // the learning: the first recall's touch landed despite its log write failing, so both are fresh again
        Assert.All(afterItems, item => Assert.Equal(1.0, item.Retrievability, precision: 9));

        // co-activation: the two entries reinforced together in the FIRST recall must have linked
        Assert.All(afterItems, item => Assert.True(item.Degree > 0,
            "co-activation did not link the two entries despite the broken log"));
    }

    /// <summary>The eviction cap, wired end to end through the engine's own options rather than called
    /// directly on the store — <see cref="MemoryGraphStoreContract.RecordReviewsAsync_evicts_down_to_the_cap"/>
    /// pins the store's own mechanism in isolation; this pins that <c>GraphMemoryEngine</c> actually PASSES
    /// <see cref="GraphMemoryOptions.ReviewLogCap"/> through rather than some other value.</summary>
    [Fact]
    public async Task The_engines_own_cap_option_bounds_the_log()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store, new GraphMemoryOptions { ReviewLogCap = 3 });
        await engine.RememberAsync(new MemoryWrite("t", "s", "capped"));

        // ten separate recalls, one review logged each time (Age > 0 every time thanks to the intervening
        // filler write, so none of them collapse into the Δt=0/no-write burst case)
        for (var i = 0; i < 10; i++)
        {
            await engine.RememberAsync(new MemoryWrite("t", "filler", $"filler {i}"));
            await engine.RecallAsync(new MemoryQuery("t", "s", "capped"));
        }

        var rows = await store.ReviewsAsync("e");
        // one row per recall (ten total), a cap of 3 whose own TrimInterval floors at 1 (Max(1, 3/10)), so
        // the log never exceeds the cap even transiently — an exact count, not merely a bound
        Assert.Equal(3, rows.Count);
    }
}
