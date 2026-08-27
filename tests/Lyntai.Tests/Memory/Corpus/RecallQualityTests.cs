namespace Lyntai.Tests.Memory.Corpus;

/// <summary>
/// Pins <see cref="RecallQuality.Measure"/> against HAND-COMPUTED expected values — never against whatever
/// the implementation currently returns. Every fact's arithmetic is written into its own comment so a
/// reader can check the expected number without running anything; if a hand-derivation ever disagreed with
/// the code, the hand-derivation is the specification, not the implementation.
/// <para>The first three facts are canonical — the ones a reader opens to learn what the two metrics mean.
/// <see cref="A_perfect_recall_scores_zero_on_both_metrics"/> is a genuine identity, its symmetry inherent
/// rather than accidental. The other two are deliberately ASYMMETRIC and bracket the definition from both
/// directions (high miss / zero pollution, then zero miss / high pollution) — a fixture pair with EQUAL
/// values for both metrics cannot tell a reader (or a mutation that swaps the two) which number is which;
/// found the hard way in this task's own mutation round, when the brief's original second example (miss
/// 0.5, pollution 0.5) let a swap of the two metrics pass undetected. The next four pin the "awkward case"
/// choices <see cref="RecallQuality"/>'s own XML docs make explicit — they are the actual point of this
/// instrument, not edge-case padding, because a sweep (Task 3) compares numbers ACROSS corpus shapes, and
/// corpus shapes in this harness routinely land in one of these four regimes (critical-rare's two-query
/// ground truth against a wide <c>limit</c>; a broad recall's relevant set smaller than a generous
/// <c>CandidateCount</c>; a hot-ephemeral query outside its window).</para>
/// </summary>
public class RecallQualityTests
{
    [Fact]
    public void A_perfect_recall_scores_zero_on_both_metrics()
    {
        var q = RecallQuality.Measure(recalled: ["a", "b", "c"], relevant: ["a", "b", "c"], limit: 3);

        Assert.Equal(0, q.MissRate, precision: 9);
        Assert.Equal(0, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void Most_of_a_large_relevant_set_never_arriving_still_scores_zero_pollution_when_everything_returned_was_on_target()
    {
        // relevant = {a, b, c, d, e, f, g, h, i, j} (10 entries); the caller asked for only 2, and both of
        // the 2 that came back were genuinely relevant — no stranger took a seat.
        // miss = 8/10 = 0.8 (c..j, eight of ten relevant ids, never fit in a 2-item page);
        // pollution = 0/2 = 0 (both recalled slots were relevant; nothing to pollute with).
        // HIGH miss, ZERO pollution — a reader must be able to tell these two numbers apart at a glance.
        var q = RecallQuality.Measure(
            recalled: ["a", "b"],
            relevant: ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"],
            limit: 2);

        Assert.Equal(0.8, q.MissRate, precision: 9);
        Assert.Equal(0, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void A_caller_asking_for_more_than_the_relevant_set_can_fill_misses_nothing_but_pollutes_most_of_the_page()
    {
        // relevant = {a, b}; the caller asked for 8, both relevant entries arrived, and six strangers
        // (x1..x6) filled the rest of the page because the relevant set could not.
        // miss = 0/2 = 0 (both a and b arrived — nothing relevant was left behind);
        // pollution = 6/8 = 0.75 (x1..x6 occupy six of the eight slots).
        // ZERO miss, HIGH pollution — the mirror image of the fact above; together the pair brackets the
        // definition from both directions instead of one symmetric example that cannot discriminate which
        // metric is which.
        var q = RecallQuality.Measure(
            recalled: ["a", "b", "x1", "x2", "x3", "x4", "x5", "x6"],
            relevant: ["a", "b"],
            limit: 8);

        Assert.Equal(0, q.MissRate, precision: 9);
        Assert.Equal(0.75, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void A_relevant_set_larger_than_limit_cannot_reach_zero_miss_rate()
    {
        // relevant = {a, b, c, d, e} (5 entries); the page can hold only 3, and even the BEST possible
        // page — all 3 slots filled with relevant entries, none wasted on a stranger — still leaves d and e
        // unrecalled purely because there was no room.
        // miss = 2/5 = 0.4 (d, e never fit); pollution = 0/3 = 0 (every recalled slot was in fact relevant).
        var q = RecallQuality.Measure(
            recalled: ["a", "b", "c"],
            relevant: ["a", "b", "c", "d", "e"],
            limit: 3);

        Assert.Equal(0.4, q.MissRate, precision: 9);
        Assert.Equal(0, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void A_relevant_set_smaller_than_limit_makes_pollution_unavoidable_once_the_page_fills()
    {
        // relevant = {a, b}; the caller asked for 5 and a real ranked recall fills the page regardless of
        // how few relevant candidates exist — both relevant entries arrived, plus three strangers occupying
        // the slots relevant couldn't fill.
        // miss = 0/2 = 0 (both a and b arrived — a perfect policy on the relevant material);
        // pollution = 3/5 = 0.6 (x, y, z occupy 3 of the 5 slots) — the unavoidable floor
        // (limit - |relevant|) / limit = (5 - 2) / 5 = 0.6 for THIS page size, reached even though the
        // policy recalled everything there was to recall.
        var q = RecallQuality.Measure(
            recalled: ["a", "b", "x", "y", "z"],
            relevant: ["a", "b"],
            limit: 5);

        Assert.Equal(0, q.MissRate, precision: 9);
        Assert.Equal(0.6, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void An_empty_recall_misses_everything_and_pollutes_nothing()
    {
        // relevant = {a, b, c}; nothing came back at all.
        // miss = 3/3 = 1.0 (all three absent); pollution = 0/5 = 0 (no slot was occupied by anything, so no
        // slot can be a stranger — the denominator is `limit`, so this is never an undefined 0/0).
        var q = RecallQuality.Measure(recalled: [], relevant: ["a", "b", "c"], limit: 5);

        Assert.Equal(1.0, q.MissRate, precision: 9);
        Assert.Equal(0, q.PollutionRate, precision: 9);
    }

    [Fact]
    public void An_empty_relevant_set_has_no_misses_and_all_recalled_items_count_as_pollution()
    {
        // relevant = {} (this query's window has closed — the hot-ephemeral corpus class reaches this
        // legitimately, per Task 1's report); two items still came back, against a limit equal to how many
        // came back so the arithmetic reads cleanly.
        // miss = 0 BY CONVENTION (nothing was ever relevant, so nothing can be missed — the alternative,
        // 0/0, is undefined and never evaluated);
        // pollution = 2/2 = 1.0, because with no relevant set at all EVERY recalled item is by definition
        // outside it — "anything returned" IS the pollution, so this rate is not suppressed to 0 the way
        // miss rate is above; the two conventions are asymmetric on purpose (see RecallQuality's own docs).
        var q = RecallQuality.Measure(recalled: ["x", "y"], relevant: [], limit: 2);

        Assert.Equal(0, q.MissRate, precision: 9);
        Assert.Equal(1.0, q.PollutionRate, precision: 9);
    }
}

/// <summary>
/// Pins <see cref="RecallQuality.Measure"/>'s <c>supportNeeded</c> parameter — the n-of scoring a FREQUENCY
/// question needs, since neither one episode nor the full relevant set can answer "what do I usually eat".
/// </summary>
public class RecallQualityNOfTests
{
    private static readonly string[] Routine =
        ["routine0", "routine1", "routine2", "routine3", "routine4", "routine5"];

    [Fact]
    public void SupportNeeded_zero_is_the_STRICT_behaviour_that_shipped()
    {
        // The default must reproduce all-of scoring exactly, or every published figure moves.
        var strict = RecallQuality.Measure(["routine0", "routine1"], Routine, limit: 10);
        var explicitly = RecallQuality.Measure(["routine0", "routine1"], Routine, limit: 10, supportNeeded: 0);

        Assert.Equal(4.0 / 6.0, strict.MissRate, precision: 9);
        Assert.Equal(strict, explicitly);
    }

    [Fact]
    public void A_single_member_alone_is_credited_but_never_scores_as_a_complete_answer()
    {
        // The owner's objection, as a fact: "on Tuesday I had noodles" is not "I usually have noodles". This
        // pins the BOUNDARY rather than repeating the exact fraction — that number
        // (Below_the_threshold_misses_in_PROPORTION_to_what_is_missing computes it as (3 - 1) / 3) belongs to
        // the sibling test, which is where a reader goes to see the arithmetic. What this test alone asserts:
        // one member is never a complete answer (nonzero miss) and the model is proportional, not a cliff, so
        // it is credited rather than scored as a total miss (less than 1.0).
        var quality = RecallQuality.Measure(["routine0"], Routine, limit: 10, supportNeeded: 3);

        Assert.True(quality.MissRate > 0.0, "one member alone must not score as a complete answer");
        Assert.True(quality.MissRate < 1.0, "one member alone must still be credited, not scored as a total miss");
    }

    [Fact]
    public void ENOUGH_members_answer_it_completely()
    {
        // Reaching the threshold is a full answer, not a partial one — the question was "what do you
        // usually eat", and three observations of the same routine answer it. Returning more adds nothing.
        var atThreshold = RecallQuality.Measure(["routine0", "routine1", "routine2"], Routine, limit: 10, supportNeeded: 3);
        var beyond = RecallQuality.Measure(["routine0", "routine1", "routine2", "routine3"], Routine, limit: 10, supportNeeded: 3);

        Assert.Equal(0.0, atThreshold.MissRate, precision: 9);
        Assert.Equal(0.0, beyond.MissRate, precision: 9);
    }

    [Fact]
    public void Below_the_threshold_misses_in_PROPORTION_to_what_is_missing()
    {
        // Not a cliff. Two of three needed is a better answer than one of three, and a metric that could not
        // say so would rank a nearly-sufficient recall with a useless one.
        var one = RecallQuality.Measure(["routine0"], Routine, limit: 10, supportNeeded: 3);
        var two = RecallQuality.Measure(["routine0", "routine1"], Routine, limit: 10, supportNeeded: 3);

        Assert.Equal(2.0 / 3.0, one.MissRate, precision: 9);
        Assert.Equal(1.0 / 3.0, two.MissRate, precision: 9);
    }

    [Fact]
    public void Pollution_is_unchanged_by_the_threshold()
    {
        // SupportNeeded says how much of the relevant set constitutes an ANSWER. It says nothing about what
        // may occupy the window, so the pollution convention is untouched.
        var quality = RecallQuality.Measure(["routine0", "noise7"], Routine, limit: 4, supportNeeded: 3);

        Assert.Equal(1.0 / 4.0, quality.PollutionRate, precision: 9);
    }

    [Fact]
    public void A_threshold_larger_than_the_relevant_set_is_clamped_to_it()
    {
        // A caller asking for more support than exists must not make the query unanswerable — that would be a
        // fixture bug scoring as a system failure, which is the expensive direction.
        var quality = RecallQuality.Measure(Routine, Routine, limit: 10, supportNeeded: 99);

        Assert.Equal(0.0, quality.MissRate, precision: 9);
    }

    [Fact]
    public void An_EMPTY_relevant_set_needs_no_support_however_much_was_asked_for()
    {
        // The other end of the same clamp, and the one that scores PERFECTLY while looking like a result:
        // relevant = {} clamps needed to 0, so miss = 0 for any recall at all — including the empty one
        // below, which returned nothing. That is the all-of branch's own empty-set convention (nothing was
        // ever relevant, so nothing can be missed) and the threshold must not change it into 3/3 = 1.0.
        // Pinned because CorpusQuery blesses an empty RelevantIds, so a future class whose relevant set can
        // close — the hot-ephemeral shape — could set SupportNeeded on a step that reaches here.
        var quality = RecallQuality.Measure(recalled: [], relevant: [], limit: 5, supportNeeded: 3);

        Assert.Equal(0.0, quality.MissRate, precision: 9);
        Assert.Equal(0.0, quality.PollutionRate, precision: 9);
    }
}
