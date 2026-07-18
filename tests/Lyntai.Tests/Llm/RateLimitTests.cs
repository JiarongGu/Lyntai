using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Caching;
using Lyntai.Llm.RateLimiting;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

/// <summary>Client-side rate limiting: the token bucket's reservation math (deterministic via an explicit
/// `now`), the front-door decorator's throttle-or-refuse, and DI composition with the cache (a cached hit
/// must not spend a permit).</summary>
public class RateLimitTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

    private static TokenBucketRateLimiter Limiter(Action<RateLimitOptions> tune)
    {
        var options = new LyntaiOptions();
        tune(options.RateLimit);
        return new TokenBucketRateLimiter(options, () => T0); // bucket start = T0; tests pass explicit now
    }

    // ---- token bucket reservation math ---------------------------------------------------------------

    [Fact]
    public void Burst_permits_go_immediately_then_the_next_waits()
    {
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 2; o.MaxWait = TimeSpan.FromSeconds(10); });

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0));       // 1st of burst 2
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0));       // 2nd of burst 2
        var wait = limiter.TryReserve("c", T0);                         // bucket empty → must wait ~1/rate
        Assert.NotNull(wait);
        Assert.True(wait!.Value > TimeSpan.Zero);
    }

    [Fact]
    public void Bucket_refills_over_time()
    {
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 1; o.MaxWait = TimeSpan.FromSeconds(10); });

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0));       // spends the one permit
        Assert.NotNull(limiter.TryReserve("c", T0));                    // empty now (a wait, not immediate)
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0.AddSeconds(5))); // refilled after 5s → immediate
    }

    [Fact]
    public void A_wait_beyond_max_wait_is_refused()
    {
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 1; o.MaxWait = TimeSpan.Zero; });

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0));       // the one permit
        Assert.Null(limiter.TryReserve("c", T0));                      // would wait 1s > MaxWait 0 → refuse
    }

    [Fact]
    public void Per_consumer_rate_is_isolated_and_others_use_the_global()
    {
        var limiter = Limiter(o =>
        {
            o.PermitsPerSecond = 0; // no global limit
            o.MaxWait = TimeSpan.Zero;
            o.PerConsumer["tight"] = new ConsumerRate(PermitsPerSecond: 1, Burst: 1);
        });

        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("tight", T0));
        Assert.Null(limiter.TryReserve("tight", T0));                  // tight consumer is capped
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("other", T0));  // no global limit → unlimited
        Assert.Equal(TimeSpan.Zero, limiter.TryReserve("other", T0));
    }

    [Fact]
    public void No_limit_configured_always_clears_immediately()
    {
        var limiter = Limiter(_ => { }); // defaults: PermitsPerSecond 0 = unlimited
        for (var i = 0; i < 5; i++) Assert.Equal(TimeSpan.Zero, limiter.TryReserve("c", T0));
    }

    // ---- decorator -----------------------------------------------------------------------------------

    [Fact]
    public async Task Over_the_rate_the_decorator_refuses_without_calling_the_provider()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("first", LlmVerdict.Ok));
        // fixed clock → no refill between the two calls, MaxWait 0 → the 2nd refuses immediately
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 1; o.MaxWait = TimeSpan.Zero; });
        var client = new RateLimitedLlmClient(inner, limiter);

        var first = await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("a")] });
        var second = await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("b")] });

        Assert.Equal("first", first.Text);
        Assert.Equal(LlmVerdict.RateLimited, second.Verdict);
        Assert.Single(inner.Calls); // the refused call never reached the provider
    }

    [Fact]
    public async Task Streaming_over_the_rate_yields_a_rate_limited_error_chunk()
    {
        var inner = new FakeLlmClient();
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 1; o.MaxWait = TimeSpan.Zero; });
        var client = new RateLimitedLlmClient(inner, limiter);

        await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("a")] }); // spend the permit
        var chunks = new List<LlmChunk>();
        await foreach (var c in client.StreamAsync(new LlmRequest { Messages = [LlmMessage.User("b")] })) chunks.Add(c);

        var only = Assert.Single(chunks);
        Assert.Equal(LlmChunkKind.Error, only.Kind);
        Assert.Equal(LlmVerdict.RateLimited, only.Verdict);
    }

    [Fact]
    public async Task SupportsToolCalls_delegates_to_the_inner_client()
    {
        var inner = new FakeLlmClient { SupportsToolCallsResult = true };
        var client = new RateLimitedLlmClient(inner, Limiter(_ => { }));
        Assert.True(client.SupportsToolCalls(new LlmRequest { Messages = [LlmMessage.User("a")] }));
    }

    // ---- DI + composition with the cache -------------------------------------------------------------

    [Fact]
    public async Task A_cancelled_wait_refunds_its_reserved_permit()
    {
        // rate 1/s, burst 1, generous MaxWait so the 2nd acquire WAITS rather than refuses
        var limiter = Limiter(o => { o.PermitsPerSecond = 1; o.Burst = 1; o.MaxWait = TimeSpan.FromSeconds(60); });
        Assert.True(await limiter.AcquireAsync("c")); // spend the one permit (tokens 1 -> 0)

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.False(await limiter.AcquireAsync("c", cts.Token)); // would wait ~1s; cancelled → refund

        // if the permit was refunded, tokens are back to 0 → the next reserve needs 1s (a single deficit),
        // NOT 2s (which is what a lost/un-refunded permit would leave)
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.TryReserve("c", T0));
    }

    [Fact]
    public async Task AddRateLimit_is_idempotent_and_does_not_double_charge()
    {
        var provider = new FakeLlmProvider("p"); // default Ok replies
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddRateLimit(o => { o.PermitsPerSecond = 0.0001; o.Burst = 2; o.MaxWait = TimeSpan.Zero; })
            .AddRateLimit()          // duplicate — must be ignored (a 2nd limiter shares the singleton →
            .DefaultCandidates("p")); // each call would spend TWO permits, exhausting burst 2 in one call)
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ILlmClient>();
        var req = new LlmRequest { Messages = [LlmMessage.User("q")] };

        Assert.Equal(LlmVerdict.Ok, (await client.CompleteAsync(req)).Verdict);          // burst 2 → 1 left
        Assert.Equal(LlmVerdict.Ok, (await client.CompleteAsync(req)).Verdict);          // 1 → 0 (only if single limiter)
        Assert.Equal(LlmVerdict.RateLimited, (await client.CompleteAsync(req)).Verdict); // now exhausted
    }

    [Fact]
    public void A_pre_registered_front_door_with_a_decorator_throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILlmClient>(new FakeLlmClient()); // BYO ILlmClient before AddLyntai
        Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddResponseCache())); // decorator would be silently dropped → guarded
    }

    [Fact]
    public async Task A_cached_hit_does_not_spend_a_rate_limit_permit()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("answer", LlmVerdict.Ok));
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddResponseCache()
            .AddRateLimit(o => { o.PermitsPerSecond = 0.0001; o.Burst = 1; o.MaxWait = TimeSpan.Zero; }) // ~1 real call
            .DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ILlmClient>();

        var a1 = await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("a")] }); // miss → permit → provider
        var a2 = await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("a")] }); // HIT → no permit spent
        var b = await client.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("b")] });  // miss → no permit left → refused

        Assert.Equal("answer", a1.Text);
        Assert.Equal("answer", a2.Text);                    // served from cache
        Assert.Equal(LlmVerdict.RateLimited, b.Verdict);    // the only real-call budget went to 'a'
        Assert.Single(provider.Calls);                       // exactly one provider call across all three
    }
}
