using System.Globalization;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="IPortableMemory"/> on a GRAPH engine: an export carries each entry's decay state, its links and
/// its subjects; an import lands them under ids the target allocates, each age as far behind the target's head as it
/// was behind the source's, so what was buried stays buried and a recall over the imported task ranks as the source's
/// did. A second import adds nothing, and an entry the target already holds is never refreshed.</summary>
public class GraphMemoryPortabilityTests
{
    private const string Name = "project/graph";

    // per-write ageing, so a burst of test writes ages entries as much as the same writes spread out would
    private static GraphMemoryEngine Engine(IMemoryGraphStore store, GraphMemorySeams? seams = null) =>
        new(Name, store, null, seams ?? new GraphMemorySeams { AgePolicies = [new PerWriteAgePolicy()] });

    private static MemoryRef Ref(MemoryWriteResult written) => written.Reference;

    private static long Id(MemoryWriteResult written) =>
        long.Parse(written.Reference.Id, CultureInfo.InvariantCulture);

    /// <summary>Six associative facts and one exact one under task <c>t</c>, recalled (so reinforced and co-activated),
    /// explicitly linked, annotated with subjects, then crowded by thirty unrelated writes in another scope so the
    /// oldest are buried.</summary>
    private static async Task<GraphMemoryEngine> SeedAsync(IMemoryGraphStore store)
    {
        var engine = Engine(store);
        var written = new List<MemoryWriteResult>();
        for (var i = 0; i < 6; i++)
            written.Add(await engine.RememberAsync(new MemoryWrite("t", "s", $"deploy fact number {i} about the pipeline")));
        written.Add(await engine.RememberAsync(new MemoryWrite("t", "s", "the production database is db-prod-2",
            Grade: MemoryGrade.Authoritative, Metadata: new Dictionary<string, string> { ["source"] = "owner" })));
        await engine.RecallAsync(new MemoryQuery("t", "s", "deploy fact number 4", Limit: 3));
        await engine.LinkAsync(Ref(written[0]), Ref(written[5]), "about", 2, symmetric: true);
        await store.RecordSubjectsAsync(Name, Id(written[1]), ["pipeline", "deploy"]);
        await store.RecordSubjectsAsync(Name, Id(written[6]), ["database"]);
        for (var i = 0; i < 30; i++)
            await engine.RememberAsync(new MemoryWrite("t", "noise", $"unrelated note {i}"));
        await engine.RecallAsync(new MemoryQuery("t", "s", "deploy fact number 5", Limit: 2));
        return engine;
    }

    private static async Task<string> ExportAsync(IPortableMemory engine, string taskKey, string? scope)
    {
        using var writer = new StringWriter();
        await engine.ExportAsync(taskKey, scope, writer);
        return writer.ToString();
    }

    private static Task<MemoryImportResult> ImportAsync(IPortableMemory engine, string taskKey, string? scope,
        string jsonl) => engine.ImportAsync(taskKey, scope, new StringReader(jsonl));

    private static string[] Lines(string jsonl) => jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // what an entry IS, independent of the id a store gave it and of where that store's clock stands
    private static async Task<List<string>> StateAsync(IMemoryGraphStore store, string taskKey, string? scope)
    {
        var export = await ((IPortableMemoryGraphStore)store).ExportAsync(Name, taskKey, scope);
        var content = export.Nodes.ToDictionary(n => n.Id, n => n.Content);
        return
        [
            .. export.Nodes.Select(n => string.Join('|', n.Scope, n.Headline, n.Content, n.Grade, n.RecallCount,
                n.Stability.ToString("R", CultureInfo.InvariantCulture), n.Difficulty.ToString("R", CultureInfo.InvariantCulture),
                n.Age.ToString("R", CultureInfo.InvariantCulture), n.OrdinalAge.ToString("R", CultureInfo.InvariantCulture),
                n.VolumeAge.ToString("R", CultureInfo.InvariantCulture), n.ProvenanceRetrievability, n.ProvenanceSalience,
                string.Join(',', n.Signals.Values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value:R}")),
                n.Metadata?.GetValueOrDefault("source"), n.Degree, n.Strength.ToString("R", CultureInfo.InvariantCulture),
                string.Join(',', (export.Subjects.GetValueOrDefault(n.Id) ?? []).Order(StringComparer.Ordinal)))),
            .. export.Links.Select(l => string.Join('|', "link", content[l.From], content[l.To], l.Kind,
                l.Weight.ToString("R", CultureInfo.InvariantCulture), l.Age.ToString("R", CultureInfo.InvariantCulture),
                l.OrdinalAge.ToString("R", CultureInfo.InvariantCulture))),
        ];
    }

    public static TheoryData<string> Backends => ["inmemory", "filesystem", "sqlite"];

    private static IMemoryGraphStore Store(string backend, TempDb db, TempRoot root) => backend switch
    {
        "inmemory" => new InMemoryMemoryGraphStore(),
        "filesystem" => new Lyntai.Storage.FileSystem.FileSystemMemoryGraphStore(root.Root),
        _ => new SqliteMemoryGraphStore(db.Factory),
    };

    /// <summary>Into the SAME backend, so the recall can be compared too: which entries a seed finds is portable, but
    /// the order among matches is each backend's own (SQLite ranks by bm25).</summary>
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_round_trip_keeps_decay_state_links_subjects_and_burial_and_recalls_alike(string backend)
    {
        using var db = new TempDb();
        using var targetDb = new TempDb();
        using var root = new TempRoot();
        using var targetRoot = new TempRoot();
        var sourceStore = Store(backend, db, root);
        var source = await SeedAsync(sourceStore);
        var targetStore = Store(backend, targetDb, targetRoot);
        var into = Engine(targetStore);

        var jsonl = await ExportAsync(source, "t", "s");
        var exported = Lines(jsonl).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(7, exported.Count(l => l.GetProperty("record").GetString() == "graph"));
        var links = exported.Count(l => l.GetProperty("record").GetString() == "link");
        Assert.True(links >= 2, $"the seed should hold the explicit symmetric link at least; it held {links}");

        var result = await ImportAsync(into, "t", "s", jsonl);

        Assert.Equal(new MemoryImportResult(7, 0) { Links = links }, result);
        Assert.Equal(await StateAsync(sourceStore, "t", "s"), await StateAsync(targetStore, "t", "s"));

        // burial is a judgement of the curve over the carried state, so it carries too
        var sourceCounts = await source.CountAsync("t", "s", minRetrievability: 0.5);
        Assert.InRange(sourceCounts!.Buried!.Value, 1, 5);
        Assert.Equal(sourceCounts, await into.CountAsync("t", "s", minRetrievability: 0.5));

        // and a recall over the imported task ranks as the source's does
        static IEnumerable<(string, MemoryGrade, double)> Page(MemoryRecall r) =>
            r.Items.Select(i => (i.Headline, i.Grade, Math.Round(i.Retrievability, 9)));
        var query = new MemoryQuery("t", "s", "pipeline deploy fact", Limit: 5);
        Assert.Equal(Page(await source.RecallAsync(query)), Page(await into.RecallAsync(query)));
    }

    /// <summary>Across backends the carried state is identical too — a SQLite memory moved into another kind of store.</summary>
    [Theory]
    [InlineData("inmemory")]
    [InlineData("filesystem")]
    public async Task A_memory_moves_between_backends_with_its_state(string backend)
    {
        using var db = new TempDb();
        using var root = new TempRoot();
        var sourceStore = new SqliteMemoryGraphStore(db.Factory);
        var source = await SeedAsync(sourceStore);
        var targetStore = Store(backend, db, root);
        var into = Engine(targetStore);

        await ImportAsync(into, "t", "s", await ExportAsync(source, "t", "s"));

        Assert.Equal(await StateAsync(sourceStore, "t", "s"), await StateAsync(targetStore, "t", "s"));
        Assert.Equal(await source.CountAsync("t", "s", minRetrievability: 0.5),
            await into.CountAsync("t", "s", minRetrievability: 0.5));
    }

    [Fact]
    public async Task Importing_twice_adds_nothing_and_never_refreshes_an_entry_the_target_holds()
    {
        var source = await SeedAsync(new InMemoryMemoryGraphStore());
        var jsonl = await ExportAsync(source, "t", "s");
        var targetStore = new InMemoryMemoryGraphStore();
        var into = Engine(targetStore);
        // the target holds one of them already, written fresh: recall count 0, its own stability, its own age
        var held = Id(await into.RememberAsync(new MemoryWrite("t", "s", "deploy fact number 0 about the pipeline")));
        await into.RememberAsync(new MemoryWrite("t", "other", "something of the target's own"));
        var before = (await targetStore.GetAsync(Name, held))!;

        var first = await ImportAsync(into, "t", "s", jsonl);
        var state = await StateAsync(targetStore, "t", null);
        var second = await ImportAsync(into, "t", "s", jsonl);

        Assert.Equal((6, 1), (first.Imported, first.AlreadyPresent));
        Assert.Equal(new MemoryImportResult(0, 7), second);
        Assert.Equal(state, await StateAsync(targetStore, "t", null));
        var after = (await targetStore.GetAsync(Name, held))!;
        Assert.Equal((before.RecallCount, before.Stability, before.Age, before.OrdinalAge, before.Headline),
            (after.RecallCount, after.Stability, after.Age, after.OrdinalAge, after.Headline));
    }

    [Fact]
    public async Task A_link_whose_endpoint_is_not_in_the_file_is_dropped_and_counted()
    {
        var store = new InMemoryMemoryGraphStore();
        var source = Engine(store);
        var a = await source.RememberAsync(new MemoryWrite("t", "a", "kept in scope a"));
        var b = await source.RememberAsync(new MemoryWrite("t", "b", "left behind in scope b"));
        var a2 = await source.RememberAsync(new MemoryWrite("t", "a", "also in scope a"));
        await source.LinkAsync(Ref(a), Ref(b), symmetric: true);
        await source.LinkAsync(Ref(a), Ref(a2));

        var jsonl = await ExportAsync(source, "t", "a");
        var into = Engine(new InMemoryMemoryGraphStore());
        var result = await ImportAsync(into, "t", null, jsonl);

        Assert.Equal(new MemoryImportResult(2, 0) { Links = 1, LinksDropped = 1 }, result);
    }

    [Fact]
    public async Task An_import_lands_under_the_task_it_names_and_a_named_scope_takes_every_line()
    {
        var source = await SeedAsync(new InMemoryMemoryGraphStore());
        var jsonl = await ExportAsync(source, "t", null);
        var targetStore = new InMemoryMemoryGraphStore();

        var result = await ImportAsync(Engine(targetStore), "x", "only", jsonl);

        Assert.Equal(37, result.Imported);
        Assert.Equal(37, (await targetStore.CountAsync(Name, "x", "only")).Entries);
        Assert.Equal(0, (await targetStore.CountAsync(Name, "t", null)).Entries);
    }

    [Fact]
    public async Task An_engine_with_a_vector_index_indexes_what_it_imports()
    {
        var source = await SeedAsync(new InMemoryMemoryGraphStore());
        var jsonl = await ExportAsync(source, "t", "s");
        var vectors = new InMemoryVectorStore();
        var targetStore = new InMemoryMemoryGraphStore();
        var into = Engine(targetStore, new GraphMemorySeams
        {
            AgePolicies = [new PerWriteAgePolicy()],
            Providers = [new LengthVectors()],
            Vectors = vectors,
        });

        await ImportAsync(into, "t", "s", jsonl);

        var nodes = await targetStore.SeedAsync(Name, "t", "s", null, int.MaxValue);
        var indexed = await ((IReadableVectorStore)vectors).GetAsync(MemoryVectorCollection.For(Name, "t", "s"),
            [.. nodes.Select(n => n.Id.ToString(CultureInfo.InvariantCulture))]);
        Assert.Equal(7, indexed.Count);
    }

    /// <summary>A graph store without <see cref="IPortableMemoryGraphStore"/> cannot carry decay state, so the engine
    /// refuses rather than export an entry stripped of it — and a blend names that member in Skipped.</summary>
    [Fact]
    public async Task Over_a_store_that_cannot_carry_state_the_engine_refuses_and_a_blend_names_it()
    {
        var engine = Engine(new DelegatingGraphStore());
        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact"));

        await Assert.ThrowsAsync<NotSupportedException>(() => ExportAsync(engine, "t", null));
        await Assert.ThrowsAsync<NotSupportedException>(() => ImportAsync(engine, "t", null, ""));

        var curated = new InMemoryCuratedMemoryStore();
        await curated.AddAsync("memory", "a curated fact", taskKey: "t");
        var blend = new CompositeMemoryEngine("p", [new CuratedMemoryEngine("p/notes", curated), engine]);
        using var writer = new StringWriter();
        var exported = await blend.ExportAsync("t", null, writer);
        Assert.Equal(1, exported.Exported);
        Assert.Equal([Name], exported.Skipped);
    }

    [Fact]
    public async Task A_blend_exports_and_imports_its_graph_member_beside_a_curated_one()
    {
        var curated = new InMemoryCuratedMemoryStore();
        await curated.AddAsync("memory", "a curated fact", taskKey: "t", scope: "s");
        var graphStore = new InMemoryMemoryGraphStore();
        var graph = await SeedAsync(graphStore);
        var blend = new CompositeMemoryEngine("p", [new CuratedMemoryEngine("p/notes", curated), graph]);
        using var writer = new StringWriter();
        var exported = await blend.ExportAsync("t", "s", writer);

        var into = new CompositeMemoryEngine("p", [
            new CuratedMemoryEngine("p/notes", new InMemoryCuratedMemoryStore()), Engine(new InMemoryMemoryGraphStore())]);
        var result = await into.ImportAsync("t", "s", new StringReader(writer.ToString()));

        Assert.Equal(8, exported.Exported);
        Assert.True(exported.Links > 0);
        Assert.Equal((8, 0, exported.Links), (result.Imported, result.AlreadyPresent, result.Links));
    }

    private const string Good = """{"v":1,"record":"graph","engine":"g","id":1,"scope":"s","headline":"h","content":"c","grade":"Associative","createdAt":"2026-10-24T00:00:00+00:00","recallCount":0,"stability":5,"difficulty":5,"age":1,"ordinalAge":1,"volumeAge":1,"elapsedAge":0}""";

    // a second, distinct entry, so a refusal names the field it is about rather than the repeated id
    private static readonly string Second = Good.Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal);

    public static TheoryData<string, string> Refused => new()
    {
        { Second.Replace("\"stability\":5", "\"stability\":\"x\"", StringComparison.Ordinal), "stability" },
        { Second.Replace("\"grade\":\"Associative\"", "\"grade\":\"Inherit\"", StringComparison.Ordinal), "grade" },
        { Second.Replace("\"age\":1", "\"age\":null", StringComparison.Ordinal), "age" },
        { Good.Replace("\"content\":\"c\"", "\"content\":\"d\"", StringComparison.Ordinal), "id" },
        { """{"v":1,"record":"link","engine":"g","from":1,"to":1,"kind":"","weight":"heavy","age":0,"ordinalAge":0,"volumeAge":0,"elapsedAge":0}""", "weight" },
        { """{"v":1,"record":"curated","engine":"g","kind":"memory","content":"c","enabled":true}""", "curated" },
    };

    /// <summary>The whole file is checked before the first write, exactly as a curated import is.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_line_this_reader_cannot_honour_refuses_the_whole_file_and_writes_nothing(string bad, string named)
    {
        var store = new InMemoryMemoryGraphStore();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ImportAsync(Engine(store), "t", null, Good + "\n" + bad + "\n"));

        Assert.Contains(named, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await store.CountAsync(Name, "t", null)).Entries);
    }

    private sealed class LengthVectors : FakeVectorProviderBase
    {
        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(t => new float[] { t.Length, 1f, 0.5f })]);
    }
}
