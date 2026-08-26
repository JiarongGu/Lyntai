using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>What a RE-REMEMBER of unchanged content does to everything the caller did not restate.</b>
///
/// <para>An entry's identity is (engine, task, scope, content), so writing the same text twice refreshes one
/// row rather than making two. That single write path applies <b>four different update rules</b> to the
/// fields around the content, and until 2026-08-26 exactly one of them was asserted anywhere:</para>
///
/// <list type="bullet">
/// <item><c>Headline</c> and <c>Grade</c> are OVERWRITTEN from the incoming write.</item>
/// <item><c>Signals</c> (and salience, and its provenance) keep what is stored when the incoming bag is
/// EMPTY — "no opinion", because a salience policy may decline for reasons of its own.</item>
/// <item><c>Difficulty</c> is narrower still: overwritten only when the incoming bag NAMES a difficulty.</item>
/// <item><c>Stability</c>, <c>provenance_retrievability</c>, <c>CreatedAt</c> and <c>Metadata</c> are never
/// revisited.</item>
/// </list>
///
/// <para>Four rules on one write, each defensible on its own and none discoverable without reading the SQL.
/// These facts make the whole set legible, so a change to any of them is a decision rather than an
/// accident.</para>
/// </summary>
public class MemoryReRememberTests
{
    private const string Engine = "rewrite";
    private const string Fact = "the production database is db-prod-1";

    private static (GraphMemoryEngine Engine, IMemoryGraphStore Store) Build()
    {
        var store = new InMemoryMemoryGraphStore();
        return (new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()]), store);
    }

    private static async Task<GraphNode> OnlyNodeAsync(IMemoryGraphStore store)
    {
        var nodes = await store.SeedAsync(Engine, "t", "s", null, 50);
        return Assert.Single(nodes);   // ONE row — a re-remember refreshes, it does not duplicate
    }

    [Fact]
    public async Task A_re_remember_that_does_not_restate_the_grade_KEEPS_the_stored_one()
    {
        // THE FIX (2026-08-26). `MemoryGrade.Inherit` is the DEFAULT on MemoryWrite, and it used to resolve
        // to Associative and then OVERWRITE the stored grade -- so an application refreshing a fact it had
        // marked authoritative, without restating the grade, silently lost it. That cost is not cosmetic:
        // an authoritative entry never decays, is never truncated to a headline, holds a reserved recall
        // slot and is exempt from PruneAsync, and design section 5.7.0's objective (1) is about exactly
        // that entry.
        //
        // `Inherit` now means what it says on a re-remember: inherit what this entry already is. The
        // engine's role still decides on a genuine FIRST write, where there is nothing to inherit from.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));   // no grade restated

        Assert.Equal(MemoryGrade.Authoritative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task Restating_the_grade_keeps_it_too()
    {
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));

        Assert.Equal(MemoryGrade.Authoritative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task An_EXPLICIT_associative_re_remember_still_demotes_because_the_caller_said_so()
    {
        // The other half of the fix, and the reason it is a distinction rather than a blanket "never touch
        // the grade": an application that deliberately writes Associative is DEMOTING, and that has to keep
        // working. What changed is only that "not stated" stopped meaning "stated as ordinary".
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Associative));

        Assert.Equal(MemoryGrade.Associative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task A_re_remember_can_PROMOTE_an_ordinary_fact_which_is_why_the_grade_is_writable()
    {
        // The capability the overwrite exists for, asserted so that any fix for the downgrade above has to
        // keep it. A rule that simply ignored the incoming grade on a re-remember would break this.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));

        Assert.Equal(MemoryGrade.Authoritative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task A_re_remember_updates_BOTH_the_headline_and_the_metadata_a_caller_supplies()
    {
        // TWO CALLER-SUPPLIED FIELDS ON ONE WRITE, and as of D91 they finally agree. Until then a corrected
        // headline stuck and a corrected metadata bag was silently ignored -- two plain caller-owned fields,
        // adjacent on the same record, behaving oppositely for no stated reason.
        //
        // Asserted together, in one fact, on purpose: apart they read as two unrelated details, and side by
        // side they were the asymmetry. Keeping them together is what makes a future divergence visible.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Headline: "db is prod-1",
            Metadata: new Dictionary<string, string> { ["note"] = "first" }));

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Headline: "the production DB is db-prod-1",
            Metadata: new Dictionary<string, string> { ["note"] = "second" }));

        var node = await OnlyNodeAsync(store);
        Assert.Equal("the production DB is db-prod-1", node.Headline);
        Assert.Equal("second", node.Metadata!["note"]);
    }

    [Fact]
    public async Task A_re_remember_that_supplies_NO_metadata_keeps_what_is_stored()
    {
        // The other half of D91's rule, and the half that keeps the fix from being a new silent loss: a
        // write that says nothing about metadata must not blank it. Otherwise every ordinary refresh --
        // which supplies none -- would erase whatever an earlier annotated write had attached.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact,
            Metadata: new Dictionary<string, string> { ["note"] = "first" }));

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));   // no metadata supplied

        Assert.Equal("first", (await OnlyNodeAsync(store)).Metadata!["note"]);
    }

    [Fact]
    public async Task A_re_remember_never_revisits_the_entry_s_age_or_its_creation_time()
    {
        // The deliberate immutables, and the reason they are deliberate: stability is what the retention
        // policy has LEARNED about this entry, and a re-remember is not a review. Resetting it would let a
        // caller launder a decayed entry back to fresh by writing the same text again -- which is the
        // "permanent change driven by the system's own decisions" that design section 5.7.0 forbids, reached
        // from the write side instead of the recall side.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        var before = await OnlyNodeAsync(store);

        // filler goes in ANOTHER scope: the position is per-engine, so it ages this entry either way, and
        // keeping the subject scope to one row is what lets the assertions below name that row
        for (var i = 0; i < 30; i++)
            await engine.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler {i}"));

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        var after = await OnlyNodeAsync(store);

        Assert.Equal(before.Stability, after.Stability);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }
}
