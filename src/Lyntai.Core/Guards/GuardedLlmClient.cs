using System.Runtime.CompilerServices;
using Lyntai.Llm;

namespace Lyntai.Guards;

/// <summary>
/// An <see cref="ILlmClient"/> that runs the <see cref="IGuardRail"/> around each completion: the input
/// gate before the model call, the output gate after. A blocked request never reaches the model (returns
/// a <see cref="LlmVerdict.Refused"/>); a blocked reply is withheld; a Replace rewrites the text. Wrap
/// the front-door client with this for guarded completions everywhere, or let the chat orchestrator apply
/// the gates. Streaming applies only the INPUT gate — a stream can't be un-sent once tokens flow.
/// </summary>
public sealed class GuardedLlmClient(ILlmClient inner, IGuardRail rail) : ILlmClient
{
    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var pre = await rail.InspectRequestAsync(req, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
            return new LlmReply("", LlmVerdict.Refused, Detail: $"blocked by guard: {pre.Reason}");
        var effective = pre.Result == GuardOutcome.Kind.Replace ? GuardRail.RewriteLastUser(req, pre.Replacement!) : req;

        var reply = await inner.CompleteAsync(effective, ct).ConfigureAwait(false);

        // gate EVERY reply, not just Ok ones — an error reply's Detail (stderr/HTTP body) can echo content
        var post = await rail.InspectResponseAsync(reply, ct).ConfigureAwait(false);
        return post.Result switch
        {
            GuardOutcome.Kind.Block => new LlmReply("", LlmVerdict.Refused, reply.Usage, $"blocked by guard: {post.Reason}"),
            // a Replace redacts the WHOLE reply — clear ToolCalls + Detail too, or denied content the output
            // gate also scans (a tool call's args, an error detail) would pass through un-redacted
            GuardOutcome.Kind.Replace => reply with { Text = post.Replacement!, ToolCalls = null, Detail = null },
            _ => reply,
        };
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pre = await rail.InspectRequestAsync(req, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
        {
            yield return LlmChunk.Error(LlmVerdict.Refused, $"blocked by guard: {pre.Reason}");
            yield break;
        }
        var effective = pre.Result == GuardOutcome.Kind.Replace ? GuardRail.RewriteLastUser(req, pre.Replacement!) : req;
        await foreach (var chunk in inner.StreamAsync(effective, ct).ConfigureAwait(false))
            yield return chunk;
    }

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);
}
