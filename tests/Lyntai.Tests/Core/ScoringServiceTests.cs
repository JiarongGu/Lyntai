using Lyntai.Cortex;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

public class ScoringServiceTests
{
    private static ScoreContext Ctx => new() { SessionId = "s1", Input = "in", Output = "out" };

    [Fact]
    public async Task Two_scorers_both_run_grouping_preserved()
    {
        var a = new FakeScorer("a", group: "deterministic", _ => new ScoreResult(0.9, "good"));
        var b = new FakeScorer("b", group: "style", _ => new ScoreResult(0.4));
        var service = new ScoringService([a, b]);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, a.Invocations);
        Assert.Equal(1, b.Invocations);
        Assert.Equal("deterministic", results.Single(r => r.ScorerId == "a").Group);
        Assert.Equal("style", results.Single(r => r.ScorerId == "b").Group);
        Assert.Equal(0.9, results.Single(r => r.ScorerId == "a").Score);
    }

    [Fact]
    public async Task Null_result_is_omitted()
    {
        var a = new FakeScorer("a", score: _ => new ScoreResult(1.0));
        var na = new FakeScorer("not-applicable", score: _ => null);
        var service = new ScoringService([a, na]);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
        Assert.Equal("a", results[0].ScorerId);
        Assert.Equal(1, na.Invocations); // it ran, it just didn't apply
    }

    [Fact]
    public async Task Faulted_scorer_is_skipped_fail_open()
    {
        var boom = new FakeScorer("boom", score: _ => throw new InvalidOperationException("scorer bug"));
        var ok = new FakeScorer("ok", score: _ => new ScoreResult(0.7));
        var service = new ScoringService([boom, ok]);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
        Assert.Equal("ok", results[0].ScorerId);
    }

    [Fact]
    public async Task Dry_run_scores_without_persisting_even_when_a_store_is_wired()
    {
        var store = new InMemoryScoreStore();
        var service = new ScoringService([new FakeScorer("a", score: _ => new ScoreResult(0.5))], store);

        var dry = await service.EvaluateAsync(Ctx, persist: false);
        Assert.Single(dry);                          // scored...
        Assert.Empty(await store.GetAsync("s1"));    // ...but nothing written

        await service.EvaluateAsync(Ctx);            // default overload persists
        Assert.Single(await store.GetAsync("s1"));
    }
}
