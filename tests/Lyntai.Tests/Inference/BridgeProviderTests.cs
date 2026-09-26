using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>A backend built from a FUNCTION — the general form of bridging something that already answers.
///
/// <para><b>Bridging costs the library no dependency</b> (<c>docs/DECISIONS.md</c> D146, D147). What matters
/// here is that a bridge is a backend like any other: it routes, it falls over, and it declares only what it
/// was given a delegate for.</para></summary>
public class BridgeProviderTests
{
    private static TextRequest Ask(string text = "hello") => new() { Messages = [TextMessage.User(text)] };

    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => configure(b));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_lambda_becomes_a_routable_backend()
    {
        using var sp = Build(b => b
            .AddBridgeProvider("vendor", (req, _) =>
                Task.FromResult(new TextResponse($"echo: {req.Messages[^1].Content}", ProviderVerdict.Ok)))
            .UseDefaultCandidates("vendor"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask("hello"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("echo: hello", reply.Text);
    }

    [Fact]
    public void Without_a_stream_delegate_it_declares_NO_stream_so_a_router_never_asks()
    {
        using var sp = Build(b => b.AddBridgeProvider("vendor", (_, _) =>
            Task.FromResult(new TextResponse("x", ProviderVerdict.Ok))));

        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.True(caps.Supports(ProviderKinds.Text, ProviderOperation.Complete));
        Assert.False(caps.Supports(ProviderKinds.Text, ProviderOperation.Stream));
    }

    [Fact]
    public void Supplying_a_stream_delegate_declares_the_operation()
    {
        using var sp = Build(b => b.AddBridgeProvider("vendor",
            (_, _) => Task.FromResult(new TextResponse("x", ProviderVerdict.Ok)),
            (_, _) => Chunks()));

        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.True(caps.Supports(ProviderKinds.Text, ProviderOperation.Stream));

        static async IAsyncEnumerable<TextChunk> Chunks()
        {
            await Task.CompletedTask;
            yield return TextChunk.Content("x");
        }
    }

    [Fact]
    public async Task A_bridge_that_reports_a_failure_verdict_FALLS_OVER_like_any_other_backend()
    {
        // The reason the delegate returns a verdict rather than throwing: the router can only advance on
        // something it can read, and this is what makes a bridge a first-class backend rather than a leaf.
        using var sp = Build(b => b
            .AddBridgeProvider("down", (_, _) =>
                Task.FromResult(new TextResponse("", ProviderVerdict.Failed, Detail: "vendor is down")))
            .AddBridgeProvider("up", (_, _) => Task.FromResult(new TextResponse("served", ProviderVerdict.Ok)))
            .UseDefaultCandidates("down", "up"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask());

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("served", reply.Text);
    }

    [Theory]
    [InlineData(ProviderKinds.Vector)]
    [InlineData(ProviderKinds.Score)]
    [InlineData(ProviderKinds.Image)]
    public void A_kind_its_delegates_cannot_produce_is_REFUSED_at_the_call(string kind)
    {
        // A Score or Vector declaration would make the bridge look like a reranker or an embedder, which no
        // router selects — those select on IScoreProvider / IVectorProvider, and the delegates only answer
        // text. D153 throws for the same mismatch on an instance; a bridge's factory would escape that check,
        // so the call applies it.
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b.AddBridgeProvider(
            "vendor", (_, _) => Task.FromResult(new TextResponse("", ProviderVerdict.Ok)),
            capabilities: new ProviderCapabilities { Produces = [ProviderKinds.Text, kind] })));

        Assert.Contains(kind, ex.Message);
    }

    [Fact]
    public void Text_is_accepted_in_any_case_as_the_routers_match_it()
    {
        using var sp = Build(b => b.AddBridgeProvider("vendor",
            (_, _) => Task.FromResult(new TextResponse("x", ProviderVerdict.Ok)),
            capabilities: new ProviderCapabilities { Produces = ["TEXT"] }));

        Assert.Single(sp.GetServices<IModelProvider>());
    }

    [Fact]
    public async Task Capabilities_that_leave_the_kinds_empty_take_the_text_defaults_and_keep_the_rest()
    {
        // the documented way to declare tool calls; its delegates take and return text, so an empty
        // Produces must not make the bridge a backend that serves nothing
        using var sp = Build(b => b
            .AddBridgeProvider("vendor", (_, _) => Task.FromResult(new TextResponse("served", ProviderVerdict.Ok)),
                capabilities: new ProviderCapabilities { SupportsToolCalls = true })
            .UseDefaultCandidates("vendor"));

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask());
        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("served", reply.Text);
        Assert.True(caps.SupportsToolCalls);
        Assert.True(caps.Supports(ProviderKinds.Text, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        Assert.False(caps.Supports(ProviderKinds.Text, ProviderOperation.Stream)); // no stream delegate
    }

    [Fact]
    public void A_field_the_caller_set_is_kept_while_an_empty_one_takes_the_default()
    {
        // an image INPUT is a real declaration for a bridge (a vision model answering in text); an output
        // kind other than text is not, and is refused above
        using var sp = Build(b => b.AddBridgeProvider("vision",
            (_, _) => Task.FromResult(new TextResponse("", ProviderVerdict.Unsupported)),
            (_, _) => Chunks(),
            capabilities: new ProviderCapabilities { Accepts = [ProviderKinds.Text, ProviderKinds.Image] }));

        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.Equal([ProviderKinds.Text, ProviderKinds.Image], caps.Accepts);
        Assert.Equal([ProviderKinds.Text], caps.Produces);
        Assert.Equal([ProviderOperation.Complete, ProviderOperation.Stream], caps.Operations);

        static async IAsyncEnumerable<TextChunk> Chunks()
        {
            await Task.CompletedTask;
            yield return TextChunk.Content("x");
        }
    }

    [Fact]
    public void An_id_or_delegate_that_is_missing_fails_at_COMPOSITION_rather_than_on_first_call()
    {
        var b = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            b.AddLyntai(cfg => cfg.AddBridgeProvider("  ", (_, _) => Task.FromResult(new TextResponse("", ProviderVerdict.Ok)))));
        Assert.Throws<ArgumentNullException>(() =>
            b.AddLyntai(cfg => cfg.AddBridgeProvider("id", null!)));
    }
}
