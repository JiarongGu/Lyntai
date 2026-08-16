using Dapper;

using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Does the graph actually LINK an entity cluster, and does it do so in every language?</b>
///
/// <para>The language sweep (<c>node devtools/dev.mjs memory-language</c>) measured Chinese sitting exactly
/// on the no-graph floor for the cluster class — miss 0.6667 with pollution 0.0000 — while English sits well
/// below it. That says the graph reaches nothing in Chinese; it does not say WHY. This narrows it to one
/// question with a countable answer, because the engine's own mechanism is countable: co-activation edges
/// are written in <c>ReinforceAsync</c> between the entries a recall actually RETURNED, capped at
/// <c>GraphMemoryOptions.CoActivationCap</c>. So a cluster links only if its members co-occur in some
/// result set, and "the graph doesn't help" and "the edges were never written" are different failures with
/// different fixes.</para>
///
/// <para>Counted straight out of <c>lyntai_memory_edge</c> after a full replay rather than inferred from
/// recall quality — a recall-quality number cannot distinguish "no edges" from "edges that did not help".
/// </para>
/// </summary>
public class MemoryClusterEdgeFormationTests
{
    private const int Seed = 12345;
    private const int QueryLimit = 10;

    private sealed record EdgeCensus(int Nodes, int Edges, int WithinCluster, int ClusterNodes);

    private static async Task<EdgeCensus> ReplayAndCountAsync(CorpusLanguage language)
    {
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, Seed);

        using var db = new TempDb();
        var engine = new GraphMemoryEngine("edge-census", new SqliteMemoryGraphStore(db.Factory),
            agePolicies: [new PerWriteAgePolicy()],
            retrievability: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy());

        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var clusterNodeIds = new HashSet<long>();

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    // "{leading} {id} …" — the corpus's own convention, in either language
                    if (w.Write.Content.Split(' ')[1].StartsWith("attribute", StringComparison.Ordinal))
                        clusterNodeIds.Add(long.Parse(memRef.Id, System.Globalization.CultureInfo.InvariantCulture));
                    break;

                case CorpusQuery q:
                    await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: QueryLimit));
                    break;

                case CorpusExpand e:
                    break;   // expansions are off on this shape
            }

        using var conn = db.Factory.Open();
        var nodes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM lyntai_memory_node");
        var edges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM lyntai_memory_edge");
        var within = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM lyntai_memory_edge WHERE from_id IN @ids AND to_id IN @ids",
            new { ids = clusterNodeIds.ToArray() });

        return new EdgeCensus(nodes, edges, within, clusterNodeIds.Count);
    }

    /// <summary><b>Co-activation writes edges at all, in every language.</b> The floor, not the goal — but a
    /// real one: if this ever reached zero the graph would be an empty structure and every recall-quality
    /// number involving it would silently become a measurement of ranking alone.
    ///
    /// <para><b>MEASURED 2026-08-12, and the reason this file exists — the goal is NOT met, in either
    /// language.</b> Over a full replay of <c>CorpusShape.Default with { AttributeCount = 3 }</c> at seed
    /// 12345, with 266 nodes:</para>
    /// <list type="table">
    ///   <item><description>English — 442 edges, of which <b>2</b> (one symmetric pair) join two of the
    ///     three cluster members</description></item>
    ///   <item><description>Chinese — 366 edges, of which <b>0</b> do</description></item>
    /// </list>
    /// <para>So English links ONE pair out of three and Chinese links none. The language sweep's headline —
    /// English below the no-graph floor, Chinese exactly on it — is therefore not "the graph works and
    /// Chinese breaks it": it is a mechanism that barely reaches an entity cluster at all, with English on
    /// the lucky side of the same coin. Co-activation links whatever a recall RETURNED, so whether a
    /// cluster connects depends on its members happening to land in one top-N together; nothing about the
    /// mechanism is about them being about the same entity.</para>
    /// <para>The two mechanisms that DO answer the case are elsewhere and are not luck: similarity edges at
    /// write time (needs an embedder and a vector store — neither is supplied here, and a deterministic fake
    /// cannot stand in, since "similar" would then mean something different from what it means in
    /// production), and <see cref="MemoryGrade.Authoritative"/>, which
    /// <c>MemoryChineseRecallTests</c> shows returning the whole cluster in both languages because grade
    /// admission never consults the tokenizer. See <c>TASKS.md</c>.</para>
    /// <para>This asserts the floor rather than the goal deliberately: pinning <c>withinCluster == 2</c> for
    /// English would make a defect the expected behaviour and fail the day it is fixed.</para></summary>
    [Theory]
    [InlineData(CorpusLanguage.English)]
    [InlineData(CorpusLanguage.Chinese)]
    public async Task Co_activation_writes_edges_in_every_language(CorpusLanguage language)
    {
        var census = await ReplayAndCountAsync(language);

        Assert.Equal(3, census.ClusterNodes);
        Assert.True(census.Edges > 0,
            $"[{language}] {census.Nodes} nodes and NO edges — co-activation stopped forming a graph, so " +
            "every graph recall-quality figure is now measuring ranking alone.");
    }
}
