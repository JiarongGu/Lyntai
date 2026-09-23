using System.Text;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>The graph store's machine-state files: every line kind round-trips exactly, and a journal treats
/// what a crash or a person left behind as D171 says — an unterminated tail is kept when it parses and set aside
/// otherwise, and an unreadable line blocks rewrites.</summary>
public class GraphJournalTests : IDisposable
{
    private static readonly DateTimeOffset When = new(2026, 9, 23, 10, 30, 0, TimeSpan.FromHours(8));
    private readonly TempRoot _temp = new();

    public void Dispose() => _temp.Dispose();

    private static GraphLine RoundTrip(string line)
    {
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        return GraphLines.Parse(doc.RootElement);
    }

    [Theory]
    [InlineData(0.1 + 0.2)]
    [InlineData(double.Epsilon)]
    [InlineData(1e308)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_node_state_round_trips_exactly_including_non_finite_doubles(double awkward)
    {
        var state = new GraphNodeState(3, awkward, 12.5, MemorySignals.Empty.With("salience", awkward), 7, 900, When, 1, 2, 5);

        var back = Assert.IsType<NodeLine>(RoundTrip(GraphLines.Node(42, state)));

        Assert.Equal(42, back.Id);
        Assert.Equal(state with { Signals = default }, back.State with { Signals = default });
        Assert.Equal(awkward, back.State.Signals.Get("salience"));
    }

    [Fact]
    public void Every_other_line_kind_round_trips()
    {
        var totals = new GraphTotals(40.25, 12, 3456, When);
        Assert.Equal(new TotalsLine("灵台", totals), RoundTrip(GraphLines.Totals("灵台", totals)));

        var key = new GraphEdgeKey(3, 9, "");
        var edge = new GraphEdgeState(2.5, 11, 4, 99, When);
        Assert.Equal(new EdgeLine(key, edge), RoundTrip(GraphLines.Edge(key, edge)));

        var subjects = Assert.IsType<SubjectsLine>(RoundTrip(GraphLines.Subjects("e", 3, ["me", "部署"])));
        // ValueTuple<..., string[]>.Equals compares its array field by reference (EqualityComparer<string[]>.Default),
        // so the array is asserted on its own rather than nested in the tuple.
        Assert.Equal(("e", 3L), (subjects.Engine, subjects.NodeId));
        Assert.Equal(new[] { "me", "部署" }, subjects.Subjects.ToArray());

        var review = new MemoryReview(5, "e", 3, Guid.NewGuid(), When, 1.5, 2, 5, 0.5, 0.25, null, 3, 4.5, 1, false);
        Assert.Equal(new ReviewLine(review), RoundTrip(GraphLines.Review(review)));
        var graded = review with { ReviewGrade = 3, Verified = null };
        Assert.Equal(new ReviewLine(graded), RoundTrip(GraphLines.Review(graded)));
    }

    [Fact]
    public void A_line_that_is_no_known_record_is_a_format_error() =>
        Assert.Throws<FormatException>(() => RoundTrip("""{"something":1}"""));

    private string JournalPath => Path.Combine(_temp.Directory, "graph", "e", "state", "journal.jsonl");

    [Fact]
    public void A_journal_created_by_its_first_append_starts_with_its_header_and_reloads()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, compactionFloor: 4096);
        journal.Append([GraphLines.Totals("e", new GraphTotals(1, 1, 5, When))], "e", highWater: 7);

        Assert.StartsWith("""{"lyntai":1,"engine":"e","ids":7}""" + "\n", File.ReadAllText(JournalPath));
        var read = new List<GraphLine>();
        var reloaded = new GraphJournal(_temp.Root, JournalPath, 4096);
        reloaded.Load((line, _) => read.Add(line));
        Assert.Equal(("e", 7L, 1), (reloaded.Engine, reloaded.HighWater, reloaded.Lines));
        Assert.IsType<TotalsLine>(Assert.Single(read));
    }

    private string TornPath => JournalPath + ".torn";

    [Fact]
    public void A_torn_tail_is_set_aside_then_cut_and_appends_continue_cleanly()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, 4096);
        journal.Append([GraphLines.Totals("e", new GraphTotals(1, 1, 5, When))], "e", 0);
        File.AppendAllText(JournalPath, """{"node":1,"stabi""");

        var reloaded = new GraphJournal(_temp.Root, JournalPath, 4096);
        reloaded.Load((_, _) => { });
        reloaded.Append([GraphLines.Totals("e", new GraphTotals(2, 2, 9, When))], "e", 0);

        Assert.Equal(0, reloaded.Unreadable);
        Assert.DoesNotContain("stabi", File.ReadAllText(JournalPath), StringComparison.Ordinal);
        Assert.EndsWith("\n", File.ReadAllText(JournalPath), StringComparison.Ordinal);
        Assert.Equal("""{"node":1,"stabi""" + "\n", File.ReadAllText(TornPath));
    }

    [Fact]
    public void A_record_line_missing_only_its_newline_is_kept_not_cut()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, 4096);
        journal.Append([GraphLines.Totals("e", new GraphTotals(1, 1, 5, When))], "e", 0);
        File.AppendAllText(JournalPath, GraphLines.Totals("e", new GraphTotals(2, 2, 9, When)));

        var read = new List<GraphLine>();
        var reloaded = new GraphJournal(_temp.Root, JournalPath, 4096);
        reloaded.Load((line, _) => read.Add(line));
        reloaded.Append([GraphLines.Totals("e", new GraphTotals(3, 3, 12, When))], "e", 0);

        Assert.Equal([1L, 2L], read.Cast<TotalsLine>().Select(t => t.Totals.Ordinal));
        Assert.Equal(3, File.ReadAllLines(JournalPath).Length - 1); // every record on its own line, header aside
        Assert.False(File.Exists(TornPath));
    }

    [Fact]
    public void A_note_typed_at_the_end_without_a_newline_is_set_aside_byte_for_byte_before_the_cut()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, 4096);
        journal.Append([GraphLines.Totals("e", new GraphTotals(1, 1, 5, When))], "e", 0);
        var note = Encoding.UTF8.GetBytes("备注: check the vault key");
        using (var file = new FileStream(JournalPath, FileMode.Append)) file.Write(note);

        new GraphJournal(_temp.Root, JournalPath, 4096).Load((_, _) => { });

        Assert.DoesNotContain("vault", File.ReadAllText(JournalPath), StringComparison.Ordinal);
        Assert.Equal([.. note, (byte)'\n'], File.ReadAllBytes(TornPath));
    }

    [Fact]
    public void An_unreadable_line_is_skipped_and_blocks_every_rewrite()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, 4096);
        journal.Append([GraphLines.Totals("e", new GraphTotals(1, 1, 5, When))], "e", 0);
        File.AppendAllText(JournalPath, "a person's note\n");
        var before = File.ReadAllBytes(JournalPath);

        var reloaded = new GraphJournal(_temp.Root, JournalPath, 4096);
        var read = 0;
        reloaded.Load((_, _) => read++);

        Assert.Equal((1, 1), (read, reloaded.Unreadable));
        Assert.False(reloaded.Rewrite([GraphLines.Totals("e", new GraphTotals(3, 3, 3, When))], "e", 0));
        Assert.Equal(before, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void A_journal_from_a_newer_schema_is_refused()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllText(JournalPath, """{"lyntai":2,"engine":"e","ids":0}""" + "\n");

        Assert.Throws<InvalidDataException>(() => new GraphJournal(_temp.Root, JournalPath, 4096).Load((_, _) => { }));
    }

    [Fact]
    public void Compaction_is_due_once_the_file_doubles_past_its_floor()
    {
        var journal = new GraphJournal(_temp.Root, JournalPath, compactionFloor: 2);
        journal.Append(["""{"totals":"e","position":0,"ordinal":0,"chars":0,"at":"2026-09-23T00:00:00.0000000+00:00"}"""], "e", 0);
        journal.Baseline = 1;
        Assert.False(journal.NeedsCompaction); // 1 line <= 2 * 1 + 2

        journal.Append(Enumerable.Repeat("""{"totals":"e","position":0,"ordinal":0,"chars":0,"at":"2026-09-23T00:00:00.0000000+00:00"}""", 4).ToList(), "e", 0);
        Assert.True(journal.NeedsCompaction);   // 5 lines > 2 * 1 + 2

        Assert.True(journal.Rewrite(["""{"totals":"e","position":0,"ordinal":0,"chars":0,"at":"2026-09-23T00:00:00.0000000+00:00"}"""], "e", 0));
        Assert.Equal((1, 1, false), (journal.Lines, journal.Baseline, journal.NeedsCompaction));
    }
}
