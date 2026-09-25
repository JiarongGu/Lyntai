using System.Diagnostics;
using Lyntai.Diagnostics;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary>A backend whose <c>StreamAsync</c> THROWS from the call itself — a non-iterator implementation
/// forwarding to an SDK that validates eagerly — rather than from its first <c>MoveNextAsync</c>, where an
/// <c>async</c> iterator defers the throw. Both routers must classify it and fall over exactly as they do a
/// throw mid-iteration; the fakes elsewhere are all iterators, so none of them could see this.</summary>
public class EagerStreamThrowTests
{
    private static readonly TextRequest Ask = new() { Messages = [TextMessage.User("hi")] };

    [Fact]
    public async Task The_text_router_classifies_an_eager_throw_and_falls_over()
    {
        var eager = new EagerTextProvider("eager", new HttpRequestException("boom", null, System.Net.HttpStatusCode.TooManyRequests));
        var next = new FakeTextProvider("next");
        var tracker = new DeadHostTracker();

        var chunks = await new TextRouter([eager, next], tracker, new LyntaiOptions())
            .StreamAsync([new("eager"), new("next")], Ask).ToListAsync();

        Assert.Equal(["next stream"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.True(tracker.IsDead("eager")); // classified RateLimited → cooled, not a raw exception
    }

    [Fact]
    public async Task The_text_router_records_an_eager_throw_as_a_failed_attempt()
    {
        var eager = new EagerTextProvider("eager-span", new InvalidOperationException("sdk rejected the request"));
        var errors = new List<string?>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LyntaiDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem("gen_ai.system"), "eager-span")) errors.Add(a.GetTagItem("error.type") as string);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var chunks = await new TextRouter([eager], new DeadHostTracker(), new LyntaiOptions())
            .StreamAsync([new("eager-span")], Ask).ToListAsync();

        Assert.Equal(ProviderVerdict.Failed, Assert.Single(chunks).Verdict);
        Assert.Equal(["Failed"], errors); // the span saw the failure rather than reporting the attempt Ok
    }

    [Fact]
    public async Task The_media_router_classifies_an_eager_throw_and_falls_over()
    {
        var eager = new EagerMediaProvider { Id = "eager" };
        var tts = new FakeGenerationStreamProvider { Id = "tts" };
        var request = new MediaRequest { Kind = ProviderKinds.Audio, Prompt = "read this aloud" };

        var chunks = new List<MediaChunk>();
        await foreach (var chunk in new MediaRouter([eager, tts]).StreamAsync([new("eager"), new("tts")], request))
            chunks.Add(chunk);

        Assert.True(chunks[^1].Final);
        Assert.Equal([1, 2, 3, 4], chunks.Where(c => c.Data is not null).SelectMany(c => c.Data!).ToArray());
    }

    private sealed class EagerTextProvider(string id, Exception error) : IModelProvider
    {
        public string Id => id;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
        };

        // NOT an iterator: the throw happens when the router CALLS this, before any enumerator exists
        public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) => throw error;
    }

    private sealed class EagerMediaProvider : IModelProvider
    {
        public string Id { get; init; } = "eager";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Audio],
            Operations = [ProviderOperation.Stream],
        };

        public IAsyncEnumerable<MediaChunk> StreamAsync(MediaRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("sdk rejected the request");
    }
}
