using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

/// <summary>
/// Named <see cref="ILlmClient"/>s, the chat counterpart of named memory engines.
/// <para>The facts worth pinning are the ones that make a name SAFE: that it selects backends and not
/// permissions, and that a name pointing at a backend nobody registered fails loudly instead of quietly
/// running on the app's default.</para>
/// </summary>
public class LlmClientFactoryTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    private static LyntaiBuilder WithProviders(LyntaiBuilder b, params string[] ids)
    {
        foreach (var id in ids) b.Services.AddSingleton<ILlmProvider>(new FakeLlmProvider(id));
        return b;
    }

    [Fact]
    public void A_named_client_resolves_by_name()
    {
        using var sp = Build(b => WithProviders(b, "cheap", "best").AddLlmClient("memory", c => c.UseProviders("cheap")));
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        Assert.Equal(["memory"], factory.Names);
        Assert.NotNull(factory.Get("memory"));
        Assert.True(factory.TryGet("memory", out _));
    }

    /// <summary>The default is always available, so a consumer can depend on the factory without registering
    /// anything — and a subsystem with an OPTIONAL configured name has something to fall back to.</summary>
    [Fact]
    public void The_default_client_is_available_with_no_named_registrations()
    {
        using var sp = Build(b => WithProviders(b, "only"));
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        Assert.Empty(factory.Names);
        Assert.Same(sp.GetRequiredService<ILlmClient>(), factory.Get());
        Assert.False(factory.TryGet("nope", out _));
    }

    /// <summary>An unknown name lists the registered ones — a typo in a composition root should be
    /// diagnosable from the exception rather than by reading the wiring.</summary>
    [Fact]
    public void An_unknown_name_throws_and_says_what_is_registered()
    {
        using var sp = Build(b => WithProviders(b, "cheap").AddLlmClient("memory", c => c.UseProviders("cheap")));
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var ex = Assert.Throws<KeyNotFoundException>(() => factory.Get("typo"));
        Assert.Contains("memory", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><b>A name pointing at a backend nobody registered FAILS.</b> The tempting alternative —
    /// narrow to whatever does exist — degrades a subsystem onto the app's default backend, which is exactly
    /// the outcome naming a client exists to prevent, and it does so silently.</summary>
    [Fact]
    public void A_name_pointing_at_an_unregistered_backend_throws()
    {
        using var sp = Build(b => WithProviders(b, "cheap").AddLlmClient("memory", c => c.UseProviders("ghost")));

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ILlmClientFactory>());
        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("cheap", ex.Message, StringComparison.Ordinal);   // and says what IS available
    }

    /// <summary>Two clients under one name would make one unreachable — the same refusal a duplicate memory
    /// engine gets, and for the same reason.</summary>
    [Fact]
    public void A_duplicate_name_is_refused()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddLyntai(b => b
            .AddLlmClient("memory")
            .AddLlmClient("memory")));

        Assert.Contains("already registered", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Naming no provider means EVERY registered one — the default client's own behaviour, which is
    /// the right meaning for a name that exists only to carry different governance later.</summary>
    [Fact]
    public void Naming_no_provider_routes_over_all_of_them()
    {
        using var sp = Build(b => WithProviders(b, "a", "b").AddLlmClient("everything"));

        Assert.NotNull(sp.GetRequiredService<ILlmClientFactory>().Get("everything"));
    }

    /// <summary><b>A named client is governed exactly like the default one.</b> The front-door decorators are
    /// folded over it in the same order, so a usage budget cannot be escaped by asking for a client by name.
    /// Asserted through the OBSERVABLE consequence — the budget refuses — rather than by inspecting the
    /// decorator list, because it is the consequence that matters and the list is an implementation
    /// detail.</summary>
    [Fact]
    public async Task A_named_client_is_governed_by_the_same_front_door_as_the_default()
    {
        using var sp = Build(b => WithProviders(b, "cheap")
            .AddUsageBudget(o => o.MaxTokens = 0)               // nothing may be spent
            .AddLlmClient("memory", c => c.UseProviders("cheap")));

        var reply = await sp.GetRequiredService<ILlmClientFactory>().Get("memory")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
    }
}
