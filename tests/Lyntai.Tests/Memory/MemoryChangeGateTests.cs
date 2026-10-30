using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>An approver seam on what a write tool would supersede or end</b> (<c>docs/DECISIONS.md</c> D241). A model's proposal
/// is not a confirmation: with no approver nothing ends, a denial reads back, an approval is bound to the revision the
/// proposer read, and the library approves nothing by itself.
/// </summary>
public class MemoryChangeGateTests
{
    private const string Name = "gate";
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphMemoryEngine Build() =>
        new(Name, new InMemoryMemoryGraphStore(), null,
            new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()], Clock = () => new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            });

    private static MemoryWrite Fact(string content, string task = "t") => new(task, "s", content) { ValidFrom = Jan };

    private static MemoryQuery Ask() => new("t", "s", "lives", Detail: MemoryDetail.Full);

    private static async Task<(MemoryRef Ref, string Revision)> ReadAsync(GraphMemoryEngine engine, string content)
    {
        var item = (await engine.RecallAsync(Ask())).Items.Single(i => i.Content == content);
        return (item.Reference, item.Revision!);
    }

    private static string[] Live(MemoryRecall recall) => [.. recall.Items.Select(i => i.Content!).Order(StringComparer.Ordinal)];

    private sealed class Recording(Func<MemoryChangeRequest, MemoryChangeDecision?> answer) : IMemoryChangeApprover
    {
        public List<MemoryChangeRequest> Seen { get; } = [];

        public Task<MemoryChangeDecision> ApproveAsync(MemoryChangeRequest request, CancellationToken ct = default)
        {
            Seen.Add(request);
            return Task.FromResult(answer(request)!);
        }
    }

    [Fact]
    public async Task With_no_approver_nothing_ends_and_a_supersession_lands_live_and_unlinked()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var paris = await ReadAsync(engine, "the client lives in Paris");
        var gate = new MemoryChangeGate([]);

        var end = await gate.EndAsync(engine, "t", paris.Ref, paris.Revision, "moved");
        Assert.Equal(MemoryChangeStatus.NoApprover, end.Status);
        Assert.Equal(["the client lives in Paris"], Live(await engine.RecallAsync(Ask())));

        var superseded = await gate.SupersedeAsync(engine, Fact("the client lives in Berlin"),
            [new MemoryChangeTarget(paris.Ref, paris.Revision)], "moved");
        Assert.Equal(MemoryChangeStatus.NoApprover, superseded.Status);
        Assert.NotNull(superseded.Successor);
        // both are current, and neither names the other
        var recall = await engine.RecallAsync(Ask() with { Validity = MemoryValidityReading.History });
        Assert.Equal(["the client lives in Berlin", "the client lives in Paris"], Live(recall));
        Assert.All(recall.Items, i => Assert.Null(i.Validity?.SupersededBy));
    }

    [Fact]
    public async Task A_denial_reads_back_and_changes_nothing_and_the_first_refusal_decides()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var paris = await ReadAsync(engine, "the client lives in Paris");
        var first = new Recording(_ => MemoryChangeDecision.Deny("the source record still says Paris"));
        var second = new Recording(_ => MemoryChangeDecision.Approve);
        var gate = new MemoryChangeGate([first, second]);

        var result = await gate.EndAsync(engine, "t", paris.Ref, paris.Revision, "moved", "an email");

        Assert.Equal(MemoryChangeStatus.Denied, result.Status);
        Assert.Contains("source record still says Paris", result.Message, StringComparison.Ordinal);
        Assert.Empty(second.Seen);   // never asked
        Assert.Equal(["the client lives in Paris"], Live(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task An_approver_that_throws_or_answers_nothing_refuses()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var paris = await ReadAsync(engine, "the client lives in Paris");

        foreach (var approver in new IMemoryChangeApprover[]
                 { new Recording(_ => throw new InvalidOperationException("down")), new Recording(_ => null) })
        {
            var result = await new MemoryChangeGate([approver]).EndAsync(engine, "t", paris.Ref, paris.Revision, "moved");
            Assert.Equal(MemoryChangeStatus.Denied, result.Status);
        }
        Assert.Equal(["the client lives in Paris"], Live(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public async Task An_approval_ends_what_was_read_and_the_approver_sees_the_exact_proposal()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var paris = await ReadAsync(engine, "the client lives in Paris");
        var approver = new Recording(_ => MemoryChangeDecision.Approve);
        var gate = new MemoryChangeGate([approver]);

        var result = await gate.SupersedeAsync(engine, Fact("the client lives in Berlin"),
            [new MemoryChangeTarget(paris.Ref, paris.Revision)], "moved", "the form of 2026-03-01");

        Assert.Equal(MemoryChangeStatus.Applied, result.Status);
        var seen = Assert.Single(approver.Seen);
        Assert.Equal((MemoryChangeKind.Supersede, "t", "the client lives in Berlin", "moved", "the form of 2026-03-01"),
            (seen.Kind, seen.TaskKey, seen.Claim, seen.Reason, seen.Evidence));
        Assert.Equal([new MemoryChangeTarget(paris.Ref, paris.Revision)], seen.Targets);
        Assert.Equal(["the client lives in Berlin"], Live(await engine.RecallAsync(Ask())));
        var history = (await engine.RecallAsync(Ask() with { Validity = MemoryValidityReading.History })).Items.Single(i => i.Content!.Contains("Paris"));
        Assert.Equal((result.Successor, "moved"), (history.Validity!.SupersededBy, history.Validity.EndedReason));
    }

    [Fact]
    public async Task An_approval_carrying_a_stale_revision_changes_nothing_and_a_fresh_one_lands()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var read = await ReadAsync(engine, "the client lives in Paris");
        await engine.RememberAsync(Fact("the client lives in Paris") with { Headline = "reworded after it was read" });
        var gate = new MemoryChangeGate([new Recording(_ => MemoryChangeDecision.Approve)]);

        var stale = await gate.EndAsync(engine, "t", read.Ref, read.Revision, "moved");

        Assert.Equal(MemoryChangeStatus.Changed, stale.Status);
        Assert.Equal(["the client lives in Paris"], Live(await engine.RecallAsync(Ask())));
        var fresh = stale.Revisions![read.Ref];
        Assert.Equal(MemoryChangeStatus.Applied, (await gate.EndAsync(engine, "t", read.Ref, fresh, "moved")).Status);
        Assert.Empty((await engine.RecallAsync(Ask())).Items);

        // the supersession's successor is stored once and the retry finds the same entry
        await engine.RememberAsync(Fact("the client lives in Rome"));
        var rome = await ReadAsync(engine, "the client lives in Rome");
        await engine.RememberAsync(Fact("the client lives in Rome") with { Headline = "changed again" });
        var first = await gate.SupersedeAsync(engine, Fact("the client lives in Oslo"), [new MemoryChangeTarget(rome.Ref, rome.Revision)], "moved");
        var retry = await gate.SupersedeAsync(engine, Fact("the client lives in Oslo"), [new MemoryChangeTarget(rome.Ref, first.Revisions![rome.Ref])], "moved");
        Assert.Equal(MemoryChangeStatus.Changed, first.Status);
        Assert.Equal(first.Successor, retry.Successor);
        Assert.Equal(MemoryChangeStatus.Applied, retry.Status);
    }

    [Fact]
    public async Task A_reference_the_host_did_not_issue_is_refused_in_words_the_proposer_can_read()
    {
        var engine = Build();
        await engine.RememberAsync(Fact("the client lives in Paris"));
        var elsewhere = (await engine.RememberAsync(Fact("the client lives in Oslo", task: "other"))).Reference;
        var gate = new MemoryChangeGate([new Recording(_ => MemoryChangeDecision.Approve)]);

        foreach (var bad in new[] { elsewhere, new MemoryRef("another-engine", "1"), new MemoryRef(Name, "9999") })
        {
            var result = await gate.EndAsync(engine, "t", bad, "1", "moved");
            Assert.Equal(MemoryChangeStatus.Refused, result.Status);
            Assert.StartsWith("Refused:", result.Message, StringComparison.Ordinal);
        }
        Assert.Equal(MemoryChangeStatus.Refused, (await gate.EndAsync(engine, "t", elsewhere, " ", "moved")).Status);   // no revision
        Assert.Equal(MemoryChangeStatus.Refused, (await gate.EndAsync(engine, "t", elsewhere, "1", "")).Status);       // no reason
        Assert.Equal(["the client lives in Paris"], Live(await engine.RecallAsync(Ask())));
    }

    [Fact]
    public void The_library_ships_no_approver_that_approves_by_itself()
    {
        var shipped = typeof(IMemoryChangeApprover).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IMemoryChangeApprover).IsAssignableFrom(t));
        Assert.Empty(shipped);
    }

    [Fact]
    public void A_registered_approver_reaches_the_gate_through_the_container()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddMemoryChangeApprover(_ => new Recording(_ => MemoryChangeDecision.Approve)));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<MemoryChangeGate>());
        Assert.Single(provider.GetServices<IMemoryChangeApprover>());
    }
}
