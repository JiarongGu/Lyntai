using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>What a RE-REMEMBER of unchanged content does to everything the caller did not restate.</b>
///
/// <para>An entry's identity is (engine, task, scope, content), so writing the same text twice refreshes one
/// row rather than making two. That single write path applies <b>several different update rules</b> to the
/// fields around the content:</para>
///
/// <list type="bullet">
/// <item><c>Grade</c> and <c>Headline</c> are overwritten only when the write STATES one
/// (<c>GradeStated</c>, <c>HeadlineStated</c>); an unstated one keeps what is stored.</item>
/// <item><c>Metadata</c>, <c>Signals</c> (and salience, and its provenance) keep what is stored when the
/// incoming bag is EMPTY — "no opinion" — and a supplied one replaces it (D91).</item>
/// <item><c>Difficulty</c> is narrower still: overwritten only when the incoming bag NAMES a difficulty.</item>
/// <item>The AGE resets: a re-remember is a new encoding, so the age primitives restamp to now.</item>
/// <item><c>Stability</c>, <c>provenance_retrievability</c> and <c>CreatedAt</c> are never revisited.</item>
/// </list>
///
/// <para>Each rule is defensible on its own and none is discoverable without reading the store. These facts
/// make the whole set legible, so a change to any of them is a decision rather than an accident.</para>
/// </summary>
public class MemoryReRememberTests
{
    private const string Engine = "rewrite";
    private const string Fact = "the production database is db-prod-1";

    private static (GraphMemoryEngine Engine, IMemoryGraphStore Store) Build()
    {
        var store = new InMemoryMemoryGraphStore();
        return (new GraphMemoryEngine(Engine, store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
            }), store);
    }

    private static async Task<GraphNode> OnlyNodeAsync(IMemoryGraphStore store)
    {
        var nodes = await store.SeedAsync(Engine, "t", "s", null, 50);
        return Assert.Single(nodes);   // ONE row — a re-remember refreshes, it does not duplicate
    }

    [Fact]
    public async Task A_re_remember_that_does_not_restate_the_grade_KEEPS_the_stored_one()
    {
        // `MemoryGrade.Inherit` is the DEFAULT on MemoryWrite, so resolving it to Associative and then
        // OVERWRITING the stored grade would make an application refreshing a fact it had marked
        // authoritative, without restating the grade, silently lose it. That cost is not cosmetic:
        // an authoritative entry never decays, is never truncated to a headline, holds a reserved recall
        // slot and is exempt from PruneAsync, and design section 5.7.0's objective (1) is about exactly
        // that entry.
        //
        // `Inherit` means what it says on a re-remember: inherit what this entry already is. The
        // engine's role decides only on a genuine FIRST write, where there is nothing to inherit from.
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
        // The other half of the rule, and the reason it is a distinction rather than a blanket "never touch
        // the grade": an application that deliberately writes Associative is DEMOTING, and that has to keep
        // working. Only "not stated" is kept from meaning "stated as ordinary".
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Associative));

        Assert.Equal(MemoryGrade.Associative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task A_re_remember_can_PROMOTE_an_ordinary_fact_which_is_why_the_grade_is_writable()
    {
        // The capability the overwrite exists for, asserted so that the downgrade guard above cannot be met
        // by dropping it. A rule that simply ignored the incoming grade on a re-remember would break this.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        await engine.RememberAsync(new MemoryWrite("t", "s", Fact, Grade: MemoryGrade.Authoritative));

        Assert.Equal(MemoryGrade.Authoritative, (await OnlyNodeAsync(store)).Grade);
    }

    [Fact]
    public async Task A_re_remember_updates_BOTH_the_headline_and_the_metadata_a_caller_supplies()
    {
        // TWO CALLER-SUPPLIED FIELDS ON ONE WRITE, and they agree (D91): a corrected headline and a
        // corrected metadata bag both land.
        //
        // Asserted together, in one fact, on purpose: apart they read as two unrelated details, and side by
        // side a divergence between them is visible.
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
    public async Task A_re_remember_that_supplies_NO_headline_keeps_the_AUTHORED_one()
    {
        // `Headline` is null-means-unstated exactly as Grade and Metadata are -- the engine DERIVES one when
        // the caller supplies none, so a store that overwrites unconditionally replaces an authored headline,
        // on a refresh that does not restate it, with a machine-derived truncation of the content. Silently,
        // and with no way back: the authored text is gone from the row.
        var (engine, store) = Build();
        const string authored = "prod DB: db-prod-1";
        const string long_ = "the production database for the phoenix project is db-prod-1 and it is "
            + "reachable only from the deployment subnet after the august migration completed";

        await engine.RememberAsync(new MemoryWrite("t", "s", long_, Headline: authored));
        await engine.RememberAsync(new MemoryWrite("t", "s", long_));   // no headline restated

        Assert.Equal(authored, (await OnlyNodeAsync(store)).Headline);
    }

    [Fact]
    public async Task A_re_remember_that_DOES_supply_a_headline_replaces_it()
    {
        // The control that keeps the rule a distinction rather than a prohibition — correcting a headline
        // has to keep working, exactly as promotion does for the grade.
        var (engine, store) = Build();
        const string long_ = "the production database for the phoenix project is db-prod-1 and it is "
            + "reachable only from the deployment subnet after the august migration completed";

        await engine.RememberAsync(new MemoryWrite("t", "s", long_, Headline: "prod DB: db-prod-1"));
        await engine.RememberAsync(new MemoryWrite("t", "s", long_, Headline: "prod DB: db-prod-1 (subnet only)"));

        Assert.Equal("prod DB: db-prod-1 (subnet only)", (await OnlyNodeAsync(store)).Headline);
    }

    [Fact]
    public async Task A_re_remember_that_supplies_NO_metadata_keeps_what_is_stored()
    {
        // The other half of D91's rule, and the half that keeps replace-on-supply from being a new silent
        // loss: a write that says nothing about metadata must not blank it. Otherwise every ordinary
        // refresh -- which supplies none -- would erase whatever an earlier annotated write had attached.
        var (engine, store) = Build();

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact,
            Metadata: new Dictionary<string, string> { ["note"] = "first" }));

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));   // no metadata supplied

        Assert.Equal("first", (await OnlyNodeAsync(store)).Metadata!["note"]);
    }

    [Fact]
    public async Task A_re_remember_resets_the_age_but_never_revisits_stability_or_creation_time()
    {
        // A re-remember is a new ENCODING, so the age resets — the primitives restamp to now. It is not a
        // REVIEW, so stability — what the curve has LEARNED about this entry — is left exactly as it was:
        // neither regrown nor reset to a fresh entry's value.
        //
        // The stability is grown FIRST (growth on, then one aged recall), so an equality below could fail:
        // an entry that was never reinforced sits at InitialStability whether or not a refresh resets it.
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine(Engine, store, seams: new GraphMemorySeams
            {
                Retrievability = new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
                AgePolicies = [new PerWriteAgePolicy()],
            });

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        // filler goes in ANOTHER scope: the position is per-engine, so it ages this entry either way, and
        // keeping the subject scope to one row is what lets the assertions below name that row
        await CrowdAsync(engine, 30);
        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", "production database"))).Items);
        await CrowdAsync(engine, 30);

        var before = await OnlyNodeAsync(store);
        Assert.True(before.Stability > new DsrOptions().InitialStability, "the recall must have grown it");
        Assert.True(before.OrdinalAge > 0, "the entry must have aged, or the reset below proves nothing");

        await engine.RememberAsync(new MemoryWrite("t", "s", Fact));
        var after = await OnlyNodeAsync(store);

        Assert.Equal(0, after.OrdinalAge, precision: 9);          // a new encoding
        Assert.Equal(before.Stability, after.Stability);          // not a review
        Assert.Equal(before.CreatedAt, after.CreatedAt);

        static async Task CrowdAsync(GraphMemoryEngine engine, int writes)
        {
            for (var i = 0; i < writes; i++)
                await engine.RememberAsync(new MemoryWrite("t", "filler", $"unrelated filler {Guid.NewGuid():N}"));
        }
    }
}
