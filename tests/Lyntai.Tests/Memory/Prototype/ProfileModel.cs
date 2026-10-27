using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>One profile slot: a subject and a schema field id (<c>address.city</c>). The field id is the attribute
/// identity <b>D217</b> found a model cannot supply, so here the CALLER names it.</summary>
internal readonly record struct ProfileField(string Subject, string Field)
{
    /// <summary>The task key both options store a subject under, so a subject-level forget and export are one
    /// store-level operation on either (<b>D92</b>: a task key is a real boundary).</summary>
    internal string TaskKey => "profile:" + Subject;
}

/// <summary>One claim a profile field held: <paramref name="Handle"/> is the opaque token a correction names as
/// the claim it replaces, and it is store-defined, so it never travels in an export.</summary>
internal sealed record ProfileClaim(
    string ClaimId, string Value, DateTimeOffset ValidFrom, DateTimeOffset? EndedAt, string Source, string Handle);

/// <summary>What a no-search read answers: the claim that holds now, and any OTHER claim that also holds now.
/// A non-empty <paramref name="Rivals"/> is a dispute, reported and never hidden.</summary>
internal sealed record ProfileRead(ProfileClaim? Current, IReadOnlyList<ProfileClaim> Rivals)
{
    internal bool Disputed => Rivals.Count > 0;
}

internal enum Correction
{
    /// <summary>The new claim is the current one; the old one is ended, not deleted.</summary>
    Applied,

    /// <summary>Another writer replaced the claim the caller read; nothing was written.</summary>
    Stale,

    /// <summary>The claim the caller named does not exist.</summary>
    Missing,
}

/// <summary>
/// What the scenario set asks of an option, and ALL it asks: eight verbs over a subject's fields. Both options
/// implement it over shipped, public store interfaces only, so choosing one adds no type to the library; this
/// interface and both implementations are TEST-ONLY. A read is exact: no search, no model, no ranking.
/// </summary>
internal interface IProfileOption
{
    string Name { get; }

    /// <summary>The first claim for a field. Replaying the same claim writes nothing.</summary>
    Task<ProfileClaim> SetAsync(ProfileField field, string value, DateTimeOffset from, string source);

    Task<ProfileRead> ReadAsync(ProfileField field, DateTimeOffset now);

    /// <summary>Replace <paramref name="basedOn"/> (a <see cref="ProfileClaim.Handle"/>) with a new claim. Of two
    /// writers holding one handle, the option decides what the second one sees.</summary>
    Task<Correction> CorrectAsync(ProfileField field, string value, DateTimeOffset from, string source, string basedOn);

    /// <summary>Every claim the field ever held, oldest first.</summary>
    Task<IReadOnlyList<ProfileClaim>> HistoryAsync(ProfileField field);

    Task<ProfileClaim?> AsOfAsync(ProfileField field, DateTimeOffset at);

    /// <summary>Remove everything held for a subject.</summary>
    Task ForgetAsync(string subject);

    /// <summary>The subject's whole record as JSONL through the store's own portable-memory capability.</summary>
    Task<int> ExportAsync(string subject, TextWriter destination);

    Task<int> ImportAsync(string subject, TextReader source);

    /// <summary>Rename a schema field for one subject, returning how many claims moved.</summary>
    Task<int> RenameAsync(string subject, string from, string to);

    /// <summary>What an operator's capacity sweep (the engine's prune at the strictest retrievability) removes from a
    /// subject: a profile claim must survive it, so this is zero.</summary>
    Task<int> PruneAsync(string subject);

    /// <summary>A digest of every stored mutable bit of a subject, so a read can be shown to have changed nothing.</summary>
    Task<string> FingerprintAsync(string subject);
}

internal static class ProfileClaims
{
    /// <summary>A claim's identity, deterministic in what it says, so replaying an event is a no-op rather than a
    /// second claim. It carries the VALUE's digest, because a value that returns (<c>Paris, Berlin, Paris</c>) is a
    /// new claim, not a refresh of the first.</summary>
    internal static string IdOf(ProfileField field, string value, DateTimeOffset from, string source)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
        return $"{field.Field}|{from:yyyy-MM-ddTHH:mm:ssZ}|{source}|{digest}";
    }

    /// <summary>The claim that holds now: the latest start, the later write on a tie. The rest are rivals.</summary>
    internal static ProfileRead Resolve(IEnumerable<(ProfileClaim Claim, long Order)> holding)
    {
        var ordered = holding.OrderByDescending(h => h.Claim.ValidFrom).ThenByDescending(h => h.Order)
            .Select(h => h.Claim).ToList();
        return new ProfileRead(ordered.FirstOrDefault(), ordered.Skip(1).ToList());
    }
}
