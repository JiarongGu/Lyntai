using System.Globalization;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// OPTION B, a CLAIM LEDGER: each claim is one node of a graph engine over a store that holds valid time
/// (<see cref="ITemporalMemoryGraphStore"/>), so the claim's start, its end and its successor are the STORE's, not
/// metadata. The field id is the node's scope and the task key is the subject, so reading one field is a scan of
/// one scope and forgetting a subject is one engine call. The ledger is the authority for what a field held; the
/// evidence a claim came from is cited in <c>src</c>, never copied.
///
/// <para><b>Two things the engine's identity forces.</b> A node's identity is its (task, scope, content), and
/// storing identical content REFRESHES it, so a value that returns (Paris, Berlin, Paris) would rewrite the first
/// claim's interval; the content is therefore an envelope carrying the claim id, which makes a returning value a
/// new claim and a replayed event a refresh. And the engine has no scope rename, so a schema rename is an export
/// of the old scope, an import under the new one and a forget of the old, which the store's portable capability
/// carries with intervals and successors intact.</para>
///
/// <para>A read is a scan of the store (<c>SeedAsync</c> with no query), never an engine recall, because a recall
/// reinforces what it returns and a profile read may write nothing it did not declare (<b>D218</b>).</para>
/// </summary>
internal sealed class ProfileLedger(IMemoryGraphStore store, DateTimeOffset engineNow, bool compensate = true) : IProfileOption
{
    private const string EngineName = "profile/ledger";

    private readonly GraphMemoryEngine _engine = new(EngineName, store, null,
        new GraphMemorySeams { Clock = () => engineNow });

    private sealed record Envelope(string C, string V);

    public string Name => compensate ? "claim-ledger" : "claim-ledger (engine refusal only)";

    public async Task<ProfileClaim> SetAsync(ProfileField field, string value, DateTimeOffset from, string source)
    {
        var id = ProfileClaims.IdOf(field, value, from, source);
        var written = await _engine.RememberAsync(Write(field, id, value, source, from));
        return Claim((await store.GetAsync(EngineName, long.Parse(written.Reference.Id, CultureInfo.InvariantCulture)))!);
    }

    public async Task<ProfileRead> ReadAsync(ProfileField field, DateTimeOffset now)
    {
        var nodes = await ScanAsync(field);
        return ProfileClaims.Resolve(nodes.Where(n => n.Validity!.IsValidAt(now)).Select(n => (Claim(n), n.Id)));
    }

    public async Task<Correction> CorrectAsync(
        ProfileField field, string value, DateTimeOffset from, string source, string basedOn)
    {
        var predecessor = long.Parse(basedOn, CultureInfo.InvariantCulture);
        if (await store.GetAsync(EngineName, predecessor) is null) return Correction.Missing;
        var claimId = ProfileClaims.IdOf(field, value, from, source);
        try
        {
            await _engine.RememberAsync(Write(field, claimId, value, source, from)
                with { Supersedes = [new MemoryRef(EngineName, basedOn)] });
            return Correction.Applied;
        }
        catch (InvalidOperationException)
        {
            // "an entry has one successor": another writer replaced the claim this one read. Two shapes: refused
            // before anything was written, or (a race between the engine's check and its write) STORED and then not
            // able to end its predecessor, which leaves a live claim the caller was told was refused.
            if (compensate) await EndOrphanAsync(field, claimId, predecessor);
            return Correction.Stale;
        }
    }

    /// <summary>End a claim the engine stored and then refused, at its own start, so it never holds. It stays in
    /// the history as a claim that held for no time, naming the claim that won. There is a window between the
    /// engine's write and this one in which a read sees two claims, and no store capability closes it.</summary>
    private async Task EndOrphanAsync(ProfileField field, string claimId, long predecessor)
    {
        var orphan = (await ScanAsync(field)).FirstOrDefault(n => Claim(n).ClaimId == claimId);
        var winner = (await store.GetAsync(EngineName, predecessor))?.SupersededBy;
        if (orphan is null || orphan.SupersededAt is not null || winner is null || winner == orphan.Id) return;
        await ((ITemporalMemoryGraphStore)store).SupersedeAsync(EngineName,
            [new GraphSupersession(orphan.Id, winner, orphan.ValidFrom!.Value)]);
    }

    public async Task<IReadOnlyList<ProfileClaim>> HistoryAsync(ProfileField field) =>
        [.. (await ScanAsync(field)).OrderBy(n => n.ValidFrom).ThenBy(n => n.Id).Select(Claim)];

    public async Task<ProfileClaim?> AsOfAsync(ProfileField field, DateTimeOffset at) =>
        ProfileClaims.Resolve((await ScanAsync(field)).Where(n => n.Validity!.IsValidAt(at)).Select(n => (Claim(n), n.Id))).Current;

    public Task ForgetAsync(string subject) => _engine.ForgetAsync(new ProfileField(subject, "").TaskKey);

    public async Task<int> ExportAsync(string subject, TextWriter destination) =>
        (await _engine.ExportAsync(new ProfileField(subject, "").TaskKey, null, destination)).Exported;

    public async Task<int> ImportAsync(string subject, TextReader source) =>
        (await _engine.ImportAsync(new ProfileField(subject, "").TaskKey, null, source)).Imported;

    public async Task<int> RenameAsync(string subject, string from, string to)
    {
        var task = new ProfileField(subject, from).TaskKey;
        using var file = new StringWriter();
        var exported = (await _engine.ExportAsync(task, from, file)).Exported;
        if (exported == 0) return 0;
        var result = await _engine.ImportAsync(task, to, new StringReader(file.ToString()));
        // the old scope goes only after the new one holds every claim (one an older export brought back is already
        // there): a fault between leaves both, never neither
        if (result.Imported + result.AlreadyPresent == exported) await _engine.ForgetAsync(task, from);
        return result.Imported + result.AlreadyPresent;
    }

    public Task<int> PruneAsync(string subject) =>
        _engine.PruneAsync(new ProfileField(subject, "").TaskKey, minRetrievability: 1.0);

    public async Task<string> FingerprintAsync(string subject)
    {
        var nodes = await store.SeedAsync(EngineName, new ProfileField(subject, "").TaskKey, null, null, int.MaxValue);
        return string.Join(";", nodes.OrderBy(n => n.Id).Select(n =>
            $"{n.Id}:{n.Scope}:{n.RecallCount}:{n.Stability:R}:{n.Age:R}:{n.SupersededAt:O}:{n.SupersededBy}"));
    }

    private Task<IReadOnlyList<GraphNode>> ScanAsync(ProfileField field) =>
        store.SeedAsync(EngineName, field.TaskKey, field.Field, null, int.MaxValue);

    private static MemoryWrite Write(ProfileField field, string claimId, string value, string source, DateTimeOffset from) =>
        // authoritative: a claim must neither decay by interference nor fall to an operator's capacity prune
        new(field.TaskKey, field.Field, JsonSerializer.Serialize(new Envelope(claimId, value)), Grade: MemoryGrade.Authoritative)
        {
            Metadata = new Dictionary<string, string> { ["src"] = source },
            ValidFrom = from,
        };

    private static ProfileClaim Claim(GraphNode node)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(node.Content)!;
        return new ProfileClaim(envelope.C, envelope.V, node.ValidFrom!.Value, node.Validity!.End,
            node.Metadata?.GetValueOrDefault("src") ?? "", node.Id.ToString(CultureInfo.InvariantCulture));
    }
}
