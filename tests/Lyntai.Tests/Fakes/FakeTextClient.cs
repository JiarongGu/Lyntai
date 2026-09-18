using Lyntai.Inference;
using System.Runtime.CompilerServices;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted <see cref="ITextClient"/> for cortex tests that don't need the real router:
/// queue replies for CompleteAsync; records every request.</summary>
public sealed class FakeTextClient : ITextClient
{
    public Queue<TextResponse> Replies { get; } = new();
    public List<TextRequest> Calls { get; } = [];

    /// <summary>Backs the <see cref="ITextClient.Capabilities.SupportsToolCalls"/> method (a settable flag for tests).</summary>
    public bool SupportsToolCallsResult { get; set; }

    public bool SupportsToolCalls(TextRequest req) => SupportsToolCallsResult;

    /// <summary>Backs <see cref="ITextClient.Capabilities.SupportsStreamingToolCalls"/>. SEPARATE from
    /// <see cref="SupportsToolCallsResult"/> on purpose — the two are independent in the contract, and a
    /// fake that conflated them could not express the case that matters most: a provider doing native
    /// tool-calling whose STREAM drops the calls.</summary>
    public bool SupportsStreamingToolCallsResult { get; set; }

    public bool SupportsStreamingToolCalls(TextRequest req) => SupportsStreamingToolCallsResult;

    public Func<TextRequest, IReadOnlyList<TextChunk>>? StreamScript { get; set; }

    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        Calls.Add(req);
        return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : new TextResponse("fake", ProviderVerdict.Ok));
    }

    public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(req);
        var chunks = StreamScript?.Invoke(req) ?? [TextChunk.Content("fake stream"), TextChunk.Final()];
        foreach (var c in chunks) { await Task.Yield(); yield return c; }
    }
}
