using Lyntai;
using Lyntai.Llm.Routing;
using Lyntai.Prompts;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Core;

/// <summary>P1 (app-owned storage): the cortex KV key namespaces are configurable, so an app can point
/// Lyntai's prompt/model overrides straight at its OWN existing keys — no prefix-translating shim, no
/// duplicated rows. The default namespaces (<c>lyntai.prompt.</c> / <c>lyntai.model.</c>) are unchanged.</summary>
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
        kv.Data["llm.model.chat"] = "haiku";

        var store = new KeyValueModelRoutingStore(kv, keyPrefix: "llm.model.");
        Assert.Equal("haiku", await store.GetModelOverrideAsync("chat"));
    }

    [Fact]
    public async Task ModelRoutingStore_default_prefix_is_unchanged()
    {
        Assert.Equal("lyntai.model.", KeyValueModelRoutingStore.DefaultKeyPrefix);

        var kv = new InMemoryKeyValueStore();
        kv.Data[KeyValueModelRoutingStore.DefaultKeyPrefix + "chat"] = "haiku";

        var store = new KeyValueModelRoutingStore(kv); // no prefix override
        Assert.Equal("haiku", await store.GetModelOverrideAsync("chat"));
    }

    [Fact]
    public async Task Configured_prompt_prefix_flows_through_AddLyntai()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["cortex.prompt.p"] = "app override: {v}";

        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("fake"))
            .Configure(o => o.PromptKeyPrefix = "cortex.prompt."));
        using var sp = services.BuildServiceProvider();

        var rendered = await sp.GetRequiredService<IPromptRegistry>().RenderAsync("p", "default {v}",
            new Dictionary<string, string> { ["v"] = "x" });

        Assert.Equal("app override: x", rendered);
    }

    [Fact]
    public async Task Configured_model_prefix_flows_through_AddLiveModelRouting()
    {
        var kv = new InMemoryKeyValueStore();
        kv.Data["llm.model.scoring"] = "haiku";

        var services = new ServiceCollection();
        services.AddSingleton<IKeyValueStore>(kv);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("fake"))
            .Configure(o => o.ModelKeyPrefix = "llm.model.")
            .AddLiveModelRouting());
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IModelRoutingStore>();
        Assert.Equal("haiku", await store.GetModelOverrideAsync("scoring"));
    }
}
