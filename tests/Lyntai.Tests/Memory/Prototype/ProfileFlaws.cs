using System.Globalization;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>The ways <see cref="ProfileFlawed"/> is wrong, one at a time.</summary>
internal enum ProfileFlaw
{
    /// <summary>A correction rewrites the entry it replaces instead of ending it and adding a new one.</summary>
    InPlaceCorrection,

    /// <summary>A read asks the store to RANK the claims (a keyword search) and returns its first hit.</summary>
    RankedRead,

    /// <summary>A correction ends the old entry with no revision check, so every writer of one claim applies.</summary>
    UncheckedCorrection,

    /// <summary>A replayed set writes the claim again.</summary>
    ReplayWritesAgain,

    /// <summary>Every subject shares one task key, so a subject is no boundary.</summary>
    SharedSubjects,
}

/// <summary>
/// A profile over curated memory with ONE deliberate flaw, built to show that a Track A bucket can fail. It is the
/// same projection as <see cref="ProfileOverCurated"/> except at the flaw: the verbs the flaw does not touch go to a
/// correct one over the same store, so a bucket that fails here fails because of the flaw.
/// </summary>
internal sealed class ProfileFlawed(ICuratedMemoryStore store, ProfileFlaw flaw) : IProfileOption
{
    private const string Claim = "claim", From = "from", Src = "src", Ended = "ended", By = "by";

    private readonly ProfileOverCurated _correct = new(store);

    public string Name => $"flawed:{flaw}";

    private ProfileField Key(ProfileField f) => flaw == ProfileFlaw.SharedSubjects ? f with { Subject = "shared" } : f;

    private string Subject(string subject) => flaw == ProfileFlaw.SharedSubjects ? "shared" : subject;

    public async Task<ProfileClaim> SetAsync(ProfileField field, string value, DateTimeOffset from, string source)
    {
        if (flaw != ProfileFlaw.ReplayWritesAgain) return await _correct.SetAsync(Key(field), value, from, source);
        var id = ProfileClaims.IdOf(field, value, from, source);
        var meta = new Dictionary<string, string> { [Claim] = id, [From] = Iso(from), [Src] = source };
        return ClaimOf((await store.GetAsync(await store.AddAsync(field.Field, value, true, field.TaskKey, null, false, meta)))!);
    }

    public async Task<ProfileRead> ReadAsync(ProfileField field, DateTimeOffset now)
    {
        if (flaw != ProfileFlaw.RankedRead) return await _correct.ReadAsync(Key(field), now);
        var rows = await store.ListAsync(field.Field, false, field.TaskKey);
        if (rows.Count == 0) return new ProfileRead(null, []);
        var hit = (await store.SearchAsync(string.Join(' ', rows.Select(r => r.Content)), field.Field, field.TaskKey, enabledOnly: false, limit: 1))
            .FirstOrDefault();
        return new ProfileRead(hit is null ? null : ClaimOf(hit), []);
    }

    public async Task<Correction> CorrectAsync(ProfileField field, string value, DateTimeOffset from, string source, string basedOn)
    {
        if (flaw is not (ProfileFlaw.InPlaceCorrection or ProfileFlaw.UncheckedCorrection))
            return await _correct.CorrectAsync(Key(field), value, from, source, basedOn);

        var id = ProfileClaims.IdOf(field, value, from, source);
        var parts = basedOn.Split(':');
        var (rowId, revision) = (long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
        if (await store.GetAsync(rowId) is not { } old || old.Kind != field.Field || old.TaskKey != field.TaskKey)
            return Correction.Missing;
        if (from < Start(old)) throw new ArgumentException("A correction cannot begin before the claim it replaces.");

        if (flaw == ProfileFlaw.InPlaceCorrection)
        {
            var edited = new Dictionary<string, string> { [Claim] = id, [From] = Iso(from), [Src] = source };
            var gate = await ((IRevisionedCuratedMemoryStore)store).UpdateAsync(rowId, revision, content: value, metadata: edited);
            return gate == CuratedUpdateOutcome.Updated ? Correction.Applied : gate == CuratedUpdateOutcome.NotFound ? Correction.Missing : Correction.Stale;
        }

        var ended = new Dictionary<string, string>(old.Metadata ?? new Dictionary<string, string>()) { [Ended] = Iso(from), [By] = id };
        await store.UpdateAsync(rowId, enabled: false, metadata: ended);
        await store.AddAsync(field.Field, value, true, field.TaskKey, null, false,
            new Dictionary<string, string> { [Claim] = id, [From] = Iso(from), [Src] = source });
        return Correction.Applied;
    }

    public Task<IReadOnlyList<ProfileClaim>> HistoryAsync(ProfileField field) => _correct.HistoryAsync(Key(field));

    public Task<ProfileClaim?> AsOfAsync(ProfileField field, DateTimeOffset at) => _correct.AsOfAsync(Key(field), at);

    public Task ForgetAsync(string subject) => _correct.ForgetAsync(Subject(subject));

    public Task<int> ExportAsync(string subject, TextWriter destination) => _correct.ExportAsync(Subject(subject), destination);

    public Task<int> ImportAsync(string subject, TextReader source) => _correct.ImportAsync(Subject(subject), source);

    public Task<int> RenameAsync(string subject, string from, string to) => _correct.RenameAsync(Subject(subject), from, to);

    public Task<int> PruneAsync(string subject) => _correct.PruneAsync(Subject(subject));

    public Task<string> FingerprintAsync(string subject) => _correct.FingerprintAsync(Subject(subject));

    private static DateTimeOffset Start(CuratedMemory row) => DateTimeOffset.Parse(row.Metadata![From], CultureInfo.InvariantCulture);

    private static ProfileClaim ClaimOf(CuratedMemory row) =>
        new(row.Metadata![Claim], row.Content, Start(row),
            row.Metadata!.TryGetValue(Ended, out var e) ? DateTimeOffset.Parse(e, CultureInfo.InvariantCulture) : null,
            row.Metadata![Src], $"{row.Id}:{row.Revision}");

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
}
