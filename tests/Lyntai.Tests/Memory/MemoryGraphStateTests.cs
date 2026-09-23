using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>The plan/apply split both in-process graph stores run: a plan is inert until applied, and a batch
/// that names one record twice compounds exactly as sequential calls would.</summary>
public class MemoryGraphStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static GraphNodeWrite Write(string content) =>
        new("e", "t", "s", "h", content, MemoryGrade.Associative, 3, 1, null);

    [Fact]
    public void A_plan_changes_nothing_until_it_is_applied()
    {
        var graph = new MemoryGraphState(() => Now);

        var change = graph.PlanUpsert(Write("the deploy key rotates monthly"));

        Assert.Empty(graph.Seed("e", "t", null, null, 10));
        Assert.Equal(default, graph.Totals("e"));

        graph.Apply(change);

        Assert.Equal(["the deploy key rotates monthly"], graph.Seed("e", "t", null, null, 10).Select(n => n.Content));
        Assert.Equal(1, graph.Totals("e").Ordinal);
    }

    [Fact]
    public void A_pair_written_twice_in_one_plan_strengthens_twice()
    {
        var graph = new MemoryGraphState(() => Now);
        var a = Apply(graph, graph.PlanUpsert(Write("alpha")));
        var b = Apply(graph, graph.PlanUpsert(Write("beta")));

        graph.Apply(graph.PlanLink("e", [new GraphEdgeWrite(a, b, Weight: 2), new GraphEdgeWrite(a, b, Weight: 3)]));

        Assert.Equal(5, graph.Get("e", a)!.Strength, 9);
    }

    [Fact]
    public void A_node_touched_twice_in_one_plan_counts_both_recalls()
    {
        var graph = new MemoryGraphState(() => Now);
        var a = Apply(graph, graph.PlanUpsert(Write("alpha")));

        graph.Apply(graph.PlanTouch("e", [new GraphTouch(a, 4), new GraphTouch(a, 6)]));

        var node = graph.Get("e", a)!;
        Assert.Equal((2, 6d), (node.RecallCount, node.Stability));
    }

    [Fact]
    public void A_refresh_that_changes_only_state_leaves_the_record_the_same()
    {
        var graph = new MemoryGraphState(() => Now);
        graph.Apply(graph.PlanUpsert(Write("alpha") with { Metadata = new Dictionary<string, string> { ["k"] = "v" } }));

        var (before, after) = graph.PlanUpsert(Write("alpha") with
        {
            HeadlineStated = false,
            GradeStated = false,
            Metadata = new Dictionary<string, string> { ["k"] = "v" },
        }).Nodes.Single();

        Assert.True(MemoryGraphState.SameRecord(before!.Record, after.Record));
        Assert.NotEqual(before.State, after.State);
    }

    private static long Apply(MemoryGraphState graph, GraphChange change)
    {
        graph.Apply(change);
        return change.Nodes.Single().After.Id;
    }
}
