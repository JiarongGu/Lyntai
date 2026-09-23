using System.Globalization;
using System.Text;
using Lyntai.Memory;
using Lyntai.Storage.FileSystem;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>What the contract cannot see, because it runs one live store: the graph survives the process, and a
/// file a person or a crash damaged is handled as D171 and D174 say. Every fact writes, then reads back what the
/// next start finds — through a NEW store over the released root, or, for a recall, the files themselves.</summary>
public class FileSystemGraphRestartTests : IDisposable
{
    private const string Broken = "a person's half-finished edit";
    private readonly TempRoot _temp = new();
    private DateTimeOffset _now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _temp.Dispose();

    private FileSystemMemoryGraphStore Open(FileSystemRoot root, int floor = 4096) => new(root, () => _now, floor);

    private static GraphNodeWrite Write(string content, string scope = "s", MemoryGrade grade = MemoryGrade.Associative,
        string headline = "h", double advance = 1, MemorySignals signals = default,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new("e", "task", scope, headline, content, grade, InitialStability: 3, Advance: advance, Metadata: metadata,
            Signals: signals, ProvenanceRetrievability: 1, ProvenanceSalience: 2);

    private string[] MemoryFiles() => Directory.GetFiles(Path.Combine(_temp.Directory, "graph"), "*.md", SearchOption.AllDirectories);

    private string JournalPath => Directory.GetFiles(Path.Combine(_temp.Directory, "graph"), "journal.jsonl", SearchOption.AllDirectories).Single();

    /// <summary>Everything a reader can observe about the engine "e", as text — records hold dictionaries,
    /// which compare by reference, so they are rendered rather than compared.</summary>
    private static async Task<string> Observe(IMemoryGraphStore store)
    {
        var text = new StringBuilder();
        var seeded = await store.SeedAsync("e", "task", null, null, 1000);
        foreach (var n in seeded.OrderBy(n => n.Id))
        {
            text.AppendLine(Render(n));
            foreach (var x in await store.NeighboursAsync("e", "task", [n.Id], 1000))
                text.AppendLine(FormattableString.Invariant($"  -> {x.Node.Id} w={x.EdgeWeight:R} age={x.EdgeAge:R} o={x.EdgeOrdinalAge:R} v={x.EdgeVolumeAge:R} t={x.EdgeElapsedAge:R}"));
        }
        foreach (var r in await store.ReviewsAsync("e")) text.AppendLine(r.ToString());
        foreach (var s in await store.KnownSubjectsAsync("e", "task", null, 100))
            text.AppendLine($"{s}: {string.Join(",", await store.NodesBySubjectAsync("e", "task", null, s, 100))}");
        return text.ToString();
    }

    private static string Render(GraphNode n) =>
        (n with { Metadata = null, Signals = default, CreatedAt = default }).ToString()
        + " created=" + n.CreatedAt.ToString("O", CultureInfo.InvariantCulture)
        + " metadata=" + string.Join(",", (n.Metadata ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        + " signals=" + string.Join(",", n.Signals.Values.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value.ToString("R", CultureInfo.InvariantCulture)));

    /// <summary>A workload touching every kind of record: grades, metadata, signals, a refresh, links of every
    /// shape, a recall's write-back, subjects, reviews, and a removal.</summary>
    private async Task<long[]> Workload(IMemoryGraphStore store)
    {
        var a = await store.UpsertAsync(Write("the deploy key rotates monthly", grade: MemoryGrade.Authoritative,
            metadata: new Dictionary<string, string> { ["source"] = "chat" }));
        _now += TimeSpan.FromHours(1);
        var b = await store.UpsertAsync(Write("the spouse is called 爱丽丝", scope: "other", advance: 2.5,
            signals: MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 1.75)));
        var c = await store.UpsertAsync(Write("the client is northern logistics", headline: "client"));
        var d = await store.UpsertAsync(Write("a passing thought"));
        _now += TimeSpan.FromHours(1);
        await store.UpsertAsync(Write("the client is northern logistics", headline: "ignored") with { HeadlineStated = false });
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);
        await store.LinkAsync("e", b, c, "subject", 2, symmetric: false);
        await store.LinkManyAsync("e", [new GraphEdgeWrite(c, d), new GraphEdgeWrite(c, d, Weight: 0.5)]);
        await store.WriteBackAsync("e", new GraphWriteBack(
            [new GraphTouch(a, 7, 1, 4.5), new GraphTouch(c, 5), new GraphTouch(c, 6)],
            [new GraphEdgeWrite(a, c, Symmetric: true)],
            [new MemoryReviewWrite(a, Guid.NewGuid(), 1, 3, 5, 1, 0, 2.5, 7, 4.5, 1, true),
             new MemoryReviewWrite(c, Guid.NewGuid(), 2, 5, 5, 0, 0, null, 6, 5)],
            ReviewLogCap: 100));
        await store.RecordSubjectsAsync("e", a, ["Me", "vault"]);
        await store.RecordSubjectsAsync("e", c, ["me"]);
        await store.DeleteAsync("e", [d]);
        return [a, b, c, d];
    }

    [Fact]
    public async Task Everything_a_reader_can_observe_survives_a_restart()
    {
        var before = Open(_temp.Root);
        await Workload(before);
        var expected = await Observe(before);

        Assert.Equal(expected, await Observe(Open(_temp.Reopen())));
    }

    [Fact]
    public async Task Everything_survives_a_restart_across_compactions_and_the_journal_stays_bounded()
    {
        var before = Open(_temp.Root, floor: 0);
        await Workload(before);
        var expected = await Observe(before);

        var after = Open(_temp.Reopen(), floor: 0);
        Assert.Equal(expected, await Observe(after));
        // 21 lines is the header plus all 20 records the workload appends — a journal that never compacted. At
        // floor 0 it compacts whenever it doubles, and 11 to 13 records are live (the delete is not journaled).
        Assert.InRange(File.ReadAllLines(JournalPath).Length, 12, 20);
    }

    [Fact]
    public async Task Ids_continue_after_a_restart_and_a_deleted_newest_id_is_never_reused()
    {
        var store = Open(_temp.Root, floor: 0);
        var one = await store.UpsertAsync(Write("one"));
        await store.UpsertAsync(Write("two"));
        var three = await store.UpsertAsync(Write("three"));
        await store.DeleteAsync("e", [three]);
        // enough appends to double the journal, so it compacts and the deleted memory's line is gone
        for (var i = 0; i < 10; i++) await store.TouchAsync("e", [new GraphTouch(one, i + 1)]);
        Assert.DoesNotContain($"\"node\":{three},", File.ReadAllText(JournalPath), StringComparison.Ordinal);

        Assert.Equal(three + 1, await Open(_temp.Reopen()).UpsertAsync(Write("four")));
    }

    [Fact]
    public async Task A_recall_changes_no_memory_file()
    {
        var store = Open(_temp.Root);
        var a = await store.UpsertAsync(Write("alpha"));
        var b = await store.UpsertAsync(Write("beta"));
        var files = MemoryFiles().ToDictionary(f => f, File.ReadAllBytes);
        var stamps = files.Keys.ToDictionary(f => f, File.GetLastWriteTimeUtc);

        await store.WriteBackAsync("e", new GraphWriteBack([new GraphTouch(a, 9), new GraphTouch(b, 9)],
            [new GraphEdgeWrite(a, b, Symmetric: true)], [new MemoryReviewWrite(a, Guid.NewGuid(), 1, 3, 5, 0, 0, 3, 9, 5)], 100));

        foreach (var (file, bytes) in files)
        {
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal(stamps[file], File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public async Task A_memory_edited_by_hand_while_no_process_owned_the_root_is_read_on_the_next_start()
    {
        var id = await Open(_temp.Root).UpsertAsync(Write("the deploy key rotates monthly", headline: "deploy key"));
        var file = MemoryFiles().Single();
        _temp.Root.Dispose();
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace("\"deploy key\"", "\"the vault key\"", StringComparison.Ordinal)
            .Replace("monthly", "weekly", StringComparison.Ordinal));

        var node = await Open(_temp.Reopen()).GetAsync("e", id);

        Assert.Equal(("the vault key", "the deploy key rotates weekly"), (node!.Headline, node.Content));
    }

    [Fact]
    public async Task A_memory_file_that_does_not_parse_keeps_its_learning_and_comes_back_whole_when_repaired()
    {
        var store = Open(_temp.Root);
        var a = await store.UpsertAsync(Write("alpha"));
        var b = await store.UpsertAsync(Write("beta"));
        await store.LinkAsync("e", a, b, null, 3, symmetric: true);
        await store.TouchAsync("e", [new GraphTouch(a, 11)]);
        await store.RecordSubjectsAsync("e", a, ["me"]);
        var file = MemoryFiles().Single(f => File.ReadAllText(f).EndsWith("alpha", StringComparison.Ordinal));
        var original = File.ReadAllText(file);
        _temp.Root.Dispose();
        File.WriteAllText(file, Broken);

        var damaged = Open(_temp.Reopen(), floor: 0);
        Assert.Null(await damaged.GetAsync("e", a));
        Assert.Empty(await damaged.NeighboursAsync("e", "task", [b], 10));
        var lines = File.ReadAllLines(JournalPath).Length;
        for (var i = 0; i < 12; i++) await damaged.TouchAsync("e", [new GraphTouch(b, 4)]);
        Assert.True(File.ReadAllLines(JournalPath).Length < lines + 12, "the journal compacted while the file was broken");
        Assert.Equal(Broken, File.ReadAllText(file));
        _temp.Root.Dispose();
        File.WriteAllText(file, original);

        var repaired = Open(_temp.Reopen());
        var node = await repaired.GetAsync("e", a);
        Assert.Equal((1, 11d), (node!.RecallCount, node.Stability));
        Assert.Equal([b], (await repaired.NeighboursAsync("e", "task", [a], 10)).Select(n => n.Node.Id));
        Assert.Equal([a], await repaired.NodesBySubjectAsync("e", "task", null, "me", 10));
    }

    [Fact]
    public async Task A_memory_file_with_no_journal_state_is_skipped_left_alone_and_keeps_its_id()
    {
        await Open(_temp.Root).UpsertAsync(Write("alpha"));
        var scope = Path.GetDirectoryName(MemoryFiles().Single())!;
        var handMade = Path.Combine(scope, "000050.md");
        _temp.Root.Dispose();
        File.WriteAllText(handMade, """
            ---
            lyntai: 1
            id: 50
            engine: "e"
            task: "task"
            scope: "s"
            headline: "written by hand"
            grade: "associative"
            created: "2026-09-23T12:00:00.0000000+00:00"
            ---
            a memory nobody journaled
            """.Replace("\r\n", "\n", StringComparison.Ordinal));

        var store = Open(_temp.Reopen());

        Assert.Null(await store.GetAsync("e", 50));
        Assert.Equal(51, await store.UpsertAsync(Write("beta")));
        Assert.Contains("a memory nobody journaled", File.ReadAllText(handMade), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_torn_journal_tail_is_cut_and_the_store_carries_on()
    {
        var id = await Open(_temp.Root).UpsertAsync(Write("alpha"));
        _temp.Root.Dispose();
        File.AppendAllText(JournalPath, """{"node":1,"stabi""");

        var store = Open(_temp.Reopen());
        Assert.NotNull(await store.GetAsync("e", id));
        await store.TouchAsync("e", [new GraphTouch(id, 8)]);

        Assert.Equal(8, (await Open(_temp.Reopen()).GetAsync("e", id))!.Stability);
    }

    [Fact]
    public async Task An_unreadable_journal_line_is_skipped_and_never_compacted_away()
    {
        var id = await Open(_temp.Root).UpsertAsync(Write("alpha"));
        _temp.Root.Dispose();
        File.AppendAllText(JournalPath, "a person's note\n");

        var store = Open(_temp.Reopen(), floor: 0);
        for (var i = 0; i < 10; i++) await store.TouchAsync("e", [new GraphTouch(id, 8)]);
        await store.TouchAsync("e", [new GraphTouch(id, 9)]); // far past double: compaction was due, and refused

        Assert.Contains("a person's note", File.ReadAllText(JournalPath), StringComparison.Ordinal);
        Assert.Equal(9, (await Open(_temp.Reopen()).GetAsync("e", id))!.Stability);
    }

    [Fact]
    public async Task A_compaction_the_file_system_refuses_fails_no_write_and_runs_once_the_journal_doubles_again()
    {
        var store = Open(_temp.Root, floor: 0);
        var id = await store.UpsertAsync(Write("alpha"));
        var obstacle = Directory.CreateDirectory(JournalPath + ".tmp"); // a rewrite writes this sibling first
        var lengths = new List<int>();
        async Task Recall(int i)
        {
            await store.WriteBackAsync("e", new GraphWriteBack([new GraphTouch(id, i)], [],
                [new MemoryReviewWrite(id, Guid.NewGuid(), 1, 3, 5, 0, 0, 3, i, 5)], ReviewLogCap: 100));
            lengths.Add(File.ReadAllLines(JournalPath).Length);
        }

        for (var i = 1; i <= 10; i++) await Recall(i); // compaction falls due at floor 0, and is refused

        var node = await store.GetAsync("e", id);
        Assert.Equal((10, 10d), (node!.RecallCount, node.Stability));
        Assert.Equal(10, (await store.ReviewsAsync("e")).Count);
        Assert.All(lengths.Zip(lengths.Skip(1)), p => Assert.True(p.Second > p.First, "every append landed and none was rewritten away"));

        obstacle.Delete();
        await Recall(11);
        Assert.True(lengths[^1] > lengths[^2], "a refused compaction waits for the journal to double again");
        for (var i = 12; i <= 40 && lengths[^1] > lengths[^2]; i++) await Recall(i);
        Assert.True(lengths[^1] < lengths[^2], "a later compaction ran once nothing blocked it");

        var expected = await Observe(store);
        Assert.Equal(expected, await Observe(Open(_temp.Reopen())));
    }

    [Fact]
    public async Task A_review_trim_the_file_system_refuses_fails_no_write_and_the_next_trim_converges()
    {
        var store = Open(_temp.Root);
        var id = await store.UpsertAsync(Write("alpha"));
        Task Batch() => store.RecordReviewsAsync("e",
            [.. Enumerable.Range(0, 5).Select(_ => new MemoryReviewWrite(id, Guid.NewGuid(), 1, 3, 5, 0, 0, 3, 4, 5))], cap: 10);
        await Batch();
        var reviews = Path.Combine(Path.GetDirectoryName(JournalPath)!, "reviews.jsonl");
        var obstacle = Directory.CreateDirectory(reviews + ".tmp");

        await Batch();
        await Batch(); // past the cap: this one trims, and the rewrite is refused

        Assert.Equal(Enumerable.Range(6, 10).Select(i => (long)i), (await store.ReviewsAsync("e")).Select(r => r.Id));
        Assert.Equal(1 + 15, File.ReadAllLines(reviews).Length); // the header, and every review still on disk
        obstacle.Delete();
        await Batch();
        Assert.Equal(1 + 10, File.ReadAllLines(reviews).Length);
        Assert.Equal(Enumerable.Range(11, 10).Select(i => (long)i), (await Open(_temp.Reopen()).ReviewsAsync("e")).Select(r => r.Id));
    }

    [Fact]
    public async Task A_memory_file_renamed_by_hand_is_updated_in_place_and_stays_deleted()
    {
        var id = await Open(_temp.Root).UpsertAsync(Write("alpha", headline: "old"));
        var file = MemoryFiles().Single();
        var renamed = Path.Combine(Path.GetDirectoryName(file)!, "alpha-note.md");
        _temp.Root.Dispose();
        File.Move(file, renamed);

        var store = Open(_temp.Reopen());
        await store.UpsertAsync(Write("alpha", headline: "new"));
        Assert.Equal([renamed], MemoryFiles());
        Assert.Contains("\"new\"", File.ReadAllText(renamed), StringComparison.Ordinal);

        await store.DeleteAsync("e", [id]);
        Assert.Empty(MemoryFiles());
        Assert.Null(await Open(_temp.Reopen()).GetAsync("e", id));
    }

    [Fact]
    public async Task A_hand_made_copy_goes_with_its_memory()
    {
        var id = await Open(_temp.Root).UpsertAsync(Write("alpha"));
        var file = MemoryFiles().Single();
        _temp.Root.Dispose();
        File.Copy(file, Path.Combine(Path.GetDirectoryName(file)!, "copy-of-alpha.md"));

        await Open(_temp.Reopen()).DeleteAsync("e", [id]);

        Assert.Empty(MemoryFiles());
        Assert.Null(await Open(_temp.Reopen()).GetAsync("e", id));
    }

    [Fact]
    public async Task The_review_log_trims_to_its_cap_on_disk_and_keeps_the_newest()
    {
        var store = Open(_temp.Root);
        var id = await store.UpsertAsync(Write("alpha"));
        for (var batch = 0; batch < 3; batch++)
            await store.RecordReviewsAsync("e",
                [.. Enumerable.Range(0, 5).Select(_ => new MemoryReviewWrite(id, Guid.NewGuid(), 1, 3, 5, 0, 0, 3, 4, 5))],
                cap: 10);

        var reviews = await Open(_temp.Reopen()).ReviewsAsync("e");

        Assert.Equal(Enumerable.Range(6, 10).Select(i => (long)i), reviews.Select(r => r.Id));
    }

    [Fact]
    public async Task A_journal_a_newer_Lyntai_wrote_is_refused_rather_than_misread()
    {
        await Open(_temp.Root).UpsertAsync(Write("alpha"));
        _temp.Root.Dispose();
        var lines = File.ReadAllLines(JournalPath);
        lines[0] = lines[0].Replace("\"lyntai\":1", "\"lyntai\":2", StringComparison.Ordinal);
        File.WriteAllText(JournalPath, string.Join("\n", lines) + "\n");

        await Assert.ThrowsAsync<InvalidDataException>(() => Open(_temp.Reopen()).GetAsync("e", 1));
    }

    private string JournalOf(string engine) =>
        Directory.GetFiles(Path.Combine(_temp.Directory, "graph"), "journal.jsonl", SearchOption.AllDirectories)
            .Single(f => File.ReadLines(f).First().Contains($"\"engine\":\"{engine}\"", StringComparison.Ordinal));

    // Each direction's line belongs to its FROM memory's engine journal. A misrouted line survives in-process —
    // the owning engine's next compaction re-emits it from memory — so only a restart taken after ONE engine
    // has compacted and before the OTHER has can tell the two apart.
    [Fact]
    public async Task An_edge_between_two_engines_survives_one_journal_compacting_without_the_other_and_a_restart()
    {
        var store = Open(_temp.Root, floor: 0);
        var a = await store.UpsertAsync(Write("alpha"));
        var b = await store.UpsertAsync(Write("beta") with { Engine = "f" });
        await store.LinkAsync("e", a, b, "cross", 2, symmetric: true);
        var fBefore = File.ReadAllLines(JournalOf("f")).Length;
        for (var i = 0; i < 8; i++) await store.TouchAsync("e", [new GraphTouch(a, 3)]);

        Assert.True(File.ReadAllLines(JournalOf("e")).Length < 1 + 3 + 8, "e's journal never compacted");
        Assert.Equal(fBefore, File.ReadAllLines(JournalOf("f")).Length);
        Assert.Single(File.ReadAllLines(JournalOf("f")), l => l.StartsWith($"{{\"edge\":[{b},{a},", StringComparison.Ordinal));
        await AssertLinked(Open(_temp.Reopen(), floor: 0));

        // and once the other engine compacts too
        var reopened = Open(_temp.Reopen(), floor: 0);
        for (var i = 0; i < 8; i++) await reopened.TouchAsync("f", [new GraphTouch(b, 3)]);
        await AssertLinked(Open(_temp.Reopen()));

        async Task AssertLinked(IMemoryGraphStore s)
        {
            var (nodeA, nodeB) = (await s.GetAsync("e", a), await s.GetAsync("f", b));
            Assert.Equal((1, 2d), (nodeA!.Degree, nodeA.Strength));
            Assert.Equal((1, 2d), (nodeB!.Degree, nodeB.Strength));
        }
    }
}
