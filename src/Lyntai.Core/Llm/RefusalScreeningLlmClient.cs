using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm;

/// <summary>
/// Front-door decorator that applies a request's optional <see cref="LlmRequest.RefusalPattern"/> to the
/// reply text: an otherwise-<c>Ok</c> completion whose text matches is surfaced as
/// <see cref="LlmVerdict.Refused"/>. This is the caller-supplied refusal check (e.g. Sonora's per-language
/// "I can't help" phrasing) layered on the central patterns. It sits OUTERMOST (above the response cache),
/// so even a cached hit is re-screened against the current request's pattern. A malformed pattern is
/// logged once and ignored (fail-open — the reply passes through unchanged). Streaming is passed through
/// unscreened (the reply text isn't assembled here, and streaming never falls back after the first token).
/// </summary>
public sealed class RefusalScreeningLlmClient(ILlmClient inner, ILogger<RefusalScreeningLlmClient>? logger = null) : ILlmClient
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private readonly ILogger _logger = logger ?? NullLogger<RefusalScreeningLlmClient>.Instance;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var reply = await inner.CompleteAsync(req, ct).ConfigureAwait(false);
        if (reply.Verdict != LlmVerdict.Ok || string.IsNullOrEmpty(req.RefusalPattern) || string.IsNullOrEmpty(reply.Text))
            return reply;

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
        return reply;
    }

    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        inner.StreamAsync(req, ct);

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);
}
