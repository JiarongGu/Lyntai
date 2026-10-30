using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>An entry is ended by reference, with a reason</b> (<c>docs/DECISIONS.md</c> D239, D241): out of the
/// <c>Current</c> reading, whole under <c>History</c> with the reason, not restored by a use, and applied only to the
/// revision the confirmer reviewed. The stores' own rules are <c>MemoryGraphStoreContract</c>'s; these are the
/// engine's refusals and the blend's routing.
/// </summary>
public class MemoryEndTests
{
    private const string Name = "end";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jul = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, string name = Name) =>
        new(name, store ?? new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()], Clock = () => Jul });

    private static MemoryWrite Fact(string content, string task = "t", DateTimeOffset? from = null) =>
        new(task, "s", content) { ValidFrom = from ?? Jan };

    private static MemoryQuery Ask(MemoryValidityReading? reading = null, string task = "t") =>
        new(task, "s", "lives", Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static string[] Contents(MemoryRecall recall) =>
        [.. recall.Items.Select(i => i.Content!).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task An_ended_entry_leaves_Current_stays_whole_under_History_and_is_not_reinforced()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var before = (await store.GetAsync(Name, long.Parse(paris.Id, CultureInfo.InvariantCulture)))!;

        var ended = await engine.EndAsync("t", paris, new MemoryEnding("moved away") { At = Mar });

        Assert.Equal(MemoryEndOutcome.Ended, ended.Outcome);
        Assert.Empty((await engine.RecallAsync(Ask())).Items);
        var history = (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single();
        Assert.Equal("lives in Paris", history.Content);
        Assert.Equal("moved away", history.Validity!.EndedReason);
        Assert.Equal(Mar, history.Validity.End);
        var after = (await store.GetAsync(Name, long.Parse(paris.Id, CultureInfo.InvariantCulture)))!;
        Assert.Equal(before.Stability, after.Stability);   // ending is not a reinforcement
        Assert.Equal(before.Content, after.Content);
        // asking again as of a date before the end still finds the fact it held then
        Assert.Equal(["lives in Paris"], Contents(await engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Jan.AddDays(5))))));
    }

    [Fact]
    public async Task A_successor_records_the_supersession_at_its_start_by_default()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var berlin = (await engine.RememberAsync(Fact("lives in Berlin", from: Mar))).Reference;

        await engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin });

        var history = (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items
            .Single(i => i.Content == "lives in Paris");
        Assert.Equal((Mar, berlin), (history.Validity!.SupersededAt, history.Validity.SupersededBy));
        Assert.Equal(["lives in Berlin"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task An_end_refuses_what_a_supersession_refuses()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris", from: Mar))).Reference;
        var berlin = (await engine.RememberAsync(Fact("lives in Berlin", from: Mar))).Reference;
        var rome = (await engine.RememberAsync(Fact("lives in Rome", from: Mar))).Reference;
        var elsewhere = (await engine.RememberAsync(Fact("lives in Oslo", task: "other"))).Reference;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("  ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", new MemoryRef("another", paris.Id), new MemoryEnding("x")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", elsewhere, new MemoryEnding("x")));                  // another task
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", new MemoryRef(Name, "9999"), new MemoryEnding("x")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("x") { At = Jan }));         // before it began
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("x") { Successor = paris })); // itself
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("x") { Successor = elsewhere }));

        await engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin });
        // an entry has one successor: a different one is refused, naming the first
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("replaced again") { Successor = rome }));
        Assert.Contains(berlin.Id, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_end_already_recorded_changes_nothing_and_says_so()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        await engine.EndAsync("t", paris, new MemoryEnding("moved away") { At = Mar });

        var again = await engine.EndAsync("t", paris, new MemoryEnding("a different reason") { At = Jul });

        Assert.Equal(MemoryEndOutcome.AlreadyEnded, again.Outcome);
        var held = (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single();
        Assert.Equal(("moved away", Mar), (held.Validity!.EndedReason, held.Validity.End));
    }

    [Fact]
    public async Task An_end_applies_only_to_the_revision_it_was_reviewed_on()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var reviewed = (await engine.RecallAsync(Ask())).Items.Single().Revision;
        Assert.NotNull(reviewed);

        // the reviewed claim keeps its validity and its (absent) confirmation, and is reworded under the reviewer
        await engine.RememberAsync(Fact("lives in Paris") with { Headline = "reviewed claim, reworded after the review" });

        var stale = await engine.EndAsync("t", paris, new MemoryEnding("outdated") { ExpectedRevision = reviewed });
        Assert.Equal(MemoryEndOutcome.Changed, stale.Outcome);
        Assert.NotEqual(reviewed, stale.Revision);
        Assert.Equal(["lives in Paris"], Contents(await engine.RecallAsync(Ask())));   // nothing was written

        // the recall carries the revision a fresh review reads, and recalling it moved nothing
        var fresh = (await engine.RecallAsync(Ask())).Items.Single().Revision;
        Assert.Equal(stale.Revision, fresh);
        Assert.Equal(MemoryEndOutcome.Ended,
            (await engine.EndAsync("t", paris, new MemoryEnding("outdated") { ExpectedRevision = fresh })).Outcome);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("x") { ExpectedRevision = "not a revision" }));
    }

    [Fact]
    public async Task A_retry_after_the_successor_is_stored_keeps_its_identity_and_needs_a_fresh_approval()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var reviewed = (await engine.RecallAsync(Ask())).Items.Single().Revision;
        var berlin = (await engine.RememberAsync(Fact("lives in Berlin", from: Mar) with { Metadata = new Dictionary<string, string> { ["source"] = "form" } })).Reference;
        await engine.RememberAsync(Fact("lives in Paris") with { Headline = "changed under the reviewer" });

        // the end returns Changed: the partial outcome is explicit, and both entries are live
        var first = await engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin, ExpectedRevision = reviewed });
        Assert.Equal(MemoryEndOutcome.Changed, first.Outcome);
        Assert.Equal(["lives in Berlin", "lives in Paris"], Contents(await engine.RecallAsync(Ask())));

        // the repeat of the successor's write is the same entry, with its provenance as it was
        var again = (await engine.RememberAsync(Fact("lives in Berlin", from: Mar))).Reference;
        Assert.Equal(berlin, again);
        var kept = (await engine.RecallAsync(Ask())).Items.Single(i => i.Reference == berlin);
        Assert.Equal("form", kept.Metadata!["source"]);

        // the guard is not dropped to repair: the stale approval still changes nothing, a fresh one lands
        Assert.Equal(MemoryEndOutcome.Changed,
            (await engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin, ExpectedRevision = reviewed })).Outcome);
        var fresh = (await engine.RecallAsync(Ask())).Items.Single(i => i.Content == "lives in Paris").Revision;
        Assert.Equal(MemoryEndOutcome.Ended,
            (await engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin, ExpectedRevision = fresh })).Outcome);
        Assert.Equal(["lives in Berlin"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task A_failed_end_leaves_both_entries_live_and_the_retry_lands_it()
    {
        var store = new FailsOnceStore();
        var engine = Build(store);
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;
        var berlin = (await engine.RememberAsync(Fact("lives in Berlin", from: Mar))).Reference;
        var revision = (await engine.RecallAsync(Ask())).Items.First(i => i.Content == "lives in Paris").Revision;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("replaced") { Successor = berlin, ExpectedRevision = revision }));
        Assert.Equal(["lives in Berlin", "lives in Paris"], Contents(await engine.RecallAsync(Ask())));

        var retried = await engine.EndAsync("t", paris,
            new MemoryEnding("replaced") { Successor = berlin, ExpectedRevision = revision });
        Assert.Equal(MemoryEndOutcome.Ended, retried.Outcome);
        Assert.Equal(["lives in Berlin"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task A_store_without_the_capability_refuses_rather_than_end_unguarded()
    {
        var engine = Build(new TemporalOnlyStore());
        var paris = (await engine.RememberAsync(Fact("lives in Paris"))).Reference;

        await Assert.ThrowsAsync<NotSupportedException>(() => engine.EndAsync("t", paris, new MemoryEnding("x")));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.EndAsync("t", paris, new MemoryEnding("x") { ExpectedRevision = "1" }));
        Assert.Equal(["lives in Paris"], Contents(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task The_blend_routes_an_end_by_the_engine_a_reference_names_and_fails_loud_where_none_can()
    {
        var graph = Build(name: "blend/graph");
        var lexical = new LexicalMemoryEngine("blend/lex", new FakeMemoryStore());
        var blend = new CompositeMemoryEngine("blend", [lexical, graph]);
        var paris = (await graph.RememberAsync(Fact("lives in Paris"))).Reference;

        Assert.Equal(MemoryEndOutcome.Ended,
            (await blend.EndAsync("t", paris, new MemoryEnding("moved away") { At = Mar })).Outcome);
        Assert.Empty((await blend.RecallAsync(Ask())).Items);
        Assert.Equal("moved away", (await blend.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single().Validity!.EndedReason);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            blend.EndAsync("t", new MemoryRef("blend/lex", "1"), new MemoryEnding("x")));
    }

    [Fact]
    public async Task An_end_and_its_reason_travel_through_an_export()
    {
        var source = Build();
        var paris = (await source.RememberAsync(Fact("lives in Paris"))).Reference;
        await source.EndAsync("t", paris, new MemoryEnding("moved away") { At = Mar });
        var file = new StringWriter();
        await source.ExportAsync("t", "s", file);

        var target = Build();
        await target.ImportAsync("t", "s", new StringReader(file.ToString()));

        var held = (await target.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single();
        Assert.Equal(("moved away", Mar), (held.Validity!.EndedReason, held.Validity.End));
        Assert.Empty((await target.RecallAsync(Ask())).Items);
    }

    /// <summary>A store that holds valid time and nothing more: what a BYO store written before an end carried a reason
    /// is. It forwards every member and is not <see cref="IEndableMemoryGraphStore"/>.</summary>
    private sealed class TemporalOnlyStore : DelegatingGraphStore, ITemporalMemoryGraphStore
    {
        public Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions,
            CancellationToken ct = default) =>
            ((ITemporalMemoryGraphStore)Inner).SupersedeAsync(engine, supersessions, ct);
    }

    /// <summary>An endable store whose first end fails, as a connection that dropped between the successor's insert and
    /// the end would.</summary>
    private sealed class FailsOnceStore : DelegatingGraphStore, IEndableMemoryGraphStore
    {
        private bool _failed;

        public Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions,
            CancellationToken ct = default) =>
            ((ITemporalMemoryGraphStore)Inner).SupersedeAsync(engine, supersessions, ct);

        public Task<GraphEndResult> EndAsync(string engine, GraphEnd end, CancellationToken ct = default)
        {
            if (_failed) return ((IEndableMemoryGraphStore)Inner).EndAsync(engine, end, ct);
            _failed = true;
            throw new InvalidOperationException("the connection dropped");
        }
    }
}
