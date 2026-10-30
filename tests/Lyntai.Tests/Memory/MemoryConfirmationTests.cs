using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>A stored fact can be re-checked by its host, and a failed check ends it</b> (<c>docs/DECISIONS.md</c> D240, D241). The
/// library stores an opaque text and an instant, runs nothing, and ends nothing by age: <c>Held</c> stamps, <c>Failed</c> is the
/// host's confirmation that the claim no longer holds, and a run that did not finish is reported by calling nothing.
/// </summary>
public class MemoryConfirmationTests
{
    private const string Name = "check";
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jul = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, string name = Name) =>
        new(name, store ?? new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()], Clock = () => Jul });

    private static MemoryWrite Fact(string content, string? recheck = "run-check-1") =>
        new("t", "s", content) { ValidFrom = Jan, Recheck = recheck };

    private static MemoryQuery Ask(MemoryValidityReading? reading = null) =>
        new("t", "s", "vault", Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static long Id(MemoryRef reference) => long.Parse(reference.Id, CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_stored_recheck_is_returned_by_the_due_read_and_never_read_by_the_library()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the vault door is red", recheck: "see the door log, entry 17"));
        await engine.RememberAsync(Fact("the vault key is brass", recheck: null));

        var due = await engine.DueAsync("t", "s", Jul);

        var item = Assert.Single(due);
        Assert.Equal(("the vault door is red", "see the door log, entry 17", null), (item.Content, item.Recheck, item.ConfirmedAt));
        Assert.NotNull(item.Revision);
        // a re-remember that names none keeps the stored text
        await engine.RememberAsync(Fact("the vault door is red", recheck: null));
        Assert.Equal("see the door log, entry 17", Assert.Single(await engine.DueAsync("t", "s", Jul)).Recheck);
    }

    [Fact]
    public async Task A_held_check_stamps_and_changes_nothing_a_reader_reads_and_leaves_due()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Build(store);
        var door = (await engine.RememberAsync(Fact("the vault door is red"))).Reference;
        var before = (await store.GetAsync(Name, Id(door)))!;

        var held = await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Held, at: Mar);

        Assert.Equal(MemoryConfirmOutcome.Stamped, held.Outcome);
        var after = (await store.GetAsync(Name, Id(door)))!;
        Assert.Equal((Mar, before.Stability, before.RecallCount, before.Headline), (after.ConfirmedAt, after.Stability, after.RecallCount, after.Headline));
        Assert.Equal(["the vault door is red"], (await engine.RecallAsync(Ask())).Items.Select(i => i.Content!));
        Assert.Empty(await engine.DueAsync("t", "s", Mar));                     // confirmed at that instant is not due before it
        Assert.Single(await engine.DueAsync("t", "s", Mar.AddDays(1)));         // and is due again after
        // never backwards
        Assert.Equal(MemoryConfirmOutcome.Unchanged, (await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Held, at: Jan)).Outcome);
        Assert.Equal(Mar, (await engine.DueAsync("t", "s", Jul)).Single().ConfirmedAt);
    }

    [Fact]
    public async Task A_failed_check_ends_the_entry_at_the_confirmation_with_the_note_and_it_reads_back_under_History()
    {
        var engine = Build();
        var door = (await engine.RememberAsync(Fact("the vault door is red"))).Reference;

        var failed = await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, at: Mar, note: "the door log says blue");

        Assert.Equal(MemoryConfirmOutcome.Ended, failed.Outcome);
        Assert.Empty((await engine.RecallAsync(Ask())).Items);
        Assert.Empty(await engine.DueAsync("t", "s", Jul));
        var history = (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single();
        Assert.Equal(("the door log says blue", Mar), (history.Validity!.EndedReason, history.Validity.End));
        // the fact as it held before the confirmation is still readable then, and a repeat changes nothing
        Assert.Single((await engine.RecallAsync(Ask(MemoryValidityReading.AsOf(Jan.AddDays(10))))).Items);
        Assert.Equal(MemoryConfirmOutcome.AlreadyEnded, (await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, at: Jul, note: "again")).Outcome);
        // the end is never earlier than the confirmation: an unstated instant is the engine's clock
        var other = (await engine.RememberAsync(Fact("the vault key is brass"))).Reference;
        await engine.ConfirmAsync("t", other, MemoryCheckOutcome.Failed, note: "gone");
        Assert.Equal(Jul, (await engine.RecallAsync(Ask(MemoryValidityReading.History))).Items.Single(i => i.Content!.Contains("key")).Validity!.End);
    }

    [Fact]
    public async Task A_failed_check_needs_its_note_and_a_run_that_did_not_finish_is_reported_by_calling_nothing()
    {
        var engine = Build();
        var door = (await engine.RememberAsync(Fact("the vault door is red"))).Reference;

        await Assert.ThrowsAsync<ArgumentException>(() => engine.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, note: "  "));

        // a timeout, a missing access or an ambiguous answer: the host calls nothing, so the entry is exactly as it was and still due
        Assert.Equal(["the vault door is red"], (await engine.RecallAsync(Ask())).Items.Select(i => i.Content!));
        Assert.Single(await engine.DueAsync("t", "s", Jul));
    }

    [Fact]
    public async Task A_confirmation_is_applied_only_to_the_revision_the_due_read_returned()
    {
        var engine = Build();
        var door = (await engine.RememberAsync(Fact("the vault door is red"))).Reference;
        var read = (await engine.DueAsync("t", "s", Jul)).Single().Revision;
        await engine.RememberAsync(Fact("the vault door is red") with { Headline = "reworded under the checker" });

        var failed = await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, at: Mar, note: "blue", expectedRevision: read);
        var held = await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Held, at: Mar, expectedRevision: read);

        Assert.Equal(MemoryConfirmOutcome.Changed, failed.Outcome);
        Assert.Equal(MemoryConfirmOutcome.Changed, held.Outcome);
        Assert.Equal(failed.Revision, held.Revision);                        // both report the revision a fresh review reads
        Assert.Single((await engine.RecallAsync(Ask())).Items);              // nothing ended
        Assert.Null((await engine.DueAsync("t", "s", Jul)).Single().ConfirmedAt);   // nothing stamped
        Assert.Equal(MemoryConfirmOutcome.Ended,
            (await engine.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, at: Mar, note: "blue", expectedRevision: failed.Revision)).Outcome);
    }

    [Fact]
    public async Task A_recheck_is_bounded_and_a_store_that_cannot_hold_it_refuses_rather_than_drop_it()
    {
        var engine = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.RememberAsync(Fact("the vault door is red", recheck: new string('x', MemoryLimits.MaxRecheckChars + 1))));
        Assert.Empty((await engine.RecallAsync(Ask())).Items);   // nothing was written

        var bare = Build(new EndableOnlyStore());
        await Assert.ThrowsAsync<NotSupportedException>(() => bare.RememberAsync(Fact("the vault door is red")));
        var door = (await bare.RememberAsync(Fact("the vault door is red", recheck: null))).Reference;
        await Assert.ThrowsAsync<NotSupportedException>(() => bare.ConfirmAsync("t", door, MemoryCheckOutcome.Held));
        await Assert.ThrowsAsync<NotSupportedException>(() => bare.DueAsync("t", "s", Jul));
    }

    [Fact]
    public async Task The_blend_routes_a_confirmation_by_reference_and_asks_every_member_that_can_what_is_due()
    {
        var graph = Build(name: "blend/graph");
        var lexical = new LexicalMemoryEngine("blend/lex", new FakeMemoryStore());
        var blend = new CompositeMemoryEngine("blend", [lexical, graph]);
        var door = (await graph.RememberAsync(Fact("the vault door is red"))).Reference;

        Assert.Single(await blend.DueAsync("t", "s", Jul));
        Assert.Equal(MemoryConfirmOutcome.Stamped, (await blend.ConfirmAsync("t", door, MemoryCheckOutcome.Held, at: Mar)).Outcome);
        Assert.Equal(MemoryConfirmOutcome.Ended, (await blend.ConfirmAsync("t", door, MemoryCheckOutcome.Failed, at: Jul, note: "blue")).Outcome);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            blend.ConfirmAsync("t", new MemoryRef("blend/lex", "1"), MemoryCheckOutcome.Held));
    }

    [Fact]
    public async Task A_recheck_and_its_stamp_travel_through_an_export()
    {
        var source = Build();
        var door = (await source.RememberAsync(Fact("the vault door is red", recheck: "door log 17"))).Reference;
        await source.ConfirmAsync("t", door, MemoryCheckOutcome.Held, at: Mar);
        var file = new StringWriter();
        await source.ExportAsync("t", "s", file);

        var target = Build();
        await target.ImportAsync("t", "s", new StringReader(file.ToString()));

        var due = Assert.Single(await target.DueAsync("t", "s", Jul));
        Assert.Equal(("door log 17", Mar), (due.Recheck, due.ConfirmedAt));
    }

    [Fact]
    public void The_confirmation_code_runs_no_process_and_opens_no_connection()
    {
        // the library stores text and an instant and ends nothing by age; running the check is the host's
        var root = RepoRoot();
        foreach (var file in new[] { "src/Lyntai.Core/Memory/MemoryConfirmation.cs", "src/Lyntai.Core/Memory/MemoryChangeGate.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            foreach (var forbidden in new[] { "Process", "HttpClient", "Socket", "WebRequest" })
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lyntai.slnx"))) return dir.FullName;
        throw new InvalidOperationException("no Lyntai.slnx above the test assembly");
    }

    /// <summary>An endable store that cannot hold a re-check: what a BYO store written before the re-check existed is.</summary>
    private sealed class EndableOnlyStore : DelegatingGraphStore, IEndableMemoryGraphStore
    {
        public Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions, CancellationToken ct = default) =>
            ((ITemporalMemoryGraphStore)Inner).SupersedeAsync(engine, supersessions, ct);

        public Task<GraphEndResult> EndAsync(string engine, GraphEnd end, CancellationToken ct = default) =>
            ((IEndableMemoryGraphStore)Inner).EndAsync(engine, end, ct);
    }
}
