using System.Globalization;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Valid time travels.</b> An export carries each entry's interval and the end a successor recorded, an import
/// restores both under the ids the target allocates, and a store that cannot hold them REFUSES the file rather than
/// import it as open-ended. A file with no valid time in it is byte-for-byte what it was before valid time existed.
/// </summary>
public class MemoryValidityPortabilityTests
{
    private const string Name = "project/graph";

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Backends => ["inmemory", "filesystem", "sqlite"];

    private static IMemoryGraphStore Store(string backend, TempDb db, TempRoot root) => backend switch
    {
        "inmemory" => new InMemoryMemoryGraphStore(),
        "filesystem" => new Lyntai.Storage.FileSystem.FileSystemMemoryGraphStore(root.Root),
        _ => new SqliteMemoryGraphStore(db.Factory),
    };

    private static GraphMemoryEngine Engine(IMemoryGraphStore store) =>
        new(Name, store, null, new GraphMemorySeams
        {
            AgePolicies = [new PerWriteAgePolicy()],
            Clock = () => new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        });

    private static MemoryWrite Fact(string scope, string content, DateTimeOffset? from = null, DateTimeOffset? to = null,
        params MemoryRef[] supersedes) =>
        new("t", scope, content) { ValidFrom = from, ValidTo = to, Supersedes = supersedes.Length == 0 ? null : supersedes };

    /// <summary>Paris from January superseded by Berlin from March; Lyon, February to March, written last; and one
    /// untimed fact.</summary>
    private static async Task SeedAsync(GraphMemoryEngine engine)
    {
        var paris = (await engine.RememberAsync(Fact("s", "lives in Paris", from: Jan))).Reference;
        await engine.RememberAsync(Fact("s", "lives in Berlin", from: Mar, supersedes: paris));
        await engine.RememberAsync(Fact("s", "lives in Lyon", from: Feb, to: Mar));
        await engine.RememberAsync(new MemoryWrite("t", "s", "owns a bicycle"));
    }

    private static async Task<string> ExportAsync(IPortableMemory engine, string? scope = "s")
    {
        using var writer = new StringWriter();
        await engine.ExportAsync("t", scope, writer);
        return writer.ToString();
    }

    private static List<JsonElement> Lines(string jsonl) =>
        [.. jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)];

    // every entry's content mapped to its interval, so the comparison is independent of the ids a store allocated
    private static async Task<Dictionary<string, (DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? At, string? By)>>
        IntervalsAsync(IMemoryGraphStore store, string? scope = "s")
    {
        var nodes = await store.SeedAsync(Name, "t", scope, null, 100);
        var content = nodes.ToDictionary(n => n.Id, n => n.Content);
        return nodes.ToDictionary(n => n.Content,
            n => (n.ValidFrom, n.ValidTo, n.SupersededAt, n.SupersededBy is { } by ? content.GetValueOrDefault(by) : null));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_round_trip_keeps_every_interval_and_every_successor_and_reads_alike(string backend)
    {
        using var db = new TempDb();
        using var targetDb = new TempDb();
        using var root = new TempRoot();
        using var targetRoot = new TempRoot();
        var sourceStore = Store(backend, db, root);
        var source = Engine(sourceStore);
        await SeedAsync(source);
        var targetStore = Store(backend, targetDb, targetRoot);
        var into = Engine(targetStore);

        var result = await into.ImportAsync("t", "s", new StringReader(await ExportAsync(source)));

        Assert.Equal(4, result.Imported);
        var expected = await IntervalsAsync(sourceStore);
        Assert.Equal(expected, await IntervalsAsync(targetStore));
        Assert.Equal("lives in Berlin", expected["lives in Paris"].By);   // the check above is not vacuous

        foreach (var reading in new[]
                 {
                     MemoryValidityReading.Current, MemoryValidityReading.History, MemoryValidityReading.AsOf(Jan),
                     MemoryValidityReading.AsOf(Feb.AddDays(10)), MemoryValidityReading.AsOf(Mar),
                 })
        {
            var query = new MemoryQuery("t", "s", "lives bicycle", Limit: 10, Detail: MemoryDetail.Full) { Validity = reading };
            Assert.Equal(
                (await source.RecallAsync(query)).Items.Select(i => i.Content).Order(),
                (await into.RecallAsync(query)).Items.Select(i => i.Content).Order());
        }
    }

    [Fact]
    public async Task Only_a_line_that_carries_valid_time_is_written_at_the_newer_format_version()
    {
        var engine = Engine(new InMemoryMemoryGraphStore());
        await SeedAsync(engine);

        var lines = Lines(await ExportAsync(engine));

        var byContent = lines.Where(l => l.GetProperty("record").GetString() == "graph")
            .ToDictionary(l => l.GetProperty("content").GetString()!);
        // a reader that predates valid time refuses `v: 2` outright, which is what stops it importing an ended fact
        // as an open one; an untimed line stays at `v: 1`, readable by every reader there has been
        Assert.Equal(2, byContent["lives in Paris"].GetProperty("v").GetInt32());
        Assert.Equal(2, byContent["lives in Berlin"].GetProperty("v").GetInt32());
        Assert.Equal(2, byContent["lives in Lyon"].GetProperty("v").GetInt32());
        Assert.Equal(1, byContent["owns a bicycle"].GetProperty("v").GetInt32());
        Assert.Equal(Jan, byContent["lives in Paris"].GetProperty("validFrom").GetDateTimeOffset());
        Assert.Equal(Mar, byContent["lives in Paris"].GetProperty("supersededAt").GetDateTimeOffset());
        Assert.False(byContent["lives in Paris"].TryGetProperty("validTo", out _));
        Assert.Equal(Mar, byContent["lives in Lyon"].GetProperty("validTo").GetDateTimeOffset());
        var berlinId = byContent["lives in Berlin"].GetProperty("id").GetInt64();
        Assert.Equal(berlinId, byContent["lives in Paris"].GetProperty("supersededBy").GetInt64());
        Assert.All(lines.Where(l => l.GetProperty("record").GetString() == "link"), l => Assert.Equal(1, l.GetProperty("v").GetInt32()));
    }

    [Fact]
    public async Task A_file_with_no_valid_time_in_it_is_exactly_what_it_was_before()
    {
        var engine = Engine(new InMemoryMemoryGraphStore());
        await engine.RememberAsync(new MemoryWrite("t", "s", "lives in Paris"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "owns a bicycle"));

        var lines = Lines(await ExportAsync(engine));

        Assert.All(lines, l => Assert.Equal(1, l.GetProperty("v").GetInt32()));
        string[] added = ["validFrom", "validTo", "supersededAt", "supersededBy"];
        Assert.All(lines, l => Assert.All(added, name => Assert.False(l.TryGetProperty(name, out _), name)));
    }

    [Fact]
    public async Task A_successor_the_file_does_not_hold_leaves_the_entry_ended_with_none_named()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Engine(store);
        var paris = (await engine.RememberAsync(Fact("s", "lives in Paris", from: Jan))).Reference;
        await engine.RememberAsync(Fact("elsewhere", "lives in Berlin", from: Mar, supersedes: paris));
        var targetStore = new InMemoryMemoryGraphStore();
        var into = Engine(targetStore);

        // scope `s` only: the entry that superseded Paris lives in another scope and is not in the file
        await into.ImportAsync("t", "s", new StringReader(await ExportAsync(engine, "s")));

        var held = Assert.Single(await IntervalsAsync(targetStore)).Value;
        Assert.Equal(Jan, held.From);
        Assert.Null(held.To);
        Assert.Equal(Mar, held.At);
        Assert.Null(held.By);
        Assert.Empty((await into.RecallAsync(new MemoryQuery("t", "s", "lives"))).Items);   // still ended: nothing reopens it
    }

    [Fact]
    public async Task Importing_twice_adds_nothing_and_never_changes_what_the_target_already_holds()
    {
        var source = Engine(new InMemoryMemoryGraphStore());
        await SeedAsync(source);
        var targetStore = new InMemoryMemoryGraphStore();
        var into = Engine(targetStore);
        // the target already holds Paris, with a different claim of its own
        await into.RememberAsync(Fact("s", "lives in Paris", from: Feb, to: Jun));
        var jsonl = await ExportAsync(source);

        var first = await into.ImportAsync("t", "s", new StringReader(jsonl));
        var second = await into.ImportAsync("t", "s", new StringReader(jsonl));

        Assert.Equal((3, 1), (first.Imported, first.AlreadyPresent));
        Assert.Equal((0, 4), (second.Imported, second.AlreadyPresent));
        var intervals = await IntervalsAsync(targetStore);
        var paris = intervals["lives in Paris"];
        Assert.Equal(Feb, paris.From);
        Assert.Equal(Jun, paris.To);
        Assert.Null(paris.At);   // the file's end for Paris was not applied: a held entry is left exactly as it is
        Assert.Equal(Mar, intervals["lives in Berlin"].From);
    }

    [Fact]
    public async Task A_store_that_can_carry_but_not_hold_valid_time_refuses_the_file_and_imports_nothing()
    {
        var source = Engine(new InMemoryMemoryGraphStore());
        await SeedAsync(source);
        var plain = new PortableOnlyStore();
        var into = Engine(plain);
        var jsonl = await ExportAsync(source);

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() =>
            into.ImportAsync("t", "s", new StringReader(jsonl)));

        Assert.Contains(nameof(ITemporalMemoryGraphStore), refused.Message, StringComparison.Ordinal);
        Assert.Empty(await plain.SeedAsync(Name, "t", null, null, 10));   // all-or-nothing: not even the untimed entry
        // and an UNTIMED file still imports into it, as it always did
        var untimed = Engine(new InMemoryMemoryGraphStore());
        await untimed.RememberAsync(new MemoryWrite("t", "s", "owns a bicycle"));
        Assert.Equal(1, (await into.ImportAsync("t", "s", new StringReader(await ExportAsync(untimed)))).Imported);
    }

    [Fact]
    public async Task A_blend_exports_and_imports_a_member_valid_time()
    {
        var sourceGraph = Engine(new InMemoryMemoryGraphStore());
        await SeedAsync(sourceGraph);
        var source = new CompositeMemoryEngine("p", [sourceGraph]);
        var targetStore = new InMemoryMemoryGraphStore();
        var target = new CompositeMemoryEngine("p", [Engine(targetStore)]);

        var result = await target.ImportAsync("t", "s", new StringReader(await ExportAsync(source)));

        Assert.Equal(4, result.Imported);
        Assert.Equal("lives in Berlin", (await IntervalsAsync(targetStore))["lives in Paris"].By);
    }

    [Fact]
    public async Task An_import_cut_short_before_it_names_a_successor_leaves_the_entry_ended_never_open()
    {
        var source = Engine(new InMemoryMemoryGraphStore());
        await SeedAsync(source);
        var cut = new CutShortStore();
        var into = Engine(cut);
        var jsonl = await ExportAsync(source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => into.ImportAsync("t", "s", new StringReader(jsonl)));

        var paris = (await cut.SeedAsync(Name, "t", "s", "Paris", 10)).Single();
        Assert.Equal(Mar, paris.SupersededAt);   // its end landed with the insert
        Assert.Null(paris.SupersededBy);          // naming the successor is the step that was cut short
        Assert.Empty((await into.RecallAsync(new MemoryQuery("t", "s", "Paris"))).Items);
    }

    /// <summary>Stores and carries valid time like the in-process store, but cannot name a successor: the step an
    /// import takes last.</summary>
    private sealed class CutShortStore : DelegatingGraphStore, IPortableMemoryGraphStore, ITemporalMemoryGraphStore
    {
        private IPortableMemoryGraphStore Portable => (IPortableMemoryGraphStore)Inner;

        public Task<MemoryGraphExport> ExportAsync(string engine, string taskKey, string? scope, CancellationToken ct = default) =>
            Portable.ExportAsync(engine, taskKey, scope, ct);

        public Task<IReadOnlyList<GraphNodeImportResult>> ImportNodesAsync(string engine, string taskKey,
            IReadOnlyList<GraphNodeImport> nodes, CancellationToken ct = default) =>
            Portable.ImportNodesAsync(engine, taskKey, nodes, ct);

        public Task<int> ImportLinksAsync(string engine, IReadOnlyList<GraphLink> links, CancellationToken ct = default) =>
            Portable.ImportLinksAsync(engine, links, ct);

        public Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("the store went away before the successor was named");
    }

    /// <summary>A store that carries decay state and holds no valid time — what a BYO store written before valid time
    /// existed is: it forwards every member and is not <see cref="ITemporalMemoryGraphStore"/>.</summary>
    private sealed class PortableOnlyStore : DelegatingGraphStore, IPortableMemoryGraphStore
    {
        private IPortableMemoryGraphStore Portable => (IPortableMemoryGraphStore)Inner;

        public Task<MemoryGraphExport> ExportAsync(string engine, string taskKey, string? scope, CancellationToken ct = default) =>
            Portable.ExportAsync(engine, taskKey, scope, ct);

        public Task<IReadOnlyList<GraphNodeImportResult>> ImportNodesAsync(string engine, string taskKey,
            IReadOnlyList<GraphNodeImport> nodes, CancellationToken ct = default) =>
            Portable.ImportNodesAsync(engine, taskKey, nodes, ct);

        public Task<int> ImportLinksAsync(string engine, IReadOnlyList<GraphLink> links, CancellationToken ct = default) =>
            Portable.ImportLinksAsync(engine, links, ct);
    }
}
