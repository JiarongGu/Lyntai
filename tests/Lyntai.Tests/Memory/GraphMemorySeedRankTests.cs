using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>What a candidate carries out of <c>GatherAsync</c>: which SOURCES matched it, and at what rank within
/// each source's OWN <see cref="GraphNode.Relevance"/> gradient.</b>
///
/// <para>Ranking per source is what keeps the scales apart — a lexical hit's rank ramp and a semantic hit's
/// cosine are never compared, and pooling them into one field is what made a semantic top hit outrankable by
/// construction.</para>
///
/// <para><b>Two rules with teeth.</b> A node the source did not MATCH earns no rank at all: store seeds
/// arrive GRADE-FIRST, so an authoritative entry the query never matched sorts to the top and crediting it
/// would silently undo <c>docs/DECISIONS.md</c> <b>D97</b>. And a source whose matched nodes ALL report one
/// relevance value is UNORDERED and earns no ranks either — that is D97 in a new costume, a candidate nobody
/// ordered by relevance reporting a relevance rank.</para>
///
/// <para><b>The gradient facts run on SQLite</b>, per the design spec and <c>pitfalls.md</c> §Storage:
/// <see cref="InMemoryMemoryGraphStore"/> reports a flat <c>1</c> for every match and orders by recency, so
/// it is the very store that has no gradient to observe. It is used only where the fixture's subject is
/// something else.</para>
/// </summary>
public sealed class GraphMemorySeedRankTests : IDisposable
{
    private const string TaskKey = "t";
    private const string Scope = "s";

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static long IdOf(MemoryRef reference) =>
        long.Parse(reference.Id, System.Globalization.CultureInfo.InvariantCulture);

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

    // ---- the lexical gradient --------------------------------------------------------------------------

    /// <summary>A lexical seed is ranked by the STORE's own relevance gradient, best first, competition-style.
    /// SQLite because that is a backend which computes one; the expected order is read back off the store
    /// rather than invented here.</summary>
    [Fact]
    public async Task A_lexical_seed_is_ranked_by_the_stores_own_relevance_gradient()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var engine = new GraphMemoryEngine("e", store, ranking: probe);

        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta gamma delta epsilon"));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta gamma"));
        await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta"));

        const int limit = 10;
        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta gamma", Limit: limit));

        // the engine asks each source for limit x CandidateMultiplier, so this is the same list it saw
        var seeded = await store.SeedAsync("e", TaskKey, Scope, "beta gamma",
            limit * new GraphMemoryOptions().CandidateMultiplier);

        // the fixture's premise: this backend really does report a gradient, or there is nothing to rank by
        Assert.Equal(3, seeded.Count);
        Assert.True(seeded.Select(n => n.Relevance).Distinct().Count() > 1,
            "the store reported a FLAT relevance, so this fixture cannot observe a gradient at all");

        var expected = seeded.OrderByDescending(n => n.Relevance).ToList();
        for (var i = 0; i < expected.Count; i++)
        {
            var candidate = CandidateFor(probe, expected[i].Id);
            Assert.True(candidate.Ranks.TryGet("lexical", out var rank),
                $"the seed at relevance position {i} carries no lexical rank at all");
            Assert.Equal(i + 1, rank);
        }
    }

    /// <summary><b>A source whose matched nodes all report ONE relevance value is UNORDERED, and contributes
    /// no relevance evidence.</b> Not a shared rank 1 — that would hand every one of them the source's BEST
    /// possible fusion term, promoting a channel that said nothing instead of silencing it.
    ///
    /// <para>This is <b>D97 in a new costume</b>: there, a candidate nobody SCORED reported maximum
    /// relevance; here, a candidate nobody ORDERED BY RELEVANCE would report a relevance RANK.
    /// <see cref="InMemoryMemoryGraphStore"/> is exactly that source — a flat <c>1</c> per match over a
    /// <c>grade → salience → recency</c> order — so the fixture is the shipped default on the shipped test
    /// backend, not a contrivance.</para>
    ///
    /// <para>Positive on the other side too: a gradient-bearing source over the SAME corpus and query DOES
    /// earn ranks, so this cannot pass by the engine simply failing to rank anything.</para></summary>
    [Fact]
    public async Task A_source_whose_matched_nodes_all_tie_on_relevance_contributes_no_ranks()
    {
        var flat = new InMemoryMemoryGraphStore();
        var flatProbe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var flatEngine = new GraphMemoryEngine("e", flat, ranking: flatProbe);

        var graded = new SqliteMemoryGraphStore(_db.Factory);
        var gradedProbe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var gradedEngine = new GraphMemoryEngine("e", graded, ranking: gradedProbe);

        foreach (var engine in new[] { flatEngine, gradedEngine })
        {
            await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta gamma delta epsilon"));
            await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta gamma"));
            await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta"));
        }

        var query = new MemoryQuery(TaskKey, Scope: Scope, Query: "beta gamma", Limit: 10);
        await flatEngine.RecallAsync(query);
        await gradedEngine.RecallAsync(query);

        // the premise, asserted rather than assumed — one store reports a gradient and the other does not
        Assert.Equal(1, (await flat.SeedAsync("e", TaskKey, Scope, "beta gamma", 40))
            .Select(n => n.Relevance).Distinct().Count());
        Assert.True((await graded.SeedAsync("e", TaskKey, Scope, "beta gamma", 40))
            .Select(n => n.Relevance).Distinct().Count() > 1);

        Assert.All(flatProbe.Captured, c => Assert.True(c.Ranks.IsEmpty,
            "an UNORDERED source earned a rank — a flat relevance means 'I have no ordering', "
            + "never 'everything is maximally relevant'"));
        Assert.Contains(gradedProbe.Captured, c => c.Ranks.TryGet("lexical", out _));
    }

    // ---- D97, the regression this change can most easily introduce -------------------------------------

    /// <summary><b>An authoritative entry the query never matched earns NO lexical rank, and is not counted
    /// as a second value in the source's gradient.</b>
    ///
    /// <para><see cref="InMemoryMemoryGraphStore"/> deliberately, and the fixture asserts why: it sorts an
    /// exact fact to position <b>0</b>, which is the placement that made rank-by-position dangerous. The two
    /// shipped SQL backends put a grade-admitted non-match at the LOW end instead, so a fixture built on one
    /// of those would pass on a rule this one refutes.</para>
    ///
    /// <para>Both assertions carry weight, against different mutations. Dropping the
    /// <see cref="GraphNode.Matched"/> skip puts the exact fact into the ranked set and the first fails.
    /// Counting it as a second distinct value — or letting it tie with the match — turns a one-node set into
    /// an UNORDERED two-node set, the match loses its rank, and the second fails.</para>
    ///
    /// <para>It still ENTERS the candidate set, so its absence from the probe would be a different defect and
    /// is asserted against too (<c>CandidateFor</c> throws on a missing id).</para></summary>
    [Fact]
    public async Task An_authoritative_non_match_sorted_first_by_grade_earns_NO_lexical_rank()
    {
        var store = new InMemoryMemoryGraphStore();
        var probe = new CandidateProbe(new ReciprocalRankFusionPolicy());
        var engine = new GraphMemoryEngine("e", store, ranking: probe);

        // shares no term with the query, and is admitted purely by grade
        var exact = await engine.RememberAsync(new MemoryWrite(TaskKey, Scope,
            "the vault passphrase is kept off site", Grade: MemoryGrade.Authoritative));
        var matched = await engine.RememberAsync(new MemoryWrite(TaskKey, Scope, "beta rollout begins monday"));

        await engine.RecallAsync(new MemoryQuery(TaskKey, Scope: Scope, Query: "beta", Limit: 10));

        var seeded = await store.SeedAsync("e", TaskKey, Scope, "beta", 40);
        Assert.Equal(IdOf(exact), seeded[0].Id);   // the dangerous placement, asserted not assumed
        Assert.False(seeded[0].Matched);           // admitted, never matched
        Assert.Single(seeded.Where(n => n.Matched == true));

        Assert.True(CandidateFor(probe, IdOf(exact)).Ranks.IsEmpty,
            "the exact fact the query never matched claimed a rank — crediting it undoes D97");
        Assert.True(CandidateFor(probe, IdOf(matched)).Ranks.TryGet("lexical", out var rank),
            "the one genuine match lost its rank — the unmatched node was counted into the gradient");
        Assert.Equal(1, rank);
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
