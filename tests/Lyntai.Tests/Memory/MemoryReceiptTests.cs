using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A keyed call is answerable</b> (<c>docs/DECISIONS.md</c> D242): a repeat with its key answers the receipt and moves nothing, a
/// key reused for another call is refused, and an engine that cannot hold a receipt refuses the key rather than drop it. The
/// stores' own rules are <c>MemoryGraphStoreContract</c>'s; these are the engine's refusals, its fingerprints and the blend's routing.
/// </summary>
public class MemoryReceiptTests
{
    private const string Name = "receipts";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = Jan;
    }

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, string name = Name, Clock? clock = null,
        GraphMemoryOptions? options = null)
    {
        clock ??= new Clock();
        return new(name, store ?? new InMemoryMemoryGraphStore(), options,
            new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()], Clock = () => clock.Now });
    }

    private static MemoryWrite Fact(string content, string key, string task = "t") => new(task, "s", content) { OperationKey = key };

    private static long IdOf(MemoryRef reference) => long.Parse(reference.Id, CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_repeated_keyed_remember_answers_its_receipt_and_moves_no_revision()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);

        var first = await engine.RememberAsync(Fact("lives in Paris", "k1"));
        var held = (await store.GetAsync(Name, IdOf(first.Reference)))!;
        var again = await engine.RememberAsync(Fact("lives in Paris", "k1"));

        Assert.False(first.Receipt!.Replayed);
        Assert.Equal(MemoryOperationOutcome.Created, first.Receipt.Outcome);
        Assert.Equal((MemoryOperationKind.Remember, first.Reference), (first.Receipt.Kind, first.Receipt.Reference!.Value));
        Assert.True(again.Receipt!.Replayed);
        Assert.Equal(first.Reference, again.Reference);
        Assert.Equal(MemorySources.Graph, again.Ran);   // nothing ran: no embed, no annotation
        Assert.Equal(held.Revision, (await store.GetAsync(Name, IdOf(first.Reference)))!.Revision);
        Assert.Single(await store.SeedAsync(Name, "t", "s", null, 10));
    }

    [Fact]
    public async Task A_keyed_remember_landing_on_another_writers_content_reports_Refreshed()
    {
        var engine = Build();
        await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));

        var landed = await engine.RememberAsync(Fact("lives in Paris", "k1"));

        Assert.Equal(MemoryOperationOutcome.Refreshed, landed.Receipt!.Outcome);
    }

    [Fact]
    public async Task A_key_reused_for_other_content_or_another_call_is_refused_and_applies_nothing()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = await engine.RememberAsync(Fact("lives in Paris", "shared"));

        var other = await Assert.ThrowsAsync<MemoryKeyConflictException>(() => engine.RememberAsync(Fact("lives in Rome", "shared")));
        await Assert.ThrowsAsync<MemoryKeyConflictException>(() =>
            engine.EndAsync("t", paris.Reference, new MemoryEnding("moved") { OperationKey = "shared" }));

        Assert.Equal("shared", other.Key);
        Assert.Single(await store.SeedAsync(Name, "t", "s", null, 10));
        Assert.Null((await store.GetAsync(Name, IdOf(paris.Reference)))!.EndedReason);
        // the same key in another task is another key
        Assert.False((await engine.RememberAsync(Fact("lives in Rome", "shared", task: "u"))).Receipt!.Replayed);
    }

    [Fact]
    public async Task A_keyed_remember_states_no_supersession_or_relation_and_a_bad_key_is_refused()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"))).Reference;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.RememberAsync(Fact("lives in Rome", "k") with { Supersedes = [paris] }));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(Fact("lives in Rome", "k") with { Disputes = [paris] }));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(Fact("lives in Rome", " ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.RememberAsync(Fact("lives in Rome", new string('k', MemoryLimits.MaxOperationKeyChars + 1))));
        Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "lives"))).Items);
    }

    [Fact]
    public async Task An_engine_that_cannot_hold_a_receipt_refuses_the_key_rather_than_drop_it()
    {
        var plain = new DelegatingGraphStore();
        var engine = Build(plain);
        Assert.False(engine.HoldsReceipts);

        await Assert.ThrowsAsync<NotSupportedException>(() => engine.RememberAsync(Fact("lives in Paris", "k")));
        await Assert.ThrowsAsync<NotSupportedException>(() => engine.ReceiptAsync("t", "k"));
        Assert.Empty((await engine.RecallAsync(new MemoryQuery("t", "s", "lives"))).Items);
        // an un-keyed call is unchanged on every store
        await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));
        Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "lives"))).Items);

        var lexical = new LexicalMemoryEngine("lex", new FakeMemoryStore());
        await Assert.ThrowsAsync<NotSupportedException>(() => lexical.RememberAsync(Fact("lives in Paris", "k")));
    }

    [Fact]
    public async Task A_keyed_relation_repeated_adds_no_weight_and_the_receipt_reads_back()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var claim = (await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key is in the vault"))).Reference;
        var doubt = (await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key was rotated"))).Reference;

        var first = await engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Disputes, "relate-1");
        var again = await engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Disputes, "relate-1");

        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(doubt, again.Reference!.Value);
        Assert.Equal(1, (await store.NeighboursAsync(Name, "t", [IdOf(doubt)], 10)).Single().EdgeWeight);
        var read = await engine.ReceiptAsync("t", "relate-1");
        Assert.Equal((MemoryOperationKind.Relate, false), (read!.Kind, read.Replayed));
        Assert.Null(await engine.ReceiptAsync("t", "never-used"));
        await Assert.ThrowsAsync<MemoryKeyConflictException>(() =>
            engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Amends, "relate-1"));
    }

    [Fact]
    public async Task A_keyed_relation_repeated_after_an_endpoint_ended_answers_its_receipt_where_a_new_one_is_refused()
    {
        var clock = new Clock();
        var engine = Build(clock: clock);
        var claim = (await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key is in the vault"))).Reference;
        var doubt = (await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key was rotated"))).Reference;
        var first = await engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Disputes, "relate-1");
        await engine.EndAsync("t", claim, new MemoryEnding("rotated") { At = Jan.AddDays(1) });
        clock.Now = Jan.AddDays(2);   // inside the receipt's retention, after the claim's end

        var again = await engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Disputes, "relate-1");

        Assert.True(again.Replayed);
        Assert.Equal(first.Reference, again.Reference);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.RelateKeyedAsync("t", doubt, claim, MemoryRelation.Disputes, "relate-2"));
    }

    [Fact]
    public async Task A_keyed_end_repeated_after_its_successor_was_removed_answers_AlreadyEnded()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris") { ValidFrom = Jan });
        var rome = await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Rome") { ValidFrom = Mar });
        var ending = new MemoryEnding("moved away") { At = Mar, Successor = rome.Reference, OperationKey = "end-gone" };
        var first = await engine.EndAsync("t", paris.Reference, ending);
        await store.DeleteAsync(Name, [IdOf(rome.Reference)]);

        var again = await engine.EndAsync("t", paris.Reference, ending);

        Assert.Equal((MemoryEndOutcome.Ended, false), (first.Outcome, first.Receipt!.Replayed));
        Assert.Equal((MemoryEndOutcome.AlreadyEnded, true), (again.Outcome, again.Receipt!.Replayed));
        Assert.Equal(first.Revision, again.Revision);
        await Assert.ThrowsAsync<MemoryKeyConflictException>(() =>
            engine.EndAsync("t", paris.Reference, ending with { Reason = "a different reason" }));
    }

    [Fact]
    public async Task A_keyed_confirmation_repeated_stamps_once_and_a_keyed_failed_check_ends_once()
    {
        var store = new InMemoryMemoryGraphStore();
        var clock = new Clock();
        var engine = Build(store, clock: clock);
        var vault = (await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key is in the vault") { Recheck = "ask" })).Reference;
        var stale = (await engine.RememberAsync(new MemoryWrite("t", "s", "the build host is host-a") { Recheck = "ask" })).Reference;

        var held = await engine.ConfirmKeyedAsync("t", vault, MemoryCheckOutcome.Held, "confirm-1", at: Mar);
        var heldAgain = await engine.ConfirmKeyedAsync("t", vault, MemoryCheckOutcome.Held, "confirm-1", at: Mar);
        var failed = await engine.ConfirmKeyedAsync("t", stale, MemoryCheckOutcome.Failed, "confirm-2", at: Mar, note: "host-b now");
        var failedAgain = await engine.ConfirmKeyedAsync("t", stale, MemoryCheckOutcome.Failed, "confirm-2", at: Mar, note: "host-b now");

        Assert.Equal((MemoryConfirmOutcome.Stamped, false), (held.Outcome, held.Receipt!.Replayed));
        Assert.Equal((MemoryConfirmOutcome.Unchanged, true), (heldAgain.Outcome, heldAgain.Receipt!.Replayed));
        Assert.Equal(held.Revision, heldAgain.Revision);
        Assert.Equal((MemoryConfirmOutcome.Ended, MemoryOperationKind.Confirm), (failed.Outcome, failed.Receipt!.Kind));
        Assert.Equal((MemoryConfirmOutcome.AlreadyEnded, true), (failedAgain.Outcome, failedAgain.Receipt!.Replayed));
        Assert.Equal(Mar, (await store.GetAsync(Name, IdOf(vault)))!.ConfirmedAt);
    }

    [Fact]
    public async Task A_keyed_end_repeated_answers_AlreadyEnded_even_on_the_revision_it_was_reviewed_on()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var paris = await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris") { ValidFrom = Jan });
        var reviewed = (await store.GetAsync(Name, IdOf(paris.Reference)))!.Revision!.Value.ToString(CultureInfo.InvariantCulture);
        var ending = new MemoryEnding("moved away") { At = Mar, ExpectedRevision = reviewed, OperationKey = "end-1" };

        var first = await engine.EndAsync("t", paris.Reference, ending);
        var again = await engine.EndAsync("t", paris.Reference, ending);

        Assert.Equal((MemoryEndOutcome.Ended, false), (first.Outcome, first.Receipt!.Replayed));
        Assert.Equal((MemoryEndOutcome.AlreadyEnded, true), (again.Outcome, again.Receipt!.Replayed));
        Assert.Equal(first.Revision, again.Revision);
        // without the key, the same repeat is a stale review
        Assert.Equal(MemoryEndOutcome.Changed, (await engine.EndAsync("t", paris.Reference,
            new MemoryEnding("moved away") { At = Mar, ExpectedRevision = reviewed })).Outcome);
    }

    [Fact]
    public async Task A_receipt_expires_by_the_option_and_a_later_keyed_write_purges_it()
    {
        var store = new InMemoryMemoryGraphStore();
        var clock = new Clock();
        var engine = Build(store, clock: clock, options: new GraphMemoryOptions { ReceiptRetention = TimeSpan.FromDays(2) });
        var first = await engine.RememberAsync(Fact("lives in Paris", "k1"));
        Assert.Equal(Jan.AddDays(2), first.Receipt!.ExpiresAt);

        clock.Now = Jan.AddDays(3);
        Assert.Null(await engine.ReceiptAsync("t", "k1"));
        var again = await engine.RememberAsync(Fact("lives in Paris", "k1"));

        Assert.False(again.Receipt!.Replayed);   // a repeat after the expiry is a new call
        Assert.Equal(MemoryOperationOutcome.Refreshed, again.Receipt.Outcome);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { ReceiptRetention = TimeSpan.Zero });
    }

    [Fact]
    public async Task The_blend_routes_a_keyed_write_to_a_member_that_keeps_receipts_and_refuses_where_none_does()
    {
        var graph = Build(name: "blend/graph");
        var lexical = new LexicalMemoryEngine("blend/lex", new FakeMemoryStore());
        var blend = new CompositeMemoryEngine("blend", [lexical, graph]);
        Assert.True(blend.HoldsReceipts);

        var first = await blend.RememberAsync(Fact("lives in Paris", "k1"));
        var again = await blend.RememberAsync(Fact("lives in Paris", "k1"));

        Assert.Equal("blend/graph", first.Reference.Engine);
        Assert.True(again.Receipt!.Replayed);
        Assert.Equal(first.Reference, (await blend.ReceiptAsync("t", "k1"))!.Reference!.Value);
        Assert.Equal(MemoryEndOutcome.Ended, (await blend.EndAsync("t", first.Reference,
            new MemoryEnding("moved") { At = Mar, OperationKey = "e1" })).Outcome);
        await Assert.ThrowsAsync<NotSupportedException>(() => blend.ConfirmKeyedAsync("t", new MemoryRef("blend/lex", "1"),
            MemoryCheckOutcome.Held, "c1"));

        var lexOnly = new CompositeMemoryEngine("lexonly", [new LexicalMemoryEngine("lexonly/lex", new FakeMemoryStore())]);
        Assert.False(lexOnly.HoldsReceipts);
        await Assert.ThrowsAsync<NotSupportedException>(() => lexOnly.RememberAsync(Fact("lives in Paris", "k1")));
        await Assert.ThrowsAsync<NotSupportedException>(() => lexOnly.ReceiptAsync("t", "k1"));
    }
}
