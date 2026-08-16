namespace Lyntai.Llm;

/// <summary>
/// Base class for front-door decorators (cache / budget / rate-limit / guards / refusal screening):
/// pass-through behavior for every <see cref="ILlmClient"/> member, so a decorator overrides ONLY the
/// members it changes (the <c>DelegatingChatClient</c> pattern). This is what keeps five decorators from
/// each hand-copying the pass-throughs — and from silently dropping one when the interface grows.
/// A BYO decorator registered via <c>AddFrontDoorDecorator</c> can derive from it the same way.
/// </summary>
public abstract class DelegatingLlmClient(ILlmClient inner) : ILlmClient
{
    /// <summary>The wrapped client the pass-throughs delegate to.</summary>
    protected ILlmClient Inner { get; } = inner;

    public virtual Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        Inner.CompleteAsync(req, ct);

    public virtual IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        Inner.StreamAsync(req, ct);

    public virtual bool SupportsToolCalls(LlmRequest req) => Inner.SupportsToolCalls(req);

    public virtual bool SupportsStreamingToolCalls(LlmRequest req) => Inner.SupportsStreamingToolCalls(req);
}
