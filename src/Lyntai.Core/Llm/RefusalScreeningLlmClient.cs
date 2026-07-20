using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm;

/// <summary>
/// Front-door decorator that screens an otherwise-<c>Ok</c> completion for a refusal — surfacing it as
/// <see cref="LlmVerdict.Refused"/> — via two layers: the request's optional
/// <see cref="LlmRequest.RefusalPattern"/> regex, then every registered <see cref="IRefusalMatcher"/> (the
/// typed seam an app registers with <c>AddRefusalMatcher</c>). These are the caller-supplied refusal checks
/// (e.g. Sonora's per-language "I can't help" phrasing) layered on the central patterns. It sits OUTERMOST
/// (above the response cache), so even a cached hit is re-screened. A malformed pattern or a matcher that
/// throws is logged and ignored (fail-open — the reply passes through unchanged). Streaming is passed
/// through unscreened (the reply text isn't assembled here, and streaming never falls back after the first
/// token).
/// </summary>
public sealed class RefusalScreeningLlmClient(
    ILlmClient inner,
    IEnumerable<IRefusalMatcher>? matchers = null,
    ILogger<RefusalScreeningLlmClient>? logger = null) : ILlmClient
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private readonly IReadOnlyList<IRefusalMatcher> _matchers = [.. matchers ?? []];
    private readonly ILogger _logger = logger ?? NullLogger<RefusalScreeningLlmClient>.Instance;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var reply = await inner.CompleteAsync(req, ct).ConfigureAwait(false);
        if (reply.Verdict != LlmVerdict.Ok || string.IsNullOrEmpty(reply.Text))
            return reply;

        if (!string.IsNullOrEmpty(req.RefusalPattern))
        {
            try
            {
                if (Regex.IsMatch(reply.Text, req.RefusalPattern, RegexOptions.IgnoreCase, MatchTimeout))
                    return reply with { Verdict = LlmVerdict.Refused, Detail = "matched the request's refusal pattern" };
            }
            catch (RegexParseException ex)
            {
                _logger.LogWarning(ex, "ignoring a malformed per-request refusal pattern");
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("per-request refusal pattern timed out on the reply text — not screened");
            }
        }

        foreach (var matcher in _matchers)
        {
            try
            {
                if (matcher.IsRefusal(req, reply.Text))
                    return reply with { Verdict = LlmVerdict.Refused, Detail = $"flagged by {matcher.GetType().Name}" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ignoring a throwing IRefusalMatcher ({Matcher}) — reply passes through", matcher.GetType().Name);
            }
        }
        return reply;
    }

    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        inner.StreamAsync(req, ct);

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);
}
