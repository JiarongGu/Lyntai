using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;

namespace Lyntai.Inference;

/// <summary>The front door's OUTERMOST layer: one span per call, tagged with the consumer, under which every
/// provider attempt nests (D207). Outside refusal screening, so the span carries the verdict the caller got; a
/// stream's span ends when the stream does — finished, abandoned or faulted.</summary>
internal sealed class CallSpanTextClient(ITextClient inner) : DelegatingTextClient(inner)
{
    private const string Operation = "chat";

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        using var span = LyntaiDiagnostics.StartCallSpan(Operation, req.Consumer, "complete");
        try
        {
            var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
            LyntaiDiagnostics.EndCallSpan(span, reply.Verdict, reply.Detail);
            return reply;
        }
        catch (Exception ex)
        {
            LyntaiDiagnostics.FaultCallSpan(span, ex);
            throw;
        }
    }

    // the inner stream is CALLED here, not at the first read: a layer beneath that reads an ambient scope when
    // its StreamAsync is called (call tracing) would otherwise read whatever is ambient at the first read
    public override IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        SpannedAsync(req, Inner.StreamAsync(req, ct), ct);

    private static async IAsyncEnumerable<TextChunk> SpannedAsync(TextRequest req, IAsyncEnumerable<TextChunk> stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var span = LyntaiDiagnostics.StartCallSpan(Operation, req.Consumer, "stream");
        var enumerator = stream.GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool more;
                try
                {
                    // a later read resumes under the CALLER's context, so an attempt it opens would not nest
                    if (span is not null) Activity.Current = span;
                    more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LyntaiDiagnostics.FaultCallSpan(span, ex);
                    throw;
                }
                if (!more) yield break;

                var chunk = enumerator.Current;
                if (chunk.Kind == TextChunkKind.Error) LyntaiDiagnostics.EndCallSpan(span, chunk.Verdict, chunk.Detail);
                yield return chunk;
            }
        }
    }
}
