using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>A text backend is asked only on a door its <see cref="ProviderCapabilities.Operations"/> declares:
/// a Complete-only candidate is skipped by <see cref="TextRouter.StreamAsync"/>, a Stream-only one by
/// <see cref="TextRouter.CompleteAsync"/> — so the next candidate serves, rather than the undeclared door's
/// <see cref="ProviderVerdict.Unsupported"/> surfacing with no fallback.</summary>
public class TextRouterOperationTests
{
    private static readonly TextRequest Ask = new() { Messages = [TextMessage.User("hi")] };

    private static TextRouter Router(params IModelProvider[] providers) =>
        new(providers, new DeadHostTracker(), new LyntaiOptions());

    private static FakeTextProvider Declaring(string id, params ProviderOperation[] operations) => new(id)
    {
        Capabilities = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = operations,
        },
    };

    [Fact]
    public async Task A_complete_only_candidate_is_never_asked_to_stream_and_the_next_one_streams()
    {
        var completeOnly = Declaring("complete-only", ProviderOperation.Complete);
        var streaming = new FakeTextProvider("streaming");

        var chunks = await Router(completeOnly, streaming)
            .StreamAsync([new("complete-only"), new("streaming")], Ask).ToListAsync();

        Assert.Equal(0, completeOnly.StreamCalls);
        Assert.Equal(["streaming stream"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task A_stream_only_candidate_is_never_asked_to_complete_and_the_next_one_answers()
    {
        var streamOnly = Declaring("stream-only", ProviderOperation.Stream);
        var complete = new FakeTextProvider("complete");

        var reply = await Router(streamOnly, complete).CompleteAsync([new("stream-only"), new("complete")], Ask);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("complete default reply", reply.Text);
        Assert.Empty(streamOnly.Calls);
    }

    [Fact]
    public async Task A_list_no_candidate_of_which_declares_the_door_is_Unsupported_and_names_each()
    {
        var completeOnly = Declaring("complete-only", ProviderOperation.Complete);

        var chunks = await Router(completeOnly).StreamAsync([new("complete-only")], Ask).ToListAsync();

        var error = Assert.Single(chunks);
        Assert.Equal(TextChunkKind.Error, error.Kind);
        Assert.Equal(ProviderVerdict.Unsupported, error.Verdict);
        Assert.Contains("complete-only: does not declare Stream", error.Detail, StringComparison.Ordinal);
        Assert.Equal(0, completeOnly.StreamCalls);
    }

    [Fact]
    public async Task The_capability_probe_answers_for_the_complete_door()
    {
        var streamOnly = Declaring("stream-only", ProviderOperation.Stream);
        var complete = new FakeTextProvider("complete");

        var caps = await Router(streamOnly, complete).GetCapabilitiesAsync([new("stream-only"), new("complete")], Ask);

        Assert.Same(complete.Capabilities, caps);
    }

    [Fact]
    public async Task A_bridge_given_no_stream_delegate_is_skipped_on_the_stream_door_through_the_container()
    {
        var bridgeCalls = 0;
        var streaming = new FakeTextProvider("streaming");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddBridgeProvider("bridge", (_, _) =>
            {
                bridgeCalls++;
                return Task.FromResult(new TextResponse("bridged", ProviderVerdict.Ok));
            })
            .AddProvider(_ => streaming)
            .UseDefaultCandidates("bridge", "streaming"));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();

        var chunks = await client.StreamAsync(Ask).ToListAsync();
        var reply = await client.CompleteAsync(Ask);

        Assert.Equal(["streaming stream"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal("bridged", reply.Text); // …while the door it DOES declare still reaches it first
        Assert.Equal(1, bridgeCalls);
    }
}
