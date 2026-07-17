using System.Runtime.CompilerServices;
using Lyntai.Llm;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted <see cref="ILlmClient"/> for cortex tests that don't need the real router:
/// queue replies for CompleteAsync; records every request.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    public Queue<LlmReply> Replies { get; } = new();
    public List<LlmRequest> Calls { get; } = [];

    /// <summary>Backs the <see cref="ILlmClient.SupportsToolCalls"/> method (a settable flag for tests).</summary>
    public bool SupportsToolCallsResult { get; set; }

    public bool SupportsToolCalls(LlmRequest req) => SupportsToolCallsResult;

    public Func<LlmRequest, IReadOnlyList<LlmChunk>>? StreamScript { get; set; }

    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        Calls.Add(req);
        return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : new LlmReply("fake", LlmVerdict.Ok));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(req);
        var chunks = StreamScript?.Invoke(req) ?? [LlmChunk.Content("fake stream"), LlmChunk.Final()];
        foreach (var c in chunks) { await Task.Yield(); yield return c; }
    }
}
