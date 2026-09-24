using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Lifecycle;

/// <summary>A backend built from a FUNCTION — the general form of bridging something that already answers.
///
/// <para><b>It replaced a 473-line adapter for one ecosystem</b> that cost every consumer a 654 KB
/// dependency and was called by nothing (<c>docs/DECISIONS.md</c> D146, D147). What matters here is that a
/// bridge is a backend like any other: it routes, it falls over, and it declares only what it was given a
/// delegate for.</para></summary>
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

    [Fact]
    public async Task Declared_capabilities_are_the_callers_to_state_not_the_librarys_to_infer()
    {
        // A bridge over an embedding SDK is the same mechanism with a different declaration — nothing here
        // is text-specific except the default.
        using var sp = Build(b => b.AddBridgeProvider("scorer",
            (_, _) => Task.FromResult(new TextResponse("", ProviderVerdict.Unsupported)),
            capabilities: new ProviderCapabilities
            {
                Accepts = [ProviderKinds.Text],
                Produces = [ProviderKinds.Score],
                Operations = [ProviderOperation.Complete],
            }));

        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.Equal([ProviderKinds.Score], caps.Produces);
        Assert.False(caps.Supports(ProviderKinds.Text, ProviderOperation.Complete));
        await Task.CompletedTask;
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
        using var sp = Build(b => b.AddBridgeProvider("scorer",
            (_, _) => Task.FromResult(new TextResponse("", ProviderVerdict.Unsupported)),
            (_, _) => Chunks(),
            capabilities: new ProviderCapabilities { Produces = [ProviderKinds.Score] }));

        var caps = Assert.Single(sp.GetServices<IModelProvider>()).Capabilities;

        Assert.Equal([ProviderKinds.Score], caps.Produces);
        Assert.Equal([ProviderKinds.Text], caps.Accepts);
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
