using Lyntai.Llm;

namespace Lyntai.Guards;

/// <summary>A simple jail: blocks any request or reply that contains a denied term (case-insensitive
/// substring). For requests it scans the user messages. Construct with your terms and register via
/// <c>AddGuard(_ =&gt; new DenylistGuard(["…"]))</c>.</summary>
public sealed class DenylistGuard(IReadOnlyList<string> terms, string? name = null) : IGuard
{
    private readonly IReadOnlyList<string> _terms = [.. terms.Where(t => !string.IsNullOrWhiteSpace(t))];

    public string Name => name ?? "denylist";

    public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default)
    {
        // scan EVERY message role, not just "user" — a denied term hiding in a system/assistant/tool
        // message (e.g. a tool result fed back mid-loop) must not slip past the jail
        var text = string.Join("\n", req.Messages.Select(m => m.Content));
        return Task.FromResult(Check(text));
    }

    public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
        Task.FromResult(Check(reply.Text + "\n" + (reply.Detail ?? ""))); // also scan error detail (may echo content)

    private GuardOutcome Check(string text)
    {
        var hit = _terms.FirstOrDefault(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        return hit is null ? GuardOutcome.Allow : GuardOutcome.Block($"denied term: {hit}");
    }
}
