using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Caching;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

/// <summary>The opt-in response cache: stable keying (output-determining fields only), the in-memory
/// store's TTL + size eviction, the front-door decorator's hit/miss/only-Ok/tool-bypass/streaming rules,
/// and the DI wiring — all deterministic via a scripted inner client + injected clock.</summary>
public class ResponseCacheTests
{
    private static LlmRequest Req(params LlmMessage[] msgs) => new() { Messages = msgs };

    // ---- key -----------------------------------------------------------------------------------------

    [Fact]
    public void Key_is_stable_for_identical_requests()
    {
        Assert.Equal(
            ResponseCacheKey.For(Req(LlmMessage.User("hello"))),
            ResponseCacheKey.For(Req(LlmMessage.User("hello"))));
    }

    [Fact]
    public void Key_ignores_the_consumer_tag()
    {
        // consumer is a routing/telemetry tag, not an output determinant → two consumers share a hit
        var a = ResponseCacheKey.For(new LlmRequest { Messages = [LlmMessage.User("hi")], Consumer = "chat" });
        var b = ResponseCacheKey.For(new LlmRequest { Messages = [LlmMessage.User("hi")], Consumer = "scoring" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Key_differs_on_every_output_determining_field()
    {
        var baseReq = Req(LlmMessage.User("hi"));
        var key = ResponseCacheKey.For(baseReq);
        Assert.NotEqual(key, ResponseCacheKey.For(Req(LlmMessage.User("bye"))));         // content
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { Model = "gpt-4" }));    // model
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { Temperature = 0.5 }));  // temperature
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { JsonSchema = "{}" }));  // schema
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { MaxTokens = 10 }));     // max tokens
    }

    [Fact]
    public void Key_folds_the_effective_per_consumer_model_but_not_the_consumer()
    {
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["a"] = "model-a";
        options.DefaultModelByConsumer["b"] = "model-b";
        options.DefaultModelByConsumer["c"] = "model-a"; // resolves to the same model as "a"
        var msg = new LlmRequest { Messages = [LlmMessage.User("same")] };

        var keyA = ResponseCacheKey.For(msg with { Consumer = "a" }, options.ResolveModel("a", null));
        var keyB = ResponseCacheKey.For(msg with { Consumer = "b" }, options.ResolveModel("b", null));
        var keyC = ResponseCacheKey.For(msg with { Consumer = "c" }, options.ResolveModel("c", null));

        Assert.NotEqual(keyA, keyB); // different resolved models → distinct keys (no cross-model serve)
        Assert.Equal(keyA, keyC);    // same resolved model → shared key (consumer stays out of the key)
    }

    [Fact]
    public async Task Cache_does_not_cross_serve_consumers_with_different_default_models()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("answer-for-a", LlmVerdict.Ok));
        inner.Replies.Enqueue(new LlmReply("answer-for-b", LlmVerdict.Ok));
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["a"] = "model-a";
        options.DefaultModelByConsumer["b"] = "model-b";
        var client = new CachingLlmClient(inner, new InMemoryResponseCache(options), options);
        LlmMessage[] same = [LlmMessage.User("same question")];

        var a = await client.CompleteAsync(new LlmRequest { Messages = same, Consumer = "a" }); // model-a
        var b = await client.CompleteAsync(new LlmRequest { Messages = same, Consumer = "b" }); // model-b

        Assert.Equal("answer-for-a", a.Text);
        Assert.Equal("answer-for-b", b.Text); // NOT served a's cached reply — different resolved model
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public void Key_is_not_fooled_by_message_boundary_shifts()
    {
        // "ab"+"c" must not collide with "a"+"bc" — length-framing prevents the concatenation collision
        var a = ResponseCacheKey.For(Req(LlmMessage.User("ab"), LlmMessage.User("c")));
        var b = ResponseCacheKey.For(Req(LlmMessage.User("a"), LlmMessage.User("bc")));
        Assert.NotEqual(a, b);
    }

    // ---- in-memory store -----------------------------------------------------------------------------

    private static (InMemoryResponseCache cache, MutableClock clock) NewCache(Action<CacheOptions>? tune = null)
    {
        var options = new LyntaiOptions();
        tune?.Invoke(options.Cache);
        var clock = new MutableClock();
        return (new InMemoryResponseCache(options, clock.Get), clock);
    }

    [Fact]
    public async Task Stores_and_returns_a_reply_misses_on_unknown_key()
    {
        var (cache, _) = NewCache();
        var reply = new LlmReply("cached", LlmVerdict.Ok);
        await cache.SetAsync("k", reply);
        Assert.Same(reply, await cache.TryGetAsync("k"));
        Assert.Null(await cache.TryGetAsync("missing"));
    }

    [Fact]
    public async Task Entry_expires_after_its_ttl()
    {
        var (cache, clock) = NewCache();
        await cache.SetAsync("k", new LlmReply("x", LlmVerdict.Ok), TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.TryGetAsync("k")); // still fresh
        clock.Advance(TimeSpan.FromMinutes(2));       // now past 5m
        Assert.Null(await cache.TryGetAsync("k"));     // expired
    }

    [Fact]
    public async Task Non_positive_ttl_disables_caching()
    {
        var (cache, _) = NewCache(c => c.Ttl = TimeSpan.Zero);
        await cache.SetAsync("k", new LlmReply("x", LlmVerdict.Ok)); // uses default ttl = Zero
        Assert.Null(await cache.TryGetAsync("k"));
    }

    [Fact]
    public async Task Evicts_the_oldest_beyond_the_size_cap()
    {
        var (cache, _) = NewCache(c => c.MaxEntries = 2);
        await cache.SetAsync("a", new LlmReply("a", LlmVerdict.Ok));
        await cache.SetAsync("b", new LlmReply("b", LlmVerdict.Ok));
        await cache.SetAsync("c", new LlmReply("c", LlmVerdict.Ok)); // over cap → shed the oldest ("a")
        Assert.Null(await cache.TryGetAsync("a"));
        Assert.NotNull(await cache.TryGetAsync("b"));
        Assert.NotNull(await cache.TryGetAsync("c"));
    }

    // ---- decorator -----------------------------------------------------------------------------------

    private static (CachingLlmClient client, FakeLlmClient inner) Decorated()
    {
        var inner = new FakeLlmClient();
        var options = new LyntaiOptions();
        return (new CachingLlmClient(inner, new InMemoryResponseCache(options), options), inner);
    }

    [Fact]
    public async Task Second_identical_completion_is_served_from_cache()
    {
        var (client, inner) = Decorated();
        inner.Replies.Enqueue(new LlmReply("answer", LlmVerdict.Ok));
        var req = Req(LlmMessage.User("q"));

        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal("answer", first.Text);
        Assert.Equal("answer", second.Text);
        Assert.Single(inner.Calls); // the provider was hit once; the second came from cache
    }

    [Fact]
    public async Task A_non_Ok_reply_is_not_cached()
    {
        var (client, inner) = Decorated();
        inner.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "boom"));
        inner.Replies.Enqueue(new LlmReply("recovered", LlmVerdict.Ok));
        var req = Req(LlmMessage.User("q"));

        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal(LlmVerdict.Failed, first.Verdict);
        Assert.Equal("recovered", second.Text); // retried the provider, not a cached failure
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Native_tool_requests_bypass_the_cache()
    {
        var (client, inner) = Decorated();
        inner.Replies.Enqueue(new LlmReply("a", LlmVerdict.Ok));
        inner.Replies.Enqueue(new LlmReply("b", LlmVerdict.Ok));
        var req = new LlmRequest { Messages = [LlmMessage.User("q")], Tools = [new LlmTool("echo")] };

        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal("a", first.Text);
        Assert.Equal("b", second.Text); // not cached — the tool loop is stateful
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Streaming_is_not_cached_and_passes_through()
    {
        var (client, inner) = Decorated();
        var req = Req(LlmMessage.User("q"));
        await foreach (var _ in client.StreamAsync(req)) { }
        await foreach (var _ in client.StreamAsync(req)) { }
        Assert.Equal(2, inner.Calls.Count); // both streamed straight through the inner client
    }

    [Fact]
    public async Task SupportsToolCalls_delegates_to_the_inner_client()
    {
        var (client, inner) = Decorated();
        inner.SupportsToolCallsResult = true;
        Assert.True(client.SupportsToolCalls(Req(LlmMessage.User("q"))));
    }

    // ---- DI wiring -----------------------------------------------------------------------------------

    [Fact]
    public async Task AddResponseCache_wires_a_caching_front_door()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("once", LlmVerdict.Ok)); // exactly one scripted reply
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddResponseCache()
            .DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ILlmClient>();
        // the always-on refusal screen wraps the front door; the caching behavior is proven below
        Assert.IsType<RefusalScreeningLlmClient>(client);

        var req = new LlmRequest { Messages = [LlmMessage.User("hi")] };
        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal("once", first.Text);
        Assert.Equal("once", second.Text);  // cached — did NOT fall through to the provider's default reply
        Assert.Single(provider.Calls);       // the provider was reached exactly once
    }
}
