using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Lyntai.Inference;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>One case of RV27's Track A: a bucket it is scored in, a stable id, and the body that throws
/// <see cref="CaseFailed"/> when the projection under test does something the case forbids.</summary>
internal sealed record TrackACase(string Bucket, string Id, Func<TrackAContext, Task> Run)
{
    internal string Question => $"{Bucket}/{Id}";
}

/// <summary>What one case did on one projection: <see cref="Reason"/> is null when it passed.</summary>
internal sealed record CaseOutcome(string Bucket, string Id, string? Reason)
{
    internal bool Passed => Reason is null;

    internal string Question => $"{Bucket}/{Id}";
}

internal sealed class CaseFailed(string message) : Exception(message);

/// <summary>A case the projection is known to fail on one backend, and the limit that makes it. An
/// <paramref name="Intermittent"/> gap is a race that does not always show, so it may fail and need not.</summary>
internal sealed record KnownGap(string Question, string Backend, string Reason, bool Intermittent = false);

/// <summary>A store a profile is built over, with every verb the profile asked of it recorded: what a read may
/// call is part of the contract (<b>D218</b>: no model, no search, no write it did not declare).</summary>
internal sealed class RecordingCuratedStore(IRevisionedCuratedMemoryStore inner) : IRevisionedCuratedMemoryStore
{
    internal static readonly IReadOnlySet<string> ExactReadVerbs =
        new HashSet<string> { nameof(ListAsync), nameof(GetAsync), nameof(ForCompositionAsync) };

    private readonly ConcurrentQueue<string> _verbs = [];

    internal IReadOnlyList<string> Verbs => [.. _verbs];

    internal void Clear() => _verbs.Clear();

    public Task<long> AddAsync(string kind, string content, bool enabled = true, string? taskKey = null,
        string? scope = null, bool dedup = false, IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(AddAsync));
        return inner.AddAsync(kind, content, enabled, taskKey, scope, dedup, metadata, ct);
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null, IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(UpdateAsync));
        return inner.UpdateAsync(id, content, enabled, kind, taskKey, scope, metadata, ct);
    }

    public Task<CuratedUpdateOutcome> UpdateAsync(long id, long expectedRevision, string? content = null,
        bool? enabled = null, string? kind = null, string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(UpdateAsync));
        return inner.UpdateAsync(id, expectedRevision, content, enabled, kind, taskKey, scope, metadata, ct);
    }

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(RemoveAsync));
        return inner.RemoveAsync(id, ct);
    }

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(GetAsync));
        return inner.GetAsync(id, ct);
    }

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(ListAsync));
        return inner.ListAsync(kind, enabledOnly, taskKey, scope, limit, metadataMatch, ct);
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(SearchAsync));
        return inner.SearchAsync(query, kind, taskKey, scope, enabledOnly, limit, metadataMatch, ct);
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        _verbs.Enqueue(nameof(ForCompositionAsync));
        return inner.ForCompositionAsync(taskKey, scopes, enabledOnly, ct);
    }
}

/// <summary>One profile over one recording store. <see cref="Reopen"/> is a LATER SESSION: a new profile over
/// the same stored entries, which is what an agent reading in a later session has.</summary>
internal sealed class ProfileSite
{
    private readonly Func<ICuratedMemoryStore, IProfileOption> _build;
    private readonly IRevisionedCuratedMemoryStore _raw;

    internal ProfileSite(ICuratedMemoryStore store, Func<ICuratedMemoryStore, IProfileOption> build)
    {
        _raw = store as IRevisionedCuratedMemoryStore
            ?? throw new InvalidOperationException("Track A needs a store with revisions: the profile's correction is gated on one.");
        _build = build;
        Store = new RecordingCuratedStore(_raw);
        Option = build(Store);
    }

    internal RecordingCuratedStore Store { get; }

    internal IProfileOption Option { get; }

    internal ProfileSite Reopen() => new(_raw, _build);
}

/// <summary>What a case reaches: fresh sites over fresh stores, released when the case ends.</summary>
internal sealed class TrackAContext(
    Func<(ICuratedMemoryStore Store, IDisposable Lease)> openStore, Func<ICuratedMemoryStore, IProfileOption> build)
    : IDisposable
{
    private readonly List<IDisposable> _leases = [];

    internal ProfileSite NewSite()
    {
        var (store, lease) = openStore();
        _leases.Add(lease);
        return new ProfileSite(store, build);
    }

    public void Dispose()
    {
        foreach (var lease in _leases) lease.Dispose();
    }
}

/// <summary>
/// <b>RV27 Track A</b>: the model-free gate a profile must pass, scored per BUCKET and never as one figure. Every
/// case runs against a profile built over curated memory (<see cref="ProfileOverCurated"/>, D224) with no model
/// seam, and a case is a pass or a fail with a reason. The cases are VN2's scenarios first, then what <b>D220</b>
/// adds (a partial amendment, a schema rename, a partial correction, an older export imported), then the shape the
/// real record set showed (<c>corpus-amendments-supersession</c>: 97% of amendments leave other fields standing, and
/// a field can be amended more than once).
///
/// <para>The cases live here, in the test project, and are LINKED into the bench, so <c>memory-profile</c> runs the
/// code the tests pin. The bench writes one RV8 row per case; <see cref="ProfileFlawed"/> is the deliberately broken
/// projections that show each bucket can fail.</para>
/// </summary>
internal static class ProfileTrackA
{
    internal static readonly string[] Buckets =
    [
        "exact-reads", "zero-model-read", "corrections-history", "isolation", "concurrent-writes",
        "replayed-event", "partial-amendment", "schema-rename", "export-import",
    ];

    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Apr = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset May = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ProfileField City = new("u1", "address.city");
    private static readonly ProfileField Street = new("u1", "address.street");
    private static readonly ProfileField Zip = new("u1", "address.zip");

    private const int RaceTrials = 20;

    internal static IReadOnlyList<TrackACase> Cases { get; } = Build();

    /// <summary>
    /// What <see cref="ProfileOverCurated"/> is allowed to fail, by case, each with the limit that causes it. A
    /// declared gap is a finding of this gate, not an exception to it: the tests require the projection to fail
    /// exactly these, so closing one makes its entry fail until it is removed, and a new failure is a regression.
    /// RV28 closed the two Track A first declared (an import that deduplicated on content, and one event delivered
    /// to four writers at once), so the list is empty and every case must pass.
    /// </summary>
    internal static readonly IReadOnlyList<KnownGap> KnownGaps = [];

    /// <summary>Whether <paramref name="question"/> is a declared gap of the projection on <paramref name="backend"/>.</summary>
    internal static bool IsKnownGap(string question, string backend) =>
        KnownGaps.Any(g => g.Question == question && g.Backend == backend);

    /// <summary>Runs every case, each over its own fresh stores. A case that throws anything but its own
    /// <see cref="CaseFailed"/> failed too: a broken projection can crash as well as answer wrong.</summary>
    internal static async Task<IReadOnlyList<CaseOutcome>> RunAsync(
        Func<(ICuratedMemoryStore Store, IDisposable Lease)> openStore, Func<ICuratedMemoryStore, IProfileOption> build,
        IEnumerable<TrackACase>? cases = null)
    {
        var outcomes = new List<CaseOutcome>();
        foreach (var c in cases ?? Cases)
        {
            using var context = new TrackAContext(openStore, build);
            try
            {
                await c.Run(context);
                outcomes.Add(new CaseOutcome(c.Bucket, c.Id, null));
            }
            catch (CaseFailed failed)
            {
                outcomes.Add(new CaseOutcome(c.Bucket, c.Id, failed.Message));
            }
            catch (Exception ex)
            {
                outcomes.Add(new CaseOutcome(c.Bucket, c.Id, $"threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        return outcomes;
    }

    private static List<TrackACase> Build()
    {
        var cases = new List<TrackACase>();
        void Add(string bucket, string id, Func<TrackAContext, Task> body) => cases.Add(new TrackACase(bucket, id, body));

        // ── exact-reads: what was set is what a read returns, and nothing else is
        Add("exact-reads", "set-then-read", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(Zip, "75001", Jan, "chat:1");
            var read = await o.ReadAsync(City, Now);
            Is("Paris", read.Current?.Value, "the value");
            Is("chat:1", read.Current?.Source, "the source");
            Is(Jan, read.Current?.ValidFrom, "the start");
            Expect(read.Current?.EndedAt is null && !read.Disputed, "an open claim with no rival");
            Is("75001", (await o.ReadAsync(Zip, Now)).Current?.Value, "the other field's value");
        });
        Add("exact-reads", "an-unset-field-reads-nothing", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            var never = new ProfileField("u1", "address.country");
            Expect((await o.ReadAsync(never, Now)).Current is null, "a field never set reads a value");
            Expect(await o.AsOfAsync(never, Now) is null, "an as-of read of a field never set returns a claim");
            Expect((await o.HistoryAsync(never)).Count == 0, "a field never set has a history");
            Expect((await o.ReadAsync(City, Jan.AddDays(-1))).Current is null, "a read before the first start returns a claim");
        });
        Add("exact-reads", "neighbouring-field-ids-do-not-bleed", async c =>
        {
            var o = c.NewSite().Option;
            var ids = new[] { "address", "address.city", "address.cit", "address.city.old", "Address.City", "address.city " };
            foreach (var id in ids) await o.SetAsync(new ProfileField("u1", id), "value of " + id, Jan, "chat:1");
            foreach (var id in ids)
                Is("value of " + id, (await o.ReadAsync(new ProfileField("u1", id), Now)).Current?.Value, $"field '{id}'");
        });
        Add("exact-reads", "sixty-fields-all-read-exactly", async c =>
        {
            var o = c.NewSite().Option;
            for (var i = 0; i < 60; i++) await o.SetAsync(new ProfileField("u1", $"f.{i:D2}"), $"value {i}", Jan, "chat:1");
            for (var i = 0; i < 60; i++)
                Is($"value {i}", (await o.ReadAsync(new ProfileField("u1", $"f.{i:D2}"), Now)).Current?.Value, $"field {i}");
        });
        Add("exact-reads", "awkward-values-round-trip", async c =>
        {
            var o = c.NewSite().Option;
            var values = new[] { "Zürich — 東京", "line one\nline two", "a|b;c:d", new string('x', 5000), "  padded  ", "\"quoted\" {json}" };
            for (var i = 0; i < values.Length; i++) await o.SetAsync(new ProfileField("u1", $"v.{i}"), values[i], Jan, "chat:1");
            for (var i = 0; i < values.Length; i++)
                Is(values[i], (await o.ReadAsync(new ProfileField("u1", $"v.{i}"), Now)).Current?.Value, $"value {i}");
        });
        Add("exact-reads", "a-later-session-reads-what-an-earlier-one-wrote", async c =>
        {
            var first = c.NewSite();
            var paris = await first.Option.SetAsync(City, "Paris", Jan, "chat:1");
            await first.Option.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            var later = first.Reopen().Option;
            Is("Berlin", (await later.ReadAsync(City, Now)).Current?.Value, "the current value");
            SameClaims(await first.Option.HistoryAsync(City), await later.HistoryAsync(City), "the history");
        });

        // ── zero-model-read: a read asks no model, no search, no ranking, and writes nothing
        Add("zero-model-read", "the-profile-holds-no-model-seam", c =>
        {
            var type = c.NewSite().Option.GetType();
            var seams = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(k => k.GetParameters().Select(p => p.ParameterType))
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType))
                .Where(t => typeof(ITextClient).IsAssignableFrom(t) || typeof(IVectorProvider).IsAssignableFrom(t))
                .ToList();
            Expect(seams.Count == 0, $"{type.Name} can reach a model through {string.Join(", ", seams.Select(t => t.Name))}");
            return Task.CompletedTask;
        });
        Add("zero-model-read", "a-read-asks-the-store-only-exact-verbs", async c =>
        {
            var site = c.NewSite();
            var paris = await site.Option.SetAsync(City, "Paris", Jan, "chat:1");
            await site.Option.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            site.Store.Clear();
            await site.Option.ReadAsync(City, Now);
            await site.Option.HistoryAsync(City);
            await site.Option.AsOfAsync(City, Feb);
            var verbs = site.Store.Verbs;
            Expect(verbs.Count > 0, "the reads reached the store through no verb at all (the instrument saw nothing)");
            var other = verbs.Where(v => !RecordingCuratedStore.ExactReadVerbs.Contains(v)).Distinct().ToList();
            Expect(other.Count == 0, $"a read called {string.Join(", ", other)}");
        });
        Add("zero-model-read", "a-read-changes-nothing-it-stores", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            var before = await o.FingerprintAsync("u1");
            for (var i = 0; i < 3; i++)
            {
                await o.ReadAsync(City, Now);
                await o.HistoryAsync(City);
                await o.AsOfAsync(City, Feb);
            }

            Is(before, await o.FingerprintAsync("u1"), "what the subject stores after nine reads");
        });
        Add("zero-model-read", "the-current-value-is-validity-not-relevance", async c =>
        {
            var o = c.NewSite().Option;
            var old = await o.SetAsync(City, "Paris Paris Paris, the city of Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", old.Handle);
            var read = await o.ReadAsync(City, Now);
            Is("Berlin", read.Current?.Value, "the current value beside a better-matching older text");
            Expect(!read.Disputed, "an ended claim reported as a rival");
        });

        // ── corrections-history: a correction ends and never edits, so the past stays readable
        Add("corrections-history", "a-correction-replaces-the-current-claim-and-history-keeps-the-old-one", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            Is(Correction.Applied, await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle), "the outcome");
            Is("Berlin", (await o.ReadAsync(City, Now)).Current?.Value, "the current value");
            Is("Paris", (await o.AsOfAsync(City, Feb))?.Value, "the value in February");
            Expect(await o.AsOfAsync(City, Jan.AddDays(-1)) is null, "a claim before the first start");
            var history = await o.HistoryAsync(City);
            Seq(["Paris", "Berlin"], history.Select(h => h.Value), "the history");
            Is(Mar, history[0].EndedAt, "the old claim's end");
            Expect(history[1].EndedAt is null, "the new claim is open");
            Is("chat:1", history[0].Source, "the old claim's source");
            Is("chat:2", history[1].Source, "the new claim's source");
        });
        Add("corrections-history", "a-second-correction-and-every-past-value", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            var berlin = (await o.ReadAsync(City, Now)).Current!;
            Is(Correction.Applied, await o.CorrectAsync(City, "Lyon", May, "chat:3", berlin.Handle), "the second correction");
            Is("Lyon", (await o.ReadAsync(City, Now)).Current?.Value, "now");
            Is("Paris", (await o.AsOfAsync(City, Feb))?.Value, "in February");
            Is("Berlin", (await o.AsOfAsync(City, Apr))?.Value, "in April");
            Is("Lyon", (await o.AsOfAsync(City, May))?.Value, "at the second correction's own start");
            Seq(["Paris", "Berlin", "Lyon"], (await o.HistoryAsync(City)).Select(h => h.Value), "the history");
        });
        Add("corrections-history", "a-value-that-returns-is-a-new-claim", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            await o.CorrectAsync(City, "Paris", Jun, "chat:3", (await o.ReadAsync(City, Now)).Current!.Handle);
            var history = await o.HistoryAsync(City);
            Seq(["Paris", "Berlin", "Paris"], history.Select(h => h.Value), "the history");
            Is(3, history.Select(h => h.ClaimId).Distinct().Count(), "distinct claim ids");
            Is("Paris", (await o.ReadAsync(City, Now)).Current?.Value, "now");
            Is("Berlin", (await o.AsOfAsync(City, Apr))?.Value, "in April");
        });
        Add("corrections-history", "a-correction-naming-no-claim-is-missing-and-writes-nothing", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            var before = await o.FingerprintAsync("u1");
            Is(Correction.Missing, await o.CorrectAsync(City, "Berlin", Mar, "chat:2", "999999:1"), "the outcome");
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
        });
        Add("corrections-history", "a-correction-from-a-stale-read-is-refused", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            var before = await o.FingerprintAsync("u1");
            Is(Correction.Stale, await o.CorrectAsync(City, "Lyon", Apr, "chat:3", paris.Handle), "the outcome");
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
            Is("Berlin", (await o.ReadAsync(City, Now)).Current?.Value, "the current value");
        });
        Add("corrections-history", "a-correction-cannot-begin-before-the-claim-it-replaces", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Mar, "chat:1");
            var before = await o.FingerprintAsync("u1");
            var refused = false;
            try { await o.CorrectAsync(City, "Berlin", Jan, "chat:2", paris.Handle); }
            catch (ArgumentException) { refused = true; }
            Expect(refused, "a correction that begins before its predecessor was accepted");
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
        });
        Add("corrections-history", "a-capacity-sweep-removes-no-claim", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            for (var i = 0; i < 30; i++) await o.SetAsync(new ProfileField("u1", $"filler.{i}"), $"value {i}", Jan, "chat:1");
            Is(0, await o.PruneAsync("u1"), "claims the sweep removed");
            Is(2, (await o.HistoryAsync(City)).Count, "the history's length");
        });

        // ── isolation: a subject is a boundary, and nothing a tenant does reaches another's claims
        Add("isolation", "reads-do-not-cross-subjects", async c =>
        {
            var o = c.NewSite().Option;
            var u2 = new ProfileField("u2", "address.city");
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(u2, "Oslo", Jan, "chat:9");
            Is("Paris", (await o.ReadAsync(City, Now)).Current?.Value, "u1's value");
            Is("Oslo", (await o.ReadAsync(u2, Now)).Current?.Value, "u2's value");
            Is("Oslo", (await o.AsOfAsync(u2, Feb))?.Value, "u2's value in February");
            Is(1, (await o.HistoryAsync(City)).Count, "u1's history");
            Expect((await o.ReadAsync(new ProfileField("u3", "address.city"), Now)).Current is null, "a subject never written reads a value");
        });
        Add("isolation", "a-subject-name-is-not-a-prefix-match", async c =>
        {
            var o = c.NewSite().Option;
            var near = new ProfileField("u10", "address.city");
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(near, "Rome", Jan, "chat:1");
            await o.ForgetAsync("u1");
            Is("Rome", (await o.ReadAsync(near, Now)).Current?.Value, "the neighbour after u1 was forgotten");
            using var export = new StringWriter();
            await o.ExportAsync("u10", export);
            Expect(!export.ToString().Contains("Paris"), "u10's export carries u1's claim");
        });
        Add("isolation", "a-forget-removes-the-subject-everywhere-and-touches-no-other", async c =>
        {
            var o = c.NewSite().Option;
            var u2 = new ProfileField("u2", "address.city");
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            await o.SetAsync(u2, "Oslo", Jan, "chat:9");
            var other = await o.FingerprintAsync("u2");

            await o.ForgetAsync("u1");

            Expect((await o.ReadAsync(City, Now)).Current is null, "a forgotten subject still reads");
            Expect(await o.AsOfAsync(City, Feb) is null, "a forgotten subject still answers as-of");
            Is(0, (await o.HistoryAsync(City)).Count, "the forgotten history");
            using var export = new StringWriter();
            Is(0, await o.ExportAsync("u1", export), "the forgotten export's lines");
            Is("", export.ToString(), "the forgotten export");
            Is(other, await o.FingerprintAsync("u2"), "what u2 stores");
        });
        Add("isolation", "an-export-holds-only-its-subject", async c =>
        {
            var source = c.NewSite().Option;
            await source.SetAsync(City, "Paris", Jan, "chat:1");
            await source.SetAsync(new ProfileField("u2", "address.city"), "Oslo", Jan, "chat:9");
            using var file = new StringWriter();
            Is(1, await source.ExportAsync("u1", file), "the export's lines");
            var target = c.NewSite().Option;
            await target.ImportAsync("u1", new StringReader(file.ToString()));
            Is("Paris", (await target.ReadAsync(City, Now)).Current?.Value, "u1 in the target");
            Expect((await target.ReadAsync(new ProfileField("u2", "address.city"), Now)).Current is null, "u2 travelled in u1's file");
        });
        Add("isolation", "one-subjects-correction-leaves-another-untouched", async c =>
        {
            var o = c.NewSite().Option;
            var u2 = new ProfileField("u2", "address.city");
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(u2, "Oslo", Jan, "chat:9");
            var other = await o.FingerprintAsync("u2");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            Is(other, await o.FingerprintAsync("u2"), "what u2 stores");
        });
        Add("isolation", "a-handle-from-another-subject-corrects-nothing", async c =>
        {
            var o = c.NewSite().Option;
            var u2 = new ProfileField("u2", "address.city");
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(u2, "Oslo", Jan, "chat:9");
            var before = await o.FingerprintAsync("u1");
            var outcome = await o.CorrectAsync(u2, "Rome", Mar, "chat:2", paris.Handle);
            Expect(outcome != Correction.Applied, "a correction of u2's field was applied from u1's claim");
            Is(before, await o.FingerprintAsync("u1"), "what u1 stores");
            Expect((await o.HistoryAsync(City)).Single().EndedAt is null, "u1's claim was ended by u2's correction");
            Is(1, (await o.HistoryAsync(u2)).Count, "u2's history");
        });

        // ── concurrent-writes: of two writers on one claim exactly one wins, and every other write lands
        Add("concurrent-writes", "two-writers-correct-one-claim-and-one-wins", async c =>
        {
            var o = c.NewSite().Option;
            var problems = new List<string>();
            for (var t = 0; t < RaceTrials; t++)
            {
                var field = new ProfileField($"race-{t}", "address.city");
                var paris = await o.SetAsync(field, "Paris", Jan, "chat:1");
                var outcomes = await Race(o, field, paris.Handle, [("Berlin", "chat:2"), ("Lyon", "chat:3")]);
                var verdict = RaceVerdict(outcomes, await o.ReadAsync(field, Now), await o.HistoryAsync(field), ["Berlin", "Lyon"]);
                if (verdict is not null) problems.Add($"trial {t}: {verdict}");
            }

            Expect(problems.Count == 0, $"{problems.Count} of {RaceTrials} trials: {problems.FirstOrDefault()}");
        });
        Add("concurrent-writes", "eight-writers-correct-one-claim-and-one-wins", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            var writers = Enumerable.Range(0, 8).Select(i => ($"city {i}", $"chat:{i}")).ToArray();
            var outcomes = await Race(o, City, paris.Handle, writers);
            var verdict = RaceVerdict(outcomes, await o.ReadAsync(City, Now), await o.HistoryAsync(City), writers.Select(w => w.Item1).ToArray());
            Expect(verdict is null, verdict ?? "");
        });
        Add("concurrent-writes", "writes-to-different-fields-all-land", async c =>
        {
            var o = c.NewSite().Option;
            var fields = Enumerable.Range(0, 16).Select(i => new ProfileField("u1", $"f.{i:D2}")).ToArray();
            await Task.WhenAll(fields.Select(f => o.SetAsync(f, "first " + f.Field, Jan, "chat:1")));
            var handles = new Dictionary<string, string>();
            foreach (var f in fields) handles[f.Field] = (await o.ReadAsync(f, Now)).Current!.Handle;
            var outcomes = await Task.WhenAll(fields.Select(f => Task.Run(() =>
                o.CorrectAsync(f, "second " + f.Field, Mar, "chat:2", handles[f.Field]))));
            Expect(outcomes.All(x => x == Correction.Applied), $"{outcomes.Count(x => x != Correction.Applied)} of 16 corrections were refused");
            foreach (var f in fields)
            {
                var read = await o.ReadAsync(f, Now);
                Is("second " + f.Field, read.Current?.Value, $"field {f.Field}");
                Expect(!read.Disputed && (await o.HistoryAsync(f)).Count == 2, $"field {f.Field} kept a stray claim");
            }
        });
        Add("concurrent-writes", "one-event-delivered-to-four-writers-at-once-is-one-claim", async c =>
        {
            var o = c.NewSite().Option;
            var problems = new List<string>();
            for (var t = 0; t < RaceTrials; t++)
            {
                var field = new ProfileField($"replay-{t}", "address.city");
                await Together(4, () => o.SetAsync(field, "Paris", Jan, "chat:1"));
                // stored entries are counted beside the history, because a read may fold copies of one claim into one
                var claims = (await o.HistoryAsync(field)).Count;
                var stored = StoredEntries(await o.FingerprintAsync(field.Subject), field);
                if (claims != 1 || stored != 1)
                {
                    problems.Add($"trial {t}: {claims} claims in {stored} entries after a set delivered four times");
                    continue;
                }

                var paris = (await o.ReadAsync(field, Now)).Current!;
                await Together(4, () => o.CorrectAsync(field, "Berlin", Mar, "chat:2", paris.Handle));
                var read = await o.ReadAsync(field, Now);
                var history = (await o.HistoryAsync(field)).Count;
                stored = StoredEntries(await o.FingerprintAsync(field.Subject), field);
                if (read.Current?.Value != "Berlin" || read.Disputed || history != 2 || stored != 2)
                    problems.Add($"trial {t}: after a correction delivered four times, '{read.Current?.Value}', disputed {read.Disputed}, {history} claims in {stored} entries");
            }

            Expect(problems.Count == 0, $"{problems.Count} of {RaceTrials} trials: {problems.FirstOrDefault()}");
        });

        // ── replayed-event: the same event twice is a no-op, however late it arrives
        Add("replayed-event", "a-replayed-set-writes-nothing", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            var before = await o.FingerprintAsync("u1");
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
            Is(1, (await o.HistoryAsync(City)).Count, "the history's length");
        });
        Add("replayed-event", "a-replayed-correction-writes-nothing", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            var before = await o.FingerprintAsync("u1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
            Is(2, (await o.HistoryAsync(City)).Count, "the history's length");
        });
        Add("replayed-event", "an-old-event-replayed-after-the-field-moved-on", async c =>
        {
            var o = c.NewSite().Option;
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            await o.CorrectAsync(City, "Lyon", Jun, "chat:3", (await o.ReadAsync(City, Now)).Current!.Handle);
            var before = await o.FingerprintAsync("u1");
            await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            Is(before, await o.FingerprintAsync("u1"), "what the subject stores");
            Is("Lyon", (await o.ReadAsync(City, Now)).Current?.Value, "the current value");
        });
        Add("replayed-event", "an-export-imported-twice-adds-nothing", async c =>
        {
            var source = c.NewSite().Option;
            var paris = await source.SetAsync(City, "Paris", Jan, "chat:1");
            await source.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            using var file = new StringWriter();
            await source.ExportAsync("u1", file);
            var target = c.NewSite().Option;
            Is(2, await target.ImportAsync("u1", new StringReader(file.ToString())), "claims the first import added");
            var before = await target.FingerprintAsync("u1");
            Is(0, await target.ImportAsync("u1", new StringReader(file.ToString())), "claims the second import added");
            Is(before, await target.FingerprintAsync("u1"), "what the subject stores");
        });

        // ── partial-amendment (D220): one field changes and the others stand, because a field is the unit
        Add("partial-amendment", "one-field-changes-and-the-others-stand", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(Street, "1 Rue Lafayette", Jan, "chat:1");
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(Zip, "75001", Jan, "chat:1");
            await o.CorrectAsync(City, "Lyon", Mar, "chat:2", paris.Handle);
            Is("Lyon", (await o.ReadAsync(City, Now)).Current?.Value, "the amended field");
            Is("1 Rue Lafayette", (await o.ReadAsync(Street, Now)).Current?.Value, "a standing field");
            Is("75001", (await o.ReadAsync(Zip, Now)).Current?.Value, "a standing field");
            Is(1, (await o.HistoryAsync(Street)).Count, "a standing field's history");
            Is(1, (await o.HistoryAsync(Zip)).Count, "a standing field's history");
            Is("Paris", (await o.AsOfAsync(City, Feb))?.Value, "the amended field before the amendment");
        });
        Add("partial-amendment", "a-correction-writes-one-field-and-no-other", async c =>
        {
            var o = c.NewSite().Option;
            await o.SetAsync(Street, "1 Rue Lafayette", Jan, "chat:1");
            var paris = await o.SetAsync(City, "Paris", Jan, "chat:1");
            await o.SetAsync(Zip, "75001", Jan, "chat:1");
            var before = Rows(await o.FingerprintAsync("u1")).Where(r => r.Kind != City.Field).ToList();
            await o.CorrectAsync(City, "Lyon", Mar, "chat:2", paris.Handle);
            var after = Rows(await o.FingerprintAsync("u1")).Where(r => r.Kind != City.Field).ToList();
            Seq(before.Select(r => r.Text), after.Select(r => r.Text), "the rows of the other fields");
        });
        Add("partial-amendment", "a-record-of-many-fields-amended-in-turn", async c =>
        {
            // the real records' shape: a median of five fields, some amended once, one twice
            var o = c.NewSite().Option;
            var fields = Enumerable.Range(0, 8).Select(i => new ProfileField("d1", $"section.{i}")).ToArray();
            foreach (var f in fields) await o.SetAsync(f, "original " + f.Field, Jan, "record");
            async Task Amend(int i, string value, DateTimeOffset at) =>
                Is(Correction.Applied, await o.CorrectAsync(fields[i], value, at, "note",
                    (await o.ReadAsync(fields[i], Now)).Current!.Handle), $"amending field {i}");
            await Amend(2, "second 2", Feb);
            await Amend(5, "second 5", Mar);
            await Amend(2, "third 2", Apr);
            await Amend(7, "second 7", May);

            var expect = new[] { "original section.0", "original section.1", "third 2", "original section.3",
                "original section.4", "second 5", "original section.6", "second 7" };
            for (var i = 0; i < 8; i++) Is(expect[i], (await o.ReadAsync(fields[i], Now)).Current?.Value, $"field {i} now");
            Is(3, (await o.HistoryAsync(fields[2])).Count, "the twice-amended field's history");
            Is(2, (await o.HistoryAsync(fields[5])).Count, "an amended field's history");
            Is(1, (await o.HistoryAsync(fields[0])).Count, "a standing field's history");
            var midMarch = Mar.AddDays(15);
            Is("second 2", (await o.AsOfAsync(fields[2], midMarch))?.Value, "the twice-amended field in mid-March");
            Is("second 5", (await o.AsOfAsync(fields[5], midMarch))?.Value, "an amended field in mid-March");
            Is("original section.7", (await o.AsOfAsync(fields[7], midMarch))?.Value, "a field amended later, in mid-March");
        });
        Add("partial-amendment", "one-claim-per-field-because-a-bundle-makes-the-second-writer-stale", async c =>
        {
            var o = c.NewSite().Option;
            var bundle = new ProfileField("u1", "address");
            var b = await o.SetAsync(bundle, "1 Rue Lafayette|Paris", Jan, "chat:1");
            Is(Correction.Applied, await o.CorrectAsync(bundle, "2 Rue Lafayette|Paris", Mar, "chat:2", b.Handle), "the first writer");
            Is(Correction.Stale, await o.CorrectAsync(bundle, "1 Rue Lafayette|Lyon", Mar, "chat:3", b.Handle), "the second writer");
            Is("2 Rue Lafayette|Paris", (await o.ReadAsync(bundle, Now)).Current?.Value, "the bundle");
        });

        // ── schema-rename (D220's second authority): the claims move and the history moves with them
        Add("schema-rename", "a-rename-moves-every-claim-with-its-history", async c =>
        {
            var o = c.NewSite().Option;
            var old = new ProfileField("u1", "home_city");
            var renamed = new ProfileField("u1", "address.city");
            var paris = await o.SetAsync(old, "Paris", Jan, "chat:1");
            await o.CorrectAsync(old, "Berlin", Mar, "chat:2", paris.Handle);
            var before = await o.HistoryAsync(old);

            Is(2, await o.RenameAsync("u1", "home_city", "address.city"), "claims moved");

            Is("Berlin", (await o.ReadAsync(renamed, Now)).Current?.Value, "the renamed field");
            Expect((await o.ReadAsync(old, Now)).Current is null, "the old id still reads a value");
            Is(0, (await o.HistoryAsync(old)).Count, "the old id's history");
            var after = await o.HistoryAsync(renamed);
            SameClaims(before, after, "the history");
            Is(Correction.Applied, await o.CorrectAsync(renamed, "Lyon", Jun, "chat:3", after[1].Handle), "a correction of the renamed field");
        });
        Add("schema-rename", "a-rename-touches-no-other-field-and-no-other-subject", async c =>
        {
            var o = c.NewSite().Option;
            var home = new ProfileField("u1", "home_city");
            var other = new ProfileField("u2", "home_city");
            await o.SetAsync(home, "Paris", Jan, "chat:1");
            await o.SetAsync(Zip, "75001", Jan, "chat:1");
            await o.SetAsync(other, "Oslo", Jan, "chat:9");
            var untouched = await o.FingerprintAsync("u2");
            var zips = Rows(await o.FingerprintAsync("u1")).Where(r => r.Kind == Zip.Field).Select(r => r.Text).ToList();

            await o.RenameAsync("u1", "home_city", "address.city");

            Is("Oslo", (await o.ReadAsync(other, Now)).Current?.Value, "u2's field of the same id");
            Is(untouched, await o.FingerprintAsync("u2"), "what u2 stores");
            Seq(zips, Rows(await o.FingerprintAsync("u1")).Where(r => r.Kind == Zip.Field).Select(r => r.Text), "the other field's rows");
        });
        Add("schema-rename", "an-export-from-before-a-rename-brings-the-old-id-back-and-renaming-again-merges", async c =>
        {
            var o = c.NewSite().Option;
            var old = new ProfileField("u1", "home_city");
            var renamed = new ProfileField("u1", "address.city");
            var paris = await o.SetAsync(old, "Paris", Jan, "chat:1");
            using var preRename = new StringWriter();
            await o.ExportAsync("u1", preRename);
            await o.CorrectAsync(old, "Berlin", Mar, "chat:2", paris.Handle);
            await o.RenameAsync("u1", "home_city", "address.city");

            await o.ImportAsync("u1", new StringReader(preRename.ToString()));
            Is("Paris", (await o.HistoryAsync(old)).SingleOrDefault()?.Value, "the old id after the old file came back");
            Is("Berlin", (await o.ReadAsync(renamed, Now)).Current?.Value, "the renamed field is untouched");

            await o.RenameAsync("u1", "home_city", "address.city");
            Is(0, (await o.HistoryAsync(old)).Count, "the old id after renaming again");
            Seq(["Berlin", "Paris"], (await o.HistoryAsync(renamed)).Select(h => h.Value).Order(), "the merged history");
            Is("Berlin", (await o.ReadAsync(renamed, Now)).Current?.Value, "the current value after the merge");
        });

        // ── export-import: a file is evidence from a time and never an overwrite (D204, D220's older export)
        Add("export-import", "an-export-imported-elsewhere-reads-and-remembers-alike", async c =>
        {
            var source = c.NewSite().Option;
            var paris = await source.SetAsync(City, "Paris", Jan, "chat:1");
            await source.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            await source.SetAsync(Zip, "10115", Mar, "chat:2");
            using var file = new StringWriter();
            Is(3, await source.ExportAsync("u1", file), "the export's lines");
            var target = c.NewSite().Option;
            Is(3, await target.ImportAsync("u1", new StringReader(file.ToString())), "claims imported");
            SameClaims(await source.HistoryAsync(City), await target.HistoryAsync(City), "the history");
            Is("Berlin", (await target.ReadAsync(City, Now)).Current?.Value, "the current value");
            Is("Paris", (await target.AsOfAsync(City, Feb))?.Value, "the value in February");
        });
        Add("export-import", "a-value-that-returns-survives-an-export", async c =>
        {
            var source = c.NewSite().Option;
            var paris = await source.SetAsync(City, "Paris", Jan, "chat:1");
            await source.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            await source.CorrectAsync(City, "Paris", Jun, "chat:3", (await source.ReadAsync(City, Now)).Current!.Handle);
            using var file = new StringWriter();
            await source.ExportAsync("u1", file);
            var target = c.NewSite().Option;
            await target.ImportAsync("u1", new StringReader(file.ToString()));
            Seq(["Paris", "Berlin", "Paris"], (await target.HistoryAsync(City)).Select(h => h.Value), "the history");
            Is("Paris", (await target.ReadAsync(City, Now)).Current?.Value, "the current value");
        });
        Add("export-import", "an-older-export-over-newer-state-is-a-dispute-and-never-wins", async c =>
        {
            var archive = c.NewSite().Option;
            await archive.SetAsync(City, "Paris", Jan, "chat:1");
            using var old = new StringWriter();
            await archive.ExportAsync("u1", old);
            var target = c.NewSite().Option;
            await target.SetAsync(City, "Berlin", May, "chat:2");

            Is(1, await target.ImportAsync("u1", new StringReader(old.ToString())), "claims imported");

            var read = await target.ReadAsync(City, Now);
            Is("Berlin", read.Current?.Value, "the current value");
            Expect(read.Disputed, "the older claim is not reported");
            Is("Paris", read.Rivals.SingleOrDefault()?.Value, "the rival");
            Seq(["Paris", "Berlin"], (await target.HistoryAsync(City)).Select(h => h.Value), "the history");
        });
        Add("export-import", "an-older-export-never-rewrites-or-reopens-what-the-target-holds", async c =>
        {
            // the file was taken when Berlin was open; the target has since ended it
            var archive = c.NewSite().Option;
            var paris = await archive.SetAsync(City, "Paris", Jan, "chat:1");
            await archive.CorrectAsync(City, "Berlin", Mar, "chat:2", paris.Handle);
            using var old = new StringWriter();
            await archive.ExportAsync("u1", old);

            var target = c.NewSite().Option;
            var tParis = await target.SetAsync(City, "Paris", Jan, "chat:1");
            await target.CorrectAsync(City, "Berlin", Mar, "chat:2", tParis.Handle);
            await target.CorrectAsync(City, "Lyon", Jun, "chat:3", (await target.ReadAsync(City, Now)).Current!.Handle);
            var before = Rows(await target.FingerprintAsync("u1")).Select(r => r.Text).ToList();

            await target.ImportAsync("u1", new StringReader(old.ToString()));

            var read = await target.ReadAsync(City, Now);
            Is("Lyon", read.Current?.Value, "the current value");
            Expect(!read.Disputed, "an older file reopened a claim the target had ended");
            var after = Rows(await target.FingerprintAsync("u1")).Select(r => r.Text).ToList();
            Expect(before.All(after.Contains), "the import rewrote a claim the target already held");
        });

        return cases;
    }

    /// <summary>Starts <paramref name="count"/> calls of one event together, released by one gate.</summary>
    private static async Task Together<T>(int count, Func<Task<T>> call)
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await call();
        })).ToArray();
        gate.SetResult();
        await Task.WhenAll(tasks);
    }

    /// <summary>Releases <paramref name="writers"/> together, each correcting the claim named by one handle.</summary>
    private static async Task<Correction[]> Race(
        IProfileOption o, ProfileField field, string handle, IReadOnlyList<(string Value, string Source)> writers)
    {
        var gate = new TaskCompletionSource();
        var tasks = writers.Select(w => Task.Run(async () =>
        {
            await gate.Task;
            return await o.CorrectAsync(field, w.Value, Mar, w.Source, handle);
        })).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    /// <summary>One winner: exactly one writer told it applied, the others told it was stale, one current claim
    /// that is the winner's, no rival, and a history of the original and the winner.</summary>
    private static string? RaceVerdict(Correction[] outcomes, ProfileRead read, IReadOnlyList<ProfileClaim> history, string[] values)
    {
        var applied = outcomes.Select((o, i) => (o, i)).Where(x => x.o == Correction.Applied).ToList();
        if (applied.Count != 1) return $"{applied.Count} writers were told they applied";
        if (outcomes.Count(o => o == Correction.Stale) != outcomes.Length - 1) return "a writer that lost was not told it was stale";
        if (read.Current is null) return "no claim holds";
        if (read.Current.Value != values[applied[0].i]) return $"reads return '{read.Current.Value}', not the winner's '{values[applied[0].i]}'";
        if (read.Disputed) return "the refused writer's claim is reported as a rival";
        return history.Count == 2 ? null : $"the history holds {history.Count} claims";
    }

    private static int StoredEntries(string fingerprint, ProfileField field) =>
        Rows(fingerprint).Count(r => r.Kind == field.Field);

    private readonly record struct Row(string Kind, string Text);

    /// <summary><see cref="IProfileOption.FingerprintAsync"/> split into its rows (<c>id:revision:enabled:kind:updated</c>).</summary>
    private static List<Row> Rows(string fingerprint) =>
        [.. fingerprint.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(r => new Row(r.Split(':')[3], r))];

    private static void Expect(bool ok, string what)
    {
        if (!ok) throw new CaseFailed(what);
    }

    private static void Is<T>(T expected, T actual, string what) =>
        Expect(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: expected {Show(expected)}, got {Show(actual)}");

    private static void Seq(IEnumerable<string> expected, IEnumerable<string> actual, string what)
    {
        var (e, a) = (expected.ToList(), actual.ToList());
        Expect(e.SequenceEqual(a), $"{what}: expected [{string.Join(", ", e)}], got [{string.Join(", ", a)}]");
    }

    /// <summary>The claims alike apart from the store-defined handle, which never travels.</summary>
    private static void SameClaims(IEnumerable<ProfileClaim> expected, IEnumerable<ProfileClaim> actual, string what) =>
        Seq(expected.Select(Shape), actual.Select(Shape), what);

    private static string Shape(ProfileClaim c) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.ClaimId}={c.Value}@{c.ValidFrom:O}..{c.EndedAt:O} ({c.Source})");

    private static string Show<T>(T value) => value is null ? "null" : $"'{value}'";
}
