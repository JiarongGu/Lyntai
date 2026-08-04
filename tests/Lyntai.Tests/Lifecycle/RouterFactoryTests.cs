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

    // ---- one call = one caller's provider set ----------------------------------------------------------

    // Every instance built for a slot reports that slot as its Id, and a router resolves candidates by id
    // with first-match-wins. Two configurations of one id in ONE call therefore leave the second built,
    // pooled and unreachable — silently. Fail at the call that made the mistake instead.
    [Fact]
    public void Two_registrations_sharing_a_slot_in_one_call_throw_and_name_it()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);

        var error = Assert.Throws<ArgumentException>(() => factory.For([
            new ProviderRegistration<IGenerationProvider>(Key("cfg-a"), () => new FakeGenerationProvider { Id = "a1111" }),
            new ProviderRegistration<IGenerationProvider>(Key("cfg-b"), () => new FakeGenerationProvider { Id = "a1111" }),
        ]));

        Assert.Contains("a1111", error.Message);
        Assert.Equal("providers", error.ParamName);
        Assert.Equal(0, pool.Statistics.Created);   // rejected before anything was built or pooled
    }

    // Ids are matched case-insensitively everywhere else, so a slot that differs only in case is the SAME
    // backend and must be rejected too — a case-sensitive check would let the bug straight back in.
    [Fact]
    public void Slots_differing_only_in_case_are_the_same_backend_and_throw()
    {
        var factory = Factory(new BoundedProviderPool<IGenerationProvider>());

        Assert.Throws<ArgumentException>(() => factory.For([
            new ProviderRegistration<IGenerationProvider>(Key("cfg-a", "a1111"), () => new FakeGenerationProvider { Id = "a1111" }),
            new ProviderRegistration<IGenerationProvider>(Key("cfg-b", "A1111"), () => new FakeGenerationProvider { Id = "A1111" }),
        ]));
    }

    [Fact]
    public async Task Distinct_slots_in_one_call_still_compose_normally()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);
        var primary = new FakeGenerationProvider { Id = "a1111" };
        var secondary = new FakeGenerationProvider { Id = "comfyui" };

        var router = factory.For([
            new ProviderRegistration<IGenerationProvider>(Key("cfg", "a1111"), () => primary),
            new ProviderRegistration<IGenerationProvider>(Key("cfg", "comfyui"), () => secondary),
        ]);
        var result = await router.GenerateAsync([new GenerationCandidate("comfyui")], Request());

        Assert.True(result.IsOk);
        Assert.Equal(1, secondary.GenerateCalls);
        Assert.Equal(0, primary.GenerateCalls);
        Assert.Equal(2, pool.Statistics.Created);
    }

    // The single-registration path is the common one and must not have grown a cost or a false positive.
    [Fact]
    public async Task A_single_registration_is_unaffected_by_the_duplicate_check()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var backend = new FakeGenerationProvider { Id = "a1111" };

        var router = Factory(pool).For([new ProviderRegistration<IGenerationProvider>(Key("a"), () => backend)]);
        var result = await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(result.IsOk);
        Assert.Equal(1, pool.Statistics.Created);
    }

    // The instance overload never touches the pool, so it is not subject to the check at all — a caller
    // that hands over two same-id instances is describing today's DI collection, which already de-duplicates
    // by first-wins and must keep doing so.
    [Fact]
    public async Task The_instance_overload_is_not_subject_to_the_duplicate_check()
    {
        var first = new FakeGenerationProvider { Id = "a1111" };
        var second = new FakeGenerationProvider { Id = "a1111" };

        var router = Factory(new BoundedProviderPool<IGenerationProvider>()).For([first, (IGenerationProvider)second]);
        var result = await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(result.IsOk);
        Assert.Equal(1, first.GenerateCalls);
        Assert.Equal(0, second.GenerateCalls);
    }

    // The same guard on the LLM side: LlmRouter._byId is built with map.TryAdd, so first wins there too.
    [Fact]
    public void The_llm_factory_rejects_two_registrations_sharing_a_slot()
    {
        var pool = new BoundedProviderPool<ILlmProvider>();
        var factory = LlmFactory(pool, new DeadHostTracker());

        var error = Assert.Throws<ArgumentException>(() => factory.For([
            new ProviderRegistration<ILlmProvider>(
                ProviderKey.For("openai").With("tenant", "a").Build(), () => new FakeLlmProvider("openai")),
            new ProviderRegistration<ILlmProvider>(
                ProviderKey.For("OpenAI").With("tenant", "b").Build(), () => new FakeLlmProvider("OpenAI")),
        ]));

        Assert.Contains("OpenAI", error.Message, StringComparison.OrdinalIgnoreCase);
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
