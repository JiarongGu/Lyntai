namespace Lyntai.Embeddings;

/// <summary>
/// Turns text into embedding vectors. App-provided (bring your own model — an OpenAI/Ollama embeddings
/// endpoint, a local model, …), registered with <c>builder.AddEmbeddings(...)</c>: Lyntai owns the
/// semantic-recall machinery, the app owns the embedding backend. The batch shape is the primitive
/// (embedding N texts in one call is what real endpoints reward); <see cref="EmbedderExtensions.EmbedAsync"/>
/// is the single-text convenience. Every vector an implementation returns MUST have the same dimension.
/// </summary>
public interface IEmbedder
{
    /// <summary>Embed a batch of texts into vectors — one per input, in the same order.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public static class EmbedderExtensions
{
    /// <summary>Embed a single text (a thin wrapper over the batch primitive).</summary>
    public static async Task<float[]> EmbedAsync(this IEmbedder embedder, string text, CancellationToken ct = default) =>
        (await embedder.EmbedAsync([text], ct).ConfigureAwait(false))[0];
}
