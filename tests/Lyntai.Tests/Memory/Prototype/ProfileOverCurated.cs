using System.Globalization;
using System.Text;
using System.Text.Json;
using Lyntai.Memory.Engines;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>What an import did with a file: the claims it added, the ones the subject already held (found by claim
/// id, never by content) and the lines it refused because they no longer match their checksum.</summary>
internal sealed record ProfileImportReport(int Imported, int Present, int Rejected);

/// <summary>One slot of a subject's profile as a snapshot reads it.</summary>
internal sealed record ProfileFieldView(string Subject, string Field, string? Scope, ProfileRead Read);

/// <summary>A subject's fields at one moment, read with the composition verb and nothing else. <paramref name="Version"/>
/// changes whenever the claim a field stands on changes, so anything derived from the snapshot can say whether it is
/// still about this profile.</summary>
internal sealed record ProfileSnapshot(string Subject, string Version, IReadOnlyList<ProfileFieldView> Fields)
{
    internal ProfileFieldView? Find(string field, string? scope = null) =>
        Fields.FirstOrDefault(f => f.Field == field && f.Scope == scope);
}

/// <summary>A narrative a caller derived from a profile. It is a CACHE: it never answers a read, it is returned with
/// whether it still describes the profile it was written for, and an export does not carry it.</summary>
internal sealed record ProfileSummary(string Text, bool Fresh);

/// <summary>
/// The RV28 prototype: a PROJECTION over curated memory (<b>D224</b>), each claim one curated entry (<c>Kind</c> = the
/// field id, <c>Content</c> = the value, the task key = the subject, <c>Scope</c> = the claim's scope), and the profile
/// is whatever the entries say. The profile keeps no state of its own, so the entries are the authority and dropping
/// the profile loses nothing.
///
/// <para><b>What the store gives and what this adds.</b> Curated memory has revision-checked updates, enable and
/// disable, a kind to re-categorise in place, and portable export. It has NO valid time and no history, so
/// <c>from</c>, <c>ended</c> and the successor live in the entry's app-owned metadata, and every rule that reads them
/// (authority, then latest start; as-of) is this class's. Each claim carries a checksum of what it says, so an entry
/// edited by another tool is reported and never read. A correction is two writes (end the old entry behind a revision
/// check, then add the new one) because the store has no atomic pair: a crash between them leaves the field with no
/// current claim, and delivering the same event again completes it.</para>
/// </summary>
internal sealed class ProfileOverCurated(ICuratedMemoryStore store) : IProfileOption
{
    internal const string SummaryKind = "profile.summary";

    private const string Claim = "claim", From = "from", Src = "src", Ended = "ended", By = "by", Auth = "auth", Sum = "sum";

    // kind null: the portable verbs span every section, and a field id IS the section. A section-bound engine
    // (the default "memory") would export nothing here, silently.
    private readonly CuratedMemoryEngine _engine = new("profile/curated", store, kind: null);

    public string Name => "curated-projection";

    public Task<ProfileClaim> SetAsync(ProfileField field, string value, DateTimeOffset from, string source) =>
        SetAsync(field, value, from, source, ProfileAuthority.User);

    public async Task<ProfileClaim> SetAsync(
        ProfileField field, string value, DateTimeOffset from, string source, ProfileAuthority authority)
    {
        Guard(field.Field);
        var id = ProfileClaims.IdOf(field, value, from, source, authority);
        if (Collapse(await SlotAsync(field)).FirstOrDefault(r => ClaimId(r) == id) is { } existing) return ClaimOf(existing);
        return ClaimOf(await AddClaimAsync(field, value, from, source, authority, id));
    }

    public async Task<ProfileRead> ReadAsync(ProfileField field, DateTimeOffset now) =>
        ReadFrom(await SlotAsync(field), now);

    public Task<Correction> CorrectAsync(
        ProfileField field, string value, DateTimeOffset from, string source, string basedOn) =>
        CorrectAsync(field, value, from, source, basedOn, ProfileAuthority.User);

    public async Task<Correction> CorrectAsync(
        ProfileField field, string value, DateTimeOffset from, string source, string basedOn, ProfileAuthority authority)
    {
        Guard(field.Field);
        var id = ProfileClaims.IdOf(field, value, from, source, authority);
        if (Collapse(await SlotAsync(field)).Any(r => ClaimId(r) == id)) return Correction.Applied;
        var (rowId, revision) = Parse(basedOn);
        // a handle is a row id, so a caller can name one it read under another subject or field: that is no claim of this one
        if (await store.GetAsync(rowId) is not { } old || old.Kind != field.Field || old.TaskKey != field.TaskKey
            || Norm(old.Scope) != field.Scope)
            return Correction.Missing;

        // the same event delivered again after a crash between the two writes: the old entry already names this
        // claim as its successor, so finish the job rather than report a stale read
        if (EndedBy(old) == id) return await FinishAsync(field, value, from, source, authority, id);

        if (!Verify(old)) return Correction.Edited;
        if (authority < AuthorityOf(old)) return Correction.Outranked;
        if (from < Start(old)) throw new ArgumentException("A correction cannot begin before the claim it replaces.");

        var ended = new Dictionary<string, string>(old.Metadata!) { [Ended] = Iso(from), [By] = id };
        var gate = await ((IRevisionedCuratedMemoryStore)store).UpdateAsync(
            rowId, revision, enabled: false, metadata: ended);
        if (gate == CuratedUpdateOutcome.NotFound) return Correction.Missing;
        if (gate != CuratedUpdateOutcome.Updated)
        {
            // a peer delivering this very event may have won the gate: if so, its second write is ours to make too
            if (await store.GetAsync(rowId) is { } moved && EndedBy(moved) == id)
                return await FinishAsync(field, value, from, source, authority, id);
            return Collapse(await SlotAsync(field)).Any(r => ClaimId(r) == id) ? Correction.Applied : Correction.Stale;
        }

        return await FinishAsync(field, value, from, source, authority, id);
    }

    public async Task<IReadOnlyList<ProfileClaim>> HistoryAsync(ProfileField field)
    {
        var rows = Collapse(await SlotAsync(field));
        return [.. rows.OrderBy(Start).ThenBy(r => r.Id).Select(ClaimOf)];
    }

    public async Task<ProfileClaim?> AsOfAsync(ProfileField field, DateTimeOffset at) =>
        ProfileClaims.Resolve(Collapse(await SlotAsync(field)).Where(r => Holds(r, at)).Select(r => (ClaimOf(r), r.Id))).Current;

    public async Task ForgetAsync(string subject)
    {
        // the engine has no forget, so the projection removes row by row: a fault part-way leaves the rest, and a
        // second forget finishes it
        foreach (var row in await store.ListAsync(taskKey: new ProfileField(subject, "").TaskKey))
            await store.RemoveAsync(row.Id);
    }

    /// <summary>The subject's claims as JSONL, through the engine's export. The summary cache is derived and is not
    /// evidence, so its line is dropped.</summary>
    public async Task<int> ExportAsync(string subject, TextWriter destination)
    {
        using var all = new StringWriter();
        await _engine.ExportAsync(new ProfileField(subject, "").TaskKey, null, all);
        var kept = 0;
        foreach (var line in all.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == SummaryKind) continue;
            await destination.WriteAsync(line + "\n");
            kept++;
        }

        return kept;
    }

    public async Task<int> ImportAsync(string subject, TextReader source) => (await ImportFileAsync(subject, source)).Imported;

    /// <summary>
    /// Reads a file written by <see cref="ExportAsync"/> (or the engine's own export) and adds each claim the subject
    /// does not hold. The engine's import is not used: it adds each entry with <c>dedup</c> on (kind, content), so a
    /// field that held <c>Paris, Berlin, Paris</c> came back with two claims. Identity here is the claim id, and a line
    /// whose content no longer matches its checksum is refused, because a file is evidence from a time and an edited
    /// one is not what was said.
    /// </summary>
    public async Task<ProfileImportReport> ImportFileAsync(string subject, TextReader source)
    {
        var task = new ProfileField(subject, "").TaskKey;
        var (imported, present, rejected, number) = (0, 0, 0, 0);
        while (await source.ReadLineAsync() is { } text)
        {
            number++;
            if (string.IsNullOrWhiteSpace(text)) continue;
            using var doc = JsonDocument.Parse(text);
            var body = doc.RootElement;
            if (Str(body, "record") != "curated")
                throw new InvalidDataException($"line {number} is not a curated record");
            var kind = Str(body, "kind") ?? throw new InvalidDataException($"line {number} names no kind");
            var content = Str(body, "content") ?? throw new InvalidDataException($"line {number} carries no content");
            if (kind == SummaryKind) { rejected++; continue; }
            var enabled = body.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
            var scope = Norm(Str(body, "scope"));
            Dictionary<string, string>? meta = null;
            if (body.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object)
                meta = m.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            var probe = new CuratedMemory(0, kind, content, enabled, default, default, task, scope, meta);
            if (!Verify(probe)) { rejected++; continue; }

            var field = new ProfileField(subject, kind, scope);
            if (Collapse(await SlotAsync(field)).Any(r => ClaimId(r) == meta![Claim])) { present++; continue; }
            await AddRowAsync(field, content, enabled, meta!, meta![Claim]);
            imported++;
        }

        return new ProfileImportReport(imported, present, rejected);
    }

    public async Task<int> RenameAsync(string subject, string from, string to)
    {
        Guard(to);
        var moved = 0;
        var task = new ProfileField(subject, from).TaskKey;
        // a re-categorise in place keeps each entry's id and creation time; one that would collide is refused, and
        // is a claim the new id already holds (an older export brought it back), so the stray copy is removed
        foreach (var row in await store.ListAsync(from, false, task))
            if (await store.UpdateAsync(row.Id, kind: to)) moved++;
            else if (ClaimId(row) is { } claim && Collapse(await SlotAsync(new ProfileField(subject, to, Norm(row.Scope))))
                .Any(r => ClaimId(r) == claim) && await store.RemoveAsync(row.Id)) moved++;
        return moved;
    }

    // a curated catalog has no cap, no TTL and no decay, so there is no capacity sweep that could remove a claim
    public Task<int> PruneAsync(string subject) => Task.FromResult(0);

    public async Task<string> FingerprintAsync(string subject)
    {
        var rows = await store.ListAsync(taskKey: new ProfileField(subject, "").TaskKey);
        return string.Join(";", rows.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Revision}:{r.Enabled}:{r.Kind}:{r.UpdatedAt:O}"));
    }

    /// <summary>Every field of a subject, read with the composition verb: an entry with no task key applies to every
    /// task and is not the profile's, so it is left out. <paramref name="scopes"/> follows the store's rule (a claim in
    /// no scope applies everywhere; none given means every scope).</summary>
    public async Task<ProfileSnapshot> SnapshotAsync(string subject, DateTimeOffset now, IEnumerable<string>? scopes = null)
    {
        var task = new ProfileField(subject, "").TaskKey;
        var rows = (await store.ForCompositionAsync(task, scopes ?? [], enabledOnly: false))
            .Where(r => r.TaskKey == task && r.Kind != SummaryKind).ToList();
        var views = rows.GroupBy(r => (r.Kind, Scope: Norm(r.Scope)))
            .OrderBy(g => g.Key.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.Scope, StringComparer.Ordinal)
            .Select(g => new ProfileFieldView(subject, g.Key.Kind, g.Key.Scope, ReadFrom(g, now))).ToList();
        var stands = views.Select(v => $"{v.Field}|{v.Scope}|{v.Read.Current?.ClaimId}|{string.Join(",", v.Read.Interrupted ?? [])}"
            + $"|{string.Join(",", (v.Read.Edited ?? []).Select(c => c.ClaimId))}");
        return new ProfileSnapshot(subject, ProfileClaims.Digest(string.Join("\n", stands)), views);
    }

    /// <summary>Keeps a narrative the caller derived from the profile, stamped with the snapshot's version. It is
    /// stored beside the claims so a forget takes it too, and it answers no read.</summary>
    public async Task PutSummaryAsync(string subject, string text, DateTimeOffset now)
    {
        var task = new ProfileField(subject, "").TaskKey;
        var meta = new Dictionary<string, string>
            { ["cache"] = "true", ["version"] = (await SnapshotAsync(subject, now)).Version };
        var held = (await store.ListAsync(SummaryKind, false, task)).FirstOrDefault();
        if (held is null) await store.AddAsync(SummaryKind, text, true, task, null, false, meta);
        else await store.UpdateAsync(held.Id, content: text, metadata: meta);
    }

    public async Task<ProfileSummary?> GetSummaryAsync(string subject, DateTimeOffset now)
    {
        var task = new ProfileField(subject, "").TaskKey;
        if ((await store.ListAsync(SummaryKind, false, task)).FirstOrDefault() is not { } row) return null;
        var fresh = row.Metadata?.GetValueOrDefault("version") == (await SnapshotAsync(subject, now)).Version;
        return new ProfileSummary(row.Content, fresh);
    }

    /// <summary>The claim's entry written by the builders that make the metadata a claim carries, so a projection that
    /// deliberately misbehaves still writes entries the correct one can read.</summary>
    internal static Dictionary<string, string> ClaimMetadata(
        string id, string value, DateTimeOffset from, string source, ProfileAuthority authority, string? scope) =>
        new()
        {
            [Claim] = id, [From] = Iso(from), [Src] = source, [Auth] = AuthName(authority),
            [Sum] = Checksum(value, Iso(from), source, AuthName(authority), scope),
        };

    private static void Guard(string field)
    {
        if (field == SummaryKind) throw new ArgumentException($"'{SummaryKind}' names the summary cache, not a field.");
    }

    private async Task<Correction> FinishAsync(ProfileField field, string value, DateTimeOffset from, string source,
        ProfileAuthority authority, string id)
    {
        await AddClaimAsync(field, value, from, source, authority, id);
        return Correction.Applied;
    }

    private Task<CuratedMemory> AddClaimAsync(ProfileField field, string value, DateTimeOffset from, string source,
        ProfileAuthority authority, string id) =>
        AddRowAsync(field, value, true, ClaimMetadata(id, value, from, source, authority, field.Scope), id);

    /// <summary>Adds the entry, then keeps the lowest row of its claim id and removes the rest. Two writers delivering
    /// one event both add (no store verb makes find-and-add one step), and each then lists the claim and removes every
    /// copy but the first, so they agree on the survivor without either waiting for the other. A reader meanwhile
    /// collapses copies by claim id (<see cref="Collapse"/>).</summary>
    private async Task<CuratedMemory> AddRowAsync(
        ProfileField field, string value, bool enabled, IReadOnlyDictionary<string, string> metadata, string claimId)
    {
        var rowId = await store.AddAsync(field.Field, value, enabled, field.TaskKey, field.Scope, false, metadata);
        var copies = (await SlotAsync(field)).Where(r => ClaimId(r) == claimId).OrderBy(r => r.Id).ToList();
        foreach (var extra in copies.Skip(1)) await store.RemoveAsync(extra.Id);
        return copies.FirstOrDefault() ?? (await store.GetAsync(rowId))!;
    }

    private async Task<IReadOnlyList<CuratedMemory>> SlotAsync(ProfileField field) =>
        [.. (await store.ListAsync(field.Field, false, field.TaskKey, field.Scope)).Where(r => Norm(r.Scope) == field.Scope)];

    /// <summary>The slot's entries with each claim once: copies a racing writer has not yet removed are one claim, and
    /// the lowest row is the one every writer keeps.</summary>
    private static List<CuratedMemory> Collapse(IEnumerable<CuratedMemory> rows) =>
        [.. rows.OrderBy(r => r.Id).GroupBy(r => Verify(r) ? ClaimId(r)! : $"row:{r.Id}").Select(g => g.First())];

    private static ProfileRead ReadFrom(IEnumerable<CuratedMemory> slot, DateTimeOffset now)
    {
        var rows = Collapse(slot);
        var holding = rows.Where(r => Holds(r, now)).ToList();
        var edited = rows.Where(r => StatusOf(r) == ClaimStatus.Edited).Select(ClaimOf).ToList();
        // an entry ended in favour of a claim nobody wrote: the field has no current claim until the event comes again
        var written = rows.Select(ClaimId).ToHashSet();
        var interrupted = rows.Where(r => Verify(r) && EndedBy(r) is { } by && !written.Contains(by) && End(r) <= now)
            .Select(r => EndedBy(r)!).Distinct().Order().ToList();
        var read = ProfileClaims.Resolve(holding.Select(r => (ClaimOf(r), r.Id)));
        if (interrupted.Count > 0) read = new ProfileRead(null, [], holding.Select(ClaimOf).ToList());
        return read with { Edited = edited, Interrupted = interrupted };
    }

    private static bool Holds(CuratedMemory row, DateTimeOffset at) =>
        StatusOf(row) is ClaimStatus.Holding or ClaimStatus.Ended && Start(row) <= at && (End(row) is not { } end || at < end);

    private static ClaimStatus StatusOf(CuratedMemory row) =>
        !Verify(row) ? ClaimStatus.Edited : End(row) is not null ? ClaimStatus.Ended : row.Enabled ? ClaimStatus.Holding : ClaimStatus.Disabled;

    /// <summary>Whether the entry is a claim the profile wrote and nothing has edited since: its metadata carries the
    /// claim's parts and a checksum over content, start, source, authority and scope. A kind change is a rename and
    /// is not covered; neither are the end and its successor, which a correction writes later.</summary>
    private static bool Verify(CuratedMemory row) =>
        row.Metadata is { } m && m.ContainsKey(Claim) && m.TryGetValue(From, out var from) && m.TryGetValue(Src, out var src)
        && m.TryGetValue(Auth, out var auth) && m.TryGetValue(Sum, out var sum)
        && DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, out _)
        && sum == Checksum(row.Content, from, src, auth, Norm(row.Scope));

    private static string? ClaimId(CuratedMemory row) => row.Metadata?.GetValueOrDefault(Claim);

    private static string? EndedBy(CuratedMemory row) => row.Metadata?.GetValueOrDefault(By);

    private static ProfileAuthority AuthorityOf(CuratedMemory row) =>
        row.Metadata?.GetValueOrDefault(Auth) == AuthName(ProfileAuthority.Hypothesis) ? ProfileAuthority.Hypothesis : ProfileAuthority.User;

    private static DateTimeOffset Start(CuratedMemory row) =>
        row.Metadata is { } m && m.TryGetValue(From, out var f) && DateTimeOffset.TryParse(f, CultureInfo.InvariantCulture, out var at)
            ? at : row.CreatedAt;

    private static DateTimeOffset? End(CuratedMemory row) =>
        row.Metadata is { } m && m.TryGetValue(Ended, out var e) ? DateTimeOffset.Parse(e, CultureInfo.InvariantCulture) : null;

    private static ProfileClaim ClaimOf(CuratedMemory row) =>
        new(ClaimId(row) ?? $"unverified:{row.Id}", row.Content, Start(row), End(row),
            row.Metadata?.GetValueOrDefault(Src) ?? "", $"{row.Id}:{row.Revision}", Norm(row.Scope), AuthorityOf(row),
            StatusOf(row), row.Revision);

    private static string Checksum(string content, string from, string source, string authority, string? scope) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\u001f', content, from, source, authority, scope ?? ""))))[..16];

    private static string AuthName(ProfileAuthority authority) => authority == ProfileAuthority.User ? "user" : "hypothesis";

    private static string? Norm(string? scope) => string.IsNullOrEmpty(scope) ? null : scope;

    private static string? Str(JsonElement body, string name) =>
        body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static (long Id, long Revision) Parse(string handle)
    {
        var parts = handle.Split(':');
        return (long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
}
