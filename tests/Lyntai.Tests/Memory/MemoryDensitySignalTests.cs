using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <see cref="SalienceContext.SimilarCount"/> counts neighbours that cleared
/// <see cref="GraphMemoryOptions.MinSimilarity"/>, which <see cref="SalienceContext.ComparableCount"/>
/// cannot: that one is the raw return of a search asking for <c>SimilarityK + 1</c> with no floor, so it
/// SATURATES and reports the same number for a write resembling one thing and a write resembling many.
/// </summary>
public class MemoryDensitySignalTests
{
    /// <summary>Exact cosine control. A bag-of-words fake cannot be used here: a correction shares nearly
    /// every word with what it corrects, so word overlap rates it maximally similar and the fixture would
    /// pass for the wrong reason.</summary>
    private sealed class ScriptedEmbedder(IReadOnlyDictionary<string, float[]> map) : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                [.. texts.Select(t => map.TryGetValue(t, out var v) ? v : new[] { 0f, 0f, 1f })]);
    }

    private sealed class CapturingSalience : IMemorySaliencePolicy
    {
        public List<SalienceContext> Seen { get; } = [];

        // A running policy must declare its own bit; None means "nothing computed this" and the engine
        // refuses the registration. 32-62 is the consumer range.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            Seen.Add(context);
            return MemorySignals.Empty;
        }
    }

    private const string Probe = "the write being judged";

    private static (GraphMemoryEngine Engine, CapturingSalience Salience) Build(
        IReadOnlyDictionary<string, float[]> map)
    {
        var salience = new CapturingSalience();
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            options: new GraphMemoryOptions { SimilarityK = 8, MinSimilarity = 0.6 },
            embedder: new ScriptedEmbedder(map),
            vectors: new InMemoryVectorStore(),
            saliencePolicies: [salience]);
        return (engine, salience);
    }

    [Fact]
    public async Task It_counts_only_neighbours_at_or_above_MinSimilarity()
    {
        // three neighbours at cosine 1.0, two at 0.0 — with a floor of 0.6 the answer is 3, not 5.
        var map = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            [Probe] = [1f, 0f, 0f],
            ["near one"] = [1f, 0f, 0f],
            ["near two"] = [1f, 0f, 0f],
            ["near three"] = [1f, 0f, 0f],
            ["far one"] = [0f, 1f, 0f],
            ["far two"] = [0f, 1f, 0f],
        };
        var (engine, salience) = Build(map);
        foreach (var text in map.Keys.Where(k => k != Probe))
            await engine.RememberAsync(new MemoryWrite("t", "s", text));
        salience.Seen.Clear();

        await engine.RememberAsync(new MemoryWrite("t", "s", Probe));

        var context = Assert.Single(salience.Seen);
        Assert.Equal(3, context.SimilarCount);
        Assert.Equal(5, context.ComparableCount);
    }

    [Fact]
    public async Task A_correction_and_a_recurrence_are_DISTINGUISHABLE_where_ComparableCount_saturates()
    {
        // The whole point. Both writes see a full search result, so ComparableCount reports the same number
        // for each; only the filtered count separates them.
        var map = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["correction"] = [1f, 0f, 0f],
            ["the one thing it corrects"] = [1f, 0f, 0f],
            ["recurrence"] = [0f, 1f, 0f],
        };
        for (var i = 0; i < 5; i++) map[$"routine instance {i}"] = [0f, 1f, 0f];
        // filler so both writes see the same number of raw neighbours
        for (var i = 0; i < 5; i++) map[$"unrelated {i}"] = [0f, 0f, 1f];

        var (engine, salience) = Build(map);
        foreach (var text in map.Keys.Where(k => k is not ("correction" or "recurrence")))
            await engine.RememberAsync(new MemoryWrite("t", "s", text));

        salience.Seen.Clear();
        await engine.RememberAsync(new MemoryWrite("t", "s", "correction"));
        var correction = Assert.Single(salience.Seen);

        salience.Seen.Clear();
        await engine.RememberAsync(new MemoryWrite("t", "s", "recurrence"));
        var recurrence = Assert.Single(salience.Seen);

        Assert.Equal(1, correction.SimilarCount);
        Assert.Equal(5, recurrence.SimilarCount);
        Assert.True(recurrence.SimilarCount > correction.SimilarCount,
            "the filtered count is the only thing that separates these two");
    }

    [Fact]
    public async Task It_excludes_this_writes_OWN_prior_vector_exactly_as_Novelty_does()
    {
        // A re-remember finds itself at cosine 1. Probe already excludes an identical-content match so a
        // re-remember is not judged minimally novel; the filtered count must use the same exclusion or the
        // two halves of one probe would disagree about what "self" means.
        var map = new Dictionary<string, float[]>(StringComparer.Ordinal) { [Probe] = [1f, 0f, 0f] };
        var (engine, salience) = Build(map);
        await engine.RememberAsync(new MemoryWrite("t", "s", Probe));
        salience.Seen.Clear();

        await engine.RememberAsync(new MemoryWrite("t", "s", Probe));

        Assert.Equal(0, Assert.Single(salience.Seen).SimilarCount);
    }

    [Fact]
    public async Task With_no_embedder_it_is_zero_rather_than_unknown()
    {
        // No search means no information. Zero is the same answer Novelty and ComparableCount already give,
        // and a policy reading a nonzero count from a store it never searched would be reading a fiction.
        var salience = new CapturingSalience();
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(), saliencePolicies: [salience]);

        await engine.RememberAsync(new MemoryWrite("t", "s", "no vectors are wired here"));

        Assert.Equal(0, Assert.Single(salience.Seen).SimilarCount);
    }

    [Fact]
    public void A_context_built_positionally_without_it_still_compiles()
    {
        var context = new SalienceContext("engine", 0.5, 3);

        Assert.Equal(0, context.SimilarCount);
    }
}
