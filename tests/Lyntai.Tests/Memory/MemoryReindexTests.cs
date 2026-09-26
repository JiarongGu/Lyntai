using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="IReindexableMemory"/>: a re-embed replaces every node's vector at its address and writes
/// nothing else — and a forget or prune landing mid-pass is never undone by it.</summary>
public class MemoryReindexTests
{
    private const string Name = "g";

    private sealed record Rig(GraphMemoryEngine Engine, InMemoryMemoryGraphStore Store, IVectorStore Vectors,
        SwitchableVectorProvider Provider);

    private static Rig Build(IVectorStore? vectors = null, GraphMemoryOptions? options = null,
        Func<DateTimeOffset>? clock = null, bool withVectors = true, bool withProvider = true)
    {
        var store = new InMemoryMemoryGraphStore(clock);
        var index = vectors ?? new InMemoryVectorStore();
        var provider = new SwitchableVectorProvider();
        var engine = new GraphMemoryEngine(Name, store, options, new GraphMemorySeams
        {
            AgePolicies = [new PerWriteAgePolicy()],
            Providers = withProvider ? [provider] : [],
            Vectors = withVectors ? index : null,
            Clock = clock,
        });
        return new Rig(engine, store, index, provider);
    }

    private static async Task<IReadOnlyList<VectorEntry>> VectorsAsync(Rig rig, string task, string scope)
    {
        var nodes = await rig.Store.SeedAsync(Name, task, scope, null, int.MaxValue);
        return await ((IReadableVectorStore)rig.Vectors).GetAsync(MemoryVectorCollection.For(Name, task, scope),
            [.. nodes.Select(n => n.Id.ToString(CultureInfo.InvariantCulture))]);
    }

    private static async Task<IReadOnlyList<VectorEntry>> AllVectorsAsync(Rig rig, string task, string scope, IEnumerable<long> ids) =>
        await ((IReadableVectorStore)rig.Vectors).GetAsync(MemoryVectorCollection.For(Name, task, scope),
            [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))]);

    private static async Task<IReadOnlyList<long>> RememberAsync(Rig rig, string task, string scope, int count,
        string label = "")
    {
        var ids = new List<long>();
        for (var i = 0; i < count; i++)
        {
            var written = await rig.Engine.RememberAsync(
                new MemoryWrite(task, scope, $"fact {scope}{label} number {i} about deploys"));
            ids.Add(long.Parse(written.Reference.Id, CultureInfo.InvariantCulture));
        }
        return ids;
    }

    [Fact]
    public async Task A_re_embed_replaces_every_vector_at_its_address_and_changes_nothing_else()
    {
        var rig = Build();
        var ids = await RememberAsync(rig, "t", "s", 5);
        await rig.Engine.LinkAsync(new MemoryRef(Name, ids[0].ToString(CultureInfo.InvariantCulture)),
            new MemoryRef(Name, ids[4].ToString(CultureInfo.InvariantCulture)), weight: 2);
        var nodesBefore = await Snapshot(rig);
        var edgesBefore = await Edges(rig, ids);
        var payloadsBefore = (await VectorsAsync(rig, "t", "s")).Select(v => (v.Id, v.Payload)).ToList();

        rig.Provider.Model = 2;
        var result = await rig.Engine.ReindexAsync("t");

        Assert.Equal(new MemoryReindexResult(5, 0), result);
        var after = await VectorsAsync(rig, "t", "s");
        Assert.Equal(5, after.Count);
        Assert.All(after, v => Assert.Equal(rig.Provider.Vector(v.Payload), v.Vector));   // the new model's
        Assert.Equal(payloadsBefore, after.Select(v => (v.Id, v.Payload)));
        Assert.Equal(nodesBefore, await Snapshot(rig));
        Assert.Equal(edgesBefore, await Edges(rig, ids));
    }

    [Fact]
    public async Task A_scoped_re_embed_touches_only_its_scope()
    {
        var rig = Build();
        await RememberAsync(rig, "t", "s1", 2);
        await RememberAsync(rig, "t", "s2", 2);

        rig.Provider.Model = 2;
        Assert.Equal(new MemoryReindexResult(2, 0), await rig.Engine.ReindexAsync("t", "s1"));

        Assert.All(await VectorsAsync(rig, "t", "s1"), v => Assert.Equal(2f, v.Vector[0]));
        Assert.All(await VectorsAsync(rig, "t", "s2"), v => Assert.Equal(1f, v.Vector[0]));
    }

    [Fact]
    public async Task Batches_follow_ReindexBatchSize()
    {
        var rig = Build(options: new GraphMemoryOptions { ReindexBatchSize = 2 });
        await RememberAsync(rig, "t", "s", 5);
        var before = rig.Provider.Calls;

        await rig.Engine.ReindexAsync("t");

        Assert.Equal(3, rig.Provider.Calls - before);
    }

    [Fact]
    public async Task A_failing_batch_is_counted_and_the_pass_continues()
    {
        var rig = Build(options: new GraphMemoryOptions { ReindexBatchSize = 2 });
        await RememberAsync(rig, "t", "s", 5);
        var second = rig.Provider.Calls + 2;
        rig.Provider.OnCall = (call, _) => call == second ? throw new InvalidOperationException("embedder down") : Task.CompletedTask;

        rig.Provider.Model = 2;
        var result = await rig.Engine.ReindexAsync("t");

        Assert.Equal(new MemoryReindexResult(3, 2), result);
        Assert.Equal(3, (await VectorsAsync(rig, "t", "s")).Count(v => v.Vector[0] == 2f));
    }

    [Fact]
    public async Task No_index_or_no_embedder_is_refused_before_any_work()
    {
        var noIndex = Build(withVectors: false);
        await RememberAsync(noIndex, "t", "s", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noIndex.Engine.ReindexAsync("t"));

        var noEmbedder = Build(withProvider: false);
        await RememberAsync(noEmbedder, "t", "s", 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => noEmbedder.Engine.ReindexAsync("t"));
        Assert.Contains("ProviderKinds.Vector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forget_during_the_embed_call_leaves_no_vector()
    {
        var rig = Build();
        var ids = await RememberAsync(rig, "t", "s", 3);
        var (parked, release) = Park(rig.Provider, rig.Provider.Calls + 1);

        var pass = rig.Engine.ReindexAsync("t");
        await parked.Task.WaitAsync(TestTimeouts.GateWait);
        await rig.Engine.ForgetAsync("t");
        release.SetResult();

        Assert.Equal(new MemoryReindexResult(0, 0), await pass.WaitAsync(TestTimeouts.GateWait));
        Assert.Empty(await AllVectorsAsync(rig, "t", "s", ids));
    }

    [Fact]
    public async Task A_forget_during_the_write_step_waits_and_leaves_no_vector()
    {
        var vectors = new ParkingVectorStore();
        var rig = Build(vectors);
        var ids = await RememberAsync(rig, "t", "s", 3);
        vectors.ParkNextUpsert();

        var pass = rig.Engine.ReindexAsync("t");
        await vectors.Parked.Task.WaitAsync(TestTimeouts.GateWait);
        var forget = rig.Engine.ForgetAsync("t");
        Assert.False(forget.IsCompleted);                        // it waits for the write step to let go
        vectors.Release.SetResult();

        await pass.WaitAsync(TestTimeouts.GateWait);
        await forget.WaitAsync(TestTimeouts.GateWait);
        Assert.Empty(await AllVectorsAsync(rig, "t", "s", ids));
    }

    [Fact]
    public async Task A_prune_during_the_embed_call_leaves_no_vector_for_what_it_removed()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z", CultureInfo.InvariantCulture);
        var rig = Build(clock: () => now);
        var old = await RememberAsync(rig, "t", "s", 1);
        now = now.AddDays(10);
        var fresh = await RememberAsync(rig, "t", "s", 2, label: "-later");
        var (parked, release) = Park(rig.Provider, rig.Provider.Calls + 1);

        rig.Provider.Model = 2;
        var pass = rig.Engine.ReindexAsync("t");
        await parked.Task.WaitAsync(TestTimeouts.GateWait);
        Assert.Equal(1, await rig.Engine.PruneAsync("t", minRetrievability: 0, olderThan: TimeSpan.FromDays(5)));
        release.SetResult();

        Assert.Equal(new MemoryReindexResult(2, 0), await pass.WaitAsync(TestTimeouts.GateWait));
        Assert.Empty(await AllVectorsAsync(rig, "t", "s", old));
        Assert.All(await AllVectorsAsync(rig, "t", "s", fresh), v => Assert.Equal(2f, v.Vector[0]));
    }

    [Fact]
    public async Task Cancellation_mid_pass_propagates_and_keeps_finished_batches()
    {
        var rig = Build(options: new GraphMemoryOptions { ReindexBatchSize = 2 });
        await RememberAsync(rig, "t", "s", 4);
        using var cts = new CancellationTokenSource();
        var second = rig.Provider.Calls + 2;
        rig.Provider.OnCall = (call, ct) =>
        {
            if (call != second) return Task.CompletedTask;
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        rig.Provider.Model = 2;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Engine.ReindexAsync("t", ct: cts.Token));

        Assert.Equal(2, (await VectorsAsync(rig, "t", "s")).Count(v => v.Vector[0] == 2f));
    }

    [Fact]
    public async Task The_composite_fans_out_skips_a_member_that_cannot_and_refuses_when_none_can()
    {
        var rig = Build();
        await RememberAsync(rig, "t", "s", 2);
        var other = new StaticEngine("static", []);

        var blend = new CompositeMemoryEngine("blend", [rig.Engine, other]);
        Assert.Equal(new MemoryReindexResult(2, 0), await blend.ReindexAsync("t"));

        var none = new CompositeMemoryEngine("none", [other]);
        await Assert.ThrowsAsync<NotSupportedException>(() => none.ReindexAsync("t"));
    }

    [Fact]
    public void ReindexBatchSize_below_one_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { ReindexBatchSize = 0 });

    private static (TaskCompletionSource Parked, TaskCompletionSource Release) Park(SwitchableVectorProvider provider, int call)
    {
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.OnCall = async (n, _) =>
        {
            if (n != call) return;
            parked.SetResult();
            await release.Task;
        };
        return (parked, release);
    }

    private static async Task<IReadOnlyList<string>> Snapshot(Rig rig) =>
        [.. (await rig.Store.SeedAsync(Name, "t", null, null, int.MaxValue))
            .OrderBy(n => n.Id)
            .Select(n => $"{n.Id}|{n.Content}|{n.RecallCount}|{n.Stability}|{n.Difficulty}|{n.OrdinalAge}|{n.VolumeAge}"
                + $"|{n.Degree}|{n.Signals}|{n.ProvenanceRetrievability}|{n.ProvenanceSalience}")];

    private static async Task<IReadOnlyList<string>> Edges(Rig rig, IReadOnlyList<long> ids) =>
        [.. (await rig.Store.NeighboursAsync(Name, "t", ids, 100))
            .Select(n => $"{n.Node.Id}|{n.EdgeWeight}|{n.EdgeOrdinalAge}|{n.EdgeVolumeAge}")
            .Order(StringComparer.Ordinal)];

    /// <summary>A vector "model" a test switches: the vector encodes the model number, so which model wrote a
    /// stored vector is readable from it.</summary>
    private sealed class SwitchableVectorProvider : FakeVectorProviderBase
    {
        private int _calls;

        public int Model { get; set; } = 1;

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Runs before each call with its 1-based number; may park or throw.</summary>
        public Func<int, CancellationToken, Task>? OnCall { get; set; }

        public float[] Vector(string text) => [Model, text.Length, 1f];

        public override async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (OnCall is { } hook) await hook(call, ct);
            return [.. texts.Select(Vector)];
        }
    }

    /// <summary>An in-memory index whose next upsert parks until released — a write step held open.</summary>
    private sealed class ParkingVectorStore : IReadableVectorStore
    {
        private readonly InMemoryVectorStore _inner = new();
        private int _parkNext;

        public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ParkNextUpsert() => Volatile.Write(ref _parkNext, 1);

        public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _parkNext, 0) == 1)
            {
                Parked.SetResult();
                await Release.Task;
            }
            await _inner.UpsertAsync(collection, id, vector, payload, ct);
        }

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            _inner.SearchAsync(collection, query, k, ct);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => _inner.DeleteAsync(collection, id, ct);

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
            _inner.RemoveCollectionAsync(collection, ct);

        public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
            CancellationToken ct = default) => _inner.GetAsync(collection, ids, ct);
    }
}
