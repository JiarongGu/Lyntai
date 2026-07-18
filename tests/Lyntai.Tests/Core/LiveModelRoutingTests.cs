using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;
using InMemoryKeyValueStore = Lyntai.Storage.InMemory.InMemoryKeyValueStore;

namespace Lyntai.Tests.Core;

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
        var provider = new FakeLlmProvider("p"); // its Calls capture the request the router built (with the effective model)
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["scoring"] = "config-model";
        var router = new LlmRouter([provider], new DeadHostTracker(), options,
            modelRouting: new KeyValueModelRoutingStore(kv));
        IReadOnlyList<LlmCandidate> candidates = [new LlmCandidate("p")];
        var req = new LlmRequest { Messages = [LlmMessage.User("hi")], Consumer = "scoring" };

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
}
