using Lyntai.Inference;
using System.Runtime.CompilerServices;
using Lyntai.Llm;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted in-memory provider: queue replies for CompleteAsync, set a chunk script for
/// StreamAsync; records every request it saw.</summary>
public sealed class FakeLlmProvider(string id) : IModelProvider
{
    public string Id { get; } = id;

    public ProviderCapabilities Capabilities { get; set; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Text],
        Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
    };

    public bool IsAvailable { get; set; } = true;

    /// <summary>Convenience setter that WRITES THROUGH to <see cref="Capabilities"/>, which is where the
    /// router reads it since D127. A plain auto-property here would let a test set the flag, watch the
    /// router ignore it, and pass for the wrong reason — which is exactly what happened when the flag
    /// moved.</summary>
    public bool SupportsToolCalls
    {
        get => Capabilities.SupportsToolCalls;
        set => Capabilities = Capabilities with { SupportsToolCalls = value };
    }

    public Queue<TextResponse> Replies { get; } = new();

    public Func<TextRequest, IReadOnlyList<TextChunk>>? StreamScript { get; set; }

    /// <summary>When set, StreamAsync throws this BEFORE yielding (a provider-side stream failure).</summary>
    public Exception? StreamThrow { get; set; }

    /// <summary>When set, CompleteAsync throws this (a provider that throws instead of returning a verdict reply).</summary>
    public Exception? CompleteThrow { get; set; }

    public List<TextRequest> Calls { get; } = [];

    public int StreamCalls { get; private set; }

    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        Calls.Add(req);
        if (CompleteThrow is not null) throw CompleteThrow;
        return Task.FromResult(Replies.Count > 0
            ? Replies.Dequeue()
            : new TextResponse($"{Id} default reply", ProviderVerdict.Ok));
    }

    public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamCalls++;
        Calls.Add(req);
        if (StreamThrow is not null) throw StreamThrow; // provider-side failure before any content
        var chunks = StreamScript?.Invoke(req) ?? [TextChunk.Content($"{Id} stream"), TextChunk.Final()];
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested(); // like a real provider: a cancelled caller stops the stream
            yield return chunk;
        }
    }
}
