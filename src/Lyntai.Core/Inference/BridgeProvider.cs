using System.Runtime.CompilerServices;
using Lyntai.Inference.Streaming;

namespace Lyntai.Inference;

/// <summary>A backend built from a FUNCTION rather than a class — the general shape of bridging something
/// that can already answer into this library: "here is a function that answers; route it like any other
/// backend". It needs no third-party type, so it belongs in Core, where a dependency-bearing adapter never could
/// (<c>docs/DECISIONS.md</c> D146, D147).
///
/// <para><b>Bridging any client is a lambda.</b> Whatever the SDK — an <c>IChatClient</c>, a vendor's own
/// client, an in-house HTTP service — the consumer owns the mapping they actually need and the library owns
/// routing, fallback, cooldown, admission, the provider timeout and the ops layer around it.</para>
///
/// <para><b>Nothing is inferred.</b> It declares exactly the operations it was given a function for, so a
/// bridge with no stream delegate reports no <see cref="ProviderOperation.Stream"/> and a router never asks it
/// to stream — the same rule every other backend follows. A caller wanting tool calls declares them the same
/// way; embeddings and scores are not a bridge's, since their routers select an <see cref="IVectorProvider"/>
/// or <see cref="IScoreProvider"/>.</para>
///
/// <para><b>The timeout is the bridge's, as on every backend</b> — the router enforces none, so without it a
/// hung vendor holds the call until the caller gives up. A completion is bounded as a whole and a stream by
/// INACTIVITY (<see cref="GuardedStream"/>); a delegate that ignores its token is abandoned, not waited for.</para></summary>
internal sealed class BridgeProvider(
    string id,
    ProviderCapabilities capabilities,
    Func<TextRequest, CancellationToken, Task<TextResponse>> complete,
    Func<TextRequest, CancellationToken, IAsyncEnumerable<TextChunk>>? stream,
    LyntaiOptions options,
    TimeProvider? time = null) : IModelProvider
{
    public string Id => id;

    public ProviderCapabilities Capabilities => capabilities;

    public async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        var timeout = options.ResolveTimeout(req);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await complete(req, cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new TextResponse("", ProviderVerdict.Timeout, Detail: $"{id}: no response within {timeout}");
        }
    }

    /// <summary>With no stream delegate, answers <see cref="ProviderVerdict.Unsupported"/> — the backstop for a
    /// direct caller; a router never reaches it, since the declared capabilities carry no Stream.</summary>
    public async IAsyncEnumerable<TextChunk> StreamAsync(
        TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (stream is null)
        {
            yield return TextChunk.Error(ProviderVerdict.Unsupported,
                $"{id} does not serve StreamAsync — no stream delegate was supplied to AddBridgeProvider.");
            yield break;
        }

        var timeout = options.ResolveTimeout(req);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clock = new InactivityClock(cts, timeout, time);
        var source = stream(req, cts.Token).GetAsyncEnumerator(cts.Token);
        Task<bool>? pending = null;
        try
        {
            // a fault the delegate raised itself is left to the router, which classifies it; only OUR clock
            // firing is turned into a terminal here
            var guarded = GuardedStream.ReadAll<TextChunk, TextChunk>(
                async () =>
                {
                    pending = source.MoveNextAsync().AsTask();
                    var more = await pending.WaitAsync(cts.Token).ConfigureAwait(false);
                    pending = null;
                    return more ? source.Current : null;
                },
                _ => cts.IsCancellationRequested
                    ? TextChunk.Error(ProviderVerdict.Timeout, $"{id}: no chunk within {timeout}")
                    : null,
                ct, clock);
            await foreach (var (chunk, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    yield return terminal;
                    yield break;
                }
                yield return chunk!;
            }
        }
        finally
        {
            await Release(source, pending, cts).ConfigureAwait(false);
        }
    }

    /// <summary>Dispose the delegate's enumerator — but never while a move is still pending: an async iterator
    /// refuses that with <see cref="NotSupportedException"/>, and a delegate that ignored its token may not
    /// finish for a long while. The pending move is cancelled and the dispose follows it, off the caller's path.</summary>
    private static async Task Release(IAsyncEnumerator<TextChunk> source, Task<bool>? pending, CancellationTokenSource cts)
    {
        if (pending is null)
        {
            await source.DisposeAsync().ConfigureAwait(false);
            return;
        }
        await cts.CancelAsync().ConfigureAwait(false);
        _ = Task.Run(async () =>
        {
            try { await pending.ConfigureAwait(false); }
            catch { /* the abandoned move's own fault: the caller has already been answered */ }
            try { await source.DisposeAsync().ConfigureAwait(false); }
            catch { /* nobody is left to tell */ }
        });
    }
}
