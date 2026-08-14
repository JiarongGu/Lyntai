using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Task 2's gate, stated in the plan's own words: <see cref="PerWriteAgePolicy"/>'s age, DERIVED from the
/// three policy-independent primitives a store now tracks unconditionally (design doc §5.7), must equal the
/// age stored TODAY — exactly, not approximately — over a REPLAYED corpus, not a synthetic state.
/// <para><b>Why this holds, and why it still has to be RUN rather than only reasoned about.</b>
/// <see cref="PerWriteAgePolicy.Advance"/> always returns <c>Position = 1</c>, so the legacy,
/// <c>Advance</c>-driven accumulator (<see cref="GraphNode.Age"/>, unchanged by this task) and the new,
/// genuinely write-count-based <see cref="GraphNode.OrdinalAge"/> primitive advance in lockstep, write for
/// write, by construction — but "by construction" is exactly the kind of claim this project has repeatedly
/// found to be almost true (see the task brief's own self-review). This test does not trust the argument; it
/// replays a real corpus of interleaved writes AND queries (which touch, i.e. reinforce, whatever they
/// recall) through a real <see cref="GraphMemoryEngine"/> over a real store, and checks the two numbers agree
/// after EVERY step — not only at the end, so a touch's own stamping is covered as thoroughly as a fresh
/// write's.</para>
/// <para><b><see cref="SqliteMemoryGraphStore"/>, not <c>InMemoryMemoryGraphStore</c> — measured, not a
/// style choice (fix round 1, C1).</b> <c>InMemoryMemoryGraphStore.SeedAsync</c> matches a query as a
/// CONTIGUOUS substring of content, and this corpus's query texts (<c>"item topic0 repeat0"</c>) are never a
/// contiguous substring of its content templates (<c>"item topic0 covers … ordinary material…"</c>) — so
/// against the in-process store every recall returns empty and <c>TouchAsync</c> is NEVER called: instrumented,
/// the first version of this test measured writes=25, queries=69, non-empty seeds=0, touches=0, and still
/// passed, because a test that only re-derives from primitives a node whose primitives were never touched
/// cannot fail on a broken touch path. This is the exact trap Plan 4's own regression guard hit first
/// (<see cref="MemoryDefaultRecallQualityTests"/>'s own doc explains it) — <c>SqliteMemoryGraphStore</c>
/// tokenizes (trigram/bm25) and genuinely matches this corpus's queries, so recalls are non-empty and
/// touches actually happen.</para>
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
        var engine = new GraphMemoryEngine("identity", store, agePolicies: [new PerWriteAgePolicy()]);
        var policy = new PerWriteAgePolicy();

        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 20260810);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var ids = new List<long>();
        var writes = 0;
        var queries = 0;
        // every corpus entry is Associative (MemoryCorpus never sets a grade), so ReinforceAsync touches
        // EVERY item a recall returns — this sum is an exact touch count, not a proxy
        var touchedItems = 0;
        var comparisons = 0;

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    var reference = await engine.RememberAsync(w.Write);
                    ids.Add(long.Parse(reference.Id));
                    writes++;
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
                comparisons++;
            }
        }

        Assert.NotEmpty(ids); // the corpus actually wrote something, or this proved nothing
        Assert.True(queries > 0, "expected the corpus to carry query steps, not just writes");
        // the direct invariant fix round 1 asked for: `comparisons > ids.Count` alone is worthless (25
        // writes ALONE already produce far more comparisons than ids.Count, so it can never detect zero
        // touches) — assert the touch count itself, which a broken TouchAsync (or a store whose recall
        // never matches this corpus's queries, the original defect) drives to zero
        Assert.True(touchedItems > 0,
            $"expected at least one item to be recalled and touched across {queries} queries, got 0 — " +
            "a touch's own primitive-stamping would go completely unexercised");
        Assert.True(comparisons > ids.Count, "expected more than one comparison round — the corpus must " +
            "carry both writes and queries for touches to be exercised, not just fresh writes");
    }
}
