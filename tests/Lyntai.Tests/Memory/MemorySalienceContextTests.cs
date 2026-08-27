using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A salience policy is handed the CALLER'S write, verbatim — so a policy judging something other than
/// novelty has everything it needs.
///
/// <para><b>Why this is worth pinning rather than left to the signature.</b>
/// <c>IMemorySaliencePolicy.Signals</c> takes a <see cref="MemoryWrite"/>, so a policy obviously receives
/// SOME write; what these assert is that it receives the one the caller made. The engine derives a headline
/// when none was given, resolves <see cref="MemoryGrade.Inherit"/> against its own role, and probes for
/// novelty before any policy runs — three places a normalized or reconstructed write could reach the policy
/// instead, and a policy reading <see cref="MemoryWrite.Metadata"/> for a host-declared judgement would then
/// silently see nothing. <b>D91</b> is the entry about a caller's silence being written as though they had
/// spoken; this is the same concern one seam over.</para>
///
/// <para><b>It also records the fact that made a library change unnecessary.</b> <see cref="SalienceContext"/>
/// carries only what the ENGINE measured — the engine name, novelty, comparables and
/// <see cref="SalienceContext.SimilarCount"/> — and reading that record alone suggests a policy can judge
/// nothing else, which is wrong and was briefly acted on. The write is the second parameter. An importance
/// policy needs no new surface; whether importance should VOTE is a separate, open measurement
/// (<c>memory-importance</c>), and no such policy ships.</para>
/// </summary>
public class MemorySalienceContextTests
{
    private sealed class CapturingSalience : IMemorySaliencePolicy
    {
        public List<MemoryWrite> Writes { get; } = [];

        public List<SalienceContext> Contexts { get; } = [];

        // A running policy must declare its own bit — None means "nothing computed this", and the engine
        // refuses the registration outright rather than letting provenance lie. 32-62 is the consumer range.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            Writes.Add(write);
            Contexts.Add(context);
            return MemorySignals.Empty;
        }
    }

    /// <summary>Exact cosine control. A bag-of-words fake would make novelty a function of word overlap, and
    /// what is being pinned here is that the ENGINE's own measurement reaches the context — not that some
    /// number does.</summary>
    private sealed class ScriptedEmbedder(IReadOnlyDictionary<string, float[]> map) : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                [.. texts.Select(t => map.TryGetValue(t, out var v) ? v : new[] { 0f, 0f, 1f })]);
    }

    private static async Task<CapturingSalience> WriteThrough(MemoryWrite write)
    {
        var policy = new CapturingSalience();
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(), saliencePolicies: [policy]);

        await engine.RememberAsync(write);

        return policy;
    }

    [Fact]
    public async Task The_policy_sees_the_content_it_is_judging()
    {
        var policy = await WriteThrough(
            new MemoryWrite("t", "s", "the incident took the cluster down for six hours"));

        Assert.Equal("the incident took the cluster down for six hours", Assert.Single(policy.Writes).Content);
    }

    [Fact]
    public async Task The_policy_sees_the_callers_metadata_so_a_host_can_DECLARE_importance()
    {
        // The shape `generic-library` rule 7 prefers: a value only the deployment can know is supplied by the
        // deployment. An app that already knows a message was consequential says so, rather than hoping a
        // library-side heuristic infers it.
        var policy = await WriteThrough(new MemoryWrite("t", "s", "the incident is resolved",
            Metadata: new Dictionary<string, string> { ["importance"] = "9" }));

        var seen = Assert.Single(policy.Writes);
        Assert.NotNull(seen.Metadata);
        Assert.Equal("9", seen.Metadata!["importance"]);
    }

    [Fact]
    public async Task An_unstated_headline_reaches_the_policy_as_UNSTATED_not_as_the_engines_derivation()
    {
        // The engine derives a headline for associative material when the caller named none. If that
        // derivation were applied before salience ran, a policy could not tell an authored one-line summary
        // from a truncation of the content — and "the caller bothered to summarise this" is exactly the kind
        // of signal an importance policy would want to read.
        var policy = await WriteThrough(new MemoryWrite("t", "s",
            "a long body that would certainly be truncated into some derived headline or other"));

        Assert.Null(Assert.Single(policy.Writes).Headline);
    }

    [Fact]
    public async Task Novelty_still_arrives_on_the_context_alongside_the_write()
    {
        // The two are complementary, not alternatives: novelty is the engine's own measurement and cannot be
        // derived from the write, which is why it lives on the context rather than being left to policies.
        // An embedder and a vector store are wired on purpose — without them `Probe` short-circuits and every
        // context reports 0, so a fixture on the bare engine cannot fail for the reason it is named for.
        const string prior = "the certificate rotation runs every ninety days";
        const string novel = "a violin string snapped during the second movement";
        const string familiar = "certificate rotation is a ninety day cycle";
        var policy = new CapturingSalience();
        var engine = new GraphMemoryEngine("e", new InMemoryMemoryGraphStore(),
            embedder: new ScriptedEmbedder(new Dictionary<string, float[]>(StringComparer.Ordinal)
            {
                [prior] = [1f, 0f, 0f],
                [novel] = [0f, 1f, 0f],
                [familiar] = [1f, 0f, 0f],
            }),
            vectors: new InMemoryVectorStore(),
            saliencePolicies: [policy]);

        await engine.RememberAsync(new MemoryWrite("t", "s", prior));
        policy.Writes.Clear();
        policy.Contexts.Clear();

        await engine.RememberAsync(new MemoryWrite("t", "s", novel));

        Assert.Equal(novel, Assert.Single(policy.Writes).Content);
        var context = Assert.Single(policy.Contexts);
        Assert.Equal("e", context.Engine);
        Assert.Equal(1, context.ComparableCount);
        Assert.Equal(1.0, context.Novelty, precision: 6);

        // and it is a MEASUREMENT rather than a constant: a write the store already holds something close to
        // reports the other end of the range, which a hardcoded 1 would fail.
        await engine.RememberAsync(new MemoryWrite("t", "s", familiar));

        Assert.Equal(0.0, policy.Contexts[^1].Novelty, precision: 6);
    }
}
