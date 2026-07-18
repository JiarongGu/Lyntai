using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IScoreStore"/> contract (A1) — run by the InMemory, SQLite, and
/// Postgres test classes so upsert/aggregate/export semantics are pinned identically.</summary>
public static class ScoreStoreContract
{
    private static ScoredResult R(string scorer, double score) => new(scorer, scorer.ToUpperInvariant(), "g", false, score);

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
