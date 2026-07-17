using Lyntai;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Prompts;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Core;

public class AddLyntaiTests
{
    [Fact]
    public async Task Minimal_setup_resolves_router_and_round_trips_a_completion()
    {
        var fake = new FakeLlmProvider("fake");
        fake.Replies.Enqueue(new LlmReply("routed!", LlmVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => fake)
            .DefaultCandidates("fake"));
        using var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<ILlmRouter>();
        var options = sp.GetRequiredService<LyntaiOptions>();
        var reply = await router.CompleteAsync(options.DefaultCandidates,
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal("routed!", reply.Text);
        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
    }

    [Fact]
    public async Task Cortex_services_resolve_without_any_storage()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("fake")));
        using var sp = services.BuildServiceProvider();

        var prompts = sp.GetRequiredService<IPromptRegistry>();
        Assert.Equal("plain x", await prompts.RenderAsync("p", "plain {v}",
            new Dictionary<string, string> { ["v"] = "x" }));

        var scoring = sp.GetRequiredService<IScoringService>();
        Assert.Empty(await scoring.EvaluateAsync(new ScoreContext { SessionId = "s" }));

        var traces = sp.GetRequiredService<ITraceService>();
        var recorder = traces.Begin("s", "test");
        recorder.Record(new TraceStep { Kind = "phase", Label = "noop" });
        await recorder.CompleteAsync(); // no store → structured no-op, must not throw
        Assert.Null(await traces.GetAsync("s"));
    }

    [Fact]
    public async Task Registered_kv_store_feeds_the_prompt_registry()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data[PromptRegistry.KeyPrefix + "p"] = "override {v}";

        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("fake")));
        using var sp = services.BuildServiceProvider();

        var rendered = await sp.GetRequiredService<IPromptRegistry>().RenderAsync("p", "default {v}",
            new Dictionary<string, string> { ["v"] = "x" });

        Assert.Equal("override x", rendered);
    }

    [Fact]
    public void Calling_AddLyntai_twice_throws_rather_than_shadowing_options()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("a")));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("b"))));
        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void Scorers_register_as_a_di_collection()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("fake"))
            .AddScorer<StubScorer>()
            .AddScorer<StubScorer>());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(2, sp.GetServices<IScorer>().Count());
    }

    private sealed class StubScorer : IScorer
    {
        public string Id => "stub";
        public string Name => "stub";
        public string Group => "test";
        public bool IsLlm => false;
        public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct) =>
            Task.FromResult<ScoreResult?>(null);
    }
}
