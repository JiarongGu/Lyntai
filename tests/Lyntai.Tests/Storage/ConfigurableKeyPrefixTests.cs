using Lyntai;
using Lyntai.Inference;
using Lyntai.Prompts;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>App-owned storage: the cortex KV key namespaces are configurable, so an app can point Lyntai's
/// prompt overrides and live routes straight at its OWN existing keys — no prefix-translating shim, no
/// duplicated rows. The defaults are <c>lyntai.prompt.</c> and <c>lyntai.route.</c>.</summary>
public class ConfigurableKeyPrefixTests
{
    [Fact]
    public async Task PromptRegistry_custom_prefix_reads_the_apps_own_key()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["cortex.prompt.plan"] = "app override: {v}";

        var registry = new PromptRegistry(kv, keyPrefix: "cortex.prompt.");
        var rendered = await registry.RenderAsync("plan", "default {v}",
            new Dictionary<string, string> { ["v"] = "x" });

        Assert.Equal("app override: x", rendered);
    }

    [Fact]
    public async Task PromptRegistry_default_prefix_is_unchanged()
    {
        Assert.Equal("lyntai.prompt.", PromptRegistry.DefaultKeyPrefix);

        var kv = new InMemoryKeyValueStore();
        kv.Data[PromptRegistry.DefaultKeyPrefix + "plan"] = "lyntai override: {v}";

        var registry = new PromptRegistry(kv); // no prefix override
        var rendered = await registry.RenderAsync("plan", "default {v}",
            new Dictionary<string, string> { ["v"] = "x" });

        Assert.Equal("lyntai override: x", rendered);
    }

    [Fact]
    public async Task ModelRoutingStore_custom_prefix_reads_the_apps_own_key()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["llm.route.chat"] = "claude:haiku";
        kv.Data["lyntai.route.chat"] = "ollama:ignored"; // the default namespace is not this store's

        var route = await new KeyValueModelRoutingStore(kv, keyPrefix: "llm.route.").GetRouteAsync("chat");

        Assert.Equal(new ProviderCandidate("claude", "haiku"), Assert.Single(route));
    }

    [Fact]
    public async Task ModelRoutingStore_default_prefix_is_lyntai_route()
    {
        Assert.Equal("lyntai.route.", KeyValueModelRoutingStore.DefaultKeyPrefix);

        var kv = new InMemoryKeyValueStore();
        kv.Data[KeyValueModelRoutingStore.DefaultKeyPrefix + "chat"] = "claude:haiku";

        var route = await new KeyValueModelRoutingStore(kv).GetRouteAsync("chat"); // no prefix override

        Assert.Equal(new ProviderCandidate("claude", "haiku"), Assert.Single(route));
    }

    [Fact]
    public async Task Configured_prompt_prefix_flows_through_AddLyntai()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["cortex.prompt.p"] = "app override: {v}";

        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("fake"))
            .Configure(o => o.PromptKeyPrefix = "cortex.prompt."));
        using var sp = services.BuildServiceProvider();

        var rendered = await sp.GetRequiredService<IPromptRegistry>().RenderAsync("p", "default {v}",
            new Dictionary<string, string> { ["v"] = "x" });

        Assert.Equal("app override: x", rendered);
    }

    [Fact]
    public async Task Configured_route_prefix_flows_through_AddLiveModelRouting()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["llm.route.scoring"] = "claude:haiku";

        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("fake"))
            .Configure(o => o.RouteKeyPrefix = "llm.route.")
            .AddLiveModelRouting());
        using var sp = services.BuildServiceProvider();

        var route = await sp.GetRequiredService<IModelRoutingStore>().GetRouteAsync("scoring");
        Assert.Equal(new ProviderCandidate("claude", "haiku"), Assert.Single(route));
    }
}
