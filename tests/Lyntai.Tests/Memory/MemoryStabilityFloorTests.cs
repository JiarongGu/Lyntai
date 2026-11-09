using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A stored stability of zero was read two ways: <see cref="DsrRetrievability"/> substitutes
/// <see cref="DsrRetrievability.InitialStability"/> for it (retrievability near 0.93 at age 1), where the
/// store's in-store prune floors it at <see cref="MemoryGraphSql.MinimumStabilityValue"/> (age over stability
/// is huge, and the row is deleted). A BYO age policy returning <c>Encoding = 0</c> reaches it, so the engine
/// floors what it stores and the two readings agree.
/// </summary>
public class MemoryStabilityFloorTests
{
    /// <summary>Accumulating, so <c>PruneAsync</c> takes the store's own criterion; encodes the entry "zero"
    /// at nothing.</summary>
    private sealed class ZeroEncodingAgePolicy : IMemoryAgePolicy
    {
        public MemoryAgeKind Kind => MemoryAgeKind.Accumulating;
        public MemoryTick Advance(MemoryWrite write, string engine) =>
            write.Content == "zero" ? new MemoryTick(1, 0) : MemoryTick.One;
        public double Age(MemoryAgeSample sample) => sample.Ordinal;
    }

    private static async Task AssertReadingsAgreeAsync(IMemoryGraphStore store)
    {
        var dsr = new DsrRetrievability();
        var engine = new GraphMemoryEngine("e", store, seams: new GraphMemorySeams
        {
            AgePolicies = [new ZeroEncodingAgePolicy()],
            Retrievability = dsr,
        });
        await engine.RememberAsync(new MemoryWrite("t", "s", "zero"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "a later note"));

        var zero = (await store.SeedAsync("e", "t", "s", null, 10)).Single(n => n.Content == "zero");
        Assert.Equal(MemoryGraphSql.MinimumStabilityValue, zero.Stability);

        var retrievability = dsr.Retrievability(new MemoryDecayState(zero.Age, zero.RecallCount, zero.Stability));
        Assert.True(retrievability < 0.5, $"DSR reads the floored stability as unretrievable: {retrievability}");

        Assert.Equal(1, await engine.PruneAsync("t", "s", minRetrievability: 0.5));
        var left = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(["a later note"], left.Select(n => n.Content));
    }

    [Fact]
    public Task A_zero_encoding_is_floored_and_DSR_and_prune_agree_in_memory() =>
        AssertReadingsAgreeAsync(new InMemoryMemoryGraphStore());

    [Fact]
    public async Task A_zero_encoding_is_floored_and_DSR_and_prune_agree_on_sqlite()
    {
        using var db = new TempDb();
        await AssertReadingsAgreeAsync(new SqliteMemoryGraphStore(db.Factory));
    }
}
