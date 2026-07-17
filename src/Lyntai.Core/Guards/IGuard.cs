using Lyntai.Llm;

namespace Lyntai.Guards;

/// <summary>
/// A scope-guard / jail hook — inspects an outbound request and/or an inbound reply and can allow, block,
/// or replace it. Registered into a DI collection via <c>builder.AddGuard&lt;T&gt;()</c> and applied at the
/// two gates of the chat orchestration (or directly by a <see cref="GuardedLlmClient"/>). A guard that only
/// cares about one direction overrides just that method; the other defaults to allow.
/// </summary>
public interface IGuard
{
    /// <summary>Stable name (for logging/telemetry which guard fired).</summary>
    string Name { get; }

    /// <summary>Inspect an outbound request before it reaches the model. Block to refuse it; Replace to
    /// rewrite the last user message (e.g. redact).</summary>
    Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default) => Task.FromResult(GuardOutcome.Allow);

    /// <summary>Inspect an inbound reply before it reaches the caller. Block to withhold it; Replace to
    /// substitute safe/redacted text.</summary>
    Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) => Task.FromResult(GuardOutcome.Allow);
}
