using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Two live entries that may disagree are marked by their writer</b> (<c>docs/DECISIONS.md</c> D240, D241).
/// <c>Disputes</c> and <c>Amends</c> end nothing, are stated and never inferred, and reach a reader as the
/// writer's attributed assertion — only where both ends are in the reading.
/// </summary>
public class MemoryRelationTests
{
    private const string Name = "rel";
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build(IMemoryGraphStore? store = null, string name = Name) =>
        new(name, store ?? new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()], Clock = () => new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            });

    private static MemoryWrite Fact(string content, string task = "t") => new(task, "s", content) { ValidFrom = Jan };

    private static MemoryQuery Ask(string query = "lives", MemoryValidityReading? reading = null) =>
        new("t", "s", query, Detail: MemoryDetail.Full) { Validity = reading ?? default };

    private static MemoryItem Item(MemoryRecall recall, string content) => recall.Items.Single(i => i.Content == content);

    [Fact]
    public async Task A_stated_dispute_is_read_on_both_ends_and_ends_nothing()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        var berlin = (await engine.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] })).Reference;

        var recall = await engine.RecallAsync(Ask());

        Assert.Equal(2, recall.Items.Count);   // both stay current
        var asserting = Item(recall, "the client lives in Berlin").Relations!.Single();
        Assert.Equal((MemoryRelation.Disputes, paris, true), (asserting.Relation, asserting.Other, asserting.AssertedByThis));
        var disputed = Item(recall, "the client lives in Paris").Relations!.Single();
        Assert.Equal((MemoryRelation.Disputes, berlin, false), (disputed.Relation, disputed.Other, disputed.AssertedByThis));
    }

    [Fact]
    public async Task An_amendment_is_stated_after_the_fact_and_a_repeat_changes_nothing()
    {
        var engine = Build();
        var record = (await engine.RememberAsync(Fact("the client lives in Paris, works at Acme"))).Reference;
        var fix = (await engine.RememberAsync(Fact("the client works at Initech"))).Reference;

        await engine.RelateAsync("t", fix, record, MemoryRelation.Amends);
        await engine.RelateAsync("t", fix, record, MemoryRelation.Amends);

        var relations = Item(await engine.RecallAsync(Ask("client")), "the client works at Initech").Relations!;
        Assert.Equal([(MemoryRelation.Amends, record, true)], relations.Select(r => (r.Relation, r.Other, r.AssertedByThis)));
    }

    [Fact]
    public async Task A_relation_is_reported_only_where_both_ends_are_in_the_reading()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        await engine.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] });

        // a page of one has no other end in it
        Assert.All((await engine.RecallAsync(Ask() with { Limit = 1 })).Items, i => Assert.Null(i.Relations));

        // once one end has ended, the Current reading no longer holds it and so reports no relation
        await engine.EndAsync("t", paris, new MemoryEnding("moved") { At = Mar });
        var current = await engine.RecallAsync(Ask());
        Assert.Null(current.Items.Single().Relations);
        // the ledger still has both, so it still says so
        var history = await engine.RecallAsync(Ask(reading: MemoryValidityReading.History));
        Assert.All(history.Items, i => Assert.NotNull(i.Relations));
    }

    [Fact]
    public async Task Co_activation_and_a_prune_leave_a_relation_as_it_was()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        await engine.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] });
        await engine.RememberAsync(Fact("the client lives in Rome"));

        for (var i = 0; i < 4; i++) await engine.RecallAsync(Ask());   // each recall links what it returns together
        await engine.PruneAsync("t", "s", minRetrievability: null, olderThan: TimeSpan.FromDays(3650));

        var recall = await engine.RecallAsync(Ask());
        Assert.Equal(3, recall.Items.Count);
        Assert.Equal(1, recall.Items.Count(i => i.Relations is { Count: 1 } r && r[0].Relation == MemoryRelation.Disputes && !r[0].AssertedByThis));
        Assert.Equal(1, recall.Items.Count(i => i.Relations is { Count: 1 } r && r[0].AssertedByThis));
        Assert.Null(Item(recall, "the client lives in Rome").Relations);
    }

    [Fact]
    public async Task The_engine_states_no_relation_it_was_not_told()
    {
        // the measured step: false disputes and unaffected-claim recall, by construction and pinned here
        var engine = Build();
        var refs = new List<MemoryRef>();
        for (var i = 0; i < 30; i++) refs.Add((await engine.RememberAsync(Fact($"the client lives at number {i} of the street"))).Reference);
        for (var i = 0; i < 5; i++)
            await engine.RememberAsync(Fact($"the client lives at a corrected number {i}") with { Disputes = [refs[i]] });

        var recall = await engine.RecallAsync(Ask() with { Limit = 50 });

        Assert.Equal(35, recall.Items.Count);   // an unaffected claim is never lost to a dispute elsewhere
        var asserted = recall.Items.Where(i => i.Relations is not null).SelectMany(i => i.Relations!).Count(r => r.AssertedByThis);
        Assert.Equal(5, asserted);              // exactly the five stated, so no false dispute
        Assert.Equal(25, recall.Items.Count(i => i.Relations is null));
    }

    [Fact]
    public async Task A_relation_refuses_what_is_not_two_live_entries_of_one_task()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        var ended = (await engine.RememberAsync(Fact("the client lives in Rome"))).Reference;
        await engine.EndAsync("t", ended, new MemoryEnding("moved") { At = Mar });
        var elsewhere = (await engine.RememberAsync(Fact("the client lives in Oslo", task: "other"))).Reference;
        var write = Fact("the client lives in Berlin");

        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(write with { Disputes = [ended] }));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(write with { Amends = [elsewhere] }));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(write with { Disputes = [new MemoryRef("other-engine", "1")] }));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RememberAsync(Fact("the client lives in Paris") with { Disputes = [paris] }));   // itself
        Assert.Equal(["the client lives in Paris"],   // nothing was written by any of them
            (await engine.RecallAsync(Ask())).Items.Select(i => i.Content!));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.RelateAsync("t", paris, paris, MemoryRelation.Disputes));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RelateAsync("t", paris, ended, MemoryRelation.Disputes));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.RelateAsync("t", paris, elsewhere, MemoryRelation.Amends));
    }

    [Fact]
    public async Task A_store_that_cannot_read_a_typed_link_refuses_rather_than_write_what_it_cannot_show()
    {
        var engine = Build(new TemporalOnlyStore());
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] }));
        Assert.Single((await engine.RecallAsync(Ask())).Items);
    }

    [Fact]
    public async Task The_blend_sends_a_relating_write_to_the_member_that_owns_the_entries()
    {
        var graph = Build(name: "blend/graph");
        var blend = new CompositeMemoryEngine("blend", [new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()), graph]);
        var paris = (await blend.RememberAsync(Fact("the client lives in Paris"))).Reference;

        await blend.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] });
        var rome = (await blend.RememberAsync(Fact("the client lives in Rome"))).Reference;
        await blend.RelateAsync("t", rome, paris, MemoryRelation.Amends);

        var recall = await blend.RecallAsync(Ask());
        Assert.Equal(2, recall.Items.Single(i => i.Reference == paris).Relations!.Count);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            blend.RelateAsync("t", new MemoryRef("blend/lex", "1"), new MemoryRef("blend/lex", "2"), MemoryRelation.Disputes));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            blend.RelateAsync("t", paris, new MemoryRef("blend/lex", "2"), MemoryRelation.Disputes));
    }

    [Fact]
    public async Task The_tool_and_the_rendered_memory_say_it_as_an_assertion_with_fixed_words()
    {
        var engine = Build();
        var paris = (await engine.RememberAsync(Fact("the client lives in Paris"))).Reference;
        await engine.RememberAsync(Fact("the client lives in Berlin") with { Disputes = [paris] });
        var recall = await engine.RecallAsync(Ask());

        using var doc = JsonDocument.Parse(MemoryTools.Render(recall));
        var asserted = doc.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.TryGetProperty("relations", out _) && i.GetProperty("relations")[0].GetProperty("assertedByThis").GetBoolean());
        Assert.Equal("disputes", asserted.GetProperty("relations")[0].GetProperty("relation").GetString());

        var text = MemoryComposition.Render("", recall.Items);
        Assert.Contains("asserts a dispute of another entry shown here", text, StringComparison.Ordinal);
        Assert.Contains("disputed by another entry shown here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong", text, StringComparison.Ordinal);   // it says who asserted, never who is right
    }

    private sealed class TemporalOnlyStore : DelegatingGraphStore, ITemporalMemoryGraphStore
    {
        public Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions,
            CancellationToken ct = default) =>
            ((ITemporalMemoryGraphStore)Inner).SupersedeAsync(engine, supersessions, ct);
    }
}
