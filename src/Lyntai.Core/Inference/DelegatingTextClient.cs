namespace Lyntai.Inference;

/// <summary>
/// Base class for front-door decorators (cache / budget / rate-limit / guards / refusal screening):
/// pass-through behavior for every <see cref="ITextClient"/> member, so a decorator overrides ONLY the
/// members it changes (the <c>DelegatingChatClient</c> pattern). This is what keeps five decorators from
/// each hand-copying the pass-throughs — and from silently dropping one when the interface grows.
/// A BYO decorator registered via <c>AddFrontDoorDecorator</c> can derive from it the same way.
/// </summary>
public abstract class DelegatingTextClient(ITextClient inner) : ITextClient
{
    /// <summary>The wrapped client the pass-throughs delegate to.</summary>
    protected ITextClient Inner { get; } = inner;

    public virtual Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        Inner.CompleteAsync(req, ct);

    public virtual IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        Inner.StreamAsync(req, ct);

    public virtual bool SupportsToolCalls(TextRequest req) => Inner.SupportsToolCalls(req);

    public virtual bool SupportsStreamingToolCalls(TextRequest req) => Inner.SupportsStreamingToolCalls(req);
}
