using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Lifecycle;

/// <summary>The DI wiring: the pool, its two strategies and the admission table reach the container, and the
/// router factories that consume them resolve for BOTH domains.
///
/// <para>The property these tests exist to protect is the one the seam was built for — swapping reuse for
/// rebuild-every-call is a REGISTRATION change with no edit at any call site. Everything else here guards
/// the additive promise: an app that never calls one of the new methods resolves exactly what it resolved
/// before.</para></summary>
public class ProviderPoolWiringTests
{
    private static ProviderKey Key(string value) => ProviderKey.For("a1111").With("v", value).Build();

    private static ServiceProvider Provider(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    private static IProviderPool<IGenerationProvider> PoolFrom(Action<LyntaiBuilder> configure)
    {
        // A pool is not IDisposable (it disposes nothing, ever), so it outlives the container that built
        // it — dispose the container anyway, so every test here leaves nothing behind.
        using var sp = Provider(configure);
        return sp.GetRequiredService<IProviderPool<IGenerationProvider>>();
    }

    [Fact]
    public void The_default_pool_reuses()
    {
        var pool = PoolFrom(_ => { });
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Same(first, second);
    }

    // The point of the seam: the SAME call site changes behaviour purely by registration.
    [Fact]
    public void UseTransientProviders_switches_the_strategy_with_no_call_site_change()
    {
        var pool = PoolFrom(b => b.UseTransientProviders());
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.NotSame(first, second);
    }

    // …and at the call site that actually matters: the router factory, whose code is IDENTICAL in both
    // halves below. Only the registration differs, and the number of backends built follows it.
    [Fact]
    public void The_strategy_swap_reaches_the_router_factory_unchanged()
    {
        Assert.Equal(1, BackendsBuiltOverThreeCalls(_ => { }));
        Assert.Equal(3, BackendsBuiltOverThreeCalls(b => b.UseTransientProviders()));

        static int BackendsBuiltOverThreeCalls(Action<LyntaiBuilder> configure)
        {
            using var sp = Provider(b =>
            {
                configure(b);
                b.AddGenerationProvider(_ => new FakeGenerationProvider { Id = "unused" });
            });
            var factory = sp.GetRequiredService<IGenerationRouterFactory>();

            var built = 0;
            for (var i = 0; i < 3; i++)
                factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () =>
                {
                    built++;
                    return new FakeGenerationProvider { Id = "a1111" };
                })]);

            return built;
        }
    }

    [Fact]
    public void UseProviderPool_carries_its_bounds()
    {
        var pool = PoolFrom(b => b.UseProviderPool(new ProviderPoolOptions { MaxEntries = 1 }));
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Equal(1, pool.Statistics.Live);
    }

    // Bounds passed once must survive a LATER bare call, which is why the null branch only ensures a
    // default exists (TryAdd) instead of registering a fresh default-valued one. Registering one
    // unconditionally is the obvious-looking line, and it silently reverts the bounds to 64: the second
    // registration wins on resolution and nothing else in this file notices.
    [Fact]
    public void A_bare_UseProviderPool_after_one_carrying_bounds_keeps_the_bounds()
    {
        var pool = PoolFrom(b => b
            .UseProviderPool(new ProviderPoolOptions { MaxEntries = 1 })
            .UseProviderPool());
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Equal(1, pool.Statistics.Live);
    }

    // …and the other order: bounds stated explicitly are a decision, so they REPLACE whatever default is
    // already registered rather than losing to it.
    [Fact]
    public void Bounds_passed_after_a_bare_UseProviderPool_still_win()
    {
        var pool = PoolFrom(b => b
            .UseProviderPool()
            .UseProviderPool(new ProviderPoolOptions { MaxEntries = 1 }));
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Equal(1, pool.Statistics.Live);
    }

    [Fact]
    public void The_pool_is_a_singleton_so_reuse_spans_resolutions()
    {
        using var sp = Provider(_ => { });

        Assert.Same(sp.GetRequiredService<IProviderPool<IGenerationProvider>>(),
                    sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
    }

    // Registered as an OPEN generic, so the chat seam gets a pool from the same registration — and a
    // concrete backend type would be a DIFFERENT pool no router ever consults, which is why nothing
    // registers one.
    [Fact]
    public void Both_provider_seams_resolve_a_pool_from_the_one_registration()
    {
        using var sp = Provider(_ => { });

        Assert.IsType<BoundedProviderPool<IGenerationProvider>>(sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
        Assert.IsType<BoundedProviderPool<ILlmProvider>>(sp.GetRequiredService<IProviderPool<ILlmProvider>>());
    }

    [Fact]
    public void UseTransientProviders_switches_both_seams()
    {
        using var sp = Provider(b => b.UseTransientProviders());

        Assert.IsType<TransientProviderPool<IGenerationProvider>>(sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
        Assert.IsType<TransientProviderPool<ILlmProvider>>(sp.GetRequiredService<IProviderPool<ILlmProvider>>());
    }

    [Fact]
    public void A_host_registered_pool_wins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderPool<IGenerationProvider>>(new TransientProviderPool<IGenerationProvider>());
        services.AddLyntai(_ => { });

        using var sp = services.BuildServiceProvider();
        Assert.IsType<TransientProviderPool<IGenerationProvider>>(sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
    }

    // A Use* method runs inside the configure callback, BEFORE AddLyntai's own TryAdd defaults — so the
    // host's choice is already there and the default is a no-op. Order INSIDE the callback must not matter
    // either: a generation Add* also seeds the default pool, so the swap has to replace rather than TryAdd.
    [Fact]
    public void The_strategy_survives_a_generation_registration_made_before_it()
    {
        using var sp = Provider(b => b
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a1111" })
            .UseTransientProviders());

        Assert.IsType<TransientProviderPool<IGenerationProvider>>(sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
    }

    [Fact]
    public void Admission_options_are_configurable_and_resolvable()
    {
        using var sp = Provider(b => b.ConfigureProviderAdmission(o => o.BySlot["local-diffusion"] = 1));
        var admission = sp.GetRequiredService<ProviderAdmission>();

        Assert.NotNull(admission);
    }

    // NotNull is not enough: the options object has to be the one the callback mutated, or the limit is
    // configured and unenforced.
    [Fact]
    public async Task The_configured_admission_limit_is_the_one_enforced()
    {
        using var sp = Provider(b => b.ConfigureProviderAdmission(o => o.BySlot["local-diffusion"] = 1));
        var admission = sp.GetRequiredService<ProviderAdmission>();
        var key = ProviderKey.For("local-diffusion").With("v", "a").Build();

        var first = await admission.EnterAsync(key);
        var second = admission.EnterAsync(key);

        Assert.False(second.IsCompleted);      // the only permit is held
        first.Dispose();
        (await second).Dispose();
    }

    // Two calls compose onto ONE options instance rather than the second silently replacing the first.
    [Fact]
    public async Task Admission_configuration_accumulates_across_calls()
    {
        using var sp = Provider(b => b
            .ConfigureProviderAdmission(o => o.BySlot["local-diffusion"] = 1)
            .ConfigureProviderAdmission(o => o.BySlot["sd-cli"] = 1));
        var admission = sp.GetRequiredService<ProviderAdmission>();

        foreach (var slot in new[] { "local-diffusion", "sd-cli" })
        {
            var key = ProviderKey.For(slot).With("v", "a").Build();
            var held = await admission.EnterAsync(key);
            var queued = admission.EnterAsync(key);

            Assert.False(queued.IsCompleted);
            held.Dispose();
            (await queued).Dispose();
        }
    }

    // Admission left unconfigured must stay unlimited — the default table is registered for everyone.
    [Fact]
    public async Task Unconfigured_admission_admits_everyone()
    {
        using var sp = Provider(_ => { });
        var admission = sp.GetRequiredService<ProviderAdmission>();
        var key = ProviderKey.For("a1111").With("v", "a").Build();

        var first = await admission.EnterAsync(key);
        var second = admission.EnterAsync(key);

        Assert.True(second.IsCompleted);
        first.Dispose();
        (await second).Dispose();
    }

    // Both domains reach their factory through the container; leaving the chat one unregistered would make
    // half the feature unreachable.
    [Fact]
    public void Both_router_factories_resolve()
    {
        using var sp = Provider(b => b.AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a1111" }));

        Assert.NotNull(sp.GetRequiredService<ILlmRouterFactory>());
        Assert.NotNull(sp.GetRequiredService<IGenerationRouterFactory>());
    }

    // The chat factory is registered even for an app with no generation domain at all.
    [Fact]
    public async Task The_llm_factory_routes_over_a_pooled_configuration()
    {
        using var sp = Provider(_ => { });
        var provider = new FakeLlmProvider("openai");

        var router = sp.GetRequiredService<ILlmRouterFactory>().For([
            new ProviderRegistration<ILlmProvider>(
                ProviderKey.For("openai").With("tenant", "a").Build(), () => provider)]);
        var reply = await router.CompleteAsync([new LlmCandidate("openai")],
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Single(provider.Calls);
    }

    // The additive promise: an app that opts into none of this still resolves the container-composed router
    // over the registered providers, keyed on the provider id and never touching the pool.
    [Fact]
    public async Task An_app_that_opts_into_nothing_still_routes_over_its_registered_backends()
    {
        var backend = new FakeGenerationProvider { Id = "a1111" };
        using var sp = Provider(b => b.AddGenerationProvider(_ => backend));

        var result = await sp.GetRequiredService<IGenerationRouter>().GenerateAsync(
            [new GenerationCandidate("a1111")],
            new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a cat" });

        Assert.True(result.IsOk);
        Assert.Equal(1, backend.GenerateCalls);
        Assert.Equal(0, sp.GetRequiredService<IProviderPool<IGenerationProvider>>().Statistics.Created);
    }
}
