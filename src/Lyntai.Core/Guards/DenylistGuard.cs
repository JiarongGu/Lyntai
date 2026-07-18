using Lyntai.Llm;

namespace Lyntai.Guards;

/// <summary>A simple jail: blocks any request or reply that contains a denied term (case-insensitive
/// substring). For requests it scans the user messages. Construct with your terms and register via
/// <c>AddGuard(_ =&gt; new DenylistGuard(["…"]))</c>.</summary>
public sealed class DenylistGuard(IReadOnlyList<string> terms, string? name = null) : IGuard
{
    private readonly IReadOnlyList<string> _terms = [.. terms.Where(t => !string.IsNullOrWhiteSpace(t))];

    public string Name => name ?? "denylist";

    public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default) =>
        // scan EVERY scannable string across EVERY message role, not just "user" content — a denied term can
        // hide in an assistant tool-call turn (Content="" with the payload on ToolCalls) or an image
        // attachment URI, not only in text. Scans each segment directly (no whole-transcript join) and
        // short-circuits on the first hit.
        Task.FromResult(Check(req.Messages.SelectMany(Segments)));

    public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
        // also scan the error detail (may echo content) AND the reply's own tool calls
        Task.FromResult(Check([reply.Text, reply.Detail ?? "", .. ToolCallSegments(reply.ToolCalls)]));

    // every scannable string in a message: content, each tool call's name + JSON arguments, each
    // attachment's URI
    private static IEnumerable<string> Segments(LlmMessage m)
    {
        yield return m.Content;
        foreach (var s in ToolCallSegments(m.ToolCalls)) yield return s;
        foreach (var a in m.Attachments ?? []) yield return a.Uri ?? "";
    }

    private static IEnumerable<string> ToolCallSegments(IReadOnlyList<LlmToolCall>? calls) =>
        calls is null ? [] : calls.Select(c => c.Name + " " + c.ArgumentsJson);

    private GuardOutcome Check(IEnumerable<string> segments)
    {
        foreach (var segment in segments)
            foreach (var term in _terms)
                if (segment.Contains(term, StringComparison.OrdinalIgnoreCase))
                    return GuardOutcome.Block($"denied term: {term}");
        return GuardOutcome.Allow;
    }
}
