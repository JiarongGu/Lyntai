using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <see cref="PerWriteAgePolicy"/>'s age, DERIVED from the three policy-independent primitives a store tracks
/// unconditionally (design doc §5.7), must equal the stored <c>Advance</c>-driven age — exactly, not
/// approximately — over a REPLAYED corpus, not a synthetic state.
/// <para><b>Why this still has to be RUN rather than only reasoned about.</b>
/// <see cref="PerWriteAgePolicy.Advance"/> always returns <c>Position = 1</c>, so the accumulator
/// (<see cref="GraphNode.Age"/>) and <see cref="GraphNode.OrdinalAge"/> advance in lockstep by construction —
/// and "by construction" is exactly the kind of claim that turns out almost true. This replays interleaved
/// writes AND queries (which touch whatever they recall) through a real engine over a real store, and checks
/// the two numbers agree after EVERY step, so a touch's own stamping is covered as thoroughly as a write's.</para>
/// <para>The guard that matters is the TOUCH count: a store whose recall never matched this corpus once left
/// this passing with zero touches (<c>.claude/knowledge/pitfalls.md</c>), because re-deriving untouched
/// primitives cannot fail on a broken touch path.</para>
/// </summary>
public class MemoryAgePrimitiveIdentityTests
{
    [Fact]
    public async Task PerWriteAgePolicys_derived_age_equals_the_stored_age_over_a_replayed_corpus()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        // undamped, matching the policy under test — BurstDampenedAgePolicy is explicitly NOT part of this
        // identity (see IMemoryAgePolicy.Age's own remarks on it being the documented exception)
        var engine = new GraphMemoryEngine("identity", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
            });
        var policy = new PerWriteAgePolicy();

        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 20260810);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var ids = new List<long>();
        var queries = 0;
        // every corpus entry is Associative (MemoryCorpus never sets a grade), so ReinforceAsync touches
        // EVERY item a recall returns — this sum is an exact touch count, not a proxy
        var touchedItems = 0;

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    var reference = (await engine.RememberAsync(w.Write)).Reference;
                    ids.Add(long.Parse(reference.Id));
                    break;
                case CorpusQuery q:
                    var recall = await engine.RecallAsync(new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text));
                    touchedItems += recall.Items.Count;
                    queries++;
                    break;
            }

            // checked after EVERY step (not just at the end) so a touch's own stamping — from whatever this
            // step's query just reinforced — is exercised exactly as thoroughly as a fresh write's
            foreach (var id in ids)
            {
                var node = await store.GetAsync("identity", id);
                Assert.NotNull(node);
                var derived = policy.Age(node!.AgeSample);
                Assert.Equal(node.Age, derived, precision: 9);
            }
        }

        Assert.NotEmpty(ids); // the corpus actually wrote something, or this proved nothing
        Assert.True(queries > 0, "expected the corpus to carry query steps, not just writes");
        // the touch count itself, which a broken TouchAsync (or a store whose recall never matches this
        // corpus's queries) drives to zero — a comparison count could not see that, writes alone inflate it
        Assert.True(touchedItems > 0,
            $"expected at least one item to be recalled and touched across {queries} queries, got 0 — " +
            "a touch's own primitive-stamping would go completely unexercised");
    }
}
