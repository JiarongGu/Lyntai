using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// The prototype's actual questions: does a metadata-only evidence ledger deliver supersession, bitemporal
/// queries and visible conflict — and what does it cost?
///
/// <para><b>These are the proposal's own test names</b>, kept verbatim so this reports on its idea rather
/// than on a restatement of it. Each one is deterministic: no model, no corpus, no ranking. A temporal
/// resolution is either right or wrong, which is what makes this answerable now while the recall-quality
/// half is not.</para>
///
/// <para><b>The in-process store on purpose.</b> The subject here is RESOLUTION LOGIC, and the store
/// behaviour it depends on — that metadata round-trips, and that it is write-once — is pinned across all
/// three backends by <c>MemoryGraphStoreContract</c> rather than re-asserted here. Running these on one
/// backend is therefore a choice about speed, not a gap.</para>
/// </summary>
public class AssertionResolverTests
{
    private const string Engine = "ledger";
    private const string Key = "project:phoenix:production-database";

    private static readonly DateTimeOffset July = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset August = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    private static (GraphMemoryEngine Engine, IMemoryGraphStore Store) Build()
    {
        var store = new InMemoryMemoryGraphStore();
        return (new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()]), store);
    }

    private static MemoryWrite Assert_(string content, string validFrom, string recordedAt, string source) =>
        new("t", "s", content, Metadata: new Dictionary<string, string>
        {
            [AssertionResolver.Keys.CanonicalKey] = Key,
            [AssertionResolver.Keys.ValidFrom] = validFrom,
            [AssertionResolver.Keys.RecordedAt] = recordedAt,
            [AssertionResolver.Keys.SourceRef] = source,
        });

    private static Task<IReadOnlyList<AssertionResolver.Assertion>> HistoryAsync(
        IMemoryGraphStore store, DateTimeOffset asOf) =>
        AssertionResolver.HistoryAsync(store, Engine, "t", "s", Key, asOf);

    [Fact]
    public async Task A_new_fact_supersedes_the_active_view_without_deleting_history()
    {
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-1",
            "2026-07-01T00:00:00Z", "2026-07-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));

        var history = await HistoryAsync(store, August);

        // BOTH survive — that is the whole claim. The new fact changes the VIEW, not the evidence.
        Assert.Equal(2, history.Count);
        var current = Assert.Single(AssertionResolver.CurrentOf(history));
        Assert.Contains("db-prod-2", current.Content, StringComparison.Ordinal);
        Assert.Equal(AssertionResolver.Status.Superseded, history[0].State);
    }

    [Fact]
    public async Task A_historical_query_returns_the_version_valid_at_that_time()
    {
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-1",
            "2026-07-01T00:00:00Z", "2026-07-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));

        var asOfJuly = Assert.Single(AssertionResolver.CurrentOf(await HistoryAsync(store, July)));

        Assert.Contains("db-prod-1", asOfJuly.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_late_arriving_assertion_uses_valid_time_not_recorded_time()
    {
        // THE CASE THAT DECIDES WHETHER TWO TIMESTAMPS EARN THEIR KEEP. A document imported in August
        // describes a configuration that took effect on 1 July. With one timestamp — whatever CreatedAt
        // gives you — it is an August fact and July's answer is wrong. The two are not interchangeable and
        // this is where that becomes visible rather than merely arguable.
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-1",
            validFrom: "2026-07-01T00:00:00Z", recordedAt: "2026-08-26T00:00:00Z", "imported-doc"));

        var asOfJuly = Assert.Single(AssertionResolver.CurrentOf(await HistoryAsync(store, July)));

        Assert.Contains("db-prod-1", asOfJuly.Content, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), asOfJuly.ValidFrom);
        // ...and the system is still honest about when it LEARNED this, which is what makes an answer
        // given in July auditable after the fact rather than retroactively wrong.
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), asOfJuly.RecordedAt);
    }

    [Fact]
    public async Task A_conflict_is_exposed_as_disputed_not_silently_resolved()
    {
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-3",
            "2026-08-01T00:00:00Z", "2026-08-02T00:00:00Z", "handover-note"));

        var current = AssertionResolver.CurrentOf(await HistoryAsync(store, August));

        // TWO answers, and neither is picked. A resolver that returned one here would be inventing
        // confidence out of a disagreement, which is the failure this whole shape exists to refuse.
        Assert.Equal(2, current.Count);
        Assert.All(current, a => Assert.Equal(AssertionResolver.Status.Disputed, a.State));
        Assert.Contains(current, a => a.SourceRef == "runbook");
        Assert.Contains(current, a => a.SourceRef == "handover-note");
    }

    [Fact]
    public async Task A_later_recording_does_NOT_by_itself_resolve_a_conflict()
    {
        // The control that keeps the fact above honest. "Newest wins" is the rule everyone reaches for, and
        // it is exactly the silent resolution the proposal forbids: the handover note was recorded a day
        // later and is not thereby more true. If this ever passes with one result, the resolver has quietly
        // grown a tie-break and the disputed fact above is no longer testing anything.
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-3",
            "2026-08-01T00:00:00Z", "2026-08-20T00:00:00Z", "handover-note"));

        Assert.Equal(2, AssertionResolver.CurrentOf(await HistoryAsync(store, August)).Count);
    }

    [Fact]
    public async Task A_superseded_fact_is_excluded_from_current_queries_and_kept_for_historical_ones()
    {
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-1",
            "2026-07-01T00:00:00Z", "2026-07-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));

        var now = AssertionResolver.CurrentOf(await HistoryAsync(store, August));
        Assert.DoesNotContain(now, a => a.Content.Contains("db-prod-1", StringComparison.Ordinal));

        var then = AssertionResolver.CurrentOf(await HistoryAsync(store, July));
        Assert.Contains(then, a => a.Content.Contains("db-prod-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_interval_is_DERIVED_because_metadata_cannot_be_revised_in_place()
    {
        // Pins the constraint that shaped the resolver rather than leaving it in prose. Metadata is
        // write-once across a re-remember (MemoryGraphStoreContract), so an assertion cannot be closed off
        // once a successor arrives — its end has to come from the successor's start. If metadata ever
        // becomes revisable, this is the test that should make someone re-read the design.
        var (engine, store) = Build();
        await engine.RememberAsync(Assert_("the production database is db-prod-1",
            "2026-07-01T00:00:00Z", "2026-07-01T00:00:00Z", "runbook"));
        await engine.RememberAsync(Assert_("the production database is db-prod-2",
            "2026-08-01T00:00:00Z", "2026-08-01T00:00:00Z", "runbook"));

        var history = await HistoryAsync(store, August);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), history[0].ValidTo);
        Assert.Null(history[1].ValidTo);   // still current, so it has no end
    }

    [Fact]
    public async Task The_prototype_changes_nothing_about_an_ordinary_recall()
    {
        // The claim that makes this safe to land: it is an ADDITIONAL reading of data the engine already
        // stores and already ignores. Recall must be byte-for-byte what it would be with no metadata at
        // all — otherwise the experiment has changed the thing it is measuring.
        var (plain, _) = Build();
        var (tagged, _) = Build();

        await plain.RememberAsync(new MemoryWrite("t", "s", "the production database is db-prod-1"));
        await tagged.RememberAsync(Assert_("the production database is db-prod-1",
            "2026-07-01T00:00:00Z", "2026-08-26T00:00:00Z", "imported-doc"));

        var a = await plain.RecallAsync(new MemoryQuery("t", "s", "production database"));
        var b = await tagged.RecallAsync(new MemoryQuery("t", "s", "production database"));

        Assert.Equal(a.Items.Count, b.Items.Count);
        Assert.Equal(a.Items[0].Headline, b.Items[0].Headline);
        Assert.Equal(a.Items[0].Grade, b.Items[0].Grade);
    }
}
