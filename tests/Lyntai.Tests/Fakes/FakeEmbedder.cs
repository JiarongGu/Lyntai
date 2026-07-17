using Lyntai.Embeddings;

namespace Lyntai.Tests.Fakes;

/// <summary>A deterministic <see cref="IEmbedder"/> for tests: a feature-hashed bag-of-words vector, so
/// texts that share words land close in cosine space (a stand-in for real semantic similarity — enough to
/// exercise ranking without a model). Uses a stable hash (not <c>string.GetHashCode</c>, which is
/// per-process randomized) so results are reproducible across runs.</summary>
public sealed class FakeEmbedder(int dim = 64) : IEmbedder
{
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Embed)]);

    private float[] Embed(string text)
    {
        var v = new float[dim];
        foreach (var word in text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            v[StableHash(word) % dim] += 1f;
        return v;
    }

    private static int StableHash(string s)
    {
        uint h = 2166136261; // FNV-1a
        foreach (var c in s) { h ^= c; h *= 16777619; }
        return (int)(h & 0x7fffffff);
    }
}
