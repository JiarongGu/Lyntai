using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>A deterministic embedding BACKEND for tests: a feature-hashed bag-of-words vector, so texts
/// that share words land close in cosine space (a stand-in for real semantic similarity — enough to
/// exercise ranking without a model). Uses a stable hash (not <c>string.GetHashCode</c>, which is
/// per-process randomized) so results are reproducible across runs.
///
/// <para><b>An <see cref="IModelProvider"/> declaring <see cref="ProviderKinds.Vector"/> (D151)</b> —
/// there is no separate vector backend seam to fake. It is the same shape a real in-process backend has, which
/// is the point: a test that passes this exercises the capability filter and the routing the production
/// path uses, rather than a seam only tests implement.</para></summary>
public sealed class FakeVectorProvider(int dim = 64) : FakeVectorProviderBase
{
    /// <summary>The declaration, reachable without an instance — what a test hands
    /// <c>AddProvider(factory, declares)</c> so composition can see the capability before anything is
    /// built.</summary>
    public static new readonly ProviderCapabilities Declared = FakeVectorProviderBase.Declared;

    public override Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
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
