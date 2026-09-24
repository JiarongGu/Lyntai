using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Caching;
using Lyntai;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("claude:", "claude|(default)")]         // a blank model is the backend's default too
    public async Task A_route_is_read_as_a_list_of_candidate_specs(string value, string expected)
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.route.memory", value);

        var route = await new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings)).GetRouteAsync("memory");

        Assert.Equal(expected, Render(route));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("claude:")]
    [InlineData(" claude :  ")]
    public void A_blank_model_in_any_candidate_spec_is_the_backends_default(string spec)
    {
        // null, not "": an empty model would outrank the request's own (candidate.Model ?? req.Model)
        Assert.Equal(new ProviderCandidate("claude"), ProviderCandidateSpec.Parse(spec));
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
    public async Task A_store_that_cannot_list_is_checked_for_lyntai_model_keys_once()
    {
        var warnings = new List<string>();
        var kv = new FaultingKeyValueStore { OnList = () => new NotSupportedException("no listing here") };
        kv.Data["lyntai.route.memory"] = "claude:haiku";
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings));

        for (var call = 0; call < 4; call++)
            Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));

        Assert.Equal(1, kv.Lists); // a store that cannot list never will: the check is done, nothing reported
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_listing_that_keeps_failing_is_given_up_after_three_attempts()
    {
        var kv = new FaultingKeyValueStore { OnList = () => new InvalidOperationException("listing down") };
        kv.Data["lyntai.route.memory"] = "claude:haiku";
        var store = new KeyValueModelRoutingStore(kv);

        for (var call = 0; call < 6; call++)
            Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));

        Assert.Equal(3, kv.Lists);
    }

    [Fact]
    public async Task A_failed_route_read_does_not_also_list_lyntai_model_keys()
    {
        var down = new FaultingKeyValueStore { OnGet = () => new InvalidOperationException("kv down") };
        var store = new KeyValueModelRoutingStore(down);

        await store.GetRouteAsync("memory");
        await store.GetRouteAsync("memory");

        Assert.Equal(0, down.Lists);
    }

    [Theory]
    [InlineData("lyntai.model.")]
    [InlineData("lyntai.model.route.")] // nested under it
    public async Task A_store_whose_own_keys_sit_under_lyntai_model_reads_them_without_the_warning(string prefix)
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync(prefix + "memory", "claude:haiku");
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings), keyPrefix: prefix);

        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_store_nested_under_lyntai_model_still_reports_a_model_only_key_beside_its_own()
    {
        var warnings = new List<string>();
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.model.route.memory", "claude:haiku");
        await kv.SetAsync("lyntai.model.memory", "haiku");
        var store = new KeyValueModelRoutingStore(kv, Logger<KeyValueModelRoutingStore>(warnings), keyPrefix: "lyntai.model.route.");

        Assert.Equal("claude|haiku", Render(await store.GetRouteAsync("memory")));
        Assert.Contains("lyntai.model.", Assert.Single(warnings));
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
    public async Task A_route_entry_takes_its_own_model_else_the_requests_never_the_consumer_default(bool streaming)
    {
        var (router, _, a, b, kv, _) = Routed(aFails: false);

        await kv.SetAsync("lyntai.route.memory", "b");
        await CallAsync(router, Configured, Memory, streaming);
        await CallAsync(router, Configured, Memory with { Model = "asked" }, streaming);
        await kv.SetAsync("lyntai.route.memory", "b:m2");
        await CallAsync(router, Configured, Memory with { Model = "asked" }, streaming);

        // a bare entry takes the request's model, else the backend's own default — never the consumer
        // default "d", which belongs to the configured candidates the route replaced
        Assert.Equal(new string?[] { null, "asked", "m2" }, b.Calls.Select(c => c.Model));
        Assert.Empty(a.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_bare_route_entry_benches_and_is_probed_under_the_backends_default_model(bool streaming)
    {
        var deadHosts = new DeadHostTracker();
        var (router, _, a, b, kv, _) = Routed(aFails: false, deadHosts: deadHosts,
            configure: o => o.Routing.CooldownScope = CooldownScope.ProviderAndModel);
        await kv.SetAsync("lyntai.route.memory", "b, a");

        deadHosts.MarkDead("b::d"); // the consumer default's key is not the one a bare entry runs under
        Assert.Same(b.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory));

        deadHosts.MarkDead("b::(default)");
        Assert.Same(a.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory));
        Assert.StartsWith("a ", await CallAsync(router, Configured, Memory, streaming)); // the probe and the call agree
        Assert.Empty(b.Calls);
        Assert.Null(Assert.Single(a.Calls).Model);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_given_candidate_still_resolves_through_the_consumer_default_under_cooldown(bool streaming)
    {
        var deadHosts = new DeadHostTracker();
        var (router, x, _, _, _, _) = Routed(aFails: false, deadHosts: deadHosts,
            configure: o => o.Routing.CooldownScope = CooldownScope.ProviderAndModel);

        deadHosts.MarkDead("x::(default)"); // not x's key: a given candidate runs under the consumer default
        Assert.Same(x.Capabilities, await router.GetCapabilitiesAsync([new("x"), new("a")], Memory));
        Assert.StartsWith("x ", await CallAsync(router, [new("x"), new("a")], Memory, streaming));
        Assert.Equal("d", Assert.Single(x.Calls).Model);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_request_model_a_fully_pinned_route_can_never_serve_is_a_warning_on_each_call(bool streaming)
    {
        var (router, _, a, _, kv, warnings) = Routed(aFails: false);
        await kv.SetAsync("lyntai.route.memory", "a:m1, b:m2");

        await CallAsync(router, Configured, Memory with { Model = "pinned" }, streaming);

        Assert.Equal("m1", Assert.Single(a.Calls).Model); // the route's model serves
        var warning = Assert.Single(warnings);
        Assert.Contains("memory", warning);
        Assert.Contains("pinned", warning);
        Assert.Contains("a:m1, b:m2", warning);

        warnings.Clear();
        await CallAsync(router, Configured, Memory with { Model = "pinned" }, streaming);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("a:m1, b")]       // a bare entry can take the request's model
    [InlineData("a:m1, b:pinned")] // an entry pins it
    [InlineData("a:m1, b:PINNED")] // matched as the predicate at composition matches it
    public async Task A_route_that_can_serve_the_requests_model_does_not_warn(string route)
    {
        var (router, _, _, _, kv, warnings) = Routed(aFails: false);
        await kv.SetAsync("lyntai.route.memory", route);

        await router.CompleteAsync(Configured, Memory with { Model = "pinned" });
        await router.CompleteAsync(Configured, Memory); // no model asked for, nothing to contradict
        await kv.SetAsync("lyntai.route.memory", "a:m1, b:m2");
        await router.CompleteAsync(Configured, Memory);

        Assert.Empty(warnings);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_route_entry_naming_a_backend_that_serves_no_text_is_skipped_as_an_unknown_one_is(bool streaming)
    {
        var v = new FakeTextProvider("v")
        {
            Capabilities = new() { Accepts = [ProviderKinds.Text], Produces = [ProviderKinds.Vector], Operations = [ProviderOperation.Complete] },
        };
        var (router, x, _, b, kv, warnings) = Routed(aFails: false, more: [v]);

        await kv.SetAsync("lyntai.route.memory", "v:e5, b:m2");
        Assert.StartsWith("b ", await CallAsync(router, Configured, Memory, streaming));
        Assert.Contains("v:e5", Assert.Single(warnings));

        warnings.Clear();
        await kv.SetAsync("lyntai.route.memory", "v:e5");
        Assert.StartsWith("x ", await CallAsync(router, Configured, Memory, streaming)); // the given candidates serve
        Assert.Contains("v:e5", Assert.Single(warnings));
        Assert.Same(x.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory));

        Assert.Empty(v.Calls);
        Assert.Equal("m2", Assert.Single(b.Calls).Model);
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

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_route_entry_unknown_here_is_skipped_with_a_warning(bool streaming)
    {
        var (router, x, _, b, kv, warnings) = Routed(aFails: false);
        await kv.SetAsync("lyntai.route.memory", "gone:m0, b:m2"); // a typo in the PRIMARY must not move traffic silently

        var served = await CallAsync(router, Configured, Memory, streaming);

        Assert.StartsWith("b ", served);
        Assert.Equal("m2", Assert.Single(b.Calls).Model);
        Assert.Empty(x.Calls);
        var warning = Assert.Single(warnings);
        Assert.Contains("gone:m0", warning);
        Assert.DoesNotContain("b:m2", warning);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_faulting_route_store_leaves_the_given_candidates_with_a_warning(bool streaming)
    {
        var (router, x, _, _, _, warnings) = Routed(aFails: false,
            store: new ThrowingRouteStore(() => new InvalidOperationException("store down")));

        var served = await CallAsync(router, Configured, Memory, streaming);

        Assert.StartsWith("x ", served);
        Assert.Equal("d", Assert.Single(x.Calls).Model);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task The_callers_cancellation_during_the_route_read_propagates()
    {
        using var cts = new CancellationTokenSource();
        var marker = new OperationCanceledException("the caller left", cts.Token);
        var (router, x, _, _, _, _) = Routed(aFails: false,
            store: new ThrowingRouteStore(() => { cts.Cancel(); return marker; }));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => router.CompleteAsync(Configured, Memory, cts.Token));

        Assert.Same(marker, thrown);
        Assert.Empty(x.Calls);
    }

    // ---- the capability probe follows the route --------------------------------------------------------

    [Fact]
    public async Task The_capability_probe_answers_for_the_first_live_candidate_of_the_live_route()
    {
        var (router, x, a, b, kv, _) = Routed(aFails: false);
        x.SupportsToolCalls = true;

        Assert.Same(x.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory)); // no route

        await kv.SetAsync("lyntai.route.memory", "b");
        Assert.Same(b.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory));

        a.IsAvailable = false;
        await kv.SetAsync("lyntai.route.memory", "a, b");
        Assert.Same(b.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory)); // the first LIVE entry

        await kv.SetAsync("lyntai.route.memory", "gone");
        Assert.Same(x.Capabilities, await router.GetCapabilitiesAsync(Configured, Memory)); // ignored, as the call ignores it

        Assert.Null(await router.GetCapabilitiesAsync([new("nobody")], Memory with { Consumer = "other" }));
    }

    [Fact]
    public async Task Under_a_route_to_a_backend_without_tool_calls_the_tool_loop_takes_the_prompt_path()
    {
        var (router, x, _, b, kv, _) = Routed(aFails: false);
        x.SupportsToolCalls = true; // the configured backend calls tools natively; the routed one does not
        await kv.SetAsync("lyntai.route.memory", "b");
        b.Replies.Enqueue(new TextResponse("""{"tool":"echo","arguments":{"x":1}}""", ProviderVerdict.Ok));
        b.Replies.Enqueue(new TextResponse("""{"final":"done"}""", ProviderVerdict.Ok));

        var result = await Loop(router).RunAsync(Memory);

        Assert.Equal(ToolTransport.Prompt, result.Transport);
        Assert.Equal("done", result.Answer);
        Assert.Equal("echo", Assert.Single(result.Steps).Tool); // the tool ran
        Assert.All(b.Calls, c => Assert.Null(c.Tools));
        Assert.Empty(x.Calls);
    }

    [Fact]
    public async Task Without_a_route_the_tool_loop_keeps_the_configured_backends_transport()
    {
        var (router, x, _, b, _, _) = Routed(aFails: false);
        x.SupportsToolCalls = true;
        x.Replies.Enqueue(new TextResponse("", ProviderVerdict.Ok) { ToolCalls = [new TextToolCall("call_1", "echo", "{}")] });
        x.Replies.Enqueue(new TextResponse("done", ProviderVerdict.Ok));

        var result = await Loop(router).RunAsync(Memory);

        Assert.Equal(ToolTransport.Native, result.Transport);
        Assert.Equal("echo", Assert.Single(result.Steps).Tool);
        Assert.NotNull(x.Calls[0].Tools);
        Assert.Empty(b.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Tools_reaching_a_backend_that_cannot_call_them_log_a_warning(bool streaming)
    {
        var (router, x, _, _, kv, warnings) = Routed(aFails: false);
        x.SupportsToolCalls = true;
        x.SupportsStreamingToolCalls = true;
        var withTools = Memory with { Tools = [new TextTool("echo", "echoes", "{}")] };

        await CallAsync(router, Configured, withTools, streaming); // x can call them
        await kv.SetAsync("lyntai.route.memory", "b");
        await CallAsync(router, Configured, Memory, streaming);    // b, but nothing to call
        Assert.Empty(warnings);

        await CallAsync(router, Configured, withTools, streaming);
        Assert.StartsWith("router: b ", Assert.Single(warnings));
    }

    [Fact]
    public async Task A_stream_carrying_tools_warns_on_a_backend_whose_stream_drops_them()
    {
        var (router, x, _, _, _, warnings) = Routed(aFails: false);
        x.SupportsToolCalls = true; // buffered replies carry calls; its stream does not
        var withTools = Memory with { Tools = [new TextTool("echo", "echoes", "{}")] };

        await router.CompleteAsync(Configured, withTools);
        Assert.Empty(warnings);

        await foreach (var _ in router.StreamAsync(Configured, withTools)) { }
        Assert.StartsWith("router: x ", Assert.Single(warnings));
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

    [Fact]
    public async Task Under_a_route_the_cache_key_names_the_requests_own_model_not_the_consumer_default()
    {
        var kv = new InMemoryKeyValueStore();
        var (client, cache, _) = Cached(kv); // the consumer default is "d"
        await kv.SetAsync("lyntai.route.memory", "b");

        await client.CompleteAsync(Memory);                     // b is asked for its own default model
        await client.CompleteAsync(Memory with { Model = "d" }); // b is asked for "d"

        Assert.NotEqual(cache.Keys[0], cache.Keys[1]);
    }

    [Fact]
    public async Task Under_a_route_the_cache_key_still_moves_with_the_consumer_default()
    {
        // a route naming nothing the router knows is ignored, and the given candidates then resolve through
        // the consumer default — so a key blind to it could serve a reply across a changed default
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("lyntai.route.memory", "gone");
        var (client, cache, options) = Cached(kv);

        await client.CompleteAsync(Memory);
        options.DefaultModelByConsumer["memory"] = "d2";
        await client.CompleteAsync(Memory);

        Assert.NotEqual(cache.Keys[0], cache.Keys[1]);
    }

    [Fact]
    public async Task The_callers_cancellation_during_the_caches_route_read_propagates()
    {
        using var cts = new CancellationTokenSource();
        var marker = new OperationCanceledException("the caller left", cts.Token);
        var inner = new FakeTextClient();
        var cache = new KeyRecordingCache();
        var client = new CachingTextClient(inner, cache, new LyntaiOptions(),
            modelRouting: new ThrowingRouteStore(() => { cts.Cancel(); return marker; }));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => client.CompleteAsync(Memory, cts.Token));

        Assert.Same(marker, thrown);
        Assert.Empty(inner.Calls);
        Assert.Empty(cache.Keys);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task AddLiveModelRouting_moves_the_containers_front_door(bool streaming)
    {
        var x = new FakeTextProvider("x");
        var b = new FakeTextProvider("b");
        var kv = new InMemoryKeyValueStore();
        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(o => o.AddProvider(_ => x).AddProvider(_ => b)
            .UseDefaultCandidates("x").AddResponseCache().AddLiveModelRouting());
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();

        await kv.SetAsync("lyntai.route.memory", "b:m2");
        if (streaming) { await foreach (var _ in client.StreamAsync(Memory)) { } }
        else await client.CompleteAsync(Memory);

        Assert.Equal("m2", Assert.Single(b.Calls).Model);
        Assert.Empty(x.Calls);
    }

    [Fact]
    public async Task A_faulting_route_store_neither_fails_the_call_nor_touches_the_cache()
    {
        var warnings = new List<string>();
        var inner = new FakeTextClient();
        var cache = new KeyRecordingCache();
        var client = new CachingTextClient(inner, cache, new LyntaiOptions(), Logger<CachingTextClient>(warnings),
            new ThrowingRouteStore(() => new InvalidOperationException("store down")));

        var reply = await client.CompleteAsync(Memory);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(inner.Calls);
        Assert.Empty(cache.Keys); // a key without the route could serve, or store, another backend's reply
        Assert.Equal(0, cache.Stores);
        Assert.Single(warnings);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static readonly IReadOnlyList<ProviderCandidate> Configured = [new("x")];

    private static readonly TextRequest Memory = new() { Messages = [TextMessage.User("hi")], Consumer = "memory" };

    private static string Render(IReadOnlyList<ProviderCandidate> route) =>
        string.Join(" ", route.Select(c => $"{c.ProviderId}|{c.Model ?? "(default)"}"));

    /// <summary>A router over x, a and b (and <paramref name="more"/>) for consumer "memory", whose configured
    /// default model is "d"; calls are given the candidates [x]. With <paramref name="aFails"/>, a throws on both
    /// doors. The route is read from the returned KV store unless another <paramref name="store"/> is given.</summary>
    private static (TextRouter Router, FakeTextProvider X, FakeTextProvider A, FakeTextProvider B,
        InMemoryKeyValueStore Kv, List<string> Warnings) Routed(bool aFails, IModelRoutingStore? store = null,
        DeadHostTracker? deadHosts = null, Action<LyntaiOptions>? configure = null, IModelProvider[]? more = null)
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
        configure?.Invoke(options);
        var warnings = new List<string>();
        var router = new TextRouter([x, a, b, .. more ?? []], deadHosts ?? new DeadHostTracker(), options,
            Logger<TextRouter>(warnings), modelRouting: store ?? new KeyValueModelRoutingStore(kv));
        return (router, x, a, b, kv, warnings);
    }

    /// <summary>A tool loop over the front door a host composes — the router given the candidates [x] — with
    /// one tool, <c>echo</c>.</summary>
    private static ToolLoop Loop(TextRouter router)
    {
        var options = new LyntaiOptions();
        var echo = new FunctionTool("echo", (args, _) => Task.FromResult($"observed:{args}"), "echoes its args");
        return new ToolLoop(new TextClient(router, options, Configured), new ToolRegistry([echo]), options);
    }

    /// <summary>A BYO store that throws on every read.</summary>
    private sealed class ThrowingRouteStore(Func<Exception> fault) : IModelRoutingStore
    {
        public Task<IReadOnlyList<ProviderCandidate>> GetRouteAsync(string consumer, CancellationToken ct = default) =>
            throw fault();
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
        public int Lists { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            OnGet is { } fault ? throw fault() : Task.FromResult(Data.GetValueOrDefault(key));

        public Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default)
        {
            Lists++;
            return OnList is { } fault
                ? throw fault()
                : Task.FromResult<IReadOnlyList<string>>([.. Data.Keys
                    .Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)]);
        }

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

        public int Stores { get; private set; }

        public Task SetAsync(string key, TextResponse reply, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            Stores++;
            return Task.CompletedTask;
        }

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
