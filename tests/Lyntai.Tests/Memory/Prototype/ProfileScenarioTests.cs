using Lyntai.Inference;
using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>VN2's scenario set</b>, run on both options over both durable stores: <c>ProfileOverCurated</c> (a projection
/// over curated memory) and <c>ProfileLedger</c> (a claim ledger on the temporal graph store), on SQLite and on the
/// file system. The comparison and the decision it supports are <c>docs/memory-measurements.md</c> §5
/// (<c>profile-ownership-vn2</c>); this file is the proof behind it, and a test here pins what an option DOES, which
/// is not always what it should.
/// </summary>
public class ProfileScenarioTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset May = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ProfileField City = new("u1", "address.city");

    public static TheoryData<string, string> Cells
    {
        get
        {
            var cells = new TheoryData<string, string>();
            foreach (var option in new[] { "curated", "ledger" })
                foreach (var backend in new[] { "sqlite", "filesystem" })
                    cells.Add(option, backend);
            return cells;
        }
    }

    /// <summary>The cells plus the ledger WITHOUT its compensation, which is the engine's own refusal and nothing else.</summary>
    public static TheoryData<string, string> RaceCells
    {
        get
        {
            var cells = Cells;
            cells.Add("ledger-raw", "sqlite");
            cells.Add("ledger-raw", "filesystem");
            return cells;
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly TempDb _db = new();
        private readonly TempRoot _root = new();

        public Rig(string option, string backend)
        {
            Option = (option, backend) switch
            {
                ("curated", "sqlite") => new ProfileOverCurated(new SqliteCuratedMemoryStore(_db.Factory)),
                ("curated", _) => new ProfileOverCurated(new FileSystemCuratedMemoryStore(_root.Root)),
                (_, "sqlite") => new ProfileLedger(new SqliteMemoryGraphStore(_db.Factory), Now, option != "ledger-raw"),
                _ => new ProfileLedger(new FileSystemMemoryGraphStore(_root.Root), Now, option != "ledger-raw"),
            };
        }

        public IProfileOption Option { get; }

        public void Dispose()
        {
            _db.Dispose();
            _root.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_set_is_read_back_exactly_and_a_read_changes_nothing_and_asks_no_model(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.SetAsync(new ProfileField("u1", "address.zip"), "75001", Jan, "chat:1");
        var before = await o.FingerprintAsync("u1");

        var read = await o.ReadAsync(City, Now);
        await o.HistoryAsync(City);
        await o.AsOfAsync(City, Feb);

        Assert.Equal("Paris", read.Current!.Value);
        Assert.False(read.Disputed);
        Assert.Equal(before, await o.FingerprintAsync("u1"));
        // no seam a model could sit behind is even constructible into either option
        Assert.DoesNotContain(o.GetType().GetConstructors().SelectMany(c => c.GetParameters()),
            p => typeof(ITextClient).IsAssignableFrom(p.ParameterType) || typeof(IVectorProvider).IsAssignableFrom(p.ParameterType));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_correction_replaces_the_current_claim_and_history_keeps_the_old_one(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");

        Assert.Equal(Correction.Applied, await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));

        Assert.Equal("Berlin", (await o.ReadAsync(City, Now)).Current!.Value);
        Assert.Equal("Paris", (await o.AsOfAsync(City, Feb))!.Value);
        Assert.Null(await o.AsOfAsync(City, Jan.AddDays(-1)));
        var history = await o.HistoryAsync(City);
        Assert.Equal(["Paris", "Berlin"], history.Select(c => c.Value));
        Assert.Equal(Mar, history[0].EndedAt);
        Assert.Null(history[1].EndedAt);
        Assert.Equal("chat:1", history[0].Source);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_capacity_prune_at_the_strictest_retrievability_removes_no_claim(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
        for (var i = 0; i < 30; i++) await o.SetAsync(new ProfileField("u1", $"filler.{i}"), $"value {i}", Jan, "chat:1");

        Assert.Equal(0, await o.PruneAsync("u1"));

        Assert.Equal(2, (await o.HistoryAsync(City)).Count);
        Assert.Equal("Berlin", (await o.ReadAsync(City, Now)).Current!.Value);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_replayed_event_writes_nothing_and_a_value_that_returns_is_a_new_claim(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.SetAsync(City, "Paris", Jan, "chat:1");
        Assert.Single(await o.HistoryAsync(City));

        await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
        await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
        var berlin = (await o.ReadAsync(City, Now)).Current!;
        await o.CorrectAsync(City, "Paris", Jun, "chat:3", berlin.Handle);

        var history = await o.HistoryAsync(City);
        Assert.Equal(["Paris", "Berlin", "Paris"], history.Select(c => c.Value));
        Assert.Equal(3, history.Select(c => c.ClaimId).Distinct().Count());
        Assert.Equal(Mar, history[0].EndedAt);
        Assert.Equal(Jun, history[1].EndedAt);
        Assert.Equal("Paris", (await o.ReadAsync(City, Now)).Current!.Value);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_forget_removes_the_subject_from_reads_and_exports_and_touches_no_other_subject(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var other = new ProfileField("u2", "address.city");
        await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.SetAsync(other, "Oslo", Jan, "chat:9");
        var otherBefore = await o.FingerprintAsync("u2");

        await o.ForgetAsync("u1");

        Assert.Null((await o.ReadAsync(City, Now)).Current);
        Assert.Empty(await o.HistoryAsync(City));
        using var export = new StringWriter();
        Assert.Equal(0, await o.ExportAsync("u1", export));
        Assert.Equal("", export.ToString());
        Assert.Equal("Oslo", (await o.ReadAsync(other, Now)).Current!.Value);
        Assert.Equal(otherBefore, await o.FingerprintAsync("u2"));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task An_export_imported_elsewhere_reads_and_remembers_alike_and_a_second_import_adds_nothing(string option, string backend)
    {
        using var source = new Rig(option, backend);
        using var target = new Rig(option, backend);
        var o = source.Option;
        var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
        await o.SetAsync(new ProfileField("u1", "address.zip"), "10115", Mar, "chat:2");
        using var file = new StringWriter();
        Assert.Equal(3, await o.ExportAsync("u1", file));

        Assert.Equal(3, await target.Option.ImportAsync("u1", new StringReader(file.ToString())));
        Assert.Equal(0, await target.Option.ImportAsync("u1", new StringReader(file.ToString())));

        Assert.Equal(Shape(await o.HistoryAsync(City)), Shape(await target.Option.HistoryAsync(City)));
        Assert.Equal("Berlin", (await target.Option.ReadAsync(City, Now)).Current!.Value);
        Assert.Equal("Paris", (await target.Option.AsOfAsync(City, Feb))!.Value);
    }

    [Theory]
    [MemberData(nameof(RaceCells))]
    public async Task Two_concurrent_corrections_of_one_claim_report_one_winner_and_that_is_what_reads_return(string option, string backend)
    {
        var (disputed, refusedWon, lost) = (0, 0, 0);
        const int trials = 20;
        for (var t = 0; t < trials; t++)
        {
            using var rig = new Rig(option, backend);
            var o = rig.Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            var gate = new TaskCompletionSource();
            Task<Correction> Racer(string value, string source) => Task.Run(async () =>
            {
                await gate.Task;
                return await o.CorrectAsync(City, value, Mar, source, paris.Handle);
            });
            var a = Racer("Berlin", "chat:2");
            var b = Racer("Lyon", "chat:3");
            gate.SetResult();
            var outcomes = await Task.WhenAll(a, b);

            Assert.Equal(1, outcomes.Count(x => x == Correction.Applied));   // one writer wins: never both, never neither
            var winner = outcomes[0] == Correction.Applied ? "Berlin" : "Lyon";
            var read = await o.ReadAsync(City, Now);
            if (read.Current is null) lost++;
            if (read.Disputed) disputed++;
            if (read.Current?.Value != winner) refusedWon++;
        }

        output.WriteLine($"{option}/{backend}: {trials} trials, disputed reads {disputed}, "
            + $"the refused writer's value is what reads return {refusedWon}, no current claim {lost}");
        Assert.Equal(0, lost);
        if (option != "ledger-raw")
        {
            Assert.Equal(0, disputed);
            Assert.Equal(0, refusedWon);
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_partial_amendment_changes_one_field_and_the_others_stand(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var street = new ProfileField("u1", "address.street");
        var zip = new ProfileField("u1", "address.zip");
        await o.SetAsync(street, "1 Rue Lafayette", Jan, "chat:1");
        var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
        await o.SetAsync(zip, "75001", Jan, "chat:1");

        await o.CorrectAsync(City, "Lyon", Mar, "chat:2", paris.Handle);

        Assert.Equal("Lyon", (await o.ReadAsync(City, Now)).Current!.Value);
        Assert.Equal("1 Rue Lafayette", (await o.ReadAsync(street, Now)).Current!.Value);
        Assert.Equal("75001", (await o.ReadAsync(zip, Now)).Current!.Value);
        Assert.Single(await o.HistoryAsync(street));
        Assert.Single(await o.HistoryAsync(zip));
        Assert.Equal("Paris", (await o.AsOfAsync(City, Feb))!.Value);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Corrections_of_different_fields_both_land_and_a_bundled_claim_makes_one_of_them_stale(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var street = new ProfileField("u1", "address.street");
        var s = await o.SetAsync(street, "1 Rue Lafayette", Jan, "chat:1");
        var c = await o.SetAsync(City, "Paris", Jan, "chat:1");

        var both = await Task.WhenAll(
            o.CorrectAsync(street, "2 Rue Lafayette", Mar, "chat:2", s.Handle),
            o.CorrectAsync(City, "Lyon", Mar, "chat:3", c.Handle));
        Assert.All(both, x => Assert.Equal(Correction.Applied, x));

        // the same two changes to ONE bundled claim: the second writer holds a stale base and must re-read
        var bundle = new ProfileField("u1", "address");
        var b = await o.SetAsync(bundle, "1 Rue Lafayette|Paris", Jan, "chat:1");
        var first = await o.CorrectAsync(bundle, "2 Rue Lafayette|Paris", Mar, "chat:2", b.Handle);
        var second = await o.CorrectAsync(bundle, "1 Rue Lafayette|Lyon", Mar, "chat:3", b.Handle);
        Assert.Equal(Correction.Applied, first);
        Assert.Equal(Correction.Stale, second);
        Assert.Equal("2 Rue Lafayette|Paris", (await o.ReadAsync(bundle, Now)).Current!.Value);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_schema_rename_moves_every_claim_with_its_history_and_leaves_the_old_id_empty(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var old = new ProfileField("u1", "home_city");
        var renamed = new ProfileField("u1", "address.city");
        var paris = await o.SetAsync(old, "Paris", Jan, "chat:1");
        await o.CorrectAsync(old, "Berlin", Mar, "chat:2", paris.Handle);
        var before = await o.HistoryAsync(old);

        Assert.Equal(2, await o.RenameAsync("u1", "home_city", "address.city"));

        Assert.Equal("Berlin", (await o.ReadAsync(renamed, Now)).Current!.Value);
        Assert.Null((await o.ReadAsync(old, Now)).Current);
        Assert.Empty(await o.HistoryAsync(old));
        var after = await o.HistoryAsync(renamed);
        Assert.Equal(Shape(before), Shape(after));
        Assert.Equal(Mar, after[0].EndedAt);
        // the renamed field still corrects: its successor chain survived the move
        Assert.Equal(Correction.Applied, await o.CorrectAsync(renamed, "Lyon", Jun, "chat:3", after[1].Handle));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task An_export_taken_before_a_rename_brings_the_old_field_id_back_and_renaming_again_merges_it(string option, string backend)
    {
        using var rig = new Rig(option, backend);
        var o = rig.Option;
        var old = new ProfileField("u1", "home_city");
        var renamed = new ProfileField("u1", "address.city");
        var paris = await o.SetAsync(old, "Paris", Jan, "chat:1");
        using var preRename = new StringWriter();
        await o.ExportAsync("u1", preRename);
        await o.CorrectAsync(old, "Berlin", Mar, "chat:2", paris.Handle);
        await o.RenameAsync("u1", "home_city", "address.city");

        // the file names the schema it was taken under and nothing migrates it, so the old id is populated again,
        // with a claim the new id already holds
        await o.ImportAsync("u1", new StringReader(preRename.ToString()));
        Assert.Equal("Paris", Assert.Single(await o.HistoryAsync(old)).Value);

        // and a file from elsewhere brings a claim the target never held
        using var elsewhere = new Rig(option, backend);
        await elsewhere.Option.SetAsync(old, "Oslo", Jan, "chat:7");
        using var other = new StringWriter();
        await elsewhere.Option.ExportAsync("u1", other);
        await o.ImportAsync("u1", new StringReader(other.ToString()));
        Assert.Equal(2, (await o.HistoryAsync(old)).Count);
        Assert.Equal("Berlin", (await o.ReadAsync(renamed, Now)).Current!.Value);   // the renamed field is untouched

        // renaming again merges: the duplicate goes, the stray claim moves and, being a dispute, is reported there
        Assert.Equal(2, await o.RenameAsync("u1", "home_city", "address.city"));
        Assert.Empty(await o.HistoryAsync(old));
        Assert.Equal(["Berlin", "Oslo", "Paris"], (await o.HistoryAsync(renamed)).Select(c => c.Value).Order());
        var read = await o.ReadAsync(renamed, Now);
        Assert.Equal("Berlin", read.Current!.Value);
        Assert.Equal("Oslo", Assert.Single(read.Rivals).Value);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task An_older_export_imported_over_newer_state_is_reported_as_a_dispute_and_never_wins(string option, string backend)
    {
        // the older file: taken in February, when Paris was the only claim and it was still open
        using var archive = new Rig(option, backend);
        await archive.Option.SetAsync(City, "Paris", Jan, "chat:1");
        using var old = new StringWriter();
        await archive.Option.ExportAsync("u1", old);

        // the target has since forgotten the subject and learned Berlin, which it holds as the current claim
        using var target = new Rig(option, backend);
        await target.Option.SetAsync(City, "Berlin", May, "chat:2");

        Assert.Equal(1, await target.Option.ImportAsync("u1", new StringReader(old.ToString())));

        var read = await target.Option.ReadAsync(City, Now);
        Assert.Equal("Berlin", read.Current!.Value);
        Assert.True(read.Disputed);
        Assert.Equal("Paris", Assert.Single(read.Rivals).Value);
        Assert.Equal(["Paris", "Berlin"], (await target.Option.HistoryAsync(City)).Select(c => c.Value));
    }

    /// <summary>A claim without the store-defined handle: what must survive a move between stores.</summary>
    private static IEnumerable<string> Shape(IEnumerable<ProfileClaim> claims) =>
        claims.Select(c => $"{c.ClaimId}={c.Value}@{c.ValidFrom:O}..{c.EndedAt:O} ({c.Source})");
}
