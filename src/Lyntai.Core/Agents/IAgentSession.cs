using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>A self-driving agent loop: the agent runs its OWN tool loop (out-of-process) and we observe
/// the stream, gate it (read-only vs write), and resume it across a human gate. Distinct from
/// <see cref="IToolLoop"/> (where Lyntai drives the loop). Adapters implement ONLY <see cref="StreamAsync"/>;
/// the result-door <c>RunAsync</c> extension folds the stream for callers that just want the outcome.</summary>
public interface IAgentSession
{
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentSessionOptions options, CancellationToken ct = default);
}

public static class AgentSessionExtensions
{
    /// <summary>Result door: run the session to completion, folding the event stream into an
    /// <see cref="AgentSessionResult"/>. <paramref name="onEvent"/> (optional) fires once per streamed
    /// event, in order, before the fold — for live logging/tracing.</summary>
    public static async Task<AgentSessionResult> RunAsync(
        this IAgentSession session, AgentSessionOptions options,
        Action<AgentStreamEvent>? onEvent = null, CancellationToken ct = default)
    {
        string? sessionId = null;
        UsageFinal? usage = null;
        SessionEnded? ended = null;

        await foreach (var e in session.StreamAsync(options, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            onEvent?.Invoke(e);
            switch (e)
            {
                case SessionStarted s: sessionId ??= s.SessionId; break;
                case UsageFinal u: usage = u; break;              // last one wins
                case SessionEnded x: ended = x; sessionId ??= x.SessionId; break;
            }
        }

        return ended is not null
            ? new AgentSessionResult(sessionId ?? ended.SessionId, ended.FinalText ?? "", ended.Verdict,
                ended.IsError, ended.Subtype, ended.Diagnostic, usage)
            : new AgentSessionResult(sessionId, "", LlmVerdict.Failed, IsError: true, Subtype: null,
                Diagnostic: "stream ended without a terminal SessionEnded event", usage);
    }
}
