using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IScoreStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so upsert/aggregate/export semantics are pinned identically.</summary>
public static class ScoreStoreContract
{
    private static ScoredResult R(string scorer, double score) => new(scorer, scorer.ToUpperInvariant(), "g", false, score);

    [Unscoped("fixed session ids")]
    public static async Task Rescore_replaces_not_accumulates(IScoreStore store)
    {
        await store.SaveAsync("s1", [R("a", 0.5), R("b", 0.3)]);
        // re-score s1's "a" — must REPLACE the row, not add a second "a"
        await store.SaveAsync("s1", [R("a", 0.9)]);

        var results = await store.GetAsync("s1");
        Assert.Equal(2, results.Count);                                  // still just a + b
        Assert.Equal(0.9, results.Single(x => x.ScorerId == "a").Score); // latest value won
        Assert.Equal(0.3, results.Single(x => x.ScorerId == "b").Score); // b untouched
    }

    /// <summary>Two batches of one session naming the same scorers in OPPOSITE order, released together, round
    /// after round: each completes. A database that locks the rows in arrival order deadlocks one of them.</summary>
    [Unscoped("fixed session ids")]
    public static async Task Batches_of_one_session_in_opposite_order_both_complete(IScoreStore store)
    {
        for (var round = 0; round < 20; round++)
        {
            var session = $"opposed-{round}";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var forward = Task.Run(async () => { await gate.Task; await store.SaveAsync(session, [R("x", 0.1), R("y", 0.2), R("z", 0.3)]); });
            var backward = Task.Run(async () => { await gate.Task; await store.SaveAsync(session, [R("z", 0.4), R("y", 0.5), R("x", 0.6)]); });
            gate.SetResult();

            await Task.WhenAll(forward, backward);

            Assert.Equal(3, (await store.GetAsync(session)).Count);
        }
    }

    [Unscoped("aggregates the whole table")]
    public static async Task Aggregate_is_per_scorer_across_sessions(IScoreStore store)
    {
        await store.SaveAsync("s1", [R("a", 0.4), R("b", 1.0)]);
        await store.SaveAsync("s2", [R("a", 0.6)]);

        var agg = (await store.AggregateAsync()).ToDictionary(x => x.ScorerId);
        Assert.Equal(0.5, agg["a"].AverageScore, 6); // (0.4 + 0.6) / 2
        Assert.Equal(2, agg["a"].Count);
        Assert.Equal(1.0, agg["b"].AverageScore, 6);
        Assert.Equal(1, agg["b"].Count);
        Assert.Equal("A", agg["a"].ScorerName);
    }

    /// <summary>Scorers sharing an id but named differently across sessions aggregate under ONE name on every
    /// backend — the greatest, ordinally, which is what the SQL stores' <c>MAX</c> reads.</summary>
    [Unscoped("aggregates the whole table")]
    public static async Task Aggregate_names_a_scorer_the_same_on_every_backend(IScoreStore store)
    {
        await store.SaveAsync("s1", [new ScoredResult("a", "Alpha", "g", false, 0.1)]);
        await store.SaveAsync("s2", [new ScoredResult("a", "Beta", "g", false, 0.3)]);

        Assert.Equal("Beta", Assert.Single(await store.AggregateAsync()).ScorerName);
    }

    /// <summary>The same rule where the greatest name depends on case: a database locale sorts `alpha-1` below `Beta-1`
    /// and a byte order does not, so only an ordinal `MAX` agrees with the in-memory and file stores.</summary>
    [Unscoped("aggregates the whole table")]
    public static async Task Aggregate_takes_the_ordinally_greatest_name_whatever_its_case(IScoreStore store)
    {
        await store.SaveAsync("s1", [new ScoredResult("a", "alpha-1", "g", false, 0.1)]);
        await store.SaveAsync("s2", [new ScoredResult("a", "Beta-1", "g", false, 0.3)]);

        Assert.Equal("alpha-1", Assert.Single(await store.AggregateAsync()).ScorerName);
    }

    [Unscoped("exports the whole table")]
    public static async Task Export_dumps_every_session_scorer_score(IScoreStore store)
    {
        await store.SaveAsync("s1", [R("a", 0.4), R("b", 1.0)]);
        await store.SaveAsync("s2", [R("a", 0.6)]);

        var rows = await store.ExportAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r is { SessionId: "s1", ScorerId: "a", Score: 0.4 });
        Assert.Contains(rows, r => r is { SessionId: "s1", ScorerId: "b", Score: 1.0 });
        Assert.Contains(rows, r => r is { SessionId: "s2", ScorerId: "a", Score: 0.6 });
    }
}
