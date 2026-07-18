using System.Runtime.CompilerServices;
using Lyntai.Llm;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted in-memory provider: queue replies for CompleteAsync, set a chunk script for
/// StreamAsync; records every request it saw.</summary>
public sealed class FakeLlmProvider(string id) : ILlmProvider
{
    public string Id { get; } = id;

    public bool IsAvailable { get; set; } = true;

    public bool SupportsToolCalls { get; set; }

    public Queue<LlmReply> Replies { get; } = new();

    public Func<LlmRequest, IReadOnlyList<LlmChunk>>? StreamScript { get; set; }

    /// <summary>When set, StreamAsync throws this BEFORE yielding (a provider-side stream failure).</summary>
    public Exception? StreamThrow { get; set; }

    public List<LlmRequest> Calls { get; } = [];

    public int StreamCalls { get; private set; }

    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        Calls.Add(req);
        return Task.FromResult(Replies.Count > 0
            ? Replies.Dequeue()
            : new LlmReply($"{Id} default reply", LlmVerdict.Ok));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamCalls++;
        Calls.Add(req);
        if (StreamThrow is not null) throw StreamThrow; // provider-side failure before any content
        var chunks = StreamScript?.Invoke(req) ?? [LlmChunk.Content($"{Id} stream"), LlmChunk.Final()];
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
