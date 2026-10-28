using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>One profile slot: a subject, a schema field id (<c>address.city</c>) and an optional scope. The field id is
/// the attribute identity <b>D217</b> found a model cannot supply, so here the CALLER names it. A null
/// <paramref name="Scope"/> applies everywhere; a named one is a separate slot, so a value held at work and one held
/// at home are two claims, never a dispute.</summary>
internal readonly record struct ProfileField(string Subject, string Field, string? Scope = null)
{
    /// <summary>The task key both options store a subject under, so a subject-level forget and export are one
    /// store-level operation on either (<b>D92</b>: a task key is a real boundary).</summary>
    internal string TaskKey => "profile:" + Subject;
}

/// <summary>Who stands behind a claim. A user's own word (a statement or a correction) outranks a hypothesis however
/// late the hypothesis starts, and a hypothesis can never end a user's claim.</summary>
internal enum ProfileAuthority
{
    /// <summary>Inferred or proposed by something other than the user.</summary>
    Hypothesis,

    /// <summary>Said or corrected by the user.</summary>
    User,
}

/// <summary>Where a stored claim stands. <see cref="Edited"/> is a claim whose entry no longer matches the checksum it was
/// written with, or one the profile never wrote: it is reported and never read as a value.</summary>
internal enum ClaimStatus
{
    Holding,
    Ended,
    Disabled,
    Edited,
}

/// <summary>One claim a profile field held: <paramref name="Handle"/> is the opaque token a correction names as
/// the claim it replaces, and it is store-defined, so it never travels in an export.</summary>
internal sealed record ProfileClaim(
    string ClaimId, string Value, DateTimeOffset ValidFrom, DateTimeOffset? EndedAt, string Source, string Handle,
    string? Scope = null, ProfileAuthority Authority = ProfileAuthority.User, ClaimStatus Status = ClaimStatus.Holding,
    long Revision = 0);

/// <summary>What a no-search read answers: the claim that holds now, and any OTHER claim of the same authority that also
/// holds now. A non-empty <paramref name="Rivals"/> is a dispute, reported and never hidden. A claim of lower authority
/// is <paramref name="Outranked"/>, not a dispute; one that fails its checksum is in <paramref name="Edited"/>; and a
/// correction that ended a claim without writing its successor is in <paramref name="Interrupted"/>, which leaves
/// <paramref name="Current"/> empty (fails closed) until the same event is delivered again.</summary>
internal sealed record ProfileRead(
    ProfileClaim? Current, IReadOnlyList<ProfileClaim> Rivals,
    IReadOnlyList<ProfileClaim>? Outranked = null, IReadOnlyList<ProfileClaim>? Edited = null,
    IReadOnlyList<string>? Interrupted = null)
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

    /// <summary>The claim the caller named is a user's and the correction is a hypothesis; nothing was written.</summary>
    Outranked,

    /// <summary>The claim the caller named no longer matches its checksum: it was edited outside the profile.</summary>
    Edited,
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
    /// new claim, not a refresh of the first. A hypothesis, and a claim in a scope, say so in the id: the same words
    /// from another authority or another scope are another event.</summary>
    internal static string IdOf(ProfileField field, string value, DateTimeOffset from, string source,
        ProfileAuthority authority = ProfileAuthority.User)
    {
        var id = $"{field.Field}|{from:yyyy-MM-ddTHH:mm:ssZ}|{source}|{Digest(value)}";
        if (authority == ProfileAuthority.Hypothesis) id += "|h";
        return field.Scope is null ? id : $"{id}|@{field.Scope}";
    }

    /// <summary>The first eight hex digits of the SHA-256 of <paramref name="text"/>.</summary>
    internal static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];

    /// <summary>The claim that holds now: the highest authority, then the latest start, then the later write on a tie.
    /// The rest of the same authority are rivals; a lower authority is outranked, not disputed.</summary>
    internal static ProfileRead Resolve(IEnumerable<(ProfileClaim Claim, long Order)> holding)
    {
        var ordered = holding.OrderByDescending(h => h.Claim.Authority).ThenByDescending(h => h.Claim.ValidFrom)
            .ThenByDescending(h => h.Order).Select(h => h.Claim).ToList();
        var current = ordered.FirstOrDefault();
        return new ProfileRead(current,
            ordered.Skip(1).Where(c => c.Authority == current!.Authority).ToList(),
            ordered.Skip(1).Where(c => c.Authority != current!.Authority).ToList());
    }
}
