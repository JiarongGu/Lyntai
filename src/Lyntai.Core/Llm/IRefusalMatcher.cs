namespace Lyntai.Llm;

/// <summary>A typed, app-registered check that decides whether an otherwise-<see cref="LlmVerdict.Ok"/>
/// reply is actually a refusal. Registered into a DI collection (<c>AddRefusalMatcher</c>); the
/// refusal-screening front door runs every matcher — after the central patterns and the per-request
/// <see cref="LlmRequest.RefusalPattern"/> — and surfaces the reply as <see cref="LlmVerdict.Refused"/>
/// (no fallback) if any returns true. This is the structured alternative to the stringly-typed
/// <see cref="LlmRequest.RefusalPattern"/> regex: a matcher can key off the request (consumer, model,
/// language) as well as the text, and encode logic a single regex can't. A matcher that throws is logged
/// and ignored (fail-open — the reply passes through). Completion-path only — streamed replies aren't
/// screened.</summary>
public interface IRefusalMatcher
{
    /// <summary>True if <paramref name="replyText"/> (the text of an Ok completion for
    /// <paramref name="request"/>) should be treated as a refusal.</summary>
    bool IsRefusal(LlmRequest request, string replyText);
}
