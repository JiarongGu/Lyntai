using System.Runtime.CompilerServices;
using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>
/// A tool-calling (ReAct-style) loop over the <see cref="ILlmClient"/> front door: it makes the
/// registered tools available to the model, executes the tool the model chooses, feeds the observation
/// back, and repeats until the model finishes or the iteration budget is hit. It prefers <b>native</b>
/// function-calling (when the provider supports it) and falls back to a <b>prompt protocol</b> over the
/// text contract for providers that don't — so it works with <em>any</em> provider (HTTP, MEAI bridge,
/// CLI, local) and stays behind the "Lyntai behaves like one provider" front door either way.
/// </summary>
public interface IToolLoop
{
    /// <summary>Run the loop for <paramref name="req"/> (its messages are the task; the tools come from
    /// the registry, not <see cref="LlmRequest.Tools"/>). <paramref name="maxIterations"/> overrides the
    /// configured default budget. The whole outcome (answer, verdict, steps, usage) folds into the returned
    /// <see cref="ToolLoopResult"/>; for LIVE progress use <see cref="StreamAsync"/>.</summary>
    Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default);

    /// <summary>Live-progress overload: run the loop and yield <see cref="AgentStreamEvent"/>s as they happen —
    /// a <see cref="ToolCall"/> then a <see cref="ToolResult"/> per tool round-trip (so an interactive UI can
    /// show tool chips as the loop runs), assistant <see cref="TextDelta"/>(s), a <see cref="UsageFinal"/> when
    /// any provider reported usage, and exactly one terminal <see cref="SessionEnded"/>. Mirrors
    /// <see cref="IAgentSession.StreamAsync"/> — except there is no <see cref="SessionStarted"/> (a Lyntai-driven
    /// loop has no external session id). Events arrive at TURN granularity: the native path needs the whole
    /// reply to read its structured tool calls, so assistant text is surfaced per turn rather than
    /// token-by-token. The result-door <see cref="RunAsync"/> folds the same run.
    /// <para>The default implementation runs <see cref="RunAsync"/> to completion and replays its outcome as
    /// events (functional, but NOT live) — so a BYO <see cref="IToolLoop"/> that only implements
    /// <see cref="RunAsync"/> still gets a working stream. The built-in <see cref="ToolLoop"/> overrides this
    /// with a genuinely live stream.</para></summary>
    async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        LlmRequest req, int? maxIterations = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await RunAsync(req, maxIterations, ct).ConfigureAwait(false);
        foreach (var s in result.Steps)
        {
            yield return new ToolCall(s.Tool, s.ArgumentsJson, null);
            yield return new ToolResult(null, s.Result, ToolObservations.IsError(s.Result));
        }
        // same event order as the live door: the answer text, then usage, then the terminal
        if (!string.IsNullOrEmpty(result.Answer))
            yield return new TextDelta(result.Answer);
        if (result.Usage is { } u)
            yield return new UsageFinal(u.InputTokens, u.OutputTokens, u.CacheReadTokens, 0, null);
        yield return new SessionEnded(result.Verdict, !result.Ok, null, null, result.Answer, result.Detail);
    }
}
