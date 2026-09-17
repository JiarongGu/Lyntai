using Lyntai.Tests.Fakes;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>The similarity-index address a graph engine stores enrichment vectors under.
///
/// <para><b>What these pin is a TASK boundary, which <c>docs/memory.md</c> §7 holds absolute (D92).</b> The
/// address composes three caller-supplied strings, so the separator decides whether two different triples
/// can name one collection. A printable one cannot promise they do not: with <c>|</c>, engine <c>E</c> +
/// task <c>a</c> + scope <c>b|c</c> and engine <c>E</c> + task <c>a|b</c> + scope <c>c</c> both compose to
/// <c>E|a|b|c</c>, so forgetting either erased the other across the boundary — and the unscoped seed swept a
/// prefix that reached a neighbouring task (<c>docs/FIXES.md</c>, 2026-09-15).</para></summary>
public class MemoryVectorCollectionTests
{
    // The exact pair that collided. Written as a theory so a THIRD ambiguous shape is one row, not one test.
    [Theory]
    [InlineData("a", "b|c", "a|b", "c")]          // the reported collision, under the old separator
    [InlineData("t", "", "t|", "")]               // an empty scope beside a task ending in the separator
    [InlineData("x|y", "z", "x", "y|z")]          // the same shape from the other side
    public void Two_different_task_scope_pairs_never_name_ONE_collection(
        string taskA, string scopeA, string taskB, string scopeB)
    {
        var a = MemoryVectorCollection.For("E", taskA, scopeA);
        var b = MemoryVectorCollection.For("E", taskB, scopeB);

        Assert.NotEqual(a, b);
    }

    // The read side. A prefix sweep is only safe if it cannot reach a task whose key merely STARTS with
    // another's — the failure the unscoped semantic seed had.
    [Fact]
    public void A_task_prefix_matches_its_own_scopes_and_NOTHING_under_a_neighbour()
    {
        var prefix = MemoryVectorCollection.PrefixFor("E", "task");

        Assert.StartsWith(prefix, MemoryVectorCollection.For("E", "task", "scope"), StringComparison.Ordinal);
        Assert.StartsWith(prefix, MemoryVectorCollection.For("E", "task", ""), StringComparison.Ordinal);
        Assert.DoesNotContain(prefix, MemoryVectorCollection.For("E", "task-two", "scope"), StringComparison.Ordinal);
        Assert.DoesNotContain(prefix, MemoryVectorCollection.For("E", "taskX", "scope"), StringComparison.Ordinal);
    }

    // The engine WRITES the address and SemanticSeedSource REBUILDS it; two spellings is how a removal
    // misses the collection a write created, which is why both now call the same function.
    [Fact]
    public void The_write_side_and_the_read_side_compose_the_SAME_address()
    {
        Assert.Equal(
            MemoryVectorCollection.For("engine", "task", "scope"),
            MemoryVectorCollection.PrefixFor("engine", "task") + "scope");
    }

    // ---- the behaviour the address exists to protect -------------------------------------------------

    /// <summary>Embeds everything to one vector, so any leak between collections shows up as a hit rather
    /// than being masked by a similarity threshold.</summary>
    private sealed class OneVectorProvider : FakeVectorProviderBase
    {
        public override Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new[] { 1f, 0f })]);
    }

    [Fact]
    public async Task Forgetting_one_TASK_leaves_a_neighbouring_task_its_vectors()
    {
        var vectors = new InMemoryVectorStore();
        var engine = new GraphMemoryEngine("E", new InMemoryMemoryGraphStore(),
            providers: [new OneVectorProvider()], vectors: vectors);

        // the two triples that composed to one address under the old separator
        await engine.RememberAsync(new MemoryWrite("a", "b|c", "kept"));
        await engine.RememberAsync(new MemoryWrite("a|b", "c", "erased"));

        await engine.ForgetAsync("a|b");

        var survivors = await ((IListableVectorStore)vectors)
            .ListCollectionsAsync(MemoryVectorCollection.PrefixFor("E", "a"));
        Assert.Single(survivors);
    }
}
