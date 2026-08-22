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

    /// <summary><b>The other half of that promise, and the half nothing asserted.</b>
    /// <c>LlmClientRegistration</c>'s own doc says every named client carries "the same outermost refusal
    /// screening as the default one" — and the fold was written TWICE, so deleting the screening from the
    /// named copy left the entire suite green. The budget fact above covers the decorator half; this covers
    /// the layer that sits outside them, which is the one a second copy loses first because it is added last.
    /// </summary>
    [Fact]
    public async Task A_named_client_is_screened_for_refusals_like_the_default()
    {
        // This one has to reach the provider and come back Ok before screening has anything to screen; the
        // budget fact above refuses before routing, which is why it needed no candidate list at all.
        using var sp = Build(b => WithProviders(b, "cheap")
            .UseDefaultCandidates("cheap")
            .AddRefusalMatcher(new AlwaysRefuses())
            .AddLlmClient("memory", c => c.UseProviders("cheap")));

        var reply = await sp.GetRequiredService<ILlmClientFactory>().Get("memory")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
    }

    /// <summary><b>The wiring the docs recommend has to ROUTE.</b> A client narrowed to one backend, on a host
    /// whose default candidates name a different one, used to resolve and then fail every call: the router's
    /// provider set was narrowed while its candidate list still came from the global options, so every
    /// candidate it tried was absent from its own pool. Reported by an adopter on 3.0.2, whose memory judge
    /// silently stopped running because both memory policies are fail-open.</summary>
    [Fact]
    public async Task A_named_client_routes_over_its_own_backends_when_the_default_candidates_name_none_of_them()
    {
        var cli = new FakeLlmProvider("claude-cli");
        var ollama = new FakeLlmProvider("ollama-chat");
        using var sp = Build(b => b
            .UseDefaultCandidates("claude-cli")
            .AddLlmClient("judge", c => c.UseProviders("ollama-chat"))
            .Services.AddSingleton<ILlmProvider>(cli).AddSingleton<ILlmProvider>(ollama));

        var reply = await sp.GetRequiredService<ILlmClientFactory>().Get("judge")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Single(ollama.Calls);
        Assert.Empty(cli.Calls);          // and the client it was NARROWED away from stays untouched
    }

    /// <summary>The other half: narrowing a name must not widen the DEFAULT client. The adopter's workaround
    /// was to append the named backend to the global candidate list, which works and quietly lets the default
    /// client reach a backend it was never meant to — so the fix is only a fix if this stays true.</summary>
    [Fact]
    public async Task Deriving_a_named_clients_candidates_leaves_the_default_client_narrow()
    {
        var cli = new FakeLlmProvider("claude-cli");
        var ollama = new FakeLlmProvider("ollama-chat");
        using var sp = Build(b => b
            .UseDefaultCandidates("claude-cli")
            .AddLlmClient("judge", c => c.UseProviders("ollama-chat"))
            .Services.AddSingleton<ILlmProvider>(cli).AddSingleton<ILlmProvider>(ollama));

        await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Single(cli.Calls);
        Assert.Empty(ollama.Calls);
    }

    /// <summary>A derived candidate INHERITS whatever the default list already said about that backend, so a
    /// model pinned once globally is not silently dropped by narrowing a client onto the same backend.</summary>
    [Fact]
    public async Task A_derived_candidate_keeps_the_model_the_default_list_pinned_for_that_backend()
    {
        var small = new FakeLlmProvider("local");
        using var sp = Build(b => b
            .UseDefaultCandidates(new LlmCandidate("hosted"), new LlmCandidate("local", "qwen3:4b"))
            .AddLlmClient("judge", c => c.UseProviders("local"))
            .Services.AddSingleton<ILlmProvider>(new FakeLlmProvider("hosted"))
                     .AddSingleton<ILlmProvider>(small));

        await sp.GetRequiredService<ILlmClientFactory>().Get("judge")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal("qwen3:4b", Assert.Single(small.Calls).Model);
    }

    /// <summary><b>The order a name declares IS its fallback order</b> — <c>UseProviders</c> has said so since
    /// it shipped, and the derived list is what finally makes that true. Pinned with a first candidate that
    /// FAILS, so the assertion is about which backend is tried first rather than about which one answers.
    /// </summary>
    [Fact]
    public async Task A_named_clients_fallback_order_is_the_order_it_declared()
    {
        var first = new FakeLlmProvider("b");
        first.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down"));
        var second = new FakeLlmProvider("a");
        using var sp = Build(b => b
            .UseDefaultCandidates("a", "b")                    // the GLOBAL order is a, then b
            .AddLlmClient("judge", c => c.UseProviders("b", "a"))
            .Services.AddSingleton<ILlmProvider>(second).AddSingleton<ILlmProvider>(first));

        var reply = await sp.GetRequiredService<ILlmClientFactory>().Get("judge")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Single(first.Calls);       // tried first, failed
        Assert.Single(second.Calls);      // …then fell over to the one declared second
    }

    /// <summary>A backend in the pool that the global list never mentions is still reachable. Under the old
    /// behaviour a PARTIAL overlap was the quietest shape of the bug: the client routed, so nothing looked
    /// broken, and the un-named half of its own pool was simply unreachable.</summary>
    [Fact]
    public async Task A_pooled_backend_absent_from_the_default_list_is_still_reachable()
    {
        var known = new FakeLlmProvider("a");
        known.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down"));
        var unlisted = new FakeLlmProvider("c");
        using var sp = Build(b => b
            .UseDefaultCandidates("a", "b")
            .AddLlmClient("judge", c => c.UseProviders("a", "c"))
            .Services.AddSingleton<ILlmProvider>(known)
                     .AddSingleton<ILlmProvider>(new FakeLlmProvider("b"))
                     .AddSingleton<ILlmProvider>(unlisted));

        var reply = await sp.GetRequiredService<ILlmClientFactory>().Get("judge")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Single(unlisted.Calls);
    }

    /// <summary>Naming no provider still means EVERY registered one routed over the GLOBAL list — there is
    /// nothing to derive from, and a name that exists only to carry different governance must not quietly
    /// acquire a different fallback order.</summary>
    [Fact]
    public async Task Naming_no_provider_keeps_the_global_candidate_list()
    {
        var primary = new FakeLlmProvider("a");
        var other = new FakeLlmProvider("b");
        using var sp = Build(b => b
            .UseDefaultCandidates("a")
            .AddLlmClient("everything")
            .Services.AddSingleton<ILlmProvider>(primary).AddSingleton<ILlmProvider>(other));

        await sp.GetRequiredService<ILlmClientFactory>().Get("everything")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Single(primary.Calls);
        Assert.Empty(other.Calls);
    }

    /// <summary><b>The explicit seam.</b> Derivation cannot express two models of ONE backend — the exact
    /// split <c>AddLlmClient</c> exists for, when the small model and the big one live behind the same id — so
    /// a name can state its candidates outright.</summary>
    [Fact]
    public async Task A_named_client_can_state_its_candidates_outright()
    {
        var backend = new FakeLlmProvider("local");
        using var sp = Build(b => b
            .UseDefaultCandidates(new LlmCandidate("local", "big"))
            .AddLlmClient("judge", c => c.UseCandidates(new LlmCandidate("local", "small")))
            .Services.AddSingleton<ILlmProvider>(backend));

        await sp.GetRequiredService<ILlmClientFactory>().Get("judge")
            .CompleteAsync(new LlmRequest { Messages = [new LlmMessage("user", "anything")] });

        Assert.Equal("small", Assert.Single(backend.Calls).Model);
    }

    /// <summary>A candidate outside the client's own pool can never be selected, so it is refused at
    /// composition rather than skipped at every call — the same loud failure an unregistered backend id
    /// already gets, for the same reason.</summary>
    [Fact]
    public void A_candidate_outside_the_clients_own_pool_is_refused()
    {
        using var sp = Build(b => WithProviders(b, "a", "b")
            .AddLlmClient("judge", c => c.UseProviders("a").UseCandidates(new LlmCandidate("b"))));

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ILlmClientFactory>());
        Assert.Contains("judge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>…and the same holds when the client names no provider, which is the case the first version of
    /// that check missed: its pool is then EVERY registered backend, so a stated candidate is outside it
    /// exactly when nothing registered answers to that id. Checking against the declared ids rather than the
    /// resolved pool left this one silent — a client that resolves and fails every call, which is the whole
    /// defect this Part is about.
    /// <para><b>It discriminates only as a PAIR</b> with
    /// <c>A_named_client_can_state_its_candidates_outright</c> above, which also declares no provider and
    /// must NOT throw. Checking the declared ids passes one or the other depending on whether an empty list
    /// short-circuits; only reading the resolved pool passes both. Neither fact is worth much alone.</para>
    /// </summary>
    [Fact]
    public void A_stated_candidate_naming_no_registered_backend_is_refused_even_with_no_pool()
    {
        using var sp = Build(b => WithProviders(b, "a")
            .AddLlmClient("judge", c => c.UseCandidates(new LlmCandidate("ghost"))));

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ILlmClientFactory>());
        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a", ex.Message, StringComparison.Ordinal);   // and says what IS available
    }

    private sealed class AlwaysRefuses : IRefusalMatcher
    {
        public bool IsRefusal(LlmRequest request, string replyText) => true;
    }
}
