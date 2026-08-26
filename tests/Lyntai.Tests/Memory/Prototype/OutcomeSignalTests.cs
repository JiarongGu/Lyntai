using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>Does the observable design §5.7.0 calls "the unlock" already exist?</b> That section names the one
/// missing input this subsystem keeps reducing to — <i>this library observes no signal for whether a recall
/// was CORRECT</i> — and says what would supply it: a consumer-supplied rating, <b>or an observation of
/// material the application expected and did not get</b>. The proposal's outcome recorder is that second
/// half. Before designing one, this establishes what the shipped review log can and cannot already say.
///
/// <para><b>The answer is that access is open and the SHAPE is the blocker</b>, which is not what "the log
/// cannot record misses" would lead you to expect, and it changes what an outcome API has to be. The three
/// facts below are the evidence; none of them proposes surface.</para>
///
/// <para><b>Deliberately no weighting ladder, no scoring, no recorder interface.</b> The proposal ranks
/// recall &lt; expansion &lt; citation &lt; successful action &lt; user confirmation, and every one of those
/// weights is a number nothing here can validate — this repository refused exactly that kind of fitting
/// against an invented corpus twice (<c>docs/DECISIONS.md</c> D49, D51). Establishing the gap is
/// answerable now; pricing the signal is not, and mixing them would produce a plausible API resting on
/// invented constants.</para>
/// </summary>
public class OutcomeSignalTests
{
    private const string Engine = "outcome";

    private static (GraphMemoryEngine Engine, IMemoryGraphStore Store) Build()
    {
        var store = new InMemoryMemoryGraphStore();
        return (new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()]), store);
    }

    [Fact]
    public async Task A_recall_logs_only_what_it_RETURNED_so_a_miss_has_no_row_by_construction()
    {
        // THE STRUCTURAL FACT, and the reason a miss signal cannot come from the engine. The log is written
        // from the entries a recall handed back; an entry it failed to return is not in that set, so no row
        // exists for it and none ever will, however the ranking changes. That is what makes the miss
        // signal something only an APPLICATION can supply -- it is the one party that knows what it wanted.
        // BOTH entries must be CANDIDATES, or this passes for the wrong reason: with only one match the log
        // would contain one row whatever the rule is, and the test would prove nothing. They share the
        // query token and the limit is 1, so exactly one of two reachable entries comes back.
        var (engine, store) = Build();
        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "deployment step one needs approval"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "deployment step two needs review"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deployment", Limit: 1));
        var returned = Assert.Single(recall.Items);

        var candidates = new[] { long.Parse(first.Id), long.Parse(second.Id) };
        var logged = (await store.ReviewsAsync(Engine)).Select(r => r.NodeId).Distinct().ToList();

        // one of the two was reachable and is simply absent from the log — which is the miss, unrecorded
        Assert.Equal([long.Parse(returned.Reference.Id)], logged);
        Assert.Single(candidates.Except(logged));
    }

    [Fact]
    public async Task The_review_log_ACCEPTS_a_row_for_an_entry_no_recall_returned()
    {
        // So the gap is not permission. IMemoryGraphStore.RecordReviewsAsync is public, takes an arbitrary
        // NodeId, and neither relational backend puts a foreign key on the review table's node_id -- unlike
        // the subject table beside it, which cascades. An application could write this row today.
        //
        // Observed here rather than pinned as a CONTRACT fact on purpose: asserting it across backends would
        // promise that a review row need not reference a live node, and whether reviews should cascade is a
        // decision nobody has taken. This says what is true now, not what must stay true.
        var (engine, store) = Build();
        var kept = await engine.RememberAsync(new MemoryWrite("t", "s", "the fact the app expected"));
        var id = long.Parse(kept.Id);

        await store.RecordReviewsAsync(Engine,
            [new MemoryReviewWrite(id, Guid.NewGuid(), PreAge: 0, PreStability: 0, PreDifficulty: 0,
                PreStrength: 0, PreStrengthAge: 0, Grade: null, PostStability: 0, PostDifficulty: 0,
                Verified: false)],
            cap: 1000);

        Assert.Contains(await store.ReviewsAsync(Engine), r => r.NodeId == id && r.Verified == false);
    }

    [Fact]
    public async Task An_app_reported_MISS_is_indistinguishable_from_a_judge_REJECTION_in_the_log()
    {
        // THE SHAPE BLOCKER, demonstrated rather than argued. Verified is the log's only non-curve column,
        // and it already means one specific thing: "the recall RETURNED this and a judge said it did not
        // answer". Reusing it to mean "the app wanted this and never got it" writes two different
        // observations into one column -- and every other field is curve state a miss has no value for, so
        // there is nothing else to tell them apart by.
        //
        // A fitter reading this log would treat an application's miss report as a judge's rejection of a
        // returned entry. Those are opposite observations: one says the ranking FAILED to surface the
        // entry, the other says it surfaced it wrongly.
        var (engine, store) = Build();
        var a = long.Parse((await engine.RememberAsync(new MemoryWrite("t", "s", "first fact"))).Id);
        var b = long.Parse((await engine.RememberAsync(new MemoryWrite("t", "s", "second fact"))).Id);

        var batch = Guid.NewGuid();
        await store.RecordReviewsAsync(Engine,
        [
            // a judge REJECTED this returned entry
            new MemoryReviewWrite(a, batch, 1, 7, 5, 0, 0, Grade: 3, PostStability: 7, PostDifficulty: 5,
                Verified: false),
            // an application reports it EXPECTED this one and never saw it — the only column that can carry
            // a negative is the same one, and the curve fields are meaningless here
            new MemoryReviewWrite(b, batch, 0, 0, 0, 0, 0, Grade: null, PostStability: 0, PostDifficulty: 0,
                Verified: false),
        ], cap: 1000);

        var rows = (await store.ReviewsAsync(Engine)).Where(r => r.BatchId == batch).ToList();
        Assert.Equal(2, rows.Count);

        // Both are Verified == false, and no column says which kind of false it is.
        //
        // A null Grade is not the discriminator it looks like: MemoryReviewWrite.Grade documents null as a
        // legitimate value for a GENUINE review — the same-position/session-burst branch, where no
        // grade-driven update ran — so reading "Grade is null" as "this was not a review" would misread
        // real rows. The zeroed curve columns are a legal state too, for a review of a brand-new entry.
        // Every candidate discriminator is a convention the schema does not carry.
        Assert.All(rows, r => Assert.False(r.Verified));
        Assert.Equal(rows[0].Verified, rows[1].Verified);
    }
}
