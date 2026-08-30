using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>The review log (2026-08-11, fsrs-properly plan Task 3) end to end through
/// <see cref="GraphMemoryEngine"/>: one row per reinforcement, bounded by default, opt-out rather than
/// opt-in, best-effort at a STRICTER grain than the reinforcement it logs, and — the property this file
/// argues hardest for — provably inert data.
/// <para><b>SQLite, never <c>InMemoryMemoryGraphStore</c></b> (<c>.claude/knowledge/pitfalls.md</c>): every
/// fact whose subject is recall or touch runs here over a real per-test SQLite database. The one exception
/// is the best-effort fact below, which uses a literal-substring query by construction — the documented
/// carve-out for the in-process store's own contiguous-substring matching.</para></summary>
public class GraphMemoryReviewLogTests
{
    /// <summary>An undamped per-write age policy, matching every other recall-quality fact in this tree, so
    /// ages advance deterministically by counting rather than by wall-clock burst damping.</summary>
    private static GraphMemoryEngine Engine(IMemoryGraphStore store, GraphMemoryOptions? options = null) =>
        new("e", store, options, agePolicies: [new PerWriteAgePolicy()]);

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

    /// <summary>THE fact requirement 1 of the task brief asks for hardest: the grade recorded is the one
    /// Reinforce ACTUALLY used, computed independently here from the raw formula
    /// (<c>g = 2 + 2·r</c>, documented on <see cref="DsrRetrievability"/>'s own class doc) against the pre-
    /// reinforcement state — never by calling <see cref="IMemoryRetrievabilityPolicy.DerivedGrade"/> itself,
    /// so this test does not just check that production code agrees with itself.
    /// <para><b>Mutation-checked live</b>: temporarily made <c>GraphMemoryEngine.ReinforceAsync</c> log
    /// <c>_policy.DerivedGrade(reinforced)</c> — the POST-reinforcement state — instead of <c>pre</c>. This
    /// fact failed (logged grade no longer matched the independently-computed pre-state expectation).
    /// Reverted; re-ran; passes again. See the task report for the exact numbers.</para></summary>
    [Fact]
    public async Task Recall_logs_the_grade_Reinforce_actually_used_from_the_pre_reinforcement_state()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = Engine(store);
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "graded on recall"));
        const int crowd = 12;
        await Crowd(engine, crowd);

        await engine.RecallAsync(new MemoryQuery("t", "s", "graded"));

        var row = Assert.Single(await store.ReviewsAsync("e"));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);
        Assert.Equal(id, row.NodeId);
        Assert.Equal(crowd, row.PreAge, precision: 6);
        Assert.Equal(20, row.PreStability, precision: 6); // DsrOptions.InitialStability's own default
        Assert.Equal(5, row.PreDifficulty, precision: 6); // neutral (mid-point, corrected 2026-08-11) — never judged

        // independently: retrievability at the PRE-reinforcement state, then the documented g = 2 + 2r
        var policy = new DsrRetrievability();
        var preState = new MemoryDecayState(Age: crowd, RecallCount: 0, Stability: 20);
        var expectedGrade = 2 + 2 * policy.Retrievability(preState);

        Assert.NotNull(row.ReviewGrade);
        Assert.Equal(expectedGrade, row.ReviewGrade!.Value, precision: 6);

        // and the post columns are the state Reinforce actually returned, never shortened
        var expectedPost = policy.Reinforce(preState);
        Assert.Equal(expectedPost.Stability, row.PostStability, precision: 6);
        Assert.Equal(expectedPost.Difficulty, row.PostDifficulty, precision: 6);
    }

    /// <summary>A same-position review (no intervening write — an immediate re-recall) is the one case
    /// <see cref="DsrRetrievability.Reinforce"/> itself skips the grade-driven update for (the Δt=0 branch,
    /// fix round 1 I1). The log must say so honestly: <c>ReviewGrade</c> null, not a synthetic "Easy" value a
    /// naive re-derivation from <c>r=1</c> would produce.</summary>
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

    /// <summary>Requirement 3 of the task brief, and the one to prove hardest: DATA, not a decision. Two
    /// otherwise-identical databases, one with wildly divergent rows sitting in <c>lyntai_memory_review</c>
    /// before anything that matters runs — if anything in <see cref="GraphMemoryEngine"/>'s recall, ranking,
    /// retrievability OR PRUNING path read that table, these numbers would have to move the result. They
    /// cannot move it: nothing reads it.
    /// <para><b>Pruning gets its own target and its own comparison, not a re-use of the recall one (fix
    /// round 1, reviewer I1).</b> <c>PruneAsync</c>'s derivable-age branch happens to call the SAME private
    /// <c>Retrievability(GraphNode)</c> helper <c>RecallAsync</c> uses for candidate scoring, so a leak
    /// written into THAT shared helper is already caught by the recall half above — but a defect written
    /// directly into <c>PruneAsync</c>'s own body (its <c>Where</c> predicate, its
    /// <c>HasUnknownStrengthUnit</c> guard, its doomed-id selection) would not be, and pruning is the one
    /// path here that DELETES. <b>"a fact worth pruning" is a SEPARATE entry from "a stable fact", never
    /// recalled</b> — <c>RecallAsync</c> reinforces whatever it returns, which would reset an entry's age to
    /// zero and make it un-prunable if the recall proof and the prune proof shared one target.
    /// <c>Crowd(200)</c> pushes "a fact worth pruning" comfortably below a 0.3 floor while "a fact just
    /// written", written afterward at age 0, stays comfortably above it, so exactly one of the three entries
    /// in scope is doomed in both runs — a trivial "nothing ever gets pruned" outcome would not actually
    /// exercise the comparison.</para>
    /// <para><b>Mutation-checked live, inside <c>PruneAsync</c>'s own body specifically</b> (not the shared
    /// helper, which the recall half already covers): added a private <c>PruneReviewFactor(GraphNode)</c>
    /// called ONLY from <c>PruneAsync</c>'s doomed-selection predicate, doubling a polluted node's effective
    /// retrievability there. This fact failed —
    /// <c>Assert.Equal() Failure: Expected: 1, Actual: 0</c> on <c>Pruned</c> (the polluted run's inflated
    /// retrievability pulled "a fact worth pruning" back above the floor, so nothing was removed) — while
    /// the pre-existing recall assertions above it kept passing throughout, confirming the mutation was
    /// confined to the prune path and not merely a re-trip of the already-covered shared helper. Reverted;
    /// re-ran; passes again. See the task report for the exact numbers.</para></summary>
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
            var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "a stable fact"));
            await Crowd(engine, 30);

            // a SEPARATE entry for the pruning half, deliberately never recalled: RecallAsync reinforces
            // whatever it returns, which would reset THIS entry's age to zero if it were the same one the
            // recall query below touches — the two halves need independent targets or the recall proof
            // would silently erase the prune proof's own setup.
            var pruneTarget = await engine.RememberAsync(new MemoryWrite("t", "s", "a fact worth pruning"));
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

    /// <summary>Best-effort at a STRICTER grain than reinforcement itself (Task 3): a broken review log must
    /// cost neither the caller's hits nor the learning that already succeeded above it in
    /// <c>ReinforceAsync</c>. A literal-substring query by construction, so the in-process store's own
    /// contiguous-substring matching — the reason every OTHER fact in this file runs on SQLite — is not the
    /// carve-out this test is exploiting; it is exactly the documented exception
    /// (<c>.claude/knowledge/pitfalls.md</c>: "InMemory is fine ... when the query is a literal substring by
    /// construction").
    /// <para><b>Mutation-checked live, and the first attempt taught something worth recording.</b> Removing
    /// the inner <c>try/catch</c> around <c>store.RecordReviewsAsync</c> in <c>GraphMemoryEngine.ReinforceAsync</c>
    /// (leaving only the outer one) did NOT fail a version of this fact that asserted only stability growth:
    /// <c>TouchAsync</c> is <c>await</c>ed and fully committed BEFORE the log write ever runs, so by the time
    /// the log throws, the touch has already landed — the outer catch alone was enough to save that
    /// particular effect, and the hits, from that specific mutation. What the missing inner catch ACTUALLY
    /// costs is the CO-ACTIVATION loop, which sits AFTER the log write inside the SAME try block: with no
    /// inner catch, the log's exception skips straight past it to the outer catch, so two nodes reinforced
    /// together in the same recall never get linked. This fact was rewritten to reinforce TWO nodes at once
    /// and assert the resulting edge, which the mutation DOES fail:
    /// <c>Assert.True(afterItems[0].Degree &gt; 0, ...)</c> — under the mutation, <c>Degree</c> read <c>0</c>
    /// for both entries (no edge formed); with the inner catch restored, it reads <c>1</c>. Reverted; re-ran;
    /// passes again. See the task report for the exact numbers.</para>
    /// </summary>
    [Fact]
    public async Task A_broken_review_log_costs_neither_the_hits_the_learning_nor_co_activation()
    {
        var engine = new GraphMemoryEngine("project/graph", new ReviewLogHostileGraphStore(),
            agePolicies: [new PerWriteAgePolicy()]);
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced despite a broken log alpha"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced despite a broken log beta"));

        // no query text: both come back together, so the co-activation loop actually has a pair to link
        var first = await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10));
        Assert.Equal(2, first.Items.Count); // the hits, despite a log write that always throws

        await Crowd(engine, 30);
        var afterItems = (await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10))).Items;

        // the learning: the same bound GraphMemoryEngineTests.Recall_reinforces_what_it_returned pins for a
        // healthy log — 30 events against a 20-event half-life is r≈0.35 unreinforced; the first recall's
        // touch, which must have succeeded despite its log write failing, pushes both back above that
        Assert.All(afterItems, item => Assert.True(item.Retrievability > 0.4,
            $"reinforcement did not extend the half-life despite the broken log (r={item.Retrievability})"));

        // co-activation: the two entries reinforced together in the FIRST recall must have linked, despite
        // the broken log sitting between the touch and the co-activation loop in ReinforceAsync's try block
        Assert.True(afterItems[0].Degree > 0, "co-activation did not link the two entries despite the broken log");
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
