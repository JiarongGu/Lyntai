using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="IPortableMemory"/> on the curated slice: an export names one task, an import names its
/// target and writes nowhere else, a re-import adds nothing, and a line this reader cannot honour refuses the whole
/// file before anything is written.</summary>
public class MemoryPortabilityTests
{
    private static readonly Dictionary<string, string> Owner = new() { ["source"] = "owner" };

    private static async Task SeedAsync(ICuratedMemoryStore store)
    {
        await store.AddAsync("memory", "the release gate is verify", taskKey: "t", scope: "a", metadata: Owner);
        await store.AddAsync("memory", "wrap prose at 110 columns", enabled: false, taskKey: "t", scope: "a");
        await store.AddAsync("memory", "scope b only", taskKey: "t", scope: "b");
        await store.AddAsync("memory", "another task", taskKey: "u", scope: "a");
        await store.AddAsync("memory", "applies everywhere");
        await store.AddAsync("style", "another section", taskKey: "t", scope: "a");
    }

    private static async Task<string> ExportAsync(IPortableMemory engine, string taskKey, string? scope)
    {
        using var writer = new StringWriter();
        await engine.ExportAsync(taskKey, scope, writer);
        return writer.ToString();
    }

    private static Task<MemoryImportResult> ImportAsync(IPortableMemory engine, string taskKey, string? scope,
        string jsonl) => engine.ImportAsync(taskKey, scope, new StringReader(jsonl));

    private static string[] Lines(string jsonl) =>
        jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task An_export_of_a_named_task_and_scope_holds_exactly_its_entries()
    {
        var store = new InMemoryCuratedMemoryStore();
        await SeedAsync(store);
        var engine = new CuratedMemoryEngine("notes", store);

        var lines = Lines(await ExportAsync(engine, "t", "a"));

        var contents = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("content").GetString())
            .Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["the release gate is verify", "wrap prose at 110 columns"], contents);
        Assert.All(lines, l => Assert.Equal(1, JsonDocument.Parse(l).RootElement.GetProperty("v").GetInt32()));
    }

    [Fact]
    public async Task A_null_scope_exports_every_scope_of_the_task_and_nothing_of_another()
    {
        var store = new InMemoryCuratedMemoryStore();
        await SeedAsync(store);

        var lines = Lines(await ExportAsync(new CuratedMemoryEngine("notes", store), "t", null));

        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("another task", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("applies everywhere", StringComparison.Ordinal));
    }

    /// <summary>No "everything" export: the task is a required argument with no default, and a blank one is
    /// refused rather than read as "no filter".</summary>
    [Fact]
    public async Task There_is_no_unscoped_export()
    {
        var export = typeof(IPortableMemory).GetMethod(nameof(IPortableMemory.ExportAsync))!;
        Assert.False(export.GetParameters()[0].HasDefaultValue);
        Assert.False(export.GetParameters()[1].HasDefaultValue);

        var engine = new CuratedMemoryEngine("notes", new InMemoryCuratedMemoryStore());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ExportAsync(engine, "", null));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ImportAsync(engine, " ", null, ""));
    }

    public static TheoryData<string> Targets => ["inmemory", "filesystem"];

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task A_round_trip_from_sqlite_keeps_content_kind_enabled_metadata_and_grade(string target)
    {
        using var db = new TempDb();
        using var root = new TempRoot();
        var source = new SqliteCuratedMemoryStore(db.Factory);
        await SeedAsync(source);
        ICuratedMemoryStore destination = target == "inmemory"
            ? new InMemoryCuratedMemoryStore()
            : new FileSystemCuratedMemoryStore(root.Root);
        static MemoryGrade BySource(CuratedMemory e) =>
            e.Metadata?.GetValueOrDefault("source") == "owner" ? MemoryGrade.Authoritative : MemoryGrade.Associative;

        var jsonl = await ExportAsync(new CuratedMemoryEngine("notes", source) { Grade = BySource }, "t", "a");
        var result = await ImportAsync(new CuratedMemoryEngine("notes", destination), "t", "a", jsonl);

        Assert.Equal(new MemoryImportResult(2, 0), result);
        var imported = await destination.ListAsync(taskKey: "t", scope: "a");
        Assert.Equal(2, imported.Count);
        var gate = imported.Single(e => e.Content == "the release gate is verify");
        Assert.Equal(("memory", true, "owner"), (gate.Kind, gate.Enabled, gate.Metadata!["source"]));
        var disabled = imported.Single(e => e.Content == "wrap prose at 110 columns");
        Assert.False(disabled.Enabled);
        Assert.Null(disabled.Metadata);

        // the grade is the source's reading, recorded for a reader; the target re-derives its own from metadata
        var grades = Lines(jsonl).Select(l => JsonDocument.Parse(l).RootElement)
            .ToDictionary(e => e.GetProperty("content").GetString()!, e => e.GetProperty("grade").GetString());
        Assert.Equal("Authoritative", grades["the release gate is verify"]);
        Assert.Equal("Associative", grades["wrap prose at 110 columns"]);

        // a revision is a concurrency token of the store that issued it: an import is a new entry at revision 1
        Assert.All(imported, e => Assert.Equal(1, e.Revision));
        Assert.Equal(CuratedUpdateOutcome.Updated, await ((IRevisionedCuratedMemoryStore)destination)
            .UpdateAsync(gate.Id, expectedRevision: 1, content: "edited"));
    }

    [Fact]
    public async Task Importing_twice_adds_nothing_and_never_overwrites_what_the_target_holds()
    {
        var source = new InMemoryCuratedMemoryStore();
        await SeedAsync(source);
        var jsonl = await ExportAsync(new CuratedMemoryEngine("notes", source), "t", "a");
        var target = new InMemoryCuratedMemoryStore();
        // the target already holds one of them, disabled and with its own metadata
        var held = await target.AddAsync("memory", "the release gate is verify", enabled: false, taskKey: "t",
            scope: "a", metadata: new Dictionary<string, string> { ["source"] = "target" });
        var engine = new CuratedMemoryEngine("notes", target);

        Assert.Equal(new MemoryImportResult(1, 1), await ImportAsync(engine, "t", "a", jsonl));
        Assert.Equal(new MemoryImportResult(0, 2), await ImportAsync(engine, "t", "a", jsonl));

        Assert.Equal(2, (await target.ListAsync(taskKey: "t", scope: "a")).Count);
        var kept = (await target.GetAsync(held))!;
        Assert.False(kept.Enabled);
        Assert.Equal("target", kept.Metadata!["source"]);
    }

    [Fact]
    public async Task A_named_scope_receives_every_line_and_nothing_lands_outside_the_named_task()
    {
        var source = new InMemoryCuratedMemoryStore();
        await SeedAsync(source);
        var jsonl = await ExportAsync(new CuratedMemoryEngine("notes", source), "t", null);
        var target = new InMemoryCuratedMemoryStore();
        var engine = new CuratedMemoryEngine("notes", target);

        await ImportAsync(engine, "x", "only", jsonl);

        var all = await target.ListAsync();
        Assert.Equal(3, all.Count);
        Assert.All(all, e => Assert.Equal(("x", "only"), (e.TaskKey, e.Scope)));
    }

    [Fact]
    public async Task A_null_target_scope_keeps_each_lines_own_scope_inside_the_named_task()
    {
        var source = new InMemoryCuratedMemoryStore();
        await SeedAsync(source);
        var jsonl = await ExportAsync(new CuratedMemoryEngine("notes", source), "t", null);
        var target = new InMemoryCuratedMemoryStore();

        await ImportAsync(new CuratedMemoryEngine("notes", target), "x", null, jsonl);

        var all = await target.ListAsync();
        Assert.All(all, e => Assert.Equal("x", e.TaskKey));
        Assert.Equal(["a", "a", "b"], all.Select(e => e.Scope!).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "not json", "line 2" },
        { """{"v":2,"record":"curated","engine":"notes","kind":"memory","content":"c","enabled":true}""", "version 2" },
        { """{"record":"curated","engine":"notes","kind":"memory","content":"c","enabled":true}""", "line 2" },
        { """{"v":1,"record":"graph","engine":"notes","kind":"memory","content":"c","enabled":true}""", "graph" },
        { """{"v":1,"record":"curated","engine":"notes","kind":"style","content":"c","enabled":true}""", "style" },
        { """{"v":1,"record":"curated","engine":"notes","kind":"memory","enabled":true}""", "content" },
    };

    /// <summary>The whole file is read and checked before the first write, so a refused line leaves the target
    /// exactly as it was — a half-imported file is a state nobody can name.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_line_this_reader_cannot_honour_refuses_the_whole_file_and_writes_nothing(
        string bad, string named)
    {
        const string good = """{"v":1,"record":"curated","engine":"notes","kind":"memory","content":"fine","enabled":true}""";
        var target = new InMemoryCuratedMemoryStore();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ImportAsync(new CuratedMemoryEngine("notes", target), "t", null, good + "\n" + bad + "\n"));

        Assert.Contains(named, ex.Message, StringComparison.Ordinal);
        Assert.Empty(await target.ListAsync());
    }

    [Fact]
    public async Task An_every_section_engine_exports_and_imports_each_lines_own_kind()
    {
        var source = new InMemoryCuratedMemoryStore();
        await SeedAsync(source);
        var jsonl = await ExportAsync(new CuratedMemoryEngine("all", source, kind: null), "t", "a");
        var target = new InMemoryCuratedMemoryStore();

        await ImportAsync(new CuratedMemoryEngine("all", target, kind: null), "t", "a", jsonl);

        Assert.Equal(["memory", "memory", "style"],
            (await target.ListAsync()).Select(e => e.Kind).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_blend_exports_its_capable_members_names_the_rest_and_routes_an_import_by_member()
    {
        var source = new InMemoryCuratedMemoryStore();
        await SeedAsync(source);
        var blend = new CompositeMemoryEngine("p", [
            new CuratedMemoryEngine("p/notes", source),
            new CuratedMemoryEngine("p/style", source, "style"),
            new RecordingEngine("p/graph", MemoryGrades.Associative)]);

        using var writer = new StringWriter();
        var exported = await blend.ExportAsync("t", "a", writer);

        Assert.Equal(3, exported.Exported);
        Assert.Equal(["p/graph"], exported.Skipped);

        var target = new InMemoryCuratedMemoryStore();
        var into = new CompositeMemoryEngine("p", [
            new CuratedMemoryEngine("p/notes", target),
            new CuratedMemoryEngine("p/style", target, "style")]);
        Assert.Equal(new MemoryImportResult(3, 0), await into.ImportAsync("t", "a", new StringReader(writer.ToString())));
        Assert.Single(await target.ListAsync(kind: "style"));

        var stranger = new CompositeMemoryEngine("q", [new CuratedMemoryEngine("q/notes", target)]);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            stranger.ImportAsync("t", "a", new StringReader(writer.ToString())));
        Assert.Contains("p/notes", ex.Message, StringComparison.Ordinal);
    }
}
