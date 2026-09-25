using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>How an HTTP-family provider answers a call of a kind its registration does not produce: an
/// <see cref="ProviderVerdict.Unsupported"/> verdict — the seam's own default — naming what to register
/// instead, never a throw. A router selects on capabilities and never sends one; this is for a direct
/// caller.</summary>
internal static class WrongKindCall
{
    /// <summary>Why <paramref name="id"/>, which produces <paramref name="produces"/>, cannot serve a
    /// <paramref name="kind"/> call.</summary>
    public static string Detail(string id, string produces, string kind)
    {
        var (backend, constant) = kind switch
        {
            ProviderKinds.Text => ("a chat model", nameof(ProviderKinds.Text)),
            ProviderKinds.Vector => ("an embedding model", nameof(ProviderKinds.Vector)),
            _ => ("a reranker", nameof(ProviderKinds.Score)),
        };
        return $"{id} produces {produces}, not {kind} — {backend} is its own backend, registered with "
            + $"Produces = ProviderKinds.{constant}.";
    }

    /// <summary>A stream that is one terminal <see cref="ProviderVerdict.Unsupported"/> error.</summary>
    public static async IAsyncEnumerable<TextChunk> Stream(string detail)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return TextChunk.Error(ProviderVerdict.Unsupported, detail);
    }
}
