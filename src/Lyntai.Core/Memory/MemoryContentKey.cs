using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Memory;

/// <summary>
/// The ONE content key an <see cref="IMemoryGraphStore"/> de-duplicates on — what makes
/// "re-remembering identical content REFRESHES rather than duplicating" mean the same thing on every
/// backend.
///
/// <para><b>Why this is shared rather than each backend's own line.</b> Both SQL backends carried an
/// identical private <c>ContentHash</c>, and it is not an implementation detail: it is the <b>dedup key</b>.
/// Two backends that hash differently do not merely differ in storage, they answer <i>"is this the same
/// memory?"</i> differently — one refreshes an entry where the other appends a second copy, so a corpus
/// migrated between them changes shape. That is a contract fact, and this repository's rule for a contract
/// fact read at N sites is one function every reader calls (see <see cref="MemorySignals.Salience"/>,
/// <see cref="MemorySubject"/>, <see cref="MemoryRelevance"/>).</para>
///
/// <para><b>Public because a BYO store needs the same key.</b> A store outside this repository that hashes
/// content its own way silently gets its own dedup semantics, and nothing would report it.</para>
///
/// <para><b>Not a security primitive.</b> SHA-256 is used for its collision resistance as a content
/// address, not to authenticate anything; nothing here depends on it being secret or salted.</para>
///
/// <para><b>Byte-for-byte, with no normalization at all</b> — no trimming, no case folding, no Unicode
/// normalization. Deliberate, and the opposite choice from <see cref="MemorySubject"/>: a subject is a
/// HANDLE meant to collide (that is how a cluster forms), while content is the fact itself, and two facts
/// differing only in whitespace are two facts a caller chose to write. Folding them would silently discard
/// one. If a caller wants them treated as one, they normalize before writing.</para>
/// </summary>
public static class MemoryContentKey
{
    /// <summary>The dedup key for <paramref name="content"/>: lowercase hex SHA-256 over its UTF-8 bytes.</summary>
    /// <param name="content">The entry's content, verbatim.</param>
    public static string Of(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
