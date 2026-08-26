using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>"Decay buries, it does not cut"</b> — design §5.7.0's standing constraint, asserted from the two
/// directions its existing coverage does not reach.
///
/// <para><c>DsrPathologyTests.A_written_entry_is_never_permanently_unreachable_after_a_realistic_session</c>
/// already proves a faded entry is reachable by a TARGETED QUERY. Both facts here are about what happens
/// when that is not true: an entry so faint that ordinary recall no longer returns it. Burial is acceptable
/// and is the whole model; the question is whether anything was LOST.</para>
///
/// <para><b>Both carry a vacuity guard, and they need one.</b> "The entry survived heavy decay" passes
/// trivially if the decay never happened, and an instrument that cannot observe the condition it claims to
/// test reads as coverage while proving nothing. Each fact therefore asserts the entry is genuinely faint
/// FIRST, and fails with that number in the message when it is not.</para>
/// </summary>
public class MemoryBurialNotDeletionTests
{
    private const string Engine = "burial";

    /// <summary>Enough unrelated writes to bury a fact well below the shipped prune floor, and few enough
    /// that the suite stays fast. <b>SCALE is not this file's subject</b> —
    /// <c>node devtools/dev.mjs memory-scale</c> is where store behaviour at 1k/10k/100k is measured. What
    /// is asserted here is the INVARIANT, which does not become more true at a larger number.</summary>
    private const int Noise = 600;

    private static GraphMemoryEngine NewEngine(TempDb db) =>
        new(Engine, new SqliteMemoryGraphStore(db.Factory),
            agePolicies: [new PerWriteAgePolicy()], retrievability: new DsrRetrievability());

    /// <summary>A second engine over the SAME store that reads without mutating — the vacuity guards use it
    /// instead of the writing engine.
    /// <para><b>An ordinary recall RESETS the age of everything it returns</b>
    /// (<c>IMemoryEngine.RecallAsync</c>: "a recall MUTATES"), so measuring faintness with one would
    /// un-fade the very entry being checked and leave the fact proving less than it claims. Reinforcement
    /// and co-activation are both off here, which makes the read a pure observation.</para></summary>
    private static GraphMemoryEngine Probe(TempDb db) =>
        new(Engine, new SqliteMemoryGraphStore(db.Factory),
            new GraphMemoryOptions
            {
                ReinforceOn = MemoryReinforcementActs.None,
                CoActivationCap = 0,
                LogReviews = false,
            },
            agePolicies: [new PerWriteAgePolicy()], retrievability: new DsrRetrievability());

    private static async Task CrowdAsync(GraphMemoryEngine engine, int writes)
    {
        for (var i = 0; i < writes; i++)
            await engine.RememberAsync(new MemoryWrite("t", "noise", $"unrelated filler number {i}"));
    }

    [Fact]
    public async Task A_never_recalled_rare_fact_stays_EXPANDABLE_however_faint()
    {
        // THE PROMISE A PRODUCT CLAIM RESTS ON: low retrievability is not absence. An application that kept
        // a MemoryRef -- from a citation, a bookmark, a link in its own data -- must still be able to open
        // that entry in full, whatever the ranking has since decided about it.
        //
        // Expansion is a DIFFERENT path from the covered one: GetAsync by id, with no query, no ranking and
        // no faintness bound anywhere in it. A fact reachable by a targeted query says nothing about a fact
        // nothing queries at all, which is exactly the "critical rare fact" case.
        using var db = new TempDb();
        var engine = NewEngine(db);

        var reference = await engine.RememberAsync(
            new MemoryWrite("t", "s", "the recovery key is written on the blue card in the safe"));
        await CrowdAsync(engine, Noise);

        // VACUITY GUARD: it must genuinely have faded, or this proves nothing. Measured through an ordinary
        // recall, which is the only public reading of retrievability.
        var probe = await Probe(db).RecallAsync(new MemoryQuery("t", "s", "recovery key", Limit: 5));
        var faint = probe.Items.SingleOrDefault(i => i.Reference.Id == reference.Id);
        Assert.True(faint is null || faint.Retrievability < 0.5,
            $"the entry never faded, so this fact is vacuous; retrievability was {faint?.Retrievability}");

        var expanded = await engine.ExpandAsync(reference);

        var self = Assert.Single(expanded.Items, i => i.Reference.Id == reference.Id);
        Assert.Equal("the recovery key is written on the blue card in the safe", self.Content);
    }

    [Fact]
    public async Task Decay_removes_no_ROW_so_the_scope_still_holds_every_write()
    {
        // "Canonical evidence loss = 0" under decay, stated as a count rather than as a spot check on one
        // entry. Nothing in this subsystem deletes as a side effect: removal is PruneAsync or ForgetAsync
        // and both are explicit calls. This is what makes that a property of the STORE rather than a habit
        // of the engine.
        //
        // Read through SeedAsync with no query, which enumerates rather than retrieves and applies no
        // faintness bound of its own -- so a decayed row is counted like any other, which is the point.
        using var db = new TempDb();
        var engine = NewEngine(db);
        var store = new SqliteMemoryGraphStore(db.Factory);

        const int facts = 25;
        for (var i = 0; i < facts; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"fact number {i} about the deployment"));

        var before = await store.SeedAsync(Engine, "t", "s", null, int.MaxValue);
        Assert.Equal(facts, before.Count);

        await CrowdAsync(engine, Noise);

        var after = await store.SeedAsync(Engine, "t", "s", null, int.MaxValue);
        Assert.Equal(facts, after.Count);

        // VACUITY GUARD, on the same shape as the fact above: the count is only interesting if the entries
        // actually aged. Retrievability is computed at READ time, so a stored row that has faded is exactly
        // what "buried, not cut" describes -- and if nothing faded, this fact is counting a corpus that
        // never decayed.
        var recall = await Probe(db).RecallAsync(new MemoryQuery("t", "s", "deployment", Limit: facts));
        Assert.All(recall.Items, i => Assert.True(i.Retrievability < 1.0,
            $"nothing decayed, so the count above is vacuous; retrievability was {i.Retrievability}"));
    }
}
