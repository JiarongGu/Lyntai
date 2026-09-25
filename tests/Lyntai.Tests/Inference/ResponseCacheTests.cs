using Lyntai.Inference;
using System.Reflection;
using Lyntai;
using Lyntai.Inference.Caching;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>The opt-in response cache: stable keying (output-determining fields only), the in-memory
/// store's TTL + size eviction, the front-door decorator's hit/miss/only-Ok/tool-bypass/streaming rules,
/// and the DI wiring — all deterministic via a scripted inner client + injected clock.</summary>
public class ResponseCacheTests
{
    private static TextRequest Req(params TextMessage[] msgs) => new() { Messages = msgs };

    // ---- key -----------------------------------------------------------------------------------------

    [Fact]
    public void Key_is_stable_for_identical_requests()
    {
        Assert.Equal(
            ResponseCacheKey.For(Req(TextMessage.User("hello"))),
            ResponseCacheKey.For(Req(TextMessage.User("hello"))));
    }

    [Fact]
    public void Key_ignores_the_consumer_tag()
    {
        // consumer is a routing/telemetry tag, not an output determinant → two consumers share a hit
        var a = ResponseCacheKey.For(new TextRequest { Messages = [TextMessage.User("hi")], Consumer = "chat" });
        var b = ResponseCacheKey.For(new TextRequest { Messages = [TextMessage.User("hi")], Consumer = "scoring" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Key_differs_on_every_output_determining_field()
    {
        var baseReq = Req(TextMessage.User("hi"));
        var key = ResponseCacheKey.For(baseReq);
        Assert.NotEqual(key, ResponseCacheKey.For(Req(TextMessage.User("bye"))));         // content
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { Model = "gpt-4" }));    // model
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { Temperature = 0.5 }));  // temperature
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { JsonSchema = "{}" }));  // schema
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { MaxTokens = 10 }));     // max tokens
        Assert.NotEqual(key, ResponseCacheKey.For(baseReq with { Reasoning = TextReasoning.Suppress })); // reasoning
    }

    // Guard against a NEW TextRequest field being added but not folded into the cache key (a silent
    // collision: two requests differing only in the new field would share a cached hit). Every field must be
    // either hashed by ResponseCacheKey.For or consciously listed here as excluded (with a reason).
    [Fact]
    public void Every_TextRequest_field_is_classified_for_the_cache_key()
    {
        var hashed = new HashSet<string>
        {
            "Messages", "Model", "MaxTokens", "Temperature", "JsonSchema",
            // Reasoning is output-determining: the same prompt asked with and without intermediate
            // reasoning can return different text, and on a thinking model that difference is the whole
            // reply. Serving a suppressed-reasoning caller a cached reasoning-laden hit is precisely the
            // silent collision this guard exists to catch — and it did catch it.
            "Reasoning",
        };
        var excluded = new HashSet<string>
        {
            "Consumer",       // captured via the effective model; two consumers → same model share a hit
            "Tools",          // native-tool requests are never cached (CachingTextClient bypasses them)
            "TimeoutSeconds", // not output-determining
            "RefusalPattern", // applied post-hoc at the front door — re-screens even a cached hit
        };
        var classified = new HashSet<string>(hashed);
        classified.UnionWith(excluded);

        var actual = typeof(TextRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToHashSet();

        Assert.True(actual.SetEquals(classified),
            "TextRequest fields not classified for ResponseCacheKey — hash them in ResponseCacheKey.For or add " +
            $"to the excluded set with a reason. Unclassified: [{string.Join(", ", actual.Except(classified))}]; " +
            $"stale: [{string.Join(", ", classified.Except(actual))}]");
    }

    [Fact]
    public void Key_folds_the_effective_per_consumer_model_but_not_the_consumer()
    {
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["a"] = "model-a";
        options.DefaultModelByConsumer["b"] = "model-b";
        options.DefaultModelByConsumer["c"] = "model-a"; // resolves to the same model as "a"
        var msg = new TextRequest { Messages = [TextMessage.User("same")] };

        var keyA = ResponseCacheKey.For(msg with { Consumer = "a" }, options.ResolveModel("a", null));
        var keyB = ResponseCacheKey.For(msg with { Consumer = "b" }, options.ResolveModel("b", null));
        var keyC = ResponseCacheKey.For(msg with { Consumer = "c" }, options.ResolveModel("c", null));

        Assert.NotEqual(keyA, keyB); // different resolved models → distinct keys (no cross-model serve)
        Assert.Equal(keyA, keyC);    // same resolved model → shared key (consumer stays out of the key)
    }

    [Fact]
    public async Task Cache_does_not_cross_serve_consumers_with_different_default_models()
    {
        var inner = new FakeTextClient();
        inner.Replies.Enqueue(new TextResponse("answer-for-a", ProviderVerdict.Ok));
        inner.Replies.Enqueue(new TextResponse("answer-for-b", ProviderVerdict.Ok));
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["a"] = "model-a";
        options.DefaultModelByConsumer["b"] = "model-b";
        var client = new CachingTextClient(inner, new InMemoryResponseCache(options), options);
        TextMessage[] same = [TextMessage.User("same question")];

        var a = await client.CompleteAsync(new TextRequest { Messages = same, Consumer = "a" }); // model-a
        var b = await client.CompleteAsync(new TextRequest { Messages = same, Consumer = "b" }); // model-b

        Assert.Equal("answer-for-a", a.Text);
        Assert.Equal("answer-for-b", b.Text); // NOT served a's cached reply — different resolved model
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public void Key_is_not_fooled_by_message_boundary_shifts()
    {
        // "ab"+"c" must not collide with "a"+"bc" — length-framing prevents the concatenation collision
        var a = ResponseCacheKey.For(Req(TextMessage.User("ab"), TextMessage.User("c")));
        var b = ResponseCacheKey.For(Req(TextMessage.User("a"), TextMessage.User("bc")));
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
        var reply = new TextResponse("cached", ProviderVerdict.Ok);
        await cache.SetAsync("k", reply);
        Assert.Same(reply, await cache.GetAsync("k"));
        Assert.Null(await cache.GetAsync("missing"));
    }

    [Fact]
    public async Task Entry_expires_after_its_ttl()
    {
        var (cache, clock) = NewCache();
        await cache.SetAsync("k", new TextResponse("x", ProviderVerdict.Ok), TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await cache.GetAsync("k")); // still fresh
        clock.Advance(TimeSpan.FromMinutes(2));       // now past 5m
        Assert.Null(await cache.GetAsync("k"));     // expired
    }

    [Fact]
    public async Task Non_positive_ttl_disables_caching()
    {
        var (cache, _) = NewCache(c => c.Ttl = TimeSpan.Zero);
        await cache.SetAsync("k", new TextResponse("x", ProviderVerdict.Ok)); // uses default ttl = Zero
        Assert.Null(await cache.GetAsync("k"));
    }

    [Fact]
    public async Task Remove_evicts_one_entry_and_a_missing_key_is_a_no_op()
    {
        var (cache, _) = NewCache();
        await cache.SetAsync("keep", new TextResponse("keep", ProviderVerdict.Ok));
        await cache.SetAsync("poisoned", new TextResponse("bad", ProviderVerdict.Ok));

        await cache.RemoveAsync("poisoned");
        await cache.RemoveAsync("never-set"); // no-op, no throw

        Assert.Null(await cache.GetAsync("poisoned"));
        Assert.NotNull(await cache.GetAsync("keep")); // surgical — the rest of the cache survives
    }

    [Fact]
    public async Task Evicts_the_oldest_beyond_the_size_cap()
    {
        var (cache, _) = NewCache(c => c.MaxEntries = 2);
        await cache.SetAsync("a", new TextResponse("a", ProviderVerdict.Ok));
        await cache.SetAsync("b", new TextResponse("b", ProviderVerdict.Ok));
        await cache.SetAsync("c", new TextResponse("c", ProviderVerdict.Ok)); // over cap → shed the oldest ("a")
        Assert.Null(await cache.GetAsync("a"));
        Assert.NotNull(await cache.GetAsync("b"));
        Assert.NotNull(await cache.GetAsync("c"));
    }

    // ---- decorator -----------------------------------------------------------------------------------

    private static (CachingTextClient client, FakeTextClient inner) Decorated()
    {
        var inner = new FakeTextClient();
        var options = new LyntaiOptions();
        return (new CachingTextClient(inner, new InMemoryResponseCache(options), options), inner);
    }

    [Fact]
    public async Task Second_identical_completion_is_served_from_cache()
    {
        var (client, inner) = Decorated();
        inner.Replies.Enqueue(new TextResponse("answer", ProviderVerdict.Ok));
        var req = Req(TextMessage.User("q"));

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
        inner.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "boom"));
        inner.Replies.Enqueue(new TextResponse("recovered", ProviderVerdict.Ok));
        var req = Req(TextMessage.User("q"));

        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal(ProviderVerdict.Failed, first.Verdict);
        Assert.Equal("recovered", second.Text); // retried the provider, not a cached failure
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Native_tool_requests_bypass_the_cache()
    {
        var (client, inner) = Decorated();
        inner.Replies.Enqueue(new TextResponse("a", ProviderVerdict.Ok));
        inner.Replies.Enqueue(new TextResponse("b", ProviderVerdict.Ok));
        var req = new TextRequest { Messages = [TextMessage.User("q")], Tools = [new TextTool("echo")] };

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
        var req = Req(TextMessage.User("q"));
        await foreach (var _ in client.StreamAsync(req)) { }
        await foreach (var _ in client.StreamAsync(req)) { }
        Assert.Equal(2, inner.Calls.Count); // both streamed straight through the inner client
    }

    [Fact]
    public async Task GetCapabilitiesAsync_delegates_to_the_inner_client()
    {
        var (client, inner) = Decorated();
        inner.SupportsToolCallsResult = true;
        Assert.Same(inner.Capabilities, await client.GetCapabilitiesAsync(Req(TextMessage.User("q"))));
    }

    // ---- DI wiring -----------------------------------------------------------------------------------

    [Fact]
    public async Task AddResponseCache_wires_a_caching_front_door()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("once", ProviderVerdict.Ok)); // exactly one scripted reply
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddResponseCache()
            .UseDefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ITextClient>();
        // the always-on refusal screen wraps the front door; the caching behavior is proven below
        Assert.IsType<RefusalScreeningTextClient>(client);

        var req = new TextRequest { Messages = [TextMessage.User("hi")] };
        var first = await client.CompleteAsync(req);
        var second = await client.CompleteAsync(req);

        Assert.Equal("once", first.Text);
        Assert.Equal("once", second.Text);  // cached — did NOT fall through to the provider's default reply
        Assert.Single(provider.Calls);       // the provider was reached exactly once
    }
}
