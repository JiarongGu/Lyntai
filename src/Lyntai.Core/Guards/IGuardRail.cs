using Lyntai.Diagnostics;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Guards;

/// <summary>Runs the registered <see cref="IGuard"/>s at a gate and returns the first non-Allow verdict
/// (a Block short-circuits; a Replace is applied and inspection continues). With no guards registered,
/// everything is allowed.</summary>
public interface IGuardRail
{
    /// <summary>Run the request guards (the input gate). Returns the effective outcome; on Replace, the
    /// returned <see cref="GuardOutcome.Replacement"/> is the rewritten last-user text.</summary>
    Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default);

    /// <summary>Run the response guards (the output gate).</summary>
    Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default);

    /// <summary>Gate a tool call the model wants to make (its name + JSON arguments) BEFORE it executes —
    /// inside the agent tool loop, not just at the chat boundary. Modelled as an outbound request carrying
    /// the tool-call turn, so existing request guards (which already scan a tool call's <c>ArgumentsJson</c>)
    /// inspect it with no new per-guard surface. Block to refuse the call; Replace to rewrite the args JSON.
    /// NOTE: this DEFAULT can't compose CHAINED Replace guards (there is no user message for the request
    /// gate's re-thread to rewrite, so each guard sees the original args) — the built-in <see cref="GuardRail"/>
    /// overrides it with an args-aware re-thread; a BYO rail with multiple rewriting guards should too.</summary>
    Task<GuardOutcome> InspectToolCallAsync(string toolName, string argumentsJson, CancellationToken ct = default) =>
        InspectRequestAsync(new LlmRequest { Messages = [LlmMessage.AssistantToolCalls([new LlmToolCall("", toolName, argumentsJson)])] }, ct);

    /// <summary>Gate a tool's observation BEFORE it is fed back to the model — so a denied term can't be
    /// exfiltrated through a tool result. Modelled as an inbound reply, so existing response guards inspect
    /// it. Block to withhold it; Replace to substitute redacted text.</summary>
    Task<GuardOutcome> InspectToolResultAsync(string toolName, string result, CancellationToken ct = default) =>
        InspectResponseAsync(new LlmReply(result, LlmVerdict.Ok), ct);
}

/// <inheritdoc/>
public sealed class GuardRail(IEnumerable<IGuard> guards, ILogger<GuardRail>? logger = null) : IGuardRail
{
    private readonly IReadOnlyList<IGuard> _guards = [.. guards];
    private readonly ILogger _logger = logger ?? NullLogger<GuardRail>.Instance;

    public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default) =>
        InspectCoreAsync(req, "input", "request",
            static (g, cur, ct) => g.InspectRequestAsync(cur, ct),
            static (cur, replacement) => RewriteLastUser(cur, replacement), ct);

    public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
        InspectCoreAsync(reply, "output", "response",
            static (g, cur, ct) => g.InspectResponseAsync(cur, ct),
            // re-thread the rewritten text AND clear ToolCalls/Detail — a Replace redacts the whole
            // reply, so later guards (and the applied result) must not see the un-redacted originals
            static (cur, replacement) => cur with { Text = replacement, ToolCalls = null, Detail = null }, ct);

    /// <summary>Tool-call gate with an ARGS-aware re-thread: a Replace rewrites the arguments JSON, and
    /// later guards must inspect the REWRITE (the interface default routes through the request gate,
    /// whose last-user re-thread is a silent no-op on a synthetic assistant-only probe — chained
    /// arg-rewriting guards wouldn't compose there).</summary>
    public Task<GuardOutcome> InspectToolCallAsync(string toolName, string argumentsJson, CancellationToken ct = default) =>
        InspectCoreAsync(ToolCallProbe(toolName, argumentsJson), "input", $"tool call '{toolName}'",
            static (g, cur, ct) => g.InspectRequestAsync(cur, ct),
            (_, replacement) => ToolCallProbe(toolName, replacement), ct);

    /// <summary>The one guard loop every gate shares: iterate the guards over the current (possibly
    /// re-threaded) subject; a Block short-circuits, a Replace is logged/counted, re-threaded via
    /// <paramref name="rethread"/> so later guards see it, and the LAST Replace outcome is returned.</summary>
    private async Task<GuardOutcome> InspectCoreAsync<T>(T current, string gate, string what,
        Func<IGuard, T, CancellationToken, Task<GuardOutcome>> inspect,
        Func<T, string, T> rethread, CancellationToken ct)
    {
        var effective = GuardOutcome.Allow;
        foreach (var guard in _guards)
        {
            var outcome = await inspect(guard, current, ct).ConfigureAwait(false);
            if (outcome.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("guard '{Guard}' blocked the {What}: {Reason}", guard.Name, what, outcome.Reason);
                LyntaiDiagnostics.RecordGuardDecision(gate, guard.Name, "block");
                return outcome; // a block is terminal
            }
            if (outcome.Result == GuardOutcome.Kind.Replace)
            {
                _logger.LogInformation("guard '{Guard}' rewrote the {What}", guard.Name, what);
                LyntaiDiagnostics.RecordGuardDecision(gate, guard.Name, "replace");
                effective = outcome;
                current = rethread(current, outcome.Replacement!); // re-thread so later guards see the rewrite
            }
        }
        return effective;
    }

    private static LlmRequest ToolCallProbe(string toolName, string argumentsJson) =>
        new() { Messages = [LlmMessage.AssistantToolCalls([new LlmToolCall("", toolName, argumentsJson)])] };

    /// <summary>Rewrite the LAST user message's content (request-gate Replace only rewrites the last user
    /// turn — a guard can't redact an earlier one through this contract).</summary>
    internal static LlmRequest RewriteLastUser(LlmRequest req, string replacement)
    {
        var msgs = req.Messages.ToList();
        for (var i = msgs.Count - 1; i >= 0; i--)
            if (msgs[i].Role == "user") { msgs[i] = msgs[i] with { Content = replacement }; break; }
        return req with { Messages = msgs };
    }
}
