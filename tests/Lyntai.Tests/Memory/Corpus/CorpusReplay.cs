using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory.Corpus;

/// <summary>One replay scored under the NOISE-SHARE metric the reinforcement, verification and salience
/// studies were measured with.
/// <para><b>Not <see cref="RecallQuality"/>, and not comparable with it.</b> <see cref="Miss"/> is POOLED —
/// every relevant id missed over every relevant id asked for — where <see cref="RecallQuality.MissRate"/>
/// is per query; <see cref="Pollution"/> is the share of RETURNED items that were corpus <c>noise*</c>
/// entries, where <see cref="RecallQuality.PollutionRate"/> is the share of the requested window outside the
/// relevant set.</para></summary>
/// <param name="Miss">Relevant ids a recall failed to return, over all relevant ids asked for.</param>
/// <param name="Pollution">Returned items that were noise, over all returned items.</param>
/// <param name="Expansions">How many <see cref="CorpusExpand"/> steps were replayed.</param>
internal readonly record struct NoiseShare(double Miss, double Pollution, int Expansions);

/// <summary>Replays a <see cref="MemoryCorpus"/> through a graph engine — every write, every query at a
/// fixed limit, every expansion — and scores it as a <see cref="NoiseShare"/>.</summary>
internal static class CorpusReplay
{
    /// <param name="engine">The engine under test, fresh.</param>
    /// <param name="corpus">The timeline to replay.</param>
    /// <param name="limit">Every query's own <see cref="MemoryQuery.Limit"/>.</param>
    /// <param name="idOf">Reads a corpus id from a written entry's content;
    /// <see cref="MemoryCorpusTestAccess.IdOf"/> when null.</param>
    /// <param name="beforeQuery">Runs before each recall with the corpus-id-to-engine-id map as it stands —
    /// how an oracle is taught a query's truth in engine ids.</param>
    /// <param name="afterWrite">Runs after each write with the reference it produced.</param>
    public static async Task<NoiseShare> RunAsync(GraphMemoryEngine engine, MemoryCorpus corpus, int limit,
        Func<string, string>? idOf = null,
        Action<CorpusQuery, IReadOnlyDictionary<string, string>>? beforeQuery = null,
        Func<MemoryRef, Task>? afterWrite = null)
    {
        idOf ??= MemoryCorpusTestAccess.IdOf;
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long returned = 0, noise = 0, wanted = 0, missed = 0;
        var expansions = 0;

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = (await engine.RememberAsync(w.Write)).Reference;
                    var corpusId = idOf(w.Write.Content);
                    byCorpusId[corpusId] = memRef.Id;
                    byRef[memRef.Id] = corpusId;
                    if (afterWrite is not null) await afterWrite(memRef);
                    break;

                case CorpusQuery q:
                    beforeQuery?.Invoke(q, byCorpusId);
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(first.TaskKey, first.Scope, q.Text, Limit: limit));
                    var got = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in recall.Items)
                    {
                        returned++;
                        if (!byRef.TryGetValue(item.Reference.Id, out var id)) continue;
                        got.Add(id);
                        if (id.StartsWith("noise", StringComparison.Ordinal)) noise++;
                    }
                    foreach (var want in q.RelevantIds)
                    {
                        wanted++;
                        if (!got.Contains(want)) missed++;
                    }
                    break;

                case CorpusExpand e:
                    if (byCorpusId.TryGetValue(e.EntryId, out var refId))
                    {
                        await engine.ExpandAsync(new MemoryRef(engine.Name, refId));
                        expansions++;
                    }
                    break;
            }

        return new NoiseShare(
            wanted == 0 ? 0 : (double)missed / wanted,
            returned == 0 ? 0 : (double)noise / returned,
            expansions);
    }

    /// <summary>Teaches <paramref name="oracle"/> a query's truth in engine ids — the entries exist by the
    /// time their query runs, which is what makes the mapping possible.</summary>
    public static Action<CorpusQuery, IReadOnlyDictionary<string, string>> Teach(OracleVerifier oracle) =>
        (q, byCorpusId) => oracle.Teach(q.Text,
            q.RelevantIds.Where(byCorpusId.ContainsKey).Select(id => byCorpusId[id]));
}
