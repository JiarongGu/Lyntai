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
}

/// <inheritdoc/>
public sealed class GuardRail(IEnumerable<IGuard> guards, ILogger<GuardRail>? logger = null) : IGuardRail
{
    private readonly IReadOnlyList<IGuard> _guards = [.. guards];
    private readonly ILogger _logger = logger ?? NullLogger<GuardRail>.Instance;

    public async Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default)
    {
        var current = req;
        var effective = GuardOutcome.Allow;
        foreach (var guard in _guards)
        {
            var outcome = await guard.InspectRequestAsync(current, ct).ConfigureAwait(false);
            if (outcome.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("guard '{Guard}' blocked the request: {Reason}", guard.Name, outcome.Reason);
                return outcome; // a block is terminal
            }
            if (outcome.Result == GuardOutcome.Kind.Replace)
            {
                _logger.LogInformation("guard '{Guard}' rewrote the request", guard.Name);
                effective = outcome;
                current = RewriteLastUser(current, outcome.Replacement!); // re-thread so later guards see the rewrite
            }
        }
        return effective;
    }

    public async Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default)
    {
        var current = reply;
        var effective = GuardOutcome.Allow;
        foreach (var guard in _guards)
        {
            var outcome = await guard.InspectResponseAsync(current, ct).ConfigureAwait(false);
            if (outcome.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("guard '{Guard}' blocked the response: {Reason}", guard.Name, outcome.Reason);
                return outcome;
            }
            if (outcome.Result == GuardOutcome.Kind.Replace)
            {
                _logger.LogInformation("guard '{Guard}' rewrote the response", guard.Name);
                effective = outcome;
                current = current with { Text = outcome.Replacement! }; // re-thread the rewritten text
            }
        }
        return effective;
    }

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
