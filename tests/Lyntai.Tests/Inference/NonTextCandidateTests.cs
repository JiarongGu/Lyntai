using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>A text candidate naming a registered backend that produces NO text — an embedder, a reranker, a
/// media backend. A CONFIGURED list is known at composition, so naming one fails there; a list passed at RUN
/// TIME is not, so the router skips it per call and its reply says why.</summary>
public class NonTextCandidateTests
{
    // ---- a configured list fails at composition ---------------------------------------------------------

    [Fact]
    public void Default_candidates_naming_a_registered_vector_backend_fail_at_composition()
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat", "embed:e5"));

        var error = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClient>());
        Assert.Contains("the default client", error.Message, StringComparison.Ordinal);
        Assert.Contains("embed:e5", error.Message, StringComparison.Ordinal);
        Assert.Contains("vector", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseDefaultCandidates", error.Message, StringComparison.Ordinal); // and says the fix

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClientFactory>());
    }

    [Theory]
    [InlineData(false)] // its candidates derived from the backends it is pooled over
    [InlineData(true)]  // its candidates stated outright
    public void A_named_clients_candidates_naming_a_non_text_backend_fail_at_composition(bool stated)
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat").AddTextClient("judge", c =>
        {
            c.UseProviders("chat", "rerank");
            if (stated) c.UseCandidates(new ProviderCandidate("chat"), new ProviderCandidate("rerank"));
        }));

        var error = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClientFactory>());
        Assert.Contains("judge", error.Message, StringComparison.Ordinal);
        Assert.Contains("rerank", error.Message, StringComparison.Ordinal);
        Assert.Contains("score", error.Message, StringComparison.Ordinal);

        Assert.NotNull(sp.GetRequiredService<ITextClient>()); // the default list names none, so it composes
    }

    [Fact]
    public void A_candidate_naming_no_registered_backend_is_not_this_checks_business()
    {
        // an adapter package may be absent in one environment; the router skips such a candidate per call
        using var sp = Build(b => b.UseDefaultCandidates("ghost", "chat"));

        Assert.NotNull(sp.GetRequiredService<ITextClient>());
        Assert.NotNull(sp.GetRequiredService<ITextClientFactory>());
    }

    [Fact]
    public void A_list_of_text_backends_composes_as_it_always_has_beside_a_registered_embedder()
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat", "chat2")
            .AddTextClient("judge", c => c.UseProviders("chat2")));

        Assert.NotNull(sp.GetRequiredService<ITextClient>());
        Assert.NotNull(sp.GetRequiredService<ITextClientFactory>().Get("judge"));
    }

    // ---- a run-time list is skipped per call --------------------------------------------------------------

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_mixed_list_skips_the_non_text_backend_and_serves_from_the_text_one(bool streaming)
    {
        var (router, chat, embed, rerank) = Routed();

        var (verdict, text) = await CallAsync(router, [new("embed"), new("rerank"), new("chat")], streaming);

        Assert.Equal(ProviderVerdict.Ok, verdict);
        Assert.StartsWith("chat ", text);
        Assert.Empty(embed.Calls);
        Assert.Empty(rerank.Calls);
        Assert.Single(chat.Calls);
    }

    [Theory]
    [InlineData(false, "embed")] // a sole candidate is skipped too: its exemption is from cooldown, not from kind
    [InlineData(true, "embed")]
    [InlineData(false, "embed:e5,rerank")]
    [InlineData(true, "embed:e5,rerank")]
    public async Task A_list_of_only_non_text_backends_is_Unsupported_naming_them(bool streaming, string list)
    {
        var tracker = new DeadHostTracker(threshold: 1, TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow);
        var (router, _, embed, rerank) = Routed(tracker);
        var specs = list.Split(',');

        var (verdict, detail) = await CallAsync(router, [.. specs.Select(ProviderCandidateSpec.Parse)], streaming);

        Assert.Equal(ProviderVerdict.Unsupported, verdict); // a capability gap, never a failed host
        foreach (var spec in specs) Assert.Contains(spec, detail);
        Assert.Contains("vector", detail);
        if (specs.Length > 1) Assert.Contains("score", detail);
        Assert.Empty(embed.Calls);
        Assert.Empty(rerank.Calls);
        Assert.False(tracker.IsDead("embed"));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_list_skipped_for_several_reasons_is_Failed_naming_each(bool streaming)
    {
        var (router, chat, embed, _) = Routed();
        chat.IsAvailable = false;

        var (verdict, detail) = await CallAsync(router, [new("embed"), new("ghost"), new("chat")], streaming);

        Assert.Equal(ProviderVerdict.Failed, verdict);
        Assert.Contains("no live candidate", detail);
        Assert.Contains("embed: produces vector", detail);
        Assert.Contains("ghost: no provider with this id registered", detail);
        Assert.Contains("chat: provider reports unavailable", detail);
        Assert.Empty(embed.Calls);
    }

    [Fact]
    public async Task The_capability_probe_skips_a_non_text_candidate_as_the_call_does()
    {
        var (router, chat, _, _) = Routed();

        Assert.Same(chat.Capabilities, await router.GetCapabilitiesAsync([new("embed"), new("chat")], Ask));
        Assert.Null(await router.GetCapabilitiesAsync([new("embed"), new("rerank")], Ask));
    }

    [Fact]
    public async Task Through_the_container_a_run_time_list_is_skipped_as_the_router_skips_it()
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat"));
        var router = sp.GetRequiredService<ITextRouter>();

        var reply = await router.CompleteAsync([new("embed"), new("chat")], Ask);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.StartsWith("chat ", reply.Text);
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static readonly TextRequest Ask = new() { Messages = [TextMessage.User("hi")] };

    private static FakeTextProvider Producing(string id, string kind) => new(id)
    {
        Capabilities = new() { Accepts = [ProviderKinds.Text], Produces = [kind], Operations = [ProviderOperation.Complete] },
    };

    /// <summary>A container over two chat backends, an embedder and a reranker, composed by
    /// <paramref name="configure"/>.</summary>
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.Services.AddSingleton<IModelProvider>(new FakeTextProvider("chat"));
            b.Services.AddSingleton<IModelProvider>(new FakeTextProvider("chat2"));
            b.Services.AddSingleton<IModelProvider>(new FakeVectorProvider { Id = "embed" });
            b.Services.AddSingleton<IModelProvider>(new ScoreBackend("rerank"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>A reranker: it serves scores, so a text call to it reaches the seam's <c>Unsupported</c>
    /// default.</summary>
    private sealed class ScoreBackend(string id) : IScoreProvider
    {
        public string Id { get; } = id;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Score],
            Operations = [ProviderOperation.Complete],
        };

        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
            Task.FromResult(ScoreResponse.Failure(ProviderVerdict.Failed, "not asked here"));
    }

    private static (TextRouter Router, FakeTextProvider Chat, FakeTextProvider Embed, FakeTextProvider Rerank) Routed(
        DeadHostTracker? tracker = null)
    {
        var chat = new FakeTextProvider("chat");
        var embed = Producing("embed", ProviderKinds.Vector);
        var rerank = Producing("rerank", ProviderKinds.Score);
        return (new TextRouter([embed, rerank, chat], tracker ?? new DeadHostTracker(), new LyntaiOptions()),
            chat, embed, rerank);
    }

    /// <summary>One call through the chosen door: the verdict, and the reply's text on success or its detail
    /// otherwise.</summary>
    private static async Task<(ProviderVerdict Verdict, string Text)> CallAsync(
        TextRouter router, IReadOnlyList<ProviderCandidate> candidates, bool streaming)
    {
        if (!streaming)
        {
            var reply = await router.CompleteAsync(candidates, Ask);
            return (reply.Verdict, reply.Verdict == ProviderVerdict.Ok ? reply.Text : reply.Detail ?? "");
        }

        var text = "";
        await foreach (var chunk in router.StreamAsync(candidates, Ask))
        {
            if (chunk.Kind == TextChunkKind.Content) text += chunk.Text;
            if (chunk.Kind == TextChunkKind.Error) return (chunk.Verdict, chunk.Detail ?? "");
        }
        return (ProviderVerdict.Ok, text);
    }
}
