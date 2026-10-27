using System.Globalization;
using Lyntai.Memory.Engines;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// OPTION A, a PROJECTION over curated memory: each claim is one curated entry (<c>Kind</c> = the field id,
/// <c>Content</c> = the value, <c>Enabled</c> = holds now), and the profile is whatever the entries say. The
/// profile keeps no state of its own, so the entries are the authority and dropping the profile loses nothing.
///
/// <para><b>What the store gives and what this adds.</b> Curated memory has revision-checked updates, enable and
/// disable, a kind to re-categorise in place, and portable export. It has NO valid time and no history, so
/// <c>from</c>, <c>ended</c> and the successor live in the entry's app-owned metadata, and every rule that
/// reads them (latest start wins, as-of) is this class's. A correction is two writes (end the old entry behind a
/// revision check, then add the new one), because the store has no atomic pair.</para>
/// </summary>
internal sealed class ProfileOverCurated(ICuratedMemoryStore store) : IProfileOption
{
    private const string Claim = "claim", From = "from", Src = "src", Ended = "ended", By = "by";

    // kind null: the portable verbs span every section, and a field id IS the section. A section-bound engine
    // (the default "memory") would export nothing here, silently.
    private readonly CuratedMemoryEngine _engine = new("profile/curated", store, kind: null);

    public string Name => "curated-projection";

    public async Task<ProfileClaim> SetAsync(ProfileField field, string value, DateTimeOffset from, string source)
    {
        var id = ProfileClaims.IdOf(field, value, from, source);
        if (await FindAsync(field, id) is { } existing) return Claim_(existing);
        var meta = new Dictionary<string, string> { [Claim] = id, [From] = Iso(from), [Src] = source };
        var row = await store.AddAsync(field.Field, value, true, field.TaskKey, null, false, meta);
        return Claim_((await store.GetAsync(row))!);
    }

    public async Task<ProfileRead> ReadAsync(ProfileField field, DateTimeOffset now)
    {
        var rows = await store.ListAsync(field.Field, true, field.TaskKey);
        return ProfileClaims.Resolve(rows.Where(r => Holds(r, now)).Select(r => (Claim_(r), r.Id)));
    }

    public async Task<Correction> CorrectAsync(
        ProfileField field, string value, DateTimeOffset from, string source, string basedOn)
    {
        var id = ProfileClaims.IdOf(field, value, from, source);
        if (await FindAsync(field, id) is not null) return Correction.Applied;
        var (rowId, revision) = Parse(basedOn);
        if (await store.GetAsync(rowId) is not { } old) return Correction.Missing;
        if (from < Start(old)) throw new ArgumentException("A correction cannot begin before the claim it replaces.");

        var ended = new Dictionary<string, string>(old.Metadata ?? new Dictionary<string, string>())
        {
            [Ended] = Iso(from), [By] = id,
        };
        var gate = await ((IRevisionedCuratedMemoryStore)store).UpdateAsync(
            rowId, revision, enabled: false, metadata: ended);
        if (gate == CuratedUpdateOutcome.NotFound) return Correction.Missing;
        if (gate != CuratedUpdateOutcome.Updated) return Correction.Stale;

        var meta = new Dictionary<string, string> { [Claim] = id, [From] = Iso(from), [Src] = source };
        await store.AddAsync(field.Field, value, true, field.TaskKey, null, false, meta);
        return Correction.Applied;
    }

    public async Task<IReadOnlyList<ProfileClaim>> HistoryAsync(ProfileField field)
    {
        var rows = await store.ListAsync(field.Field, false, field.TaskKey);
        return [.. rows.OrderBy(Start).ThenBy(r => r.Id).Select(Claim_)];
    }

    public async Task<ProfileClaim?> AsOfAsync(ProfileField field, DateTimeOffset at)
    {
        var rows = await store.ListAsync(field.Field, false, field.TaskKey);
        return ProfileClaims.Resolve(rows.Where(r => Holds(r, at)).Select(r => (Claim_(r), r.Id))).Current;
    }

    public async Task ForgetAsync(string subject)
    {
        // the engine has no forget, so the projection removes row by row: a fault part-way leaves the rest
        foreach (var row in await store.ListAsync(taskKey: new ProfileField(subject, "").TaskKey))
            await store.RemoveAsync(row.Id);
    }

    public async Task<int> ExportAsync(string subject, TextWriter destination) =>
        (await _engine.ExportAsync(new ProfileField(subject, "").TaskKey, null, destination)).Exported;

    public async Task<int> ImportAsync(string subject, TextReader source) =>
        (await _engine.ImportAsync(new ProfileField(subject, "").TaskKey, null, source)).Imported;

    public async Task<int> RenameAsync(string subject, string from, string to)
    {
        var moved = 0;
        var task = new ProfileField(subject, from).TaskKey;
        // a re-categorise in place keeps each entry's id and creation time; one that would collide is refused, and
        // is a claim the new id already holds (an older export brought it back), so the stray copy is removed
        foreach (var row in await store.ListAsync(from, false, task))
            if (await store.UpdateAsync(row.Id, kind: to)) moved++;
            else if (await FindAsync(new ProfileField(subject, to), row.Metadata![Claim]) is not null && await store.RemoveAsync(row.Id)) moved++;
        return moved;
    }

    // a curated catalog has no cap, no TTL and no decay, so there is no capacity sweep that could remove a claim
    public Task<int> PruneAsync(string subject) => Task.FromResult(0);

    public async Task<string> FingerprintAsync(string subject)
    {
        var rows = await store.ListAsync(taskKey: new ProfileField(subject, "").TaskKey);
        return string.Join(";", rows.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Revision}:{r.Enabled}:{r.Kind}:{r.UpdatedAt:O}"));
    }

    private async Task<CuratedMemory?> FindAsync(ProfileField field, string claimId) =>
        (await store.ListAsync(field.Field, false, field.TaskKey, metadataMatch: new Dictionary<string, string> { [Claim] = claimId }))
        .FirstOrDefault();

    private static bool Holds(CuratedMemory row, DateTimeOffset at) =>
        Start(row) <= at && (End(row) is not { } end || at < end);

    private static DateTimeOffset Start(CuratedMemory row) => DateTimeOffset.Parse(row.Metadata![From], CultureInfo.InvariantCulture);

    private static DateTimeOffset? End(CuratedMemory row) =>
        row.Metadata!.TryGetValue(Ended, out var e) ? DateTimeOffset.Parse(e, CultureInfo.InvariantCulture) : null;

    private static ProfileClaim Claim_(CuratedMemory row) =>
        new(row.Metadata![Claim], row.Content, Start(row), End(row), row.Metadata![Src], $"{row.Id}:{row.Revision}");

    private static (long Id, long Revision) Parse(string handle)
    {
        var parts = handle.Split(':');
        return (long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
}
