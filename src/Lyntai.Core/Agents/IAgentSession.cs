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
        System.Text.StringBuilder? text = null;

        await foreach (var e in session.StreamAsync(options, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            onEvent?.Invoke(e);
            switch (e)
            {
                case SessionStarted s: sessionId ??= s.SessionId; break;
                case TextDelta t: (text ??= new System.Text.StringBuilder()).Append(t.Text); break;
                case UsageFinal u: usage = u; break;              // last one wins
                case SessionEnded x: ended = x; sessionId ??= x.SessionId; break;
            }
        }

        if (ended is null)
            return new AgentSessionResult(sessionId, "", LlmVerdict.Failed, IsError: true, Subtype: null,
                Diagnostic: "stream ended without a terminal SessionEnded event", usage);

        // Fall back to the streamed assistant text when the terminal event carried no final text — an
        // adapter whose terminal result came back empty (truncation / older CLI / provider variant) still
        // yields the answer to callers that treat empty FinalText as failure.
        var finalText = ended.FinalText;
        if (string.IsNullOrWhiteSpace(finalText) && text is { Length: > 0 })
            finalText = text.ToString();

        return new AgentSessionResult(sessionId ?? ended.SessionId, finalText ?? "", ended.Verdict,
            ended.IsError, ended.Subtype, ended.Diagnostic, usage);
    }
}
