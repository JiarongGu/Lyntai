using Lyntai.Cortex;
using Lyntai.Inference;
using Lyntai.Inference.Tracing;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary><c>AddTextCallTracing</c>: every front-door call leaves one trace step and the selected scores — cached or
/// not, streamed or not — a scorer's own model call is never traced, and tracing never fails the call.</summary>
public class TextCallTracingTests
{
    private static TextRequest Req(string text = "hi", string consumer = "chat") =>
        new() { Messages = [TextMessage.User(text)], Consumer = consumer, Model = "m1" };

    private static FakeTextProvider Answering(string reply = "the reply", int times = 8)
    {
        var provider = new FakeTextProvider("p");
        for (var i = 0; i < times; i++)
            provider.Replies.Enqueue(new TextResponse(reply, ProviderVerdict.Ok, new TextUsage(3, 5, CostUsd: 0.01)));
        return provider;
    }

    private static ServiceProvider Build(FakeTextProvider provider, Action<LyntaiBuilder>? more = null)
    {
        var (traces, scores) = (new CapturingTraceStore(), new CapturingScoreStore());
        return BuildWith(traces, provider, more, services => services.AddSingleton(traces).AddSingleton(scores), scores);
    }

    private static ServiceProvider BuildWith(
        ITraceStore traces, FakeTextProvider provider, Action<LyntaiBuilder>? more = null,
        Action<IServiceCollection>? expose = null, IScoreStore? scores = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(traces);
        services.AddSingleton(scores ?? new CapturingScoreStore());
        expose?.Invoke(services);
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => provider).UseDefaultCandidates("p");
            more?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task One_call_leaves_one_trace_with_one_llm_step_carrying_usage_verdict_and_consumer()
    {
        using var sp = Build(Answering(), b => b.AddTextCallTracing());
        var traces = sp.GetRequiredService<CapturingTraceStore>();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        Assert.Equal("the reply", reply.Text);
        var trace = Assert.Single(traces.Saved);
        Assert.Equal("text-call", trace.Mode);
        var step = Assert.Single(trace.Steps);
        Assert.Equal("llm", step.Kind);
        Assert.Equal("chat", step.Label);
        Assert.Equal((3, 5, 0.01), (step.InputTokens, step.OutputTokens, step.CostUsd));
        Assert.True(step.DurationMs >= 0);
        Assert.Contains("Ok", step.Detail, StringComparison.Ordinal);
        Assert.Contains("m1", step.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("the reply", step.Detail, StringComparison.Ordinal);   // RecordText is off by default
    }

    [Fact]
    public async Task RecordText_stores_the_reply_cut_to_MaxRecordedChars()
    {
        using var sp = Build(Answering("abcdefghij"), b => b.AddTextCallTracing(o =>
        {
            o.RecordText = true;
            o.MaxRecordedChars = 4;
        }));

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        var detail = Assert.Single(Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved).Steps).Detail;
        Assert.Contains("abcd", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("abcde", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("hi", detail, StringComparison.Ordinal);                // the prompt never is
    }

    [Fact]
    public async Task Deterministic_scorers_run_under_the_trace_session_and_an_LLM_scorer_does_not_by_default()
    {
        var judge = new ModelScorer();
        using var sp = Build(Answering(), b => b
            .AddScorer(_ => new FakeScorer("len", score: c => new ScoreResult(c.Output!.Length / 100.0)))
            .AddScorer(s => judge.Bind(s))
            .AddTextCallTracing());

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        var trace = Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved);
        var (session, results) = Assert.Single(sp.GetRequiredService<CapturingScoreStore>().Saved);
        Assert.Equal(trace.SessionId, session);
        var scored = Assert.Single(results);
        Assert.Equal(("len", 0.09), (scored.ScorerId, scored.Score));
        Assert.Equal(0, judge.Invocations);
    }

    [Fact]
    public async Task An_opted_in_LLM_scorers_own_call_is_neither_traced_nor_scored()
    {
        var judge = new ModelScorer();
        var provider = Answering();
        using var sp = Build(provider, b => b
            .AddScorer(s => judge.Bind(s))
            .AddTextCallTracing(o => o.Scorers = _ => true));

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        Assert.Equal(1, judge.Invocations);
        Assert.Equal(2, provider.Calls.Count);                                         // the call, then the judge's
        Assert.Single(Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved).Steps);
        Assert.Single(sp.GetRequiredService<CapturingScoreStore>().Saved);
    }

    [Fact]
    public async Task A_cache_hit_is_traced()
    {
        var provider = Answering();
        using var sp = Build(provider, b => b.AddResponseCache().AddTextCallTracing());
        var client = sp.GetRequiredService<ITextClient>();

        await client.CompleteAsync(Req());
        await client.CompleteAsync(Req());

        Assert.Single(provider.Calls);
        Assert.Equal(2, sp.GetRequiredService<CapturingTraceStore>().Saved.Count);
    }

    [Fact]
    public async Task Inside_Into_calls_append_to_the_callers_recorder_and_no_per_call_trace_is_written()
    {
        using var sp = Build(Answering(), b => b.AddTextCallTracing());
        var client = sp.GetRequiredService<ITextClient>();
        var recorder = sp.GetRequiredService<ITraceService>().Begin("run-1", "batch");

        using (TextCallTracing.Into(recorder))
        {
            await client.CompleteAsync(Req(consumer: "first"));
            await client.CompleteAsync(Req(consumer: "second"));
        }
        var saved = sp.GetRequiredService<CapturingTraceStore>().Saved;
        Assert.Empty(saved);                                                           // the caller completes it
        await recorder.CompleteAsync();

        var trace = Assert.Single(saved);
        Assert.Equal(("run-1", "batch"), (trace.SessionId, trace.Mode));
        Assert.Equal(["first", "second"], trace.Steps.Select(s => s.Label));
        await client.CompleteAsync(Req());                                             // the scope is over
        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public async Task Inside_Into_each_calls_scores_are_kept_apart_from_the_other_calls_and_from_the_run()
    {
        // the real store UPSERTS on (session, scorer): one session id for the run would keep only the last call's
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("short", ProviderVerdict.Ok));
        provider.Replies.Enqueue(new TextResponse("a much longer reply", ProviderVerdict.Ok));
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider).UseDefaultCandidates("p").UseInMemoryStorage()
            .AddScorer(_ => new FakeScorer("len", score: c => new ScoreResult(c.Output!.Length)))
            .AddTextCallTracing());
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();
        var recorder = sp.GetRequiredService<ITraceService>().Begin("run-1", "batch");

        using (TextCallTracing.Into(recorder))
        {
            await client.CompleteAsync(Req());
            await client.CompleteAsync(Req());
        }
        await recorder.CompleteAsync();

        var scores = sp.GetRequiredService<IScoreStore>();
        Assert.Equal(5, Assert.Single(await scores.GetAsync("run-1#0")).Score);
        Assert.Equal(19, Assert.Single(await scores.GetAsync("run-1#1")).Score);
        Assert.Empty(await scores.GetAsync("run-1"));                                  // the run's own id is the app's
        var steps = (await sp.GetRequiredService<ITraceService>().GetAsync("run-1"))!.Steps;
        Assert.Equal(["run-1#0", "run-1#1"], steps.Select(s => s.Detail!.Split("scores=")[1]));
    }

    [Fact]
    public async Task A_throwing_trace_store_leaves_the_reply_intact()
    {
        using var sp = BuildWith(new ThrowingTraceStore(), Answering(), b => b.AddTextCallTracing());

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        Assert.Equal(("the reply", ProviderVerdict.Ok), (reply.Text, reply.Verdict));
    }

    [Fact]
    public async Task A_throwing_scorer_leaves_the_reply_intact_and_the_step_recorded()
    {
        // Applies is deliberately NOT swallowed by ScoringService, so this reaches the tracer's own guard
        using var sp = Build(Answering(), b => b
            .AddScorer(_ => new FakeScorer("bad", applies: _ => throw new InvalidOperationException("boom")))
            .AddTextCallTracing());

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        Assert.Equal(("the reply", ProviderVerdict.Ok), (reply.Text, reply.Verdict));
        Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved);
    }

    [Fact]
    public async Task A_cancellation_after_the_reply_does_not_fail_the_call()
    {
        using var cts = new CancellationTokenSource();
        using var sp = Build(Answering(), b => b
            .AddScorer(_ => new FakeScorer("cancels", score: _ =>
            {
                cts.Cancel();                                // ScoringService rethrows the caller's cancel
                cts.Token.ThrowIfCancellationRequested();
                return new ScoreResult(1);
            }))
            .AddTextCallTracing());

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req(), cts.Token);

        Assert.Equal("the reply", reply.Text);
    }

    [Fact]
    public async Task A_stream_read_to_the_end_is_traced_once_with_the_final_usage_and_scored_on_all_its_text()
    {
        var provider = Answering();
        provider.StreamScript = _ => [TextChunk.Content("ab"), TextChunk.Content("cd"), TextChunk.Final(new TextUsage(2, 4))];
        using var sp = Build(provider, b => b
            .AddScorer(_ => new FakeScorer("echo", score: c => new ScoreResult(c.Output == "abcd" ? 1 : 0)))
            .AddTextCallTracing());

        var text = "";
        await foreach (var chunk in sp.GetRequiredService<ITextClient>().StreamAsync(Req())) text += chunk.Text;

        Assert.Equal("abcd", text);
        var step = Assert.Single(Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved).Steps);
        Assert.Equal((2, 4), (step.InputTokens, step.OutputTokens));
        Assert.Equal(1, Assert.Single(Assert.Single(sp.GetRequiredService<CapturingScoreStore>().Saved).Results).Score);
    }

    [Fact]
    public async Task A_stream_abandoned_half_way_is_traced_once_on_dispose()
    {
        var provider = Answering();
        provider.StreamScript = _ => [TextChunk.Content("ab"), TextChunk.Content("cd"), TextChunk.Final()];
        using var sp = Build(provider, b => b.AddTextCallTracing());

        await foreach (var _ in sp.GetRequiredService<ITextClient>().StreamAsync(Req())) break;

        Assert.Single(Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved).Steps);
    }

    [Fact]
    public async Task Include_false_passes_the_call_through_untraced()
    {
        using var sp = Build(Answering(), b => b
            .AddScorer(_ => new FakeScorer("len"))
            .AddTextCallTracing(o => o.Include = r => r.Consumer != "chat"));

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        Assert.Empty(sp.GetRequiredService<CapturingTraceStore>().Saved);
        Assert.Empty(sp.GetRequiredService<CapturingScoreStore>().Saved);
    }

    [Fact]
    public async Task Repeating_AddTextCallTracing_re_applies_its_options_without_stacking_a_second_layer()
    {
        using var sp = Build(Answering(), b => b.AddTextCallTracing().AddTextCallTracing(o => o.Mode = "again"));

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req());

        var trace = Assert.Single(sp.GetRequiredService<CapturingTraceStore>().Saved);
        Assert.Equal("again", trace.Mode);
        Assert.Single(trace.Steps);
    }

    [Fact]
    public void A_custom_decorator_on_the_tracing_slot_is_refused_naming_both()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddTextCallTracing()
            .AddFrontDoorDecorator(LyntaiBuilder.TracingDecoratorOrder, (_, inner) => inner)));

        Assert.Contains(nameof(LyntaiBuilder.AddTextCallTracing), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_MaxRecordedChars_is_refused_at_composition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddTextCallTracing(o => o.MaxRecordedChars = -1)));
    }

    private sealed class CapturingTraceStore : ITraceStore
    {
        public List<RunTrace> Saved { get; } = [];

        public Task SaveAsync(RunTrace trace, CancellationToken ct = default)
        {
            lock (Saved) Saved.Add(trace);
            return Task.CompletedTask;
        }

        public Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(Saved.LastOrDefault(t => t.SessionId == sessionId));
    }

    private sealed class ThrowingTraceStore : ITraceStore
    {
        public Task SaveAsync(RunTrace trace, CancellationToken ct = default) => throw new InvalidOperationException("down");

        public Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default) => throw new InvalidOperationException("down");
    }

    private sealed class CapturingScoreStore : IScoreStore
    {
        public List<(string Session, IReadOnlyList<ScoredResult> Results)> Saved { get; } = [];

        public Task SaveAsync(string sessionId, IReadOnlyList<ScoredResult> results, CancellationToken ct = default)
        {
            lock (Saved) Saved.Add((sessionId, results));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScoredResult>>([.. Saved.Where(s => s.Session == sessionId).SelectMany(s => s.Results)]);

        public Task<IReadOnlyList<ScorerAggregate>> AggregateAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScorerAggregate>>([]);

        public Task<IReadOnlyList<ScoreExportEntry>> ExportAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScoreExportEntry>>([]);
    }

    /// <summary>An LLM judge: its score is a front-door call through the same (traced) client.</summary>
    private sealed class ModelScorer : IScorer
    {
        private IServiceProvider? _services;

        public int Invocations { get; private set; }
        public string Id => "judge";
        public string Name => "judge";
        public string Group => "llm";
        public bool IsLlm => true;

        public IScorer Bind(IServiceProvider services)
        {
            _services = services;
            return this;
        }

        public async Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct = default)
        {
            Invocations++;
            var client = _services!.GetRequiredService<ITextClient>();
            await client.CompleteAsync(new TextRequest { Messages = [TextMessage.User("judge it")], Consumer = "scoring" }, ct);
            return new ScoreResult(1);
        }
    }
}
