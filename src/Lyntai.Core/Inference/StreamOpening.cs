using System.Runtime.CompilerServices;

namespace Lyntai.Inference;

/// <summary>Opens a backend's stream so that a throw from the CALL itself surfaces where a throw mid-iteration
/// does — at the first <c>MoveNextAsync</c> — and a router's guarded read classifies both alike. An <c>async</c>
/// iterator defers its throw that way on its own; a non-iterator implementation (a bridge forwarding to an SDK
/// that validates eagerly) throws before any enumerator exists, past every guard.</summary>
internal static class StreamOpening
{
    /// <summary>The stream <paramref name="open"/> returns, opened lazily on the first read.</summary>
    internal static async IAsyncEnumerable<T> Deferred<T>(
        Func<IAsyncEnumerable<T>> open, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in open().WithCancellation(ct).ConfigureAwait(false))
            yield return item;
    }
}
