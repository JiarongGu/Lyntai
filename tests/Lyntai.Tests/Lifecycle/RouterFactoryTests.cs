using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Lifecycle;

/// <summary>The router factories: a router is built PER CALL over the provider set the caller chose, while
/// the bookkeeping it routes against — the dead-host tracker, the limiter, the ledger, the admission table —
/// is injected once and shared by every router handed out.
///
/// <para>That split is the whole point. A consumer that hand-builds a router per render also rebuilds its
/// tracker per render, so a failing backend can never actually be benched; the tests below pin both halves,
/// and the instance overload pins that an app which never touches the pool behaves exactly as it did.</para></summary>
public class RouterFactoryTests
{
    private static GenerationRequest Request() => new() { Kind = GenerationKinds.Image, Prompt = "a cat" };

    private static ProviderKey Key(string value, string slot = "a1111") =>
        ProviderKey.For(slot).With("v", value).Build();

    // every remaining constructor parameter is optional: no policy, no governance, no admission
    private static GenerationRouterFactory Factory(
        IProviderPool<IGenerationProvider> pool, DeadHostTracker? tracker = null) =>
        new(pool, tracker ?? new DeadHostTracker());

    [Fact]
    public async Task The_pooled_overload_routes_over_the_pooled_instance()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);
        var backend = new FakeGenerationProvider { Id = "a1111" };

        var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () => backend)]);
        var result = await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(result.IsOk);
        Assert.Equal(1, backend.GenerateCalls);
    }

    [Fact]
    public void The_pooled_overload_reuses_across_calls()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);
        var built = 0;

        for (var i = 0; i < 3; i++)
            factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () =>
            {
                built++;
                return new FakeGenerationProvider { Id = "a1111" };
            })]);

        Assert.Equal(1, built);
    }

    // The regression this whole task exists for: rebuilding the router per call must NOT reset the bench.
    [Fact]
    public async Task Cooldown_survives_a_router_rebuilt_on_every_call()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 2);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);

        for (var i = 0; i < 2; i++)
        {
            var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () => failing)]);
            await router.GenerateAsync([new GenerationCandidate("a1111")], Request());
        }

        Assert.True(tracker.IsDead($"generation::{Key("a")}"));
    }

    // Two configurations of one id must bench independently.
    [Fact]
    public async Task Two_configurations_of_one_backend_bench_independently()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("cfg-a"), () => failing)]);
        await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(tracker.IsDead($"generation::{Key("cfg-a")}"));
        Assert.False(tracker.IsDead($"generation::{Key("cfg-b")}"));
    }

    // The container-composed path keeps today's behaviour exactly: keyed on the id, no pool involved.
    [Fact]
    public async Task The_instance_overload_keys_cooldown_on_the_provider_id()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = factory.For([(IGenerationProvider)failing]);
        await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(tracker.IsDead("generation::a1111"));
        Assert.Equal(0, pool.Statistics.Created);
    }

    // ---- the container-composed path -------------------------------------------------------------------

    // The decorator order the factory now owns is load-bearing: the limiter sits INSIDE the budget, so a
    // call refused for spend never consumes a permit. With only one permit available, the discriminator is
    // the verdict of the second call — Refused if the budget is outermost, RateLimited if the order flipped.
    [Fact]
    public async Task The_rate_limiter_sits_inside_the_budget_so_a_refused_call_keeps_its_permit()
    {
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 5.0 };
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => backend)
            .AddGenerationUsageBudget(budget => budget.MaxCostUsd = 1.0)
            .AddGenerationRateLimit(limits =>
            {
                limits.PermitsPerSecond = 1;      // one permit, and no refill inside a test's microseconds
                limits.Burst = 1;
                limits.MaxWait = TimeSpan.Zero;   // over the rate = refused immediately, never queued
            }));
        using var sp = services.BuildServiceProvider();
        var router = sp.GetRequiredService<IGenerationRouter>();

        var first = await router.GenerateAsync([new GenerationCandidate("hosted")], Request());
        var second = await router.GenerateAsync([new GenerationCandidate("hosted")], Request());

        Assert.True(first.IsOk);                                     // spent the only permit, and 5.0
        Assert.Equal(GenerationVerdict.Refused, second.Verdict);     // NOT RateLimited — the budget refused first
        Assert.Contains("cost budget", second.Detail);
        Assert.Equal(1, backend.GenerateCalls);
    }

    // EnsureRouter runs inside the configure callback, BEFORE AddLyntai registers the options-built tracker.
    // A TryAddSingleton<DeadHostTracker>() there would win and silently swap in the defaults, discarding the
    // configured threshold, cooldown and logger for BOTH domains — with no other test noticing.
    [Fact]
    public void The_shared_tracker_still_comes_from_LyntaiOptions()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.Options.DeadHostThreshold = 1;   // the default is 3
            cfg.AddGenerationProvider(_ => new FakeGenerationProvider { Id = "hosted" });
        });
        using var sp = services.BuildServiceProvider();

        var tracker = sp.GetRequiredService<DeadHostTracker>();
        tracker.RecordFailure("hosted");

        Assert.True(tracker.IsDead("hosted"));   // one failure benches only at the CONFIGURED threshold
    }

    // ---- the LLM factory ------------------------------------------------------------------------------

    private static LlmRouterFactory LlmFactory(IProviderPool<ILlmProvider> pool, DeadHostTracker tracker) =>
        new(pool, tracker, new LyntaiOptions());

    private static LlmRequest Prompt() => new() { Messages = [LlmMessage.User("hi")] };

    [Fact]
    public async Task The_llm_pooled_overload_routes_over_the_pooled_instance_and_benches_its_configuration()
    {
        var pool = new BoundedProviderPool<ILlmProvider>();
        var tracker = new DeadHostTracker(threshold: 1);
        var key = ProviderKey.For("openai").With("tenant", "a").Build();

        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));

        var router = LlmFactory(pool, tracker)
            .For([new ProviderRegistration<ILlmProvider>(key, () => provider)]);
        await router.CompleteAsync([new LlmCandidate("openai")], Prompt());

        Assert.Single(provider.Calls);
        Assert.True(tracker.IsDead(key.ToString()));
        Assert.False(tracker.IsDead("openai"));       // never the bare id when a configuration is known
    }

    [Fact]
    public async Task The_llm_instance_overload_keys_cooldown_on_the_provider_id()
    {
        var pool = new BoundedProviderPool<ILlmProvider>();
        var tracker = new DeadHostTracker(threshold: 1);

        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));

        var router = LlmFactory(pool, tracker).For([(ILlmProvider)provider]);
        await router.CompleteAsync([new LlmCandidate("openai")], Prompt());

        Assert.True(tracker.IsDead("openai"));
        Assert.Equal(0, pool.Statistics.Created);
    }
}
