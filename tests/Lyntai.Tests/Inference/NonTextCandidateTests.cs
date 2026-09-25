using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        Assert.DoesNotContain("declare ProviderKinds.Text", error.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClientFactory>());
    }

    [Theory]
    [InlineData(false, "UseProviders")] // its candidates derived from the backends it is pooled over
    [InlineData(true, "UseCandidates")]  // its candidates stated outright
    public void A_named_clients_candidates_naming_a_non_text_backend_fail_at_composition(bool stated, string fix)
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
        Assert.Contains(fix, error.Message, StringComparison.Ordinal);

        Assert.NotNull(sp.GetRequiredService<ITextClient>()); // the default list names none, so it composes
    }

    [Fact]
    public void A_named_client_inheriting_the_default_list_is_pointed_at_the_default_list()
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat", "embed").AddTextClient("judge", _ => { }));

        var error = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClientFactory>());
        Assert.Contains("'judge'", error.Message, StringComparison.Ordinal);
        Assert.Contains("inherited from the default candidates", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseDefaultCandidates", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("UseCandidates", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_pool_holding_an_embedder_composes_when_its_stated_candidates_leave_it_out()
    {
        using var sp = Build(b => b.UseDefaultCandidates("chat")
            .AddTextClient("judge", c => c.UseProviders("chat", "embed").UseCandidates(new ProviderCandidate("chat"))));

        Assert.NotNull(sp.GetRequiredService<ITextClientFactory>().Get("judge"));
    }

    [Fact]
    public void A_backend_declaring_no_output_is_told_to_declare_text_rather_than_to_be_removed()
    {
        // an empty Produces serves nothing under ProviderCapabilities' contract, but a BYO chat backend that
        // simply forgot to declare is the likelier case, so removing it would be the wrong fix
        using var sp = Build(b =>
        {
            b.Services.AddSingleton<IModelProvider>(DeclaringNothing("custom"));
            b.UseDefaultCandidates("custom", "chat");
        });

        var error = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITextClient>());
        Assert.Contains("custom (produces nothing)", error.Message, StringComparison.Ordinal);
        Assert.Contains("declare ProviderKinds.Text in its ProviderCapabilities.Produces", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove", error.Message, StringComparison.Ordinal);
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
        var warnings = new List<string>();
        var (router, chat, embed, rerank) = Routed(warnings: warnings);

        var (verdict, text) = await CallAsync(router, [new("embed"), new("rerank"), new("chat")], streaming);

        Assert.Equal(ProviderVerdict.Ok, verdict);
        Assert.StartsWith("chat ", text);
        Assert.Empty(embed.Calls);
        Assert.Empty(rerank.Calls);
        Assert.Single(chat.Calls);
        // a caller defect, not transient state — warned of as the live route warns of the same entry
        Assert.Equal(2, warnings.Count);
        Assert.Contains("embed", warnings[0]);
        Assert.Contains("rerank", warnings[1]);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task A_backend_declaring_no_output_is_skipped_per_call(bool streaming)
    {
        var chat = new FakeTextProvider("chat");
        var custom = DeclaringNothing("custom");
        var router = new TextRouter([custom, chat], new DeadHostTracker(), new LyntaiOptions());

        var (served, text) = await CallAsync(router, [new("custom"), new("chat")], streaming);
        Assert.Equal(ProviderVerdict.Ok, served);
        Assert.StartsWith("chat ", text);

        var (alone, detail) = await CallAsync(router, [new("custom")], streaming);
        Assert.Equal(ProviderVerdict.Unsupported, alone);
        Assert.Contains("custom: produces nothing, not text", detail);
        Assert.Empty(custom.Calls);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task An_empty_list_is_Failed_saying_none_was_given(bool streaming)
    {
        var (router, _, _, _) = Routed();

        var (verdict, detail) = await CallAsync(router, [], streaming);

        Assert.Equal(ProviderVerdict.Failed, verdict);
        Assert.Equal("no live candidate (none given)", detail);
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

    /// <summary>A BYO backend that answers text but declared no output kind at all.</summary>
    private static FakeTextProvider DeclaringNothing(string id) => new(id)
    {
        Capabilities = new() { Accepts = [ProviderKinds.Text], Operations = [ProviderOperation.Complete, ProviderOperation.Stream] },
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
        DeadHostTracker? tracker = null, List<string>? warnings = null)
    {
        var chat = new FakeTextProvider("chat");
        var embed = Producing("embed", ProviderKinds.Vector);
        var rerank = Producing("rerank", ProviderKinds.Score);
        var logger = warnings is null ? null : new CapturingLogger<TextRouter>(warnings);
        return (new TextRouter([embed, rerank, chat], tracker ?? new DeadHostTracker(), new LyntaiOptions(), logger),
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
