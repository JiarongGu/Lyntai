using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>What a candidate carries out of <c>GatherAsync</c>: which SOURCES matched it, and at what 1-based
/// position within each source's own returned order.</b>
///
/// <para>Position is the rank because no score crosses the <see cref="IMemorySeedSource"/> boundary — a
/// lexical hit's rank ramp and a semantic hit's cosine share no scale, and pooling them into one
/// <see cref="GraphNode.Relevance"/> field is what made a semantic top hit outrankable by construction.</para>
///
/// <para><b>The rule with teeth is the exception:</b> a node the source did not MATCH is skipped and the
/// counter does NOT advance for it. Store seeds arrive GRADE-FIRST, so an authoritative entry the query
/// never matched sorts to position 0 — ranking by raw position would hand it the best lexical rank and
/// silently undo <c>docs/DECISIONS.md</c> <b>D97</b>.</para>
/// </summary>
public sealed class GraphMemorySeedRankTests
{
    private const string TaskKey = "t";
    private const string Scope = "s";

    /// <summary>Captures the candidate set the engine handed the ranker, then delegates — the only way to
    /// observe <see cref="MemoryCandidate.Ranks"/>, which never reaches <see cref="MemoryItem"/>. Mirrors
    /// <c>MemoryLocomoBench</c>'s own <c>EvidenceRankProbe</c>.</summary>
    private sealed class CandidateProbe(IMemoryRankingPolicy inner) : IMemoryRankingPolicy
    {
        internal List<MemoryCandidate> Captured { get; } = [];

        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context)
        {
            Captured.Clear();
            Captured.AddRange(candidates);
            return inner.Rank(candidates, context);
        }
    }

    private static MemoryCandidate CandidateFor(CandidateProbe probe, long id) =>
        probe.Captured.Single(c => c.Node.Id == id);

    // ---- lexical position ------------------------------------------------------------------------------

    /// <summary>Rank is POSITION within the source's own list, 1-based. Compared against a direct
    /// <see cref="IMemoryGraphStore.SeedAsync"/> call at the width the engine asks for, so the expected
    /// order is the store's own rather than one this test invented.</summary>
    [Fact]
    public async Task A_lexical_seed_carries_its_1_based_position_within_its_own_source()
    {
        var store = new InMemoryMemoryGraphStore();
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var engine = new GraphMemoryEngine("e", store, ranking: probe);

        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta one"));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta two"));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta three"));

        const int limit = 10;
        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta", Limit: limit));

        // the engine asks each source for limit x CandidateMultiplier, so this is the same list it saw
        var expected = await store.SeedAsync("e", TaskKey, Scope, "beta",
            limit * new GraphMemoryOptions().CandidateMultiplier);

        Assert.Equal(3, expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var candidate = CandidateFor(probe, expected[i].Id);
            Assert.True(candidate.Ranks.TryGet("lexical", out var rank),
                $"the seed at position {i} carries no lexical rank at all");
            Assert.Equal(i + 1, rank);
        }
    }

    // ---- D97, the regression this change can most easily introduce -------------------------------------

    /// <summary><b>An authoritative entry the query never matched earns NO lexical rank, and does not consume
    /// the number.</b> Both halves: it must not claim relevance evidence it did not earn, and the matched
    /// entry behind it must hold rank <c>1</c> rather than rank <c>2</c>.
    /// <para>It still ENTERS the candidate set — the grade carve-out admits it and the engine re-admits it
    /// into the returned page — so its absence from the probe would be a different defect, and is asserted
    /// against too.</para></summary>
    [Fact]
    public async Task An_authoritative_non_match_sorted_first_by_grade_earns_NO_lexical_rank()
    {
        var store = new InMemoryMemoryGraphStore();
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var engine = new GraphMemoryEngine("e", store, ranking: probe);

        // shares no term with the query, and is admitted purely by grade — the store sorts it FIRST
        var exact = await engine.RememberAsync(new MemoryWrite(TaskKey, Scope,
            "the vault passphrase is kept off site", Grade: MemoryGrade.Authoritative));
        var matched = await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta rollout begins monday"));

        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta", Limit: 10));

        var seeded = await store.SeedAsync("e", TaskKey, Scope, "beta", 40);
        Assert.Equal(long.Parse(exact.Id, System.Globalization.CultureInfo.InvariantCulture), seeded[0].Id);
        Assert.False(seeded[0].Matched);   // the fixture's whole premise: admitted, never matched

        var exactCandidate = CandidateFor(probe,
            long.Parse(exact.Id, System.Globalization.CultureInfo.InvariantCulture));
        var matchedCandidate = CandidateFor(probe,
            long.Parse(matched.Id, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(exactCandidate.Ranks.IsEmpty,
            "the exact fact the query never matched claimed a rank — ranking by raw position undoes D97");
        Assert.True(matchedCandidate.Ranks.TryGet("lexical", out var rank));
        Assert.Equal(1, rank);   // NOT 2: the skipped node must not consume the number
    }

    // ---- agreement across sources ----------------------------------------------------------------------

    /// <summary>Exact text to exact vector, so a hit is the semantic channel or nothing.</summary>
    private sealed class ScriptedEmbedder(string text) : IEmbedder
    {
        private static readonly float[] On = [1f, 0f];
        private static readonly float[] Off = [0f, 1f];

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                [.. texts.Select(t => string.Equals(t, text, StringComparison.Ordinal) ? On : Off)]);
    }

    /// <summary>A node two sources found accumulates BOTH ranks — that is what lets rank fusion reward
    /// agreement rather than average it away.</summary>
    [Fact]
    public async Task A_node_two_sources_found_carries_both_ranks()
    {
        const string target = "beta rollout begins monday";
        var store = new InMemoryMemoryGraphStore();
        var vectors = new InMemoryVectorStore();
        var embedder = new ScriptedEmbedder(target);
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());

        var engine = new GraphMemoryEngine("e", store, ranking: probe,
            embedder: embedder, vectors: vectors,
            seedSources: [new LexicalSeedSource(), new SemanticSeedSource(embedder, vectors)]);

        var both = await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, target));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "unrelated kitchen roster note"));

        // the query IS the target text, so it matches lexically and embeds onto the target's own vector
        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: target, Limit: 10));

        var candidate = CandidateFor(probe,
            long.Parse(both.Id, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(candidate.Ranks.TryGet("lexical", out var lexical), "no lexical rank");
        Assert.True(candidate.Ranks.TryGet("semantic", out var semantic), "no semantic rank");
        Assert.Equal(1, lexical);
        Assert.Equal(1, semantic);
        Assert.Equal(2, candidate.Ranks.Count);
    }

    // ---- what carries no rank --------------------------------------------------------------------------

    /// <summary>A hop neighbour is reached by TRAVERSAL, not by a query, so no source ever ranked it. Empty
    /// is "no relevance evidence", never "ranked worst".</summary>
    [Fact]
    public async Task A_hop_neighbour_carries_no_ranks()
    {
        var store = new InMemoryMemoryGraphStore();
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var engine = new GraphMemoryEngine("e", store, options: new GraphMemoryOptions { Hops = 1 },
            ranking: probe);

        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta rollout begins monday"));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "unrelated kitchen roster note"));

        var all = await store.SeedAsync("e", TaskKey, Scope, query: null, limit: 10);
        var anchor = all.Single(n => n.Content.Contains("beta", StringComparison.Ordinal)).Id;
        var walked = all.Single(n => n.Content.Contains("kitchen", StringComparison.Ordinal)).Id;
        await store.LinkAsync("e", anchor, walked, "similar", 1, symmetric: true);

        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta", Limit: 10));

        var neighbour = CandidateFor(probe, walked);
        Assert.Equal(1, neighbour.Hop);
        Assert.True(neighbour.Ranks.IsEmpty);
        Assert.False(CandidateFor(probe, anchor).Ranks.IsEmpty);   // the control: the seed DID earn one
    }

    // ---- registration ----------------------------------------------------------------------------------

    /// <summary>The default registration reproduces what the removed options defaulted to: the unconditional
    /// store read (<c>lexical</c>), the handle channel that was on at <c>SubjectSeedK = 5</c>
    /// (<c>subject</c>), and NOT the vector channel, which was off at <c>SemanticSeedK = 0</c>.</summary>
    [Fact]
    public void The_default_registration_wires_lexical_and_subject_but_not_semantic()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemory());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["lexical", "subject"],
            sp.GetServices<IMemorySeedSource>().Select(s => s.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>Registering the vector channel is one call, and it ADDS rather than replaces — the seam is
    /// plural, so the two shipped channels keep running beside it.</summary>
    [Fact]
    public void AddMemorySemanticSeeds_adds_the_vector_channel_beside_the_two_defaults()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemory()
            .AddMemorySemanticSeeds());
        services.AddSingleton<IEmbedder>(new ScriptedEmbedder("x"));
        services.AddSingleton<IVectorStore>(new InMemoryVectorStore());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["lexical", "semantic", "subject"],
            sp.GetServices<IMemorySeedSource>().Select(s => s.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>Calling the subject registration on top of the default configures the ONE source rather than
    /// adding a second under the same name — two sources sharing a <see cref="IMemorySeedSource.Name"/> would
    /// each contribute their own fusion term for the same evidence.</summary>
    [Fact]
    public void AddMemorySubjectSeeds_configures_the_default_subject_source_rather_than_duplicating_it()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemory()
            .AddMemorySubjectSeeds(new SubjectSeedOptions { K = 11 }));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["lexical", "subject"],
            sp.GetServices<IMemorySeedSource>().Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal(11, sp.GetRequiredService<SubjectSeedOptions>().K);
    }

    // ---- the two guards --------------------------------------------------------------------------------

    /// <summary>Two sources under one <see cref="IMemorySeedSource.Name"/> is refused at construction rather
    /// than silently double-counted: <see cref="MemorySeedRanks.TryGet"/> keys by name and reports only the
    /// first, while rank fusion sums a term for EVERY entry — so the second one is invisible evidence that
    /// still moves the score. Reported at wiring time (<b>D85</b>).</summary>
    [Fact]
    public void Two_sources_sharing_a_name_are_refused_rather_than_double_counted()
    {
        var ex = Assert.Throws<ArgumentException>(() => new GraphMemoryEngine("e",
            new InMemoryMemoryGraphStore(),
            seedSources: [new LexicalSeedSource(), new LexicalSeedSource()]));

        Assert.Contains("lexical", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Returns the same node twice — a BYO source's contract says best-first, never that the list is
    /// distinct.</summary>
    private sealed class RepeatingSource(long id) : IMemorySeedSource
    {
        public string Name => "repeating";

        public async Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct)
        {
            var node = await request.Store.GetAsync(request.Engine, id, ct).ConfigureAwait(false);
            return node is null ? [] : [node, node];
        }
    }

    /// <summary>One source contributes at most ONE rank per candidate. A repeat is skipped without advancing
    /// the counter, so a duplicate cannot buy a second fusion term for the same evidence.</summary>
    [Fact]
    public async Task A_source_that_returns_the_same_node_twice_contributes_one_rank()
    {
        var store = new InMemoryMemoryGraphStore();
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var seeded = new GraphMemoryEngine("e", store);
        var only = await seeded.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta rollout begins monday"));
        var id = long.Parse(only.Id, System.Globalization.CultureInfo.InvariantCulture);

        var engine = new GraphMemoryEngine("e", store, ranking: probe, seedSources: [new RepeatingSource(id)]);
        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta", Limit: 10));

        var candidate = CandidateFor(probe, id);
        Assert.Equal(1, candidate.Ranks.Count);
        Assert.True(candidate.Ranks.TryGet("repeating", out var rank));
        Assert.Equal(1, rank);
    }
}
