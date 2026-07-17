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

    public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default) =>
        RunAsync((g, c) => g.InspectRequestAsync(req, c), "request", ct);

    public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
        RunAsync((g, c) => g.InspectResponseAsync(reply, c), "response", ct);

    private async Task<GuardOutcome> RunAsync(Func<IGuard, CancellationToken, Task<GuardOutcome>> inspect, string gate, CancellationToken ct)
    {
        var effective = GuardOutcome.Allow;
        foreach (var guard in _guards)
        {
            var outcome = await inspect(guard, ct).ConfigureAwait(false);
            if (outcome.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("guard '{Guard}' blocked the {Gate}: {Reason}", guard.Name, gate, outcome.Reason);
                return outcome; // a block is terminal
            }
            if (outcome.Result == GuardOutcome.Kind.Replace)
            {
                _logger.LogInformation("guard '{Guard}' rewrote the {Gate}", guard.Name, gate);
                effective = outcome; // keep the latest replacement, let later guards inspect too
            }
        }
        return effective;
    }
}
