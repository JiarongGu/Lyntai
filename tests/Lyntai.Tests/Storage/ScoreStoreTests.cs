using Lyntai.Cortex;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class ScoreStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqliteScoreStore _store;

    public ScoreStoreTests() => _store = new SqliteScoreStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Rescore_replaces() => ScoreStoreContract.Rescore_replaces_not_accumulates(_store);
    [Fact] public Task Aggregate() => ScoreStoreContract.Aggregate_is_per_scorer_across_sessions(_store);
    [Fact] public Task Export() => ScoreStoreContract.Export_dumps_every_session_scorer_score(_store);

    [Fact]
    public async Task Save_and_load_by_session()
    {
        await _store.SaveAsync("s1",
        [
            new ScoredResult("outcome", "Outcome", "deterministic", false, 0.123456789, "close"),
            new ScoredResult("judge", "Judge", "llm", true, 0.8, null),
        ]);
        await _store.SaveAsync("other-session", [new ScoredResult("x", "X", "g", false, 0.5)]);

        var results = await _store.GetAsync("s1");

        Assert.Equal(2, results.Count);
        Assert.Equal("outcome", results[0].ScorerId);
        Assert.Equal("deterministic", results[0].Group);
        Assert.False(results[0].IsLlm);
        Assert.True(results[1].IsLlm);
        Assert.Null(results[1].Reason);
    }

    [Fact]
    public async Task Doubles_round_trip_exactly_the_affinity_trap()
    {
        // 1.0 is the trap: without CAST(score AS REAL) SQLite can hand back INTEGER 1
        await _store.SaveAsync("s", [
            new ScoredResult("a", "A", "g", false, 0.123456789),
            new ScoredResult("b", "B", "g", false, 1.0),
            new ScoredResult("c", "C", "g", false, 0.0),
        ]);

        var results = await _store.GetAsync("s");

        Assert.Equal(0.123456789, results[0].Score);
        Assert.Equal(1.0, results[1].Score);
        Assert.Equal(0.0, results[2].Score);
    }

    [Fact]
    public async Task Unknown_session_returns_empty()
    {
        Assert.Empty(await _store.GetAsync("nope"));
    }
}
