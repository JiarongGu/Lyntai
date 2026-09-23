using Lyntai.Inference;
using Lyntai.Inference.Caching;
using Lyntai;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.Logging;
using InMemoryKeyValueStore = Lyntai.Storage.InMemory.InMemoryKeyValueStore;

namespace Lyntai.Tests.Inference;

/// <summary>Live routing: a consumer's ROUTE — <c>lyntai.route.&lt;consumer&gt;</c> = <c>provider:model[, …]</c> in
/// the KV store — replaces the candidates a call was given, from the very next call and without a restart. Each
/// candidate carries its own model, so a fallback backend is never asked for another backend's model.</summary>
public class LiveModelRoutingTests
{
    // ---- the value is the library's own candidate spec ----------------------------------------------

    [Theory]
    [InlineData("claude:haiku", "claude|haiku")]
    [InlineData("llama:qwen3-4b-gguf, claude:haiku", "llama|qwen3-4b-gguf claude|haiku")]
    [InlineData("ollama:qwen3:4b", "ollama|qwen3:4b")]  // split at the FIRST colon
    [InlineData(" claude ", "claude|(default)")]        // a bare provider serves its default model
    [InlineData("a:x, ,b:y", "a|x b|y")]                // a blank entry is skipped
    public async Task A_route_is_read_as_a_list_of_candidate_specs(string value, string expected)
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.route.memory", value);

        var route = await new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings)).GetRouteAsync("memory");

        Assert.Equal(expected, Render(route));
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task An_entry_naming_no_provider_is_skipped_with_a_warning()
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings));

        await kv.SetAsync("lyntai.route.memory", ":haiku");
        Assert.Empty(await store.GetRouteAsync("memory"));
        Assert.Contains(":haiku", Assert.Single(warnings));

        warnings.Clear();
        await kv.SetAsync("lyntai.route.memory", ":haiku, claude:haiku");
        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Single(warnings);
    }

    // ---- the store --------------------------------------------------------------------------------------

    [Fact]
    public async Task No_route_reads_as_an_empty_list()
    {
        var kv = new InMemoryKeyValueStore();
        var store = new KeyValueModelRoutingStore(kv);

        Assert.Empty(await store.GetRouteAsync("memory"));                                   // no key
        await kv.SetAsync("lyntai.route.memory", "  ");
        Assert.Empty(await store.GetRouteAsync("memory"));                                   // a blank value
        Assert.Empty(await new KeyValueModelRoutingStore(kv: null).GetRouteAsync("memory")); // no store
    }

    [Fact]
    public async Task A_faulting_store_reads_as_no_route_with_one_warning()
    {
        var warnings = new List<string>();
        var logger = Logger<KeyValueModelRoutingStore>(warnings);

        var down = new FaultingKeyValueStore { OnGet = () => new InvalidOperationException("kv down") };
        down.Data["lyntai.route.memory"] = "claude:haiku";
        Assert.Empty(await new KeyValueModelRoutingStore(down, logger).GetRouteAsync("memory"));
        Assert.Single(warnings);

        // a TIMEOUT is a cancellation nobody asked for, so it fails open too
        warnings.Clear();
        var slow = new FaultingKeyValueStore { OnGet = () => new TaskCanceledException("kv timed out") };
        slow.Data["lyntai.route.memory"] = "claude:haiku";
        Assert.Empty(await new KeyValueModelRoutingStore(slow, logger).GetRouteAsync("memory"));
        Assert.Single(warnings);
    }

    [Fact]
    public async Task The_callers_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var marker = new OperationCanceledException("the caller left", cts.Token);
        // cancelled MID-call, so only a rethrow can surface this exact exception
        var kv = new FaultingKeyValueStore { OnGet = () => { cts.Cancel(); return marker; } };

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => new KeyValueModelRoutingStore(kv).GetRouteAsync("memory", cts.Token));
        Assert.Same(marker, thrown);
    }

    [Fact]
    public async Task A_key_left_under_lyntai_model_is_warned_of_once_and_never_read_as_a_route()
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.memory", "claude"); // would name a provider, were it read as a route
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings));

        for (var call = 0; call < 3; call++)
            Assert.Empty(await store.GetRouteAsync("memory"));

        var warning = Assert.Single(warnings);
        Assert.Contains("lyntai.model.", warning);
        Assert.Contains("lyntai.route.", warning);
    }

    [Fact]
    public async Task No_key_under_lyntai_model_no_warning()
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.route.memory", "claude:haiku");
        await kv.SetAsync("lyntai.modelling", "not under the prefix");
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings));

        await store.GetRouteAsync("memory");
        await store.GetRouteAsync("memory");

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_failed_check_for_lyntai_model_keys_costs_the_route_nothing_and_is_asked_again()
    {
        var warnings = new List<string>();
        var kv = new FaultingKeyValueStore { OnList = () => new InvalidOperationException("listing down") };
        kv.Data["lyntai.route.memory"] = "claude:haiku";
        kv.Data["lyntai.model.memory"] = "haiku";
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings));

        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Empty(warnings);

        kv.OnList = null; // a failed check is a moment, not an answer
        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Contains("lyntai.model.", Assert.Single(warnings));
    }

    [Fact]
    public async Task A_store_configured_onto_lyntai_model_reads_it_as_routes_without_the_warning()
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.memory", "claude:haiku");
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings), keyPrefix: "lyntai.model.");

        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Empty(warnings);
    }

    // ---- the router, on both doors ----------------------------------------------------------------------

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_live_route_replaces_the_configured_candidates(bool streaming)
    {
        var (router, x, a, b, kv, _) = Routed(aFails: true);
        await kv.SetAsync("lyntai.route.memory", "a:m1, b:m2");

        var served = await CallAsync(router, Configured, Memory, streaming);

        Assert.StartsWith("b ", served);
        Assert.NotEmpty(a.Calls);
        Assert.All(a.Calls, c => Assert.Equal("m1", c.Model));
        Assert.Equal("m2", Assert.Single(b.Calls).Model); // its own model — never a's
        Assert.Empty(x.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_rebind_moves_the_provider_and_the_model_together_on_the_next_call(bool streaming)
    {
        var (router, x, a, b, kv, _) = Routed(aFails: false);
        await kv.SetAsync("lyntai.route.memory", "a:m1");
        await CallAsync(router, Configured, Memory, streaming);
        Assert.Equal("m1", Assert.Single(a.Calls).Model);

        await kv.SetAsync("lyntai.route.memory", "b:m2");
        await CallAsync(router, Configured, Memory, streaming);

        Assert.Single(a.Calls);
        Assert.Equal("m2", Assert.Single(b.Calls).Model);
        Assert.Empty(x.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_route_entry_resolves_its_model_as_a_configured_candidate_does(bool streaming)
    {
        var (router, _, a, b, kv, _) = Routed(aFails: false);

        await kv.SetAsync("lyntai.route.memory", "b");
        await CallAsync(router, Configured, Memory, streaming);
        await CallAsync(router, Configured, Memory with { Model = "asked" }, streaming);
        await kv.SetAsync("lyntai.route.memory", "b:m2");
        await CallAsync(router, Configured, Memory with { Model = "asked" }, streaming);

        // a bare entry takes the request's model, else the consumer default; an entry's own model outranks both
        Assert.Equal(new string?[] { "d", "asked", "m2" }, b.Calls.Select(c => c.Model));
        Assert.Empty(a.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_route_naming_no_known_provider_is_ignored_with_a_warning(bool streaming)
    {
        var (router, x, a, b, kv, warnings) = Routed(aFails: false);
        await kv.SetAsync("lyntai.route.memory", "gone:m1, missing:m2");

        var served = await CallAsync(router, Configured, Memory, streaming);

        Assert.StartsWith("x ", served);
        Assert.Equal("d", Assert.Single(x.Calls).Model);
        Assert.Empty(a.Calls);
        Assert.Empty(b.Calls);
        var warning = Assert.Single(warnings);
        Assert.Contains("memory", warning);
        Assert.Contains("gone", warning);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Without_a_route_the_configured_candidates_serve_as_they_always_have(bool streaming)
    {
        var (router, x, a, b, _, warnings) = Routed(aFails: false);

        await CallAsync(router, Configured, Memory, streaming);
        await CallAsync(router, Configured, Memory with { Model = "asked" }, streaming);

        Assert.Equal(new string?[] { "d", "asked" }, x.Calls.Select(c => c.Model));
        Assert.Empty(a.Calls);
        Assert.Empty(b.Calls);
        Assert.Empty(warnings);
    }

    // ---- the response cache -----------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_route_the_cache_keys_on_the_resolved_model_alone()
    {
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.memory", "haiku"); // not a route, so it moves nothing
        var (client, cache, options) = Cached(kv);

        await client.CompleteAsync(Memory);
        await client.CompleteAsync(Memory with { Model = "asked" });

        Assert.Equal(ResponseCacheKey.For(Memory, options.ResolveModel("memory", null)), cache.Keys[0]);
        Assert.Equal(ResponseCacheKey.For(Memory with { Model = "asked" }, "asked"), cache.Keys[1]);
    }

    [Fact]
    public async Task A_route_moves_the_cache_key_and_a_changed_route_moves_it_again()
    {
        var kv = new InMemoryKeyValueStore();
        var (client, cache, _) = Cached(kv);

        await client.CompleteAsync(Memory);
        var unrouted = cache.Keys[^1];

        await kv.SetAsync("lyntai.route.memory", "llama:qwen3, claude:haiku");
        await client.CompleteAsync(Memory);
        var routed = cache.Keys[^1];
        Assert.NotEqual(unrouted, routed);

        await kv.SetAsync("lyntai.route.memory", "llama:qwen3, claude:sonnet"); // another model
        await client.CompleteAsync(Memory);
        var remodelled = cache.Keys[^1];
        Assert.DoesNotContain(remodelled, new[] { unrouted, routed });

        await kv.SetAsync("lyntai.route.memory", "claude:sonnet, llama:qwen3"); // the same pairs, another order
        await client.CompleteAsync(Memory);
        Assert.DoesNotContain(cache.Keys[^1], new[] { unrouted, routed, remodelled });

        await kv.SetAsync("lyntai.route.memory", "LLAMA:qwen3, Claude:haiku"); // a provider id's case is not a route
        await client.CompleteAsync(Memory);
        Assert.Equal(routed, cache.Keys[^1]);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static readonly IReadOnlyList<ProviderCandidate> Configured = [new("x")];

    private static readonly TextRequest Memory = new() { Messages = [TextMessage.User("hi")], Consumer = "memory" };

    private static string Render(IReadOnlyList<ProviderCandidate> route) =>
        string.Join(" ", route.Select(c => $"{c.ProviderId}|{c.Model ?? "(default)"}"));

    /// <summary>A router over x, a and b for consumer "memory", whose configured default model is "d"; calls are
    /// given the candidates [x]. With <paramref name="aFails"/>, a throws on both doors.</summary>
    private static (TextRouter Router, FakeTextProvider X, FakeTextProvider A, FakeTextProvider B,
        InMemoryKeyValueStore Kv, List<string> Warnings) Routed(bool aFails)
    {
        var kv = new InMemoryKeyValueStore();
        var x = new FakeTextProvider("x");
        var a = new FakeTextProvider("a");
        if (aFails)
        {
            a.CompleteThrow = new InvalidOperationException("a is down");
            a.StreamThrow = new InvalidOperationException("a is down");
        }
        var b = new FakeTextProvider("b");
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["memory"] = "d";
        var warnings = new List<string>();
        var router = new TextRouter([x, a, b], new DeadHostTracker(), options, Logger<TextRouter>(warnings),
            modelRouting: new KeyValueModelRoutingStore(kv));
        return (router, x, a, b, kv, warnings);
    }

    /// <summary>One call through the chosen door; returns what the serving provider said.</summary>
    private static async Task<string> CallAsync(TextRouter router, IReadOnlyList<ProviderCandidate> candidates,
        TextRequest req, bool streaming)
    {
        if (!streaming) return (await router.CompleteAsync(candidates, req)).Text;
        var text = "";
        await foreach (var chunk in router.StreamAsync(candidates, req))
            if (chunk.Kind == TextChunkKind.Content) text += chunk.Text;
        return text;
    }

    private static (CachingTextClient Client, KeyRecordingCache Cache, LyntaiOptions Options) Cached(IKeyValueStore kv)
    {
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["memory"] = "d";
        var cache = new KeyRecordingCache();
        var client = new CachingTextClient(new FakeTextClient(), cache, options,
            modelRouting: new KeyValueModelRoutingStore(kv));
        return (client, cache, options);
    }

    private static ILogger<T> Logger<T>(List<string> warnings) => new CapturingLogger<T>(warnings);

    /// <summary>Answers from <see cref="Data"/> until a fault is scripted for a call.</summary>
    private sealed class FaultingKeyValueStore : IKeyValueStore
    {
        public Dictionary<string, string> Data { get; } = [];
        public Func<Exception>? OnGet { get; set; }
        public Func<Exception>? OnList { get; set; }

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
