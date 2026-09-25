using Lyntai.Cortex;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class ScoringServiceTests
{
    private static ScoreContext Ctx => new() { SessionId = "s1", Input = "in", Output = "out" };

    // ---- whose cancellation is it? --------------------------------------------------------------------
    // A BYO scorer may impose its own deadline, and its timeout arrives as the SAME exception type as the
    // caller's cancel. A bare `catch (OperationCanceledException) { throw; }` therefore let one scorer's own
    // deadline sink the whole evaluation — discarding every score already computed, since `results` is a
    // method-local — against a type doc promising "skips null/faulted results". This is the distinction the
    // 2026-09-09 fail-open sweep drew in Lyntai.Memory; Cortex was never swept.

    [Fact]
    public async Task A_scorers_OWN_timeout_is_skipped_like_any_other_fault()
    {
        var doomed = new FakeScorer("doomed",
            score: _ => throw new OperationCanceledException("the scorer's own deadline"));
        var ok = new FakeScorer("ok", score: _ => new ScoreResult(0.8));
        var service = new ScoringService([doomed, ok]);

        var results = await service.EvaluateAsync(Ctx);   // caller's token NEVER cancelled

        Assert.Single(results);
        Assert.Equal("ok", results[0].ScorerId);
    }

    [Fact]
    public async Task Scores_ALREADY_computed_survive_a_later_scorers_timeout()
    {
        // The sharper half: order matters, because the old rethrow threw away the earlier result too.
        var first = new FakeScorer("first", score: _ => new ScoreResult(0.9));
        var doomed = new FakeScorer("doomed", score: _ => throw new OperationCanceledException());
        var service = new ScoringService([first, doomed]);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
        Assert.Equal("first", results[0].ScorerId);
    }

    [Fact]
    public async Task The_CALLERS_cancellation_still_propagates()
    {
        // The control that keeps the fix honest: cancellation belongs to the caller and must NOT be
        // swallowed. Without this, "skip every OperationCanceledException" would pass everything above.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scorer = new FakeScorer("s", score: _ => throw new OperationCanceledException());
        var service = new ScoringService([scorer]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.EvaluateAsync(Ctx, cts.Token));
    }

    [Fact]
    public async Task A_score_STORES_own_timeout_still_returns_the_results()
    {
        // Same shape one layer down, and its own log line already promised "results still returned".
        var scorer = new FakeScorer("a", score: _ => new ScoreResult(0.7));
        var service = new ScoringService([scorer], Throwing.Of<Lyntai.Storage.IScoreStore>(() => new OperationCanceledException("the store's own deadline")));

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
    }

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
    public async Task Scorer_whose_Applies_is_false_is_not_scored()
    {
        // Applies(ctx) == false must gate the scorer BEFORE ScoreAsync — so ScoreAsync is never invoked
        // and the scorer contributes no ScoredResult. (Distinct from ScoreAsync returning null, which
        // means "ran, but not applicable".)
        var gated = new FakeScorer(
            "gated",
            score: _ => throw new InvalidOperationException("ScoreAsync must not be called when Applies is false"),
            applies: _ => false);
        var ok = new FakeScorer("ok", score: _ => new ScoreResult(0.8));
        var service = new ScoringService([gated, ok]);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
        Assert.Equal("ok", results[0].ScorerId);
        Assert.Equal(0, gated.Invocations); // never ran
    }

    [Fact]
    public async Task Applicable_scorer_still_scores_and_persists()
    {
        var store = new InMemoryScoreStore();
        var gated = new FakeScorer("gated", score: _ => new ScoreResult(0.3), applies: _ => false);
        var ok = new FakeScorer("ok", score: _ => new ScoreResult(0.9), applies: _ => true);
        var service = new ScoringService([gated, ok], store);

        var results = await service.EvaluateAsync(Ctx);

        Assert.Single(results);
        Assert.Equal("ok", results[0].ScorerId);
        var persisted = await store.GetAsync("s1");
        Assert.Single(persisted);
        Assert.Equal("ok", persisted[0].ScorerId); // only the applicable scorer was persisted
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

    // A dashboard reads/aggregates/exports through the SERVICE seam, not by reaching past it into the store.
    [Fact]
    public async Task Service_surfaces_read_aggregate_and_export_over_the_store()
    {
        var store = new InMemoryScoreStore();
        var service = new ScoringService([new FakeScorer("a", score: _ => new ScoreResult(0.6))], store);
        await service.EvaluateAsync(Ctx); // persists s1/a

        Assert.Single(await service.GetAsync("s1"));       // read
        Assert.Single(await service.AggregateAsync());     // cross-session aggregate
        Assert.Single(await service.ExportAsync());        // flat export
    }

    [Fact]
    public async Task Read_methods_are_empty_when_no_store_is_wired()
    {
        var service = new ScoringService([new FakeScorer("a", score: _ => new ScoreResult(0.5))]);
        Assert.Empty(await service.GetAsync("s1"));
        Assert.Empty(await service.AggregateAsync());
        Assert.Empty(await service.ExportAsync());
    }
}
