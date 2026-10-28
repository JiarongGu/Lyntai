using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>RV28</b>: what the prototype adds beyond RV27's Track A, on each durable store. The costs <b>D224</b> accepted
/// (a crash between a correction's two writes, an entry edited in place), the two questions <b>D220</b> asks of the
/// built thing (which record is the authority, a partial amendment), and the attributes a field carries: scope,
/// authority, status, revision, and a narrative that is a cache and never evidence.
/// </summary>
public class ProfilePrototypeTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Apr = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ProfileField City = new("u1", "address.city");

    public static TheoryData<string> Backends => ["sqlite", "files"];

    /// <summary>A store that can be told to fail the next add, the crash that leaves a correction half written.</summary>
    private sealed class CrashingStore(IRevisionedCuratedMemoryStore inner) : IRevisionedCuratedMemoryStore
    {
        internal bool CrashOnAdd { get; set; }

        public Task<long> AddAsync(string kind, string content, bool enabled = true, string? taskKey = null,
            string? scope = null, bool dedup = false, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken ct = default) =>
            CrashOnAdd ? throw new IOException("injected crash") : inner.AddAsync(kind, content, enabled, taskKey, scope, dedup, metadata, ct);

        public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
            string? taskKey = null, string? scope = null, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken ct = default) =>
            inner.UpdateAsync(id, content, enabled, kind, taskKey, scope, metadata, ct);

        public Task<CuratedUpdateOutcome> UpdateAsync(long id, long expectedRevision, string? content = null,
            bool? enabled = null, string? kind = null, string? taskKey = null, string? scope = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
            inner.UpdateAsync(id, expectedRevision, content, enabled, kind, taskKey, scope, metadata, ct);

        public Task<bool> RemoveAsync(long id, CancellationToken ct = default) => inner.RemoveAsync(id, ct);

        public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default) => inner.GetAsync(id, ct);

        public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
            string? taskKey = null, string? scope = null, int? limit = null,
            IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default) =>
            inner.ListAsync(kind, enabledOnly, taskKey, scope, limit, metadataMatch, ct);

        public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
            string? scope = null, bool enabledOnly = false, int? limit = null,
            IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default) =>
            inner.SearchAsync(query, kind, taskKey, scope, enabledOnly, limit, metadataMatch, ct);

        public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
            bool enabledOnly = true, CancellationToken ct = default) =>
            inner.ForCompositionAsync(taskKey, scopes, enabledOnly, ct);
    }

    private sealed class Rig : IDisposable
    {
        private readonly IDisposable _lease;

        internal Rig(string backend)
        {
            if (backend == "sqlite")
            {
                var db = new TempDb();
                (_lease, Raw) = (db, new SqliteCuratedMemoryStore(db.Factory));
            }
            else
            {
                var root = new TempRoot();
                (_lease, Raw) = (root, new FileSystemCuratedMemoryStore(root.Root));
            }

            Crashing = new CrashingStore(Raw);
            Profile = new ProfileOverCurated(Crashing);
        }

        internal IRevisionedCuratedMemoryStore Raw { get; }

        internal CrashingStore Crashing { get; }

        internal ProfileOverCurated Profile { get; }

        /// <summary>A later session: a new profile over the same entries, holding nothing the last one held.</summary>
        internal ProfileOverCurated Reopen() => new(Raw);

        public void Dispose() => _lease.Dispose();
    }

    private static long RowOf(ProfileClaim claim) => long.Parse(claim.Handle.Split(':')[0]);

    // ── the crash D224 accepted: a correction is two writes, and the field fails closed between them

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_crash_between_a_corrections_two_writes_leaves_the_field_with_no_current_claim_and_says_why(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");

        rig.Crashing.CrashOnAdd = true;
        await Assert.ThrowsAsync<IOException>(() => rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));
        rig.Crashing.CrashOnAdd = false;

        var read = await rig.Reopen().ReadAsync(City, Now);
        Assert.Null(read.Current);                                                    // fails closed: no stale Paris
        Assert.Equal([ProfileClaims.IdOf(City, "Berlin", Mar, "chat:2")], read.Interrupted);
        Assert.Equal("Paris", (await rig.Reopen().AsOfAsync(City, Feb))!.Value);      // the past is intact
        Assert.Null(await rig.Reopen().AsOfAsync(City, Apr));
        Assert.Single(await rig.Reopen().HistoryAsync(City));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task The_same_event_delivered_again_after_the_crash_completes_the_correction_once(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        rig.Crashing.CrashOnAdd = true;
        await Assert.ThrowsAsync<IOException>(() => rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));
        rig.Crashing.CrashOnAdd = false;

        var later = rig.Reopen();
        Assert.Equal(Correction.Applied, await later.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));
        Assert.Equal(Correction.Applied, await later.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));

        var read = await later.ReadAsync(City, Now);
        Assert.Equal("Berlin", read.Current!.Value);
        Assert.Empty(read.Interrupted!);
        Assert.False(read.Disputed);
        Assert.Equal(["Paris", "Berlin"], (await later.HistoryAsync(City)).Select(c => c.Value));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_hypothesis_does_not_fill_the_gap_a_crash_left(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Profile.SetAsync(City, "Oslo", Feb, "model:1", ProfileAuthority.Hypothesis);
        rig.Crashing.CrashOnAdd = true;
        await Assert.ThrowsAsync<IOException>(() => rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));
        rig.Crashing.CrashOnAdd = false;

        var read = await rig.Reopen().ReadAsync(City, Now);

        Assert.Null(read.Current);
        Assert.Equal("Oslo", Assert.Single(read.Outranked!).Value);   // reported, never promoted
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_different_correction_from_the_same_read_is_stale_after_the_crash_and_writes_nothing(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        rig.Crashing.CrashOnAdd = true;
        await Assert.ThrowsAsync<IOException>(() => rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle));
        rig.Crashing.CrashOnAdd = false;

        var before = await rig.Profile.FingerprintAsync("u1");
        Assert.Equal(Correction.Stale, await rig.Profile.CorrectAsync(City, "Lyon", Apr, "chat:3", paris.Handle));
        Assert.Equal(before, await rig.Profile.FingerprintAsync("u1"));
    }

    // ── the in-place edit D224 accepted: the catalogue's own edit path can change an entry under the profile

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task An_entry_whose_content_was_edited_in_place_is_reported_and_never_read(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Profile.SetAsync(new ProfileField("u1", "address.zip"), "75001", Jan, "chat:1");

        Assert.True(await rig.Raw.UpdateAsync(RowOf(paris), content: "Atlantis"));

        var read = await rig.Reopen().ReadAsync(City, Now);
        Assert.Null(read.Current);
        var edited = Assert.Single(read.Edited!);
        Assert.Equal(ClaimStatus.Edited, edited.Status);
        Assert.Equal(paris.ClaimId, edited.ClaimId);
        Assert.Equal(ClaimStatus.Edited, Assert.Single(await rig.Reopen().HistoryAsync(City)).Status);
        Assert.Null(await rig.Reopen().AsOfAsync(City, Feb));
        Assert.Equal("75001", (await rig.Reopen().ReadAsync(new ProfileField("u1", "address.zip"), Now)).Current!.Value);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task An_edit_of_the_start_the_source_the_authority_or_the_scope_is_caught_like_an_edit_of_the_value(string backend)
    {
        using var rig = new Rig(backend);
        var cases = new (string Name, Func<CuratedMemory, Task<bool>> Edit)[]
        {
            ("start", r => rig.Raw.UpdateAsync(r.Id, metadata: new Dictionary<string, string>(r.Metadata!) { ["from"] = Feb.ToString("O") })),
            ("source", r => rig.Raw.UpdateAsync(r.Id, metadata: new Dictionary<string, string>(r.Metadata!) { ["src"] = "chat:forged" })),
            ("authority", r => rig.Raw.UpdateAsync(r.Id, metadata: new Dictionary<string, string>(r.Metadata!) { ["auth"] = "user" })),
            ("scope", r => rig.Raw.UpdateAsync(r.Id, scope: "elsewhere")),
        };
        foreach (var (name, edit) in cases)
        {
            var field = new ProfileField("u1", "f." + name);
            await rig.Profile.SetAsync(field, "value", Jan, "chat:1", name == "authority" ? ProfileAuthority.Hypothesis : ProfileAuthority.User);
            var row = (await rig.Raw.ListAsync(field.Field, false, field.TaskKey)).Single();
            Assert.True(await edit(row), name);

            var seen = await rig.Profile.SnapshotAsync("u1", Now);
            var view = seen.Fields.Single(f => f.Field == field.Field);
            Assert.True(view.Read.Current is null && view.Read.Edited!.Count == 1, $"the edited {name} was read as a claim");
        }
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_correction_of_an_edited_claim_is_refused_and_writes_nothing(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Raw.UpdateAsync(RowOf(paris), content: "Atlantis");
        var current = (await rig.Raw.GetAsync(RowOf(paris)))!;
        var before = await rig.Profile.FingerprintAsync("u1");

        var outcome = await rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", $"{current.Id}:{current.Revision}");

        Assert.Equal(Correction.Edited, outcome);
        Assert.Equal(before, await rig.Profile.FingerprintAsync("u1"));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task An_entry_added_by_hand_under_a_profile_field_is_no_claim(string backend)
    {
        using var rig = new Rig(backend);
        await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Raw.AddAsync(City.Field, "Atlantis", true, City.TaskKey);   // the catalogue's own add, no claim metadata

        var read = await rig.Profile.ReadAsync(City, Now);

        Assert.Equal("Paris", read.Current!.Value);
        Assert.False(read.Disputed);
        Assert.Equal("Atlantis", Assert.Single(read.Edited!).Value);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_line_edited_in_a_file_is_refused_and_the_rest_of_the_file_lands(string backend)
    {
        using var source = new Rig(backend);
        using var target = new Rig(backend);
        await source.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await source.Profile.SetAsync(new ProfileField("u1", "address.zip"), "75001", Jan, "chat:1");
        using var file = new StringWriter();
        await source.Profile.ExportAsync("u1", file);

        var edited = file.ToString().Replace("Paris", "Atlantis");
        var report = await target.Profile.ImportFileAsync("u1", new StringReader(edited));

        Assert.Equal(new ProfileImportReport(1, 0, 1), report);
        Assert.Null((await target.Profile.ReadAsync(City, Now)).Current);
        Assert.Equal("75001", (await target.Profile.ReadAsync(new ProfileField("u1", "address.zip"), Now)).Current!.Value);
    }

    // ── D220's two questions, on the built thing

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task The_entry_is_the_authority_so_a_catalogue_disable_is_followed_and_dropping_the_profile_loses_nothing(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        var zip = await rig.Profile.SetAsync(new ProfileField("u1", "address.zip"), "75001", Jan, "chat:1");

        await rig.Raw.UpdateAsync(RowOf(zip), enabled: false);   // an operator disables an entry in the catalogue

        var later = rig.Reopen();
        Assert.Equal("Paris", (await later.ReadAsync(City, Now)).Current!.Value);
        Assert.Null((await later.ReadAsync(new ProfileField("u1", "address.zip"), Now)).Current);
        Assert.Equal(ClaimStatus.Disabled, Assert.Single(await later.HistoryAsync(new ProfileField("u1", "address.zip"))).Status);
        Assert.Equal(paris.ClaimId, (await later.ReadAsync(City, Now)).Current!.ClaimId);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_partial_amendment_moves_one_field_and_leaves_every_other_claim_untouched_down_to_its_revision(string backend)
    {
        using var rig = new Rig(backend);
        var p = rig.Profile;
        var fields = new[] { "address.street", "address.city", "address.zip", "address.country" }
            .Select(f => new ProfileField("u1", f)).ToArray();
        foreach (var f in fields) await p.SetAsync(f, "first " + f.Field, Jan, "record");
        var before = await p.SnapshotAsync("u1", Now);

        var city = (await p.ReadAsync(fields[1], Now)).Current!;
        await p.CorrectAsync(fields[1], "second address.city", Mar, "note", city.Handle);

        var after = await p.SnapshotAsync("u1", Now);
        foreach (var f in fields.Where(f => f.Field != "address.city"))
            Assert.Equal(before.Find(f.Field)!.Read.Current, after.Find(f.Field)!.Read.Current);   // value, revision and all
        Assert.Equal("second address.city", after.Find("address.city")!.Read.Current!.Value);
        Assert.NotEqual(before.Version, after.Version);
    }

    // ── what a field carries

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_snapshot_carries_each_fields_subject_scope_source_status_and_revision_through_the_composition_verb(string backend)
    {
        using var rig = new Rig(backend);
        var p = rig.Profile;
        var home = new ProfileField("u1", "address.city", "home");
        var work = new ProfileField("u1", "address.city", "work");
        await p.SetAsync(home, "Paris", Jan, "chat:1");
        var lyon = await p.SetAsync(work, "Lyon", Jan, "chat:2");
        await p.CorrectAsync(work, "Nice", Mar, "chat:3", lyon.Handle);
        await p.SetAsync(new ProfileField("u1", "name"), "Ada", Jan, "chat:1");
        await p.SetAsync(new ProfileField("u2", "name"), "Bo", Jan, "chat:9");
        await rig.Raw.AddAsync("persona", "A global entry with no task", true);   // applies to every task, and is not a profile

        var snapshot = await p.SnapshotAsync("u1", Now);

        Assert.Equal(["address.city@home", "address.city@work", "name@"],
            snapshot.Fields.Select(f => $"{f.Field}@{f.Scope}"));
        var nice = snapshot.Find("address.city", "work")!;
        Assert.Equal("u1", nice.Subject);
        Assert.Equal("Nice", nice.Read.Current!.Value);
        Assert.Equal("chat:3", nice.Read.Current.Source);
        Assert.Equal("work", nice.Read.Current.Scope);
        Assert.Equal(ClaimStatus.Holding, nice.Read.Current.Status);
        Assert.Equal(1, nice.Read.Current.Revision);
        Assert.False(nice.Read.Disputed);   // a value at home and one at work are two slots, never a dispute
        Assert.Equal(ClaimStatus.Ended, (await p.HistoryAsync(work))[0].Status);
        Assert.Equal(2, (await p.HistoryAsync(work))[0].Revision);   // ended once: added at 1, ended at 2

        var onlyHome = await p.SnapshotAsync("u1", Now, ["home"]);
        Assert.Equal(["address.city@home", "name@"], onlyHome.Fields.Select(f => $"{f.Field}@{f.Scope}"));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_snapshot_and_a_summary_read_ask_the_store_only_exact_verbs_and_write_nothing(string backend)
    {
        using var rig = new Rig(backend);
        var recording = new RecordingCuratedStore(rig.Raw);
        var p = new ProfileOverCurated(recording);
        await p.SetAsync(City, "Paris", Jan, "chat:1");
        await p.PutSummaryAsync("u1", "Lives in Paris.", Now);
        var before = await p.FingerprintAsync("u1");
        recording.Clear();

        await p.SnapshotAsync("u1", Now);
        await p.GetSummaryAsync("u1", Now);

        Assert.NotEmpty(recording.Verbs);
        Assert.All(recording.Verbs, v => Assert.Contains(v, RecordingCuratedStore.ExactReadVerbs));
        Assert.Equal(before, await p.FingerprintAsync("u1"));
    }

    // ── a user's correction outranks a hypothesis

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_users_claim_outranks_a_later_hypothesis_which_is_reported_and_not_a_dispute(string backend)
    {
        using var rig = new Rig(backend);
        await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Profile.SetAsync(City, "Oslo", Mar, "model:1", ProfileAuthority.Hypothesis);

        var read = await rig.Profile.ReadAsync(City, Now);

        Assert.Equal("Paris", read.Current!.Value);
        Assert.False(read.Disputed);
        var outranked = Assert.Single(read.Outranked!);
        Assert.Equal(("Oslo", ProfileAuthority.Hypothesis), (outranked.Value, outranked.Authority));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_hypothesis_cannot_end_a_users_claim_but_a_users_correction_ends_a_hypothesis(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        var before = await rig.Profile.FingerprintAsync("u1");

        Assert.Equal(Correction.Outranked,
            await rig.Profile.CorrectAsync(City, "Oslo", Mar, "model:1", paris.Handle, ProfileAuthority.Hypothesis));
        Assert.Equal(before, await rig.Profile.FingerprintAsync("u1"));

        var guess = await rig.Profile.SetAsync(new ProfileField("u1", "name"), "Al", Jan, "model:1", ProfileAuthority.Hypothesis);
        Assert.Equal(Correction.Applied,
            await rig.Profile.CorrectAsync(new ProfileField("u1", "name"), "Ada", Feb, "chat:2", guess.Handle));
        var name = await rig.Profile.ReadAsync(new ProfileField("u1", "name"), Now);
        Assert.Equal(("Ada", ProfileAuthority.User), (name.Current!.Value, name.Current.Authority));
        Assert.Empty(name.Outranked!);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_hypothesis_alone_is_current_until_the_user_speaks_and_a_replay_of_it_writes_nothing(string backend)
    {
        using var rig = new Rig(backend);
        await rig.Profile.SetAsync(City, "Oslo", Jan, "model:1", ProfileAuthority.Hypothesis);
        var before = await rig.Profile.FingerprintAsync("u1");
        await rig.Profile.SetAsync(City, "Oslo", Jan, "model:1", ProfileAuthority.Hypothesis);
        Assert.Equal(before, await rig.Profile.FingerprintAsync("u1"));
        Assert.Equal(ProfileAuthority.Hypothesis, (await rig.Profile.ReadAsync(City, Now)).Current!.Authority);

        await rig.Profile.SetAsync(City, "Paris", Feb, "chat:1");

        Assert.Equal("Paris", (await rig.Profile.ReadAsync(City, Now)).Current!.Value);
    }

    // ── a narrative is a cache, never evidence

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_summary_is_stale_once_a_claim_it_describes_changes_and_never_answers_a_read(string backend)
    {
        using var rig = new Rig(backend);
        var paris = await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Profile.PutSummaryAsync("u1", "The user lives in Atlantis.", Now);   // a wrong narrative

        Assert.True((await rig.Profile.GetSummaryAsync("u1", Now))!.Fresh);
        Assert.Equal("Paris", (await rig.Profile.ReadAsync(City, Now)).Current!.Value);   // the claim answers, not the text
        Assert.DoesNotContain(await rig.Profile.HistoryAsync(City), c => c.Value.Contains("Atlantis"));
        Assert.DoesNotContain((await rig.Profile.SnapshotAsync("u1", Now)).Fields, f => f.Field == ProfileOverCurated.SummaryKind);

        await rig.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);

        var summary = await rig.Profile.GetSummaryAsync("u1", Now);
        Assert.False(summary!.Fresh);
        Assert.Equal("The user lives in Atlantis.", summary.Text);   // returned as the cache it is, flagged
        await rig.Profile.PutSummaryAsync("u1", "The user lives in Berlin.", Now);
        Assert.True((await rig.Profile.GetSummaryAsync("u1", Now))!.Fresh);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_summary_is_not_exported_is_forgotten_with_its_subject_and_cannot_be_a_field(string backend)
    {
        using var rig = new Rig(backend);
        await rig.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await rig.Profile.PutSummaryAsync("u1", "Lives in Paris.", Now);

        using var file = new StringWriter();
        Assert.Equal(1, await rig.Profile.ExportAsync("u1", file));
        Assert.DoesNotContain("Lives in Paris", file.ToString());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Profile.SetAsync(new ProfileField("u1", ProfileOverCurated.SummaryKind), "x", Jan, "chat:1"));

        await rig.Profile.ForgetAsync("u1");
        Assert.Null(await rig.Profile.GetSummaryAsync("u1", Now));
    }

    // ── the closed Track A gaps, at a size Track A does not reach

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task One_file_imported_by_four_writers_at_once_adds_each_claim_once(string backend)
    {
        using var source = new Rig(backend);
        var paris = await source.Profile.SetAsync(City, "Paris", Jan, "chat:1");
        await source.Profile.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
        await source.Profile.CorrectAsync(City, "Paris", Jun(), "chat:3", (await source.Profile.ReadAsync(City, Now)).Current!.Handle);
        using var file = new StringWriter();
        await source.Profile.ExportAsync("u1", file);

        for (var trial = 0; trial < 10; trial++)
        {
            using var target = new Rig(backend);
            var gate = new TaskCompletionSource();
            var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                return await target.Profile.ImportFileAsync("u1", new StringReader(file.ToString()));
            })).ToArray();
            gate.SetResult();
            await Task.WhenAll(runs);

            Assert.Equal(["Paris", "Berlin", "Paris"], (await target.Profile.HistoryAsync(City)).Select(c => c.Value));
        }

        static DateTimeOffset Jun() => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
