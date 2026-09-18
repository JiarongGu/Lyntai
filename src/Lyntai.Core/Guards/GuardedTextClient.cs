using Lyntai.Inference;
using System.Runtime.CompilerServices;

namespace Lyntai.Guards;

/// <summary>
/// An <see cref="ITextClient"/> that runs the <see cref="IGuardRail"/> around each completion: the input
/// gate before the model call, the output gate after. A blocked request never reaches the model (returns
/// a <see cref="ProviderVerdict.Refused"/>); a blocked reply is withheld; a Replace rewrites the text. Wrap
/// the front-door client with this for guarded completions everywhere, or let the chat orchestrator apply
/// the gates. Streaming applies only the INPUT gate — a stream can't be un-sent once tokens flow.
/// </summary>
public sealed class GuardedTextClient(ITextClient inner, IGuardRail rail) : DelegatingTextClient(inner)
{
    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        var pre = await rail.InspectRequestAsync(req, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
            return new TextResponse("", ProviderVerdict.Refused, Detail: $"blocked by guard: {pre.Reason}");
        var effective = pre.Result == GuardOutcome.Kind.Replace ? GuardRail.RewriteLastUser(req, pre.Replacement!) : req;

        var reply = await Inner.CompleteAsync(effective, ct).ConfigureAwait(false);

        // gate EVERY reply, not just Ok ones — an error reply's Detail (stderr/HTTP body) can echo content
        var post = await rail.InspectResponseAsync(reply, ct).ConfigureAwait(false);
        return post.Result switch
        {
            GuardOutcome.Kind.Block => new TextResponse("", ProviderVerdict.Refused, reply.Usage, $"blocked by guard: {post.Reason}"),
            GuardOutcome.Kind.Replace => GuardRail.Redact(reply, post.Replacement!), // whole-reply redaction (shared)
            _ => reply,
        };
    }

    public override async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pre = await rail.InspectRequestAsync(req, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
        {
            yield return TextChunk.Error(ProviderVerdict.Refused, $"blocked by guard: {pre.Reason}");
            yield break;
        }
        var effective = pre.Result == GuardOutcome.Kind.Replace ? GuardRail.RewriteLastUser(req, pre.Replacement!) : req;
        await foreach (var chunk in Inner.StreamAsync(effective, ct).ConfigureAwait(false))
            yield return chunk;
    }
}
