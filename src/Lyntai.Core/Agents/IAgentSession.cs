using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>A self-driving agent loop: the agent runs its OWN tool loop (out-of-process) and we observe
/// the stream, gate it (read-only vs write), and resume it across a human gate. Distinct from
/// <see cref="IToolLoop"/> (where Lyntai drives the loop). Adapters implement ONLY <see cref="StreamAsync"/>;
/// the result-door <c>RunAsync</c> extension folds the stream for callers that just want the outcome.
///
/// <para><b>A session is NOT bound by the front door.</b> <c>AddUsageBudget</c> and <c>AddRateLimit</c>
/// decorate <see cref="Lyntai.Llm.ILlmClient"/>, and a session never goes through one — it spawns a CLI that
/// runs its own loop — so a configured cap does not cap it and a configured limiter does not throttle it.
/// Stated here because nothing else says so, and "the app has one budget" is the reasonable thing to assume.
/// <list type="bullet">
/// <item><description><b>Usage</b> is reported, not priced: <see cref="UsageFinal"/> hands the caller raw
/// counts deliberately, so an app that needs one wallet records them against its own ledger.</description>
/// </item>
/// <item><description><b>Tools</b> ARE gated. Custom tools reach the agent over MCP, and that door applies
/// <see cref="Lyntai.Guards.IGuardRail"/> to every call and observation — the second-door case
/// <c>.claude/knowledge/pitfalls.md</c> records as closed.</description></item>
/// <item><description><b>Guards on the agent's own prose</b> are not applied: a guard is typed on
/// <see cref="LlmRequest"/>/<see cref="LlmReply"/> and a session emits
/// <see cref="AgentStreamEvent"/>s. Gate the outcome yourself if that matters.</description></item>
/// <item><description><b>Caching</b> does not apply and should not: replaying a turn that edited a
/// filesystem would be wrong.</description></item>
/// </list></para></summary>
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

        // Fall back to the streamed assistant text when a SUCCESSFUL terminal carried no final text — an
        // adapter whose terminal result came back empty (truncation / older CLI / provider variant) still
        // yields the answer to callers that treat empty FinalText as failure. But a terminal that IS an
        // error (Timeout/Failed) must NOT be dressed up as a partial success from its truncated pre-error
        // deltas — those same callers would then consume garbage instead of retrying (Verdict/IsError still
        // report the truth).
        var finalText = ended.FinalText;
        if (!ended.IsError && string.IsNullOrWhiteSpace(finalText) && text is { Length: > 0 })
            finalText = text.ToString();

        return new AgentSessionResult(sessionId ?? ended.SessionId, finalText ?? "", ended.Verdict,
            ended.IsError, ended.Subtype, ended.Diagnostic, usage);
    }
}
