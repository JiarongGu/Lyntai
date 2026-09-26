using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>Text providers registered at run time (<see cref="ITextProviderRegistry"/>): served by the DEFAULT text
/// client — so the budget, cache and screening folded onto it apply — read as one snapshot per call, and refused at
/// the edit when they could never be routed to.</summary>
public class TextProviderRegistryTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")] };

    private static ProviderKey Key(string slot, string url = "http://a") => ProviderKey.For(slot).With("url", url).Build();

    private static FakeTextProvider Answering(string id, double cost = 0)
    {
        var provider = new FakeTextProvider(id);
        for (var i = 0; i < 8; i++)
            provider.Replies.Enqueue(new TextResponse($"from {id}", ProviderVerdict.Ok, new TextUsage(0, 0, CostUsd: cost)));
        return provider;
    }

    private static ServiceProvider Build(Action<LyntaiBuilder>? more = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => Answering("base")).UseDefaultCandidates("base").UseTextProviderRegistry();
            more?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_registered_provider_named_by_the_default_candidates_answers_the_next_call()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var client = sp.GetRequiredService<ITextClient>();
        Assert.Equal("from base", (await client.CompleteAsync(Req)).Text);

        registry.Register(new(Key("user"), () => Answering("user")));
        registry.SetDefaultCandidates([new ProviderCandidate("user")]);

        Assert.Equal("from user", (await client.CompleteAsync(Req)).Text);
        Assert.Equal([Key("user")], registry.Registered);

        registry.SetDefaultCandidates(null);                                   // back to LyntaiOptions.DefaultCandidates
        Assert.Equal("from base", (await client.CompleteAsync(Req)).Text);
    }

    [Fact]
    public async Task Unregister_stops_it_and_reports_whether_there_was_one()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var client = sp.GetRequiredService<ITextClient>();
        registry.Register(new(Key("user"), () => Answering("user")));
        registry.SetDefaultCandidates([new ProviderCandidate("user"), new ProviderCandidate("base")]);

        Assert.True(registry.Unregister("user"));
        Assert.False(registry.Unregister("user"));

        Assert.Equal("from base", (await client.CompleteAsync(Req)).Text);   // the unknown id is skipped
        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task The_budget_folded_onto_the_client_governs_a_registered_provider()
    {
        using var sp = Build(b => b.AddUsageBudget(o => o.MaxCostUsd = 0.05));
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var client = sp.GetRequiredService<ITextClient>();
        registry.Register(new(Key("user"), () => Answering("user", cost: 0.06)));
        registry.SetDefaultCandidates([new ProviderCandidate("user")]);

        Assert.Equal(ProviderVerdict.Ok, (await client.CompleteAsync(Req)).Verdict);         // spends past the cap
        Assert.Equal(ProviderVerdict.Refused, (await client.CompleteAsync(Req)).Verdict);    // the cap binds
    }

    [Fact]
    public void An_id_a_container_provider_holds_is_refused_naming_it()
    {
        using var sp = Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            sp.GetRequiredService<ITextProviderRegistry>().Register(new(Key("BASE"), () => Answering("base"))));

        Assert.Contains("BASE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_that_could_never_be_routed_to_is_refused_and_not_leaked()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var pool = sp.GetRequiredService<IProviderPool<IModelProvider>>();
        var embedder = new FakeTextProvider("vec") { Capabilities = new() { Produces = [ProviderKinds.Vector] } };

        Assert.Throws<ArgumentException>(() => registry.Register(new(Key("user"), () => Answering("someone-else"))));
        Assert.Throws<ArgumentException>(() => registry.Register(new(Key("vec"), () => embedder)));

        Assert.Equal(0, pool.Statistics.Live);                                // neither left behind in the pool
        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task A_replaced_configuration_is_retired_and_a_benched_one_does_not_bench_its_replacement()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var client = sp.GetRequiredService<ITextClient>();
        var pool = sp.GetRequiredService<IProviderPool<IModelProvider>>();
        var deadHosts = sp.GetRequiredService<DeadHostTracker>();
        registry.Register(new(Key("user", "http://old"), () => Answering("user")));
        // two candidates: a SOLE candidate is exempt from benching, which would hide what this pins
        registry.SetDefaultCandidates([new ProviderCandidate("user"), new ProviderCandidate("base")]);

        deadHosts.MarkDead("user");                                            // an id-keyed bench does not reach it
        Assert.Equal("from user", (await client.CompleteAsync(Req)).Text);
        deadHosts.MarkDead(Key("user", "http://old").ToString());              // its configuration's bench does
        Assert.Equal("from base", (await client.CompleteAsync(Req)).Text);

        registry.Register(new(Key("user", "http://new"), () => Answering("user")));

        Assert.Equal("from user", (await client.CompleteAsync(Req)).Text);     // a fresh configuration, a fresh bench
        Assert.Equal(1, pool.Statistics.Retired);
        Assert.Equal([Key("user", "http://new")], registry.Registered);
    }

    [Fact]
    public async Task A_call_in_flight_across_an_edit_completes_on_its_snapshot()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        var client = sp.GetRequiredService<ITextClient>();
        var release = new TaskCompletionSource();
        var blocking = new BlockingProvider("user", release.Task);
        registry.Register(new(Key("user"), () => blocking));
        registry.SetDefaultCandidates([new ProviderCandidate("user")]);

        var inFlight = client.CompleteAsync(Req);
        await blocking.Entered;
        Assert.True(registry.Unregister("user"));
        release.SetResult();

        Assert.Equal("from user", (await inFlight).Text);
    }

    [Fact]
    public async Task A_registered_provider_is_admitted_under_its_configuration()
    {
        var admission = new RecordingAdmission();
        var services = new ServiceCollection();
        services.AddSingleton<IProviderAdmission>(admission);                   // before AddLyntai: its TryAdd stands down
        services.AddLyntai(b => b.AddProvider(_ => Answering("base")).UseDefaultCandidates("base").UseTextProviderRegistry());
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        registry.Register(new(Key("user"), () => Answering("user")));
        registry.SetDefaultCandidates([new ProviderCandidate("user")]);

        Assert.Equal("from user", (await sp.GetRequiredService<ITextClient>().CompleteAsync(Req)).Text);

        Assert.Equal([Key("user")], admission.Entered);
        Assert.Equal(1, admission.Released);
    }

    [Fact]
    public void Re_registering_an_unchanged_configuration_keeps_the_instance_it_holds()
    {
        // a host re-syncing its store re-registers every endpoint; a pool that no longer holds the entry (transient
        // here, idle-evicted in the bounded pool) would otherwise build — and strand — a second copy of each
        var builds = 0;
        using var sp = Build(b => b.UseTransientProviders());
        var registry = sp.GetRequiredService<ITextProviderRegistry>();

        registry.Register(new(Key("user"), () => { builds++; return Answering("user"); }));
        registry.Register(new(Key("user"), () => { builds++; return Answering("user"); }));

        Assert.Equal(1, builds);
        Assert.Equal([Key("user")], registry.Registered);
    }

    [Fact]
    public async Task A_named_client_never_tries_a_registered_provider()
    {
        using var sp = Build(b => b.AddTextClient("judge", c => c.UseProviders("base")));
        var registry = sp.GetRequiredService<ITextProviderRegistry>();
        registry.Register(new(Key("user"), () => Answering("user")));
        registry.SetDefaultCandidates([new ProviderCandidate("user")]);

        var reply = await sp.GetRequiredService<ITextClientFactory>().Get("judge").CompleteAsync(Req);

        Assert.Equal("from base", reply.Text);
    }

    [Fact]
    public void Without_UseTextProviderRegistry_there_is_no_registry()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => Answering("base")).UseDefaultCandidates("base"));
        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<ITextProviderRegistry>());
        Assert.NotNull(sp.GetRequiredService<ITextClient>());
    }

    private sealed class BlockingProvider(string id, Task release) : IModelProvider
    {
        private readonly TaskCompletionSource _entered = new();

        public Task Entered => _entered.Task;

        public string Id { get; } = id;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text], Produces = [ProviderKinds.Text], Operations = [ProviderOperation.Complete],
        };

        public bool IsAvailable => true;

        public async Task<TextResponse> CompleteAsync(TextRequest request, CancellationToken ct = default)
        {
            _entered.TrySetResult();
            await release.ConfigureAwait(false);
            return new TextResponse($"from {Id}", ProviderVerdict.Ok);
        }
    }
}
