using Lyntai.Inference;
using Lyntai.Inference.Caching;
using Lyntai;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.Logging;
using InMemoryKeyValueStore = Lyntai.Storage.InMemory.InMemoryKeyValueStore;

namespace Lyntai.Tests.Inference;

/// <summary>Live per-consumer model routing (A6): an admin-set model override in the KV store takes effect
/// on the very next call — no restart — resolved above the code/env default but below an explicit model.</summary>
public class LiveModelRoutingTests
{
    [Fact]
    public void ResolveModel_precedence_request_then_live_then_config()
    {
        var opts = new LyntaiOptions();
        opts.DefaultModelByConsumer["scoring"] = "config";
        opts.DefaultModelByConsumer["default"] = "config-default";

        Assert.Equal("explicit", opts.ResolveModel("scoring", "explicit", "live")); // request wins
        Assert.Equal("live", opts.ResolveModel("scoring", null, "live"));            // live over config
        Assert.Equal("config", opts.ResolveModel("scoring", null, null));            // consumer default
        Assert.Equal("config-default", opts.ResolveModel("other", null, null));      // "default" entry
        Assert.Null(new LyntaiOptions().ResolveModel("x", null, null));              // nothing configured → null
    }

    [Fact]
    public async Task Kv_store_reads_the_override_and_fails_open_without_a_store()
    {
        var kv = new InMemoryKeyValueStore();
        var store = new KeyValueModelRoutingStore(kv);
        Assert.Null(await store.GetModelOverrideAsync("scoring"));         // unset → null
        await kv.SetAsync("lyntai.model.scoring", "haiku");
        Assert.Equal("haiku", await store.GetModelOverrideAsync("scoring"));

        Assert.Null(await new KeyValueModelRoutingStore(kv: null).GetModelOverrideAsync("scoring")); // no store → null
    }

    [Fact]
    public async Task An_admin_retune_takes_effect_live_without_restart()
    {
        var kv = new InMemoryKeyValueStore();
        var provider = new FakeTextProvider("p"); // its Calls capture the request the router built (with the effective model)
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["scoring"] = "config-model";
        var router = new TextRouter([provider], new DeadHostTracker(), options,
            modelRouting: new KeyValueModelRoutingStore(kv));
        IReadOnlyList<ProviderCandidate> candidates = [new ProviderCandidate("p")];
        var req = new TextRequest { Messages = [TextMessage.User("hi")], Consumer = "scoring" };

        await router.CompleteAsync(candidates, req);
        Assert.Equal("config-model", provider.Calls[^1].Model);            // no override → configured default

        await kv.SetAsync("lyntai.model.scoring", "live-model");           // admin retunes...
        await router.CompleteAsync(candidates, req);
        Assert.Equal("live-model", provider.Calls[^1].Model);             // ...and the next call uses it — no restart

        await kv.SetAsync("lyntai.model.scoring", "live-model-2");         // retune again, live
        await router.CompleteAsync(candidates, req);
        Assert.Equal("live-model-2", provider.Calls[^1].Model);

        await router.CompleteAsync(candidates, req with { Model = "explicit" }); // an explicit request model still wins
        Assert.Equal("explicit", provider.Calls[^1].Model);
    }

    // ---- scoped to a provider ------------------------------------------------------------------------

    [Fact]
    public void ModelOverrides_For_prefers_the_providers_own_entry_else_the_consumer_wide_one()
    {
        // an ORDINAL map on purpose: matching the provider id case-insensitively is For's promise, not the map's
        var live = new ModelOverrides("wide", new Dictionary<string, string> { ["llama"] = "scoped" });

        Assert.Equal("scoped", live.For("llama"));
        Assert.Equal("scoped", live.For("LLAMA"));
        Assert.Equal("wide", live.For("ollama"));                              // another provider → consumer-wide
        Assert.Null(new ModelOverrides(null, live.ByProvider).For("ollama"));  // neither → null
        Assert.Null(ModelOverrides.None.For("llama"));
    }

    [Fact]
    public async Task Kv_store_reads_scoped_keys_beside_the_consumer_wide_one()
    {
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.memory", "a");
        await kv.SetAsync("lyntai.model.memory@llama", "b");
        await kv.SetAsync("lyntai.model.memory@ollama", "  ");   // blank → ignored
        await kv.SetAsync("lyntai.model.memory2@x", "c");        // another consumer's key
        var store = new KeyValueModelRoutingStore(kv);

        var live = await store.GetModelOverridesAsync("memory");

        Assert.Equal("a", live.Any);
        var scoped = Assert.Single(live.ByProvider);
        Assert.Equal(("llama", "b"), (scoped.Key, scoped.Value));
        Assert.Equal("b", live.For("Llama"));
        Assert.Equal("a", await store.GetModelOverrideAsync("memory")); // the consumer-wide read is unchanged
    }

    [Fact]
    public async Task Kv_store_yields_no_overrides_without_a_store()
    {
        var live = await new KeyValueModelRoutingStore(kv: null).GetModelOverridesAsync("memory");

        Assert.Null(live.Any);
        Assert.Empty(live.ByProvider);
    }

    [Fact]
    public async Task Kv_store_fails_open_with_one_warning_keeping_what_it_already_read()
    {
        var warnings = new List<string>();
        var logger = new CapturingLogger<KeyValueModelRoutingStore>(warnings);

        var down = new FaultingKeyValueStore { OnGet = () => new InvalidOperationException("kv down") };
        down.Data["lyntai.model.memory"] = "a";
        var none = await new KeyValueModelRoutingStore(down, logger).GetModelOverridesAsync("memory");
        Assert.Null(none.Any);
        Assert.Empty(none.ByProvider);
        Assert.Single(warnings);

        // the listing TIMES OUT — a cancellation nobody asked for — after the consumer-wide read succeeded
        warnings.Clear();
        var slow = new FaultingKeyValueStore { OnList = () => new TaskCanceledException("kv timed out") };
        slow.Data["lyntai.model.memory"] = "a";
        slow.Data["lyntai.model.memory@llama"] = "b";
        var partial = await new KeyValueModelRoutingStore(slow, logger).GetModelOverridesAsync("memory");
        Assert.Equal("a", partial.Any);
        Assert.Empty(partial.ByProvider);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task Kv_store_rethrows_the_callers_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var marker = new OperationCanceledException("the caller left", cts.Token);
        // cancelled MID-call, after the consumer-wide read — so only a rethrow can surface this exact exception
        var kv = new FaultingKeyValueStore { OnList = () => { cts.Cancel(); return marker; } };

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => new KeyValueModelRoutingStore(kv).GetModelOverridesAsync("memory", cts.Token));
        Assert.Same(marker, thrown);
    }

    [Fact]
    public async Task A_store_implementing_only_the_consumer_wide_read_serves_it_as_Any()
    {
        IModelRoutingStore store = new ConsumerWideStore("byo");

        var live = await store.GetModelOverridesAsync("memory");

        Assert.Equal("byo", live.Any);
        Assert.Empty(live.ByProvider);
    }

    [Fact]
    public async Task A_scoped_override_reaches_only_its_provider_on_fallback()
    {
        var (router, a, b, kv) = FallbackRoute();
        await kv.SetAsync("lyntai.model.memory@A", "a-model"); // cased unlike the provider id, on purpose

        var reply = await router.CompleteAsync(AThenB, Memory);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("a-model", c.Model));
        Assert.Equal("d", Assert.Single(b.Calls).Model); // its configured default — never a's model
    }

    [Fact]
    public async Task A_scoped_override_reaches_only_its_provider_when_streaming()
    {
        var (router, a, b, kv) = FallbackRoute();
        await kv.SetAsync("lyntai.model.memory@a", "a-model");

        await foreach (var _ in router.StreamAsync(AThenB, Memory)) { }

        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("a-model", c.Model));
        Assert.Equal("d", Assert.Single(b.Calls).Model);
    }

    [Fact]
    public async Task A_scope_naming_a_provider_outside_the_route_reaches_nobody()
    {
        var (router, a, b, kv) = FallbackRoute();
        await kv.SetAsync("lyntai.model.memory@rebound", "rebound-model"); // written before the restart that rewires the route

        await router.CompleteAsync(AThenB, Memory);

        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("d", c.Model));
        Assert.Equal("d", Assert.Single(b.Calls).Model);
    }

    [Fact]
    public async Task A_consumer_wide_override_still_reaches_every_candidate()
    {
        var (router, a, b, kv) = FallbackRoute();
        await kv.SetAsync("lyntai.model.memory", "wide");

        await router.CompleteAsync(AThenB, Memory);

        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("wide", c.Model));
        Assert.Equal("wide", Assert.Single(b.Calls).Model);
    }

    [Fact]
    public async Task A_scoped_override_outranks_the_consumer_wide_one_for_its_provider_only()
    {
        var (router, a, b, kv) = FallbackRoute();
        await kv.SetAsync("lyntai.model.memory", "wide");
        await kv.SetAsync("lyntai.model.memory@a", "a-model");

        await router.CompleteAsync(AThenB, Memory);

        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("a-model", c.Model));
        Assert.Equal("wide", Assert.Single(b.Calls).Model);
    }

    [Fact]
    public async Task Cache_key_is_unchanged_without_a_scoped_entry_and_moves_with_one()
    {
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.memory", "wide");
        var options = new LyntaiOptions();
        var cache = new KeyRecordingCache();
        var client = new CachingTextClient(new FakeTextClient(), cache, options,
            modelRouting: new KeyValueModelRoutingStore(kv));

        await client.CompleteAsync(Memory);
        var unscoped = cache.Keys[^1];
        Assert.Equal(ResponseCacheKey.For(Memory, options.ResolveModel("memory", null, "wide")), unscoped);

        await kv.SetAsync("lyntai.model.memory@llama", "b");
        await client.CompleteAsync(Memory);
        var scoped = cache.Keys[^1];
        Assert.NotEqual(unscoped, scoped);

        await kv.SetAsync("lyntai.model.memory@llama", "c"); // a scoped retune invalidates too
        await client.CompleteAsync(Memory);
        Assert.NotEqual(scoped, cache.Keys[^1]);
        Assert.NotEqual(unscoped, cache.Keys[^1]);

        var named = Memory with { Model = "explicit" }; // the request's own model outranks every live entry
        await client.CompleteAsync(named);
        Assert.Equal(ResponseCacheKey.For(named, "explicit"), cache.Keys[^1]);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static readonly IReadOnlyList<ProviderCandidate> AThenB = [new("a"), new("b")];

    private static readonly TextRequest Memory = new() { Messages = [TextMessage.User("hi")], Consumer = "memory" };

    /// <summary>Route [a, b] for consumer "memory", whose configured default is "d". Provider a always throws,
    /// so b serves every call.</summary>
    private static (TextRouter Router, FakeTextProvider A, FakeTextProvider B, InMemoryKeyValueStore Kv) FallbackRoute()
    {
        var kv = new InMemoryKeyValueStore();
        var a = new FakeTextProvider("a")
        {
            CompleteThrow = new InvalidOperationException("a is down"),
            StreamThrow = new InvalidOperationException("a is down"),
        };
        var b = new FakeTextProvider("b");
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["memory"] = "d";
        var router = new TextRouter([a, b], new DeadHostTracker(), options,
            modelRouting: new KeyValueModelRoutingStore(kv));
        return (router, a, b, kv);
    }

    /// <summary>A BYO store written against the consumer-wide member alone.</summary>
    private sealed class ConsumerWideStore(string? model) : IModelRoutingStore
    {
        public Task<string?> GetModelOverrideAsync(string consumer, CancellationToken ct = default) =>
            Task.FromResult(model);
    }

    /// <summary>Answers from <see cref="Data"/> until a fault is scripted for a call.</summary>
    private sealed class FaultingKeyValueStore : IKeyValueStore
    {
        public Dictionary<string, string> Data { get; } = [];
        public Func<Exception>? OnGet { get; init; }
        public Func<Exception>? OnList { get; init; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            OnGet is { } fault ? throw fault() : Task.FromResult(Data.GetValueOrDefault(key));

        public Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default) =>
            OnList is { } fault
                ? throw fault()
                : Task.FromResult<IReadOnlyList<string>>([.. Data.Keys
                    .Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)]);

        public Task SetAsync(string key, string value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Records every key looked up and always misses, so each call's key is observable.</summary>
    private sealed class KeyRecordingCache : IResponseCache
    {
        public List<string> Keys { get; } = [];

        public Task<TextResponse?> GetAsync(string key, CancellationToken ct = default)
        {
            Keys.Add(key);
            return Task.FromResult<TextResponse?>(null);
        }

        public Task SetAsync(string key, TextResponse reply, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (level >= LogLevel.Warning) sink.Add(fmt(state, ex));
        }
    }
}
