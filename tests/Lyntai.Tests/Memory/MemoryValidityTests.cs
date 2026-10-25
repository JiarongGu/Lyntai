using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Valid time on a memory write and explicit supersession</b> (<c>docs/memory.md</c> §Say when a fact held, and what it replaced): what an entry
/// says about WHEN it held in the world, what a later write that names it supersedes, and the readings — now, then,
/// all of it — that follow. The engine decides nothing here that a caller did not state: two entries valid at one
/// instant are both returned, and nothing is inferred from similar text.
/// </summary>
public class MemoryValidityTests
{
    private const string Name = "valid";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jul = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record Rig(GraphMemoryEngine Engine, IMemoryGraphStore Store, MutableClock Clock);

    /// <summary>Per-write ageing, so a burst of writes ages entries as the same writes spread out would; the clock
    /// stands in July, after every interval below, unless a test moves it.</summary>
    private static Rig Build(IMemoryGraphStore? store = null, string name = Name)
    {
        store ??= new InMemoryMemoryGraphStore();
        var clock = new MutableClock { Now = Jul };
        return new Rig(new GraphMemoryEngine(name, store, null,
            new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()], Clock = clock.Get }), store, clock);
    }

    private static MemoryWrite Fact(string content, DateTimeOffset? from = null, DateTimeOffset? to = null,
        params MemoryRef[] supersedes) =>
        new("t", "s", content) { ValidFrom = from, ValidTo = to, Supersedes = supersedes.Length == 0 ? null : supersedes };

    private static MemoryQuery Ask(MemoryValidityReading? reading = null, string query = "lives", string task = "t") =>
        new(task, "s", query, Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static string[] Contents(MemoryRecall recall) =>
        [.. recall.Items.Select(i => i.Content!).Order(StringComparer.Ordinal)];

    private static long Id(MemoryRef reference) => long.Parse(reference.Id, CultureInfo.InvariantCulture);

    /// <summary>Paris from January, superseded by Berlin from March — the shape most of these facts read.</summary>
    private static async Task<(MemoryRef Paris, MemoryRef Berlin)> MoveAsync(Rig rig)
    {
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        var berlin = (await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris))).Reference;
        return (paris, berlin);
    }

    // ---- the readings ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_recall_reads_the_facts_valid_now_by_default()
    {
        var rig = Build();
        await MoveAsync(rig);

        var recall = await rig.Engine.RecallAsync(Ask());

        Assert.Equal(["lives in Berlin"], Contents(recall));
    }

    [Fact]
    public async Task A_historical_reading_returns_what_held_then_and_the_boundaries_are_half_open()
    {
        var rig = Build();
        await MoveAsync(rig);

        async Task<string[]> At(DateTimeOffset instant) =>
            Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(instant))));

        Assert.Empty(await At(Jan.AddTicks(-1)));             // before the first fact began
        Assert.Equal(["lives in Paris"], await At(Jan));      // valid AT its start
        Assert.Equal(["lives in Paris"], await At(Feb));
        Assert.Equal(["lives in Paris"], await At(Mar.AddTicks(-1)));
        Assert.Equal(["lives in Berlin"], await At(Mar));     // the successor takes over AT the instant, and Paris is not valid at its end
        Assert.Equal(["lives in Berlin"], await At(Jun));
    }

    [Fact]
    public async Task History_returns_every_entry_each_carrying_its_interval_and_its_successor()
    {
        var rig = Build();
        var (paris, berlin) = await MoveAsync(rig);

        var recall = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.History));

        Assert.Equal(["lives in Berlin", "lives in Paris"], Contents(recall));
        var ended = recall.Items.Single(i => i.Content == "lives in Paris").Validity!;
        Assert.Equal(Jan, ended.From);
        Assert.Null(ended.To);                        // its own claim is not overwritten by being superseded
        Assert.Equal(Mar, ended.SupersededAt);
        Assert.Equal(berlin, ended.SupersededBy);
        Assert.Equal(Mar, ended.End);
        var current = recall.Items.Single(i => i.Content == "lives in Berlin").Validity!;
        Assert.Equal((Mar, null), (current.From, current.SupersededAt));
        Assert.NotEqual(paris, berlin);
    }

    [Fact]
    public async Task A_fact_not_yet_started_is_pending_and_a_scheduled_change_takes_effect_at_its_start()
    {
        var rig = Build();
        rig.Clock.Now = Feb;
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));   // announced in advance

        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask())));          // still Paris in February
        rig.Clock.Now = Mar;
        Assert.Equal(["lives in Berlin"], Contents(await rig.Engine.RecallAsync(Ask())));         // Berlin from the instant
    }

    [Fact]
    public async Task An_untimed_fact_is_valid_at_every_reading_and_reads_as_it_always_did()
    {
        var rig = Build();
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives near the river"));

        var current = await rig.Engine.RecallAsync(Ask());
        var past = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero))));
        var all = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.History));

        Assert.Equal(["lives in Paris", "lives near the river"], Contents(current));
        Assert.Equal(Contents(current), Contents(past));
        Assert.Equal(Contents(current), Contents(all));
        Assert.All(current.Items, i => Assert.Null(i.Validity));
        var nodes = await rig.Store.SeedAsync(Name, "t", "s", null, 10);
        Assert.All(nodes, n => Assert.Null(n.Validity));
    }

    [Fact]
    public async Task A_bound_with_no_successor_ends_a_fact_without_naming_one()
    {
        var rig = Build();
        await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan, to: Jun));

        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Mar)))));
        Assert.Empty(Contents(await rig.Engine.RecallAsync(Ask())));   // July: the bound has passed
    }

    [Fact]
    public async Task A_superseding_write_with_no_start_begins_when_it_is_written_and_repeating_it_moves_nothing()
    {
        var rig = Build();
        rig.Clock.Now = Feb;
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        var write = Fact("lives in Berlin", supersedes: paris);

        var berlin = (await rig.Engine.RememberAsync(write)).Reference;
        rig.Clock.Now = Mar;
        await rig.Engine.RememberAsync(write);   // the same assertion, again, a month later

        var started = (await rig.Store.GetAsync(Name, Id(berlin)))!;
        Assert.Equal(Feb, started.ValidFrom);    // stamped at the first write and kept
        Assert.Equal(Feb, (await rig.Store.GetAsync(Name, Id(paris)))!.SupersededAt);
        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Feb.AddDays(-1))))));
        Assert.Equal(["lives in Berlin"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Feb)))));
    }

    // ---- late and out-of-order writes -------------------------------------------------------------------------

    [Fact]
    public async Task A_late_write_for_an_earlier_period_overlaps_what_was_recorded_first_and_nothing_is_hidden()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));
        // recorded last, valid FIRST: it was true in February and the system learned that only now
        await rig.Engine.RememberAsync(Fact("lives in Lyon", from: Feb, to: Mar));

        var february = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Feb.AddDays(14))));

        Assert.Equal(["lives in Lyon", "lives in Paris"], Contents(february));   // two claims, both shown
        Assert.Equal(["lives in Berlin"], Contents(await rig.Engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task A_late_write_may_supersede_an_entry_not_yet_superseded_with_an_earlier_instant()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        await rig.Engine.RememberAsync(Fact("lives in Lyon", from: Feb, supersedes: paris));

        var ended = (await rig.Store.GetAsync(Name, Id(paris)))!;

        Assert.Equal(Feb, ended.SupersededAt);
        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Jan.AddDays(30))))));
        Assert.Equal(["lives in Lyon"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Feb)))));
    }

    [Fact]
    public async Task An_entry_has_one_successor_and_a_second_claim_is_refused_before_anything_is_written()
    {
        var rig = Build();
        var (paris, _) = await MoveAsync(rig);
        var before = await rig.Store.SeedAsync(Name, "t", null, null, 50);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Lyon", from: Feb, supersedes: paris)));

        Assert.Contains("lives in Berlin", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before.Count, (await rig.Store.SeedAsync(Name, "t", null, null, 50)).Count);   // Lyon was not stored
        var kept = (await rig.Store.GetAsync(Name, Id(paris)))!;
        Assert.Equal(Mar, kept.SupersededAt);
    }

    // ---- never inferred ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_claims_valid_at_one_instant_are_both_returned_and_the_engine_picks_neither()
    {
        var rig = Build();
        await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan));
        await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Jan));

        Assert.Equal(["lives in Berlin", "lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task Near_identical_text_is_never_read_as_a_supersession()
    {
        var rig = Build();
        await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan));
        await rig.Engine.RememberAsync(Fact("lives in Paris, France", from: Mar));
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris these days"));

        var recall = await rig.Engine.RecallAsync(Ask());

        Assert.Equal(3, recall.Items.Count);
        Assert.All(await rig.Store.SeedAsync(Name, "t", "s", null, 10), n => Assert.Null(n.SupersededAt));
    }

    // ---- raw evidence, decay, and what stays untouched ----------------------------------------------------------

    [Fact]
    public async Task A_superseded_entry_stays_whole_countable_expandable_and_unpruned()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris")
        {
            ValidFrom = Jan, Headline = "the old address", Metadata = new Dictionary<string, string> { ["source"] = "form" },
        })).Reference;
        await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));

        var node = (await rig.Store.GetAsync(Name, Id(paris)))!;
        Assert.Equal(("lives in Paris", "the old address", "form"),
            (node.Content, node.Headline, node.Metadata!["source"]));
        Assert.Equal(2, (await rig.Engine.CountAsync("t"))!.Entries);
        Assert.Equal(0, await rig.Engine.PruneAsync("t", minRetrievability: 0));
        var expanded = await rig.Engine.ExpandAsync(paris);
        var itself = Assert.Single(expanded.Items, i => i.Reference == paris);
        Assert.Equal("lives in Paris", itself.Content);          // asking for it by reference is how history is read
        Assert.Equal(Mar, itself.Validity!.SupersededAt);
    }

    [Fact]
    public async Task Superseding_buries_nothing_reinforces_nothing_and_ages_nothing_else()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        var other = (await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "owns a bicycle"))).Reference;
        var before = new[] { paris, other }.Select(r => rig.Store.GetAsync(Name, Id(r)).Result!).ToList();

        // the ONLY thing that moves the ending entry's decay state is the successor's own write advancing the position
        await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));

        var after = new[] { paris, other }.Select(r => rig.Store.GetAsync(Name, Id(r)).Result!).ToList();
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].RecallCount, after[i].RecallCount);
            Assert.Equal(before[i].Stability, after[i].Stability);
            Assert.Equal(before[i].Difficulty, after[i].Difficulty);
            Assert.Equal(before[i].Age + 1, after[i].Age);              // one write happened: the same for both
            Assert.Equal(before[i].OrdinalAge + 1, after[i].OrdinalAge);
        }
    }

    [Fact]
    public async Task A_superseded_authoritative_fact_leaves_the_current_page_and_its_reserved_slot_goes_with_it()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(
            new MemoryWrite("t", "s", "lives in Paris", Grade: MemoryGrade.Authoritative) { ValidFrom = Jan })).Reference;
        await rig.Engine.RememberAsync(
            new MemoryWrite("t", "s", "lives in Berlin", Grade: MemoryGrade.Authoritative)
                { ValidFrom = Mar, Supersedes = [paris] });

        var current = await rig.Engine.RecallAsync(Ask());
        var history = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.History));

        Assert.Equal(["lives in Berlin"], Contents(current));
        Assert.Equal(["lives in Berlin", "lives in Paris"], Contents(history));   // objective (1): nothing authoritative is lost
        Assert.All(history.Items, i => Assert.Equal(MemoryGrade.Authoritative, i.Grade));
    }

    [Fact]
    public async Task Under_a_history_reading_valid_time_changes_no_ranking_no_retrievability_and_no_relevance()
    {
        // the same writes with and without valid time: if stating it moved ANY of what the ranking reads, one of these
        // two pages would differ from the other
        var timed = Build();
        var plain = Build();
        var paris = (await timed.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        await plain.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));
        await timed.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));
        await plain.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Berlin"));
        foreach (var rig in new[] { timed, plain })
            for (var i = 0; i < 6; i++)
                await rig.Engine.RememberAsync(new MemoryWrite("t", "s", $"lives next to neighbour number {i}"));

        static IEnumerable<(string, double, double)> Page(MemoryRecall r) =>
            r.Items.Select(i => (i.Headline, Math.Round(i.Retrievability, 9), Math.Round(i.Relevance, 9)));
        var asked = new MemoryQuery("t", "s", "lives", Limit: 20);

        var ledger = Page(await timed.Engine.RecallAsync(asked with { Validity = MemoryValidityReading.History })).ToList();

        Assert.Equal(8, ledger.Count);
        Assert.Equal(Page(await plain.Engine.RecallAsync(asked)), ledger);
    }

    [Fact]
    public async Task An_explicit_forget_removes_ended_entries_too_because_evidence_is_kept_not_protected()
    {
        var rig = Build();
        var (paris, berlin) = await MoveAsync(rig);

        await rig.Engine.ForgetAsync("t", "s");

        Assert.Empty(await rig.Store.SeedAsync(Name, "t", null, null, 10));
        Assert.Null(await rig.Store.GetAsync(Name, Id(paris)));
        Assert.Null(await rig.Store.GetAsync(Name, Id(berlin)));
    }

    [Fact]
    public async Task Forgetting_a_successor_leaves_what_it_ended_ended_and_a_new_successor_may_take_over()
    {
        var rig = Build();
        var (paris, berlin) = await MoveAsync(rig);
        await rig.Store.DeleteAsync(Name, [Id(berlin)]);

        Assert.Empty(Contents(await rig.Engine.RecallAsync(Ask())));      // Paris stays ended: nothing reopens it
        var lyon = (await rig.Engine.RememberAsync(Fact("lives in Lyon", from: Jun, supersedes: paris))).Reference;

        var retaken = (await rig.Store.GetAsync(Name, Id(paris)))!;
        Assert.Equal((Id(lyon), Jun), (retaken.SupersededBy, retaken.SupersededAt));
    }

    // ---- traversal, expansion and the walk ----------------------------------------------------------------------

    private static async Task<(Rig Rig, MemoryRef Hub, MemoryRef Ended, MemoryRef Live)> HubAsync()
    {
        var rig = Build();
        var hub = (await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "the address book"))).Reference;
        var ended = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan, to: Mar))).Reference;
        var live = (await rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar))).Reference;
        await rig.Engine.LinkAsync(hub, ended, symmetric: true);
        await rig.Engine.LinkAsync(hub, live, symmetric: true);
        return (rig, hub, ended, live);
    }

    [Fact]
    public async Task Expansion_admits_neighbours_by_the_reading_and_always_returns_the_entry_asked_for()
    {
        var (rig, hub, ended, live) = await HubAsync();

        var current = await ((IExpandableMemory)rig.Engine).ExpandAsync(hub, 1, null, MemoryDetail.Full,
            MemoryValidityReading.Current);
        var history = await ((IExpandableMemory)rig.Engine).ExpandAsync(hub, 1, null, MemoryDetail.Full,
            MemoryValidityReading.History);
        var fromTheEnded = await ((IExpandableMemory)rig.Engine).ExpandAsync(ended, 1, null, MemoryDetail.Full,
            MemoryValidityReading.Current);

        Assert.Equal([hub, live], current.Items.Select(i => i.Reference).OrderBy(Id));
        Assert.Equal([hub, ended, live], history.Items.Select(i => i.Reference).OrderBy(Id));
        Assert.Equal(ended, fromTheEnded.Items[0].Reference);   // the ended entry itself, though nothing reads it as current
    }

    [Fact]
    public async Task A_walk_follows_its_querys_reading_into_every_expansion()
    {
        var (rig, _, ended, _) = await HubAsync();
        static async Task<List<MemoryRef>> Held(IMemoryEngine engine, MemoryQuery query)
        {
            var held = new List<MemoryRef>();
            await foreach (var step in engine.WalkAsync(query)) held = [.. step.Items.Select(i => i.Reference)];
            return held;
        }

        var current = await Held(rig.Engine, new MemoryQuery("t", "s", "address book"));
        var history = await Held(rig.Engine, new MemoryQuery("t", "s", "address book") { Validity = MemoryValidityReading.History });

        Assert.DoesNotContain(ended, current);
        Assert.Contains(ended, history);
    }

    [Fact]
    public async Task A_recall_still_walks_edges_through_an_ended_entry()
    {
        // traversal is unchanged: an ended entry is filtered from the PAGE, not from the graph, so what it links to
        // is still reached from a seed that matched it
        var rig = Build();
        var ended = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan, to: Mar))).Reference;
        var bridge = (await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "the river cruise booking"))).Reference;
        await rig.Engine.LinkAsync(ended, bridge, symmetric: true);

        var recall = await rig.Engine.RecallAsync(Ask(query: "Paris"));

        Assert.Equal(["the river cruise booking"], Contents(recall));
    }

    // ---- explaining -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Explain_names_a_candidate_the_reading_left_out()
    {
        var rig = Build();
        var (paris, berlin) = await MoveAsync(rig);

        var recall = await rig.Engine.RecallAsync(Ask() with { Explain = true });

        var left = Assert.Single(recall.Candidates!, c => c.Reference == paris);
        Assert.Equal(MemoryCandidateOutcome.OutsideValidity, left.Outcome);
        Assert.Null(left.Score);
        Assert.Null(left.Position);
        Assert.Equal(MemoryCandidateOutcome.Returned, Assert.Single(recall.Candidates!, c => c.Reference == berlin).Outcome);
    }

    [Fact]
    public async Task Explain_still_lists_the_candidates_when_the_reading_leaves_nothing()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan, to: Mar))).Reference;

        var recall = await rig.Engine.RecallAsync(Ask() with { Explain = true });

        Assert.Empty(recall.Items);
        Assert.Equal(MemoryCandidateOutcome.OutsideValidity, Assert.Single(recall.Candidates!, c => c.Reference == paris).Outcome);
    }

    // ---- what a write may name, and what refuses ----------------------------------------------------------------

    [Fact]
    public async Task A_stated_end_before_the_start_is_refused()
    {
        var rig = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Paris", from: Mar, to: Jan)));
        Assert.Equal(0, (await rig.Engine.CountAsync("t"))!.Entries);
    }

    [Fact]
    public async Task A_reading_that_is_both_history_and_an_instant_is_refused_rather_than_guessed()
    {
        var rig = Build();

        var incoherent = new MemoryValidityReading { IsHistory = true, At = Jan };

        await Assert.ThrowsAsync<ArgumentException>(() => rig.Engine.RecallAsync(Ask(incoherent)));
    }

    [Fact]
    public async Task A_predecessor_must_be_this_engines_own_entry_in_this_writes_task()
    {
        var rig = Build();
        var other = Build(rig.Store, "other");
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        var elsewhere = (await rig.Engine.RememberAsync(new MemoryWrite("other-task", "s", "lives in Rome"))).Reference;

        // another ENGINE over the same store may not end this engine's entry, and an id means a node only in the
        // store that wrote it under that engine
        await Assert.ThrowsAsync<ArgumentException>(() =>
            other.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris)));
        // another TASK is another consumer's memory
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: elsewhere)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: new MemoryRef(Name, "999999"))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: new MemoryRef(Name, "not-a-number"))));

        Assert.Null((await rig.Store.GetAsync(Name, Id(paris)))!.SupersededAt);
        Assert.Null((await rig.Store.GetAsync(Name, Id(elsewhere)))!.SupersededAt);
        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask())));
        Assert.Empty(await rig.Store.SeedAsync("other", "t", null, null, 10));   // the refused writes stored nothing
    }

    [Fact]
    public async Task An_entry_cannot_supersede_itself()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan, supersedes: paris)));
        Assert.Null((await rig.Store.GetAsync(Name, Id(paris)))!.SupersededAt);
    }

    [Fact]
    public async Task A_store_that_cannot_hold_valid_time_refuses_the_write_and_stores_nothing()
    {
        var rig = Build(new DelegatingGraphStore());

        Assert.False(rig.Engine.HoldsValidity);
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Engine.RememberAsync(Fact("lives in Paris", from: Jan)));
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Engine.RememberAsync(Fact("lives in Paris", to: Jun)));
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));   // untimed: unchanged
        var paris = (await rig.Store.SeedAsync(Name, "t", "s", null, 10)).Single();
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Engine.RememberAsync(
            Fact("lives in Berlin", supersedes: new MemoryRef(Name, paris.Id.ToString(CultureInfo.InvariantCulture)))));

        Assert.Single(await rig.Store.SeedAsync(Name, "t", "s", null, 10));
        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask())));   // and every reading admits an untimed entry
        Assert.Equal(["lives in Paris"], Contents(await rig.Engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Jan)))));
    }

    [Fact]
    public async Task Engines_sharing_a_store_each_hold_their_own_valid_time()
    {
        var shared = new InMemoryMemoryGraphStore();
        var a = Build(shared, "a");
        var b = Build(shared, "b");
        var aParis = (await a.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;
        await b.Engine.RememberAsync(Fact("lives in Paris", from: Jan));   // the same words, another engine's entry
        await a.Engine.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: aParis));

        Assert.Equal(["lives in Berlin"], Contents(await a.Engine.RecallAsync(Ask())));
        Assert.Equal(["lives in Paris"], Contents(await b.Engine.RecallAsync(Ask())));
    }

    // ---- blends -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_blend_routes_a_write_stating_valid_time_to_the_member_that_holds_it()
    {
        var graph = Build().Engine;
        var blend = new CompositeMemoryEngine("blend", [new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()), graph]);

        var written = await blend.RememberAsync(Fact("lives in Paris", from: Jan, to: Jun));

        Assert.Equal(Name, written.Reference.Engine);   // not the first member: that one would have dropped the bound
        Assert.True(((ITemporalMemory)blend).HoldsValidity);
        Assert.Empty((await blend.RecallAsync(Ask())).Items);
        Assert.Single((await blend.RecallAsync(Ask(MemoryValidityReading.AsOf(Feb)))).Items);
    }

    [Theory]
    [InlineData(MemoryWriteRouting.FirstCapable)]
    [InlineData(MemoryWriteRouting.EveryCapable)]
    public async Task A_write_that_supersedes_goes_to_the_one_member_that_owns_what_it_supersedes(MemoryWriteRouting routing)
    {
        var shared = new InMemoryMemoryGraphStore();
        var first = Build(shared, "blend/first");
        var second = Build(shared, "blend/second");
        var blend = new CompositeMemoryEngine("blend", [first.Engine, second.Engine]) { WriteRouting = routing };
        var paris = (await second.Engine.RememberAsync(Fact("lives in Paris", from: Jan))).Reference;

        var written = await blend.RememberAsync(Fact("lives in Berlin", from: Mar, supersedes: paris));

        Assert.Equal("blend/second", written.Reference.Engine);   // not the first member, which would refuse the reference
        Assert.Empty(await shared.SeedAsync("blend/first", "t", null, null, 10));   // and no fan-out copy anywhere else
        Assert.Equal(Mar, (await shared.GetAsync("blend/second", Id(paris)))!.SupersededAt);
        await Assert.ThrowsAsync<ArgumentException>(() => blend.RememberAsync(
            Fact("lives in Rome", from: Jun, supersedes: new MemoryRef("blend/nowhere", "1"))));
        Assert.Empty(await shared.SeedAsync("blend/first", "t", null, null, 10));
    }

    [Fact]
    public async Task An_entry_cannot_be_superseded_before_it_began()
    {
        var rig = Build();
        var paris = (await rig.Engine.RememberAsync(Fact("lives in Paris", from: Mar))).Reference;

        // a STATED start earlier than the entry's own: refused before anything is written
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Berlin", from: Jan, supersedes: paris)));
        Assert.Single(await rig.Store.SeedAsync(Name, "t", null, null, 10));

        // an UNSTATED one starts at the clock, which is after Paris began: fine; before it, the write is stored and
        // the end refused, loudly, with nothing ended
        rig.Clock.Now = Feb;
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Engine.RememberAsync(Fact("lives in Lyon", supersedes: paris)));
        Assert.Contains("nothing was ended", refused.Message, StringComparison.Ordinal);
        Assert.Null((await rig.Store.GetAsync(Name, Id(paris)))!.SupersededAt);
        rig.Clock.Now = Jun;
        await rig.Engine.RememberAsync(Fact("lives in Rome", supersedes: paris));
        Assert.Equal(Jun, (await rig.Store.GetAsync(Name, Id(paris)))!.SupersededAt);
    }

    [Fact]
    public async Task A_blend_with_no_member_that_holds_valid_time_refuses_and_so_does_a_fan_out_with_one_that_cannot()
    {
        var lexical = new LexicalMemoryEngine("blend/lex", new FakeMemoryStore());
        var none = new CompositeMemoryEngine("none", [lexical]);
        var mixed = new CompositeMemoryEngine("mixed", [Build().Engine, lexical])
            { WriteRouting = MemoryWriteRouting.EveryCapable };

        Assert.False(((ITemporalMemory)none).HoldsValidity);
        await Assert.ThrowsAsync<NotSupportedException>(() => none.RememberAsync(Fact("lives in Paris", from: Jan)));
        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => mixed.RememberAsync(Fact("lives in Paris", from: Jan)));
        Assert.Contains("blend/lex", refused.Message, StringComparison.Ordinal);   // names the member that could not hold it
        Assert.Empty((await mixed.RecallAsync(Ask(MemoryValidityReading.History))).Items);   // nothing was written anywhere
    }

    // ---- what a prompt and a tool are told ----------------------------------------------------------------------

    [Fact]
    public async Task A_rendered_entry_says_when_it_held_so_an_ended_fact_is_not_read_as_current()
    {
        var rig = Build();
        await MoveAsync(rig);
        var history = await rig.Engine.RecallAsync(Ask(MemoryValidityReading.History));

        var text = MemoryComposition.Render("", history.Items);

        Assert.Contains("- lives in Paris (valid from 2026-01-01 until 2026-03-01, superseded)", text, StringComparison.Ordinal);
        Assert.Contains("- lives in Berlin (valid from 2026-03-01)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_untimed_entry_renders_exactly_as_before()
    {
        var rig = Build();
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));
        var recall = await rig.Engine.RecallAsync(Ask());

        Assert.Equal("## Recalled context (associative — may be stale or partial)\n- lives in Paris",
            MemoryComposition.Render("", recall.Items));
    }

    [Fact]
    public async Task The_tool_reports_an_entrys_valid_time_only_when_it_has_one()
    {
        var rig = Build();
        await MoveAsync(rig);
        await rig.Engine.RememberAsync(new MemoryWrite("t", "s", "lives near the river"));
        var tool = MemoryTools.Recall(rig.Engine, "valid", "t", "s");

        var json = await tool.InvokeAsync("""{"query":"lives","limit":10}""");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        // the current reading: Paris is superseded, so it is not on the page at all
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var berlin = items.Single(i => i.GetProperty("headline").GetString() == "lives in Berlin");
        Assert.Equal(Mar, berlin.GetProperty("validFrom").GetDateTimeOffset());
        var river = items.Single(i => i.GetProperty("headline").GetString() == "lives near the river");
        Assert.False(river.TryGetProperty("validFrom", out _));
        Assert.False(river.TryGetProperty("validTo", out _));
        Assert.False(river.TryGetProperty("supersededBy", out _));
    }
}
