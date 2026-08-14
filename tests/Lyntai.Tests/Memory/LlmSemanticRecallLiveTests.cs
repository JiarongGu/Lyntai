using System.Globalization;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Live;
using Lyntai.Tests.Memory.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary><b>Does the embedder EARN its cost when the question is one only it can answer?</b>
/// `TASKS.md` Part 69, and the measurement that decides whether that item is a defect or an artefact.
///
/// <para><b>The finding it re-examines.</b> Enabling an <see cref="IEmbedder"/> + vector store raised the
/// corpus miss rate from <c>0.5357</c> to <c>0.8357</c> — an order of magnitude more movement than any
/// policy — because semantic neighbours compete for the same bounded slots as lexical hits. That was
/// recorded as pinned-not-fixed, on the grounds that the corpus defines relevance LEXICALLY, so a semantic
/// neighbour is wrong there by construction.</para>
///
/// <para><b>Two things were wrong with how that was measured, and both flatter the pessimistic reading.</b>
/// The corpus had no question a semantic route could uniquely answer, so enrichment could only ever be seen
/// costing slots it never earned back. And it was measured with <c>FakeEmbedder</c> — a feature-hashed bag
/// of WORDS, in which "semantic similarity" IS word overlap. A test double that cannot represent meaning
/// cannot show meaning-based retrieval helping, so that arm was never a test of the idea.</para>
///
/// <para><see cref="CorpusLexicon.ParaphrasePairs"/> supplies the missing question — a statement and a cue
/// that mean the same and share NO index term, asserted rather than assumed — and this file supplies the
/// missing instrument: a REAL embedding model. Both were needed; either alone still measures nothing.</para>
///
/// <para><b>THE ANSWER, measured 2026-08-13, and it is not the one this file was built to find.</b> A real
/// embedding model recovers <b>none</b> of the paraphrased facts — 0/3, the same as no embedder at all. The
/// reason is structural: <c>GraphMemoryEngine.GatherAsync</c> seeds candidates only from
/// <c>IMemoryGraphStore.SeedAsync</c>, a LEXICAL query, and then walks edges. The vector store is consulted
/// at WRITE time — novelty for salience, and similarity linking — and never at recall time. <b>The graph
/// engine has no semantic RETRIEVAL path at all</b>, so no embedder can reach a fact whose wording shares
/// nothing with the query.</para>
///
/// <para>That corrects Part 69's own explanation of its finding: the embedder's cost was attributed to
/// semantic neighbours "competing for the same bounded slots as lexical hits", and there are no semantic
/// neighbours at recall. The cost is real but its mechanism is write-time linking and salience.</para>
///
/// <para>Runs only when <c>LYNTAI_LIVE_OLLAMA</c> is set AND the endpoint is reachable; otherwise SKIPPED,
/// never a pass that observed nothing. <c>LYNTAI_OLLAMA_EMBED_MODEL</c> overrides the model
/// (default <c>nomic-embed-text</c>).</para></summary>
public class LlmSemanticRecallLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string BaseUrl => OllamaLive.BaseUrl;

    private static string EmbedModel =>
        Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_EMBED_MODEL") ?? "nomic-embed-text";

    private static Task<bool> LiveAsync() => OllamaLive.IsAvailableAsync();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOllamaProvider(baseUrl: BaseUrl, defaultModel: EmbedModel)
            .UseDefaultCandidates("ollama")
            // The chat provider does not register an embedder — that is its own seam, and Ollama's native
            // batched /api/embed is reached through the OpenAI-compatible registration.
            .AddOpenAiCompatibleEmbedder("ollama-embed", o =>
            {
                o.BaseUrl = BaseUrl;
                o.Model = EmbedModel;
            }));
        return services.BuildServiceProvider();
    }

    /// <summary><b>THE MEASUREMENT.</b> Write every paraphrase statement plus filler, then ask each cue —
    /// which shares no index term with its target — and count how often the right entry comes back.
    ///
    /// <para>Without an embedder this must be near-total failure: that is what "shares no index term"
    /// means, and it is asserted as a CONTROL rather than assumed, because a lexical route that somehow
    /// answered these would make the whole comparison meaningless.</para></summary>
    [SkippableFact]
    public async Task A_real_embedder_recovers_paraphrased_facts_that_the_lexical_path_cannot_reach()
    {
        Skip.IfNot(await LiveAsync(), "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable");

        using var sp = Build();
        var embedder = sp.GetRequiredService<IEmbedder>();

        var lexical = await RunAsync(null);
        var semantic = await RunAsync(embedder);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
             embedder: {EmbedModel}
                              paraphrase hits
               no embedder    {lexical.Hits}/{lexical.Asked}
               real embedder  {semantic.Hits}/{semantic.Asked}
             """);
        output.WriteLine(table);

        // (1) The CONTROL. If the lexical path can answer these, the pairs overlap and nothing below means
        //     anything — the same failure mode the authoritative probe guards against.
        Assert.True(lexical.Hits == 0,
            $"the lexical path answered a cue it shares no term with — the pairs are not disjoint:\n{table}");

        // (2) THE FINDING, and it is not the one this file was written to look for: a REAL embedding model
        //     recovers none of them either. `GraphMemoryEngine.GatherAsync` seeds candidates ONLY from
        //     `IMemoryGraphStore.SeedAsync` — a lexical query — and then walks edges. The vector store is
        //     consulted at WRITE time (novelty for salience, and similarity LINKING) and never at recall
        //     time. So the graph engine has no semantic RETRIEVAL path: an embedder cannot reach a fact
        //     whose wording shares nothing with the query, however good the model is.
        //
        //     Pinned rather than asserted-away, because it is the load-bearing correction to `TASKS.md`
        //     Part 69. That item explains the embedder's measured cost as semantic neighbours "competing
        //     for the same bounded slots as lexical hits" — there are no semantic neighbours at recall, so
        //     the mechanism is write-time linking and salience instead. A future change that adds
        //     query-time vector seeding will flip this assertion, which is exactly when someone should be
        //     made to come back and re-read the Part.
        Assert.Equal(0, semantic.Hits);
    }

    /// <summary><b>Switched ON, the paraphrase becomes REACHABLE — the 0/3 above was never the model's
    /// fault, it was the engine never asking it anything at recall time.</b>
    ///
    /// <para>Measured at a WIDE limit, because the question this answers is whether the entry enters the
    /// candidate set at all. At a realistic limit the default ranking still loses it to recent unrelated
    /// material — the same reachable-but-outranked shape as every other miss in this subsystem, measured
    /// separately in <c>SemanticSeedProbeTests</c>. That is why the option is off by default and why it
    /// pairs with <c>AddMemoryVerification</c> rather than replacing it: seeding widens what is CONSIDERED,
    /// the judge is what SURFACES it.</para></summary>
    [SkippableFact]
    public async Task Semantic_seeding_makes_a_paraphrase_reachable_when_it_is_switched_on()
    {
        Skip.IfNot(await LiveAsync(), "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable");

        using var sp = Build();
        var embedder = sp.GetRequiredService<IEmbedder>();

        var off = await RunAsync(embedder, limit: 500);
        var on = await RunAsync(embedder, semanticSeedK: 5, limit: 500);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
             embedder: {EmbedModel}
                                   paraphrases reachable (limit 500)
               SemanticSeedK = 0   {off.Hits}/{off.Asked}
               SemanticSeedK = 5   {on.Hits}/{on.Asked}
             """);
        output.WriteLine(table);

        Assert.Equal(0, off.Hits);                     // no lexical route exists, at any limit
        Assert.True(on.Hits > 0, $"semantic seeding reached nothing:\n{table}");
    }

    private readonly record struct Arm(int Hits, int Asked);

    private static async Task<Arm> RunAsync(IEmbedder? embedder, int semanticSeedK = 0, int limit = 5)
    {
        var lex = CorpusLexicon.For(CorpusLanguage.English);
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            options: new GraphMemoryOptions { SemanticSeedK = semanticSeedK },
            policy: new DsrRetrievability(),
            agePolicies: [new PerWriteAgePolicy()],
            embedder: embedder,
            vectors: embedder is null ? null : new InMemoryVectorStore());

        // the statements under test, plus unrelated filler so a recall has something to get wrong
        var targets = new List<string>();
        foreach (var (statement, _) in lex.ParaphrasePairs)
            targets.Add((await engine.RememberAsync(new MemoryWrite("t", "s", statement))).Id);
        foreach (var word in lex.NoiseVocabulary.Take(20))
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated note about {word} and nothing else"));

        var hits = 0;
        for (var i = 0; i < lex.ParaphrasePairs.Count; i++)
        {
            var recall = await engine.RecallAsync(
                new MemoryQuery("t", "s", lex.ParaphrasePairs[i].Cue, Limit: limit));
            if (recall.Items.Any(item => item.Reference.Id == targets[i])) hits++;
        }

        return new Arm(hits, lex.ParaphrasePairs.Count);
    }
}
