using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Storage.FileSystem;

/// <summary>
/// A consumer string (a key, a task, a thread id) as a file or directory NAME: a readable slug, then 64 bits
/// of the string's SHA-256 — <c>user.prefs</c> becomes <c>user-prefs-3fa2c1…</c>.
///
/// <para><b>The hash makes it injective and the slug makes it browsable; nothing ever DECODES a name.</b> The
/// exact string lives in the record's header, which is the truth a store loads. So the slug may lose
/// whatever it likes: it keeps letters and digits (any script), lowercases, turns every other run into one
/// <c>-</c> and stops at <see cref="SlugChars"/>. What that buys on every file system at once: no separator
/// or <c>..</c> survives (no traversal); a slug followed by <c>-</c> and hex is never a Windows device name
/// (<c>CON</c>, <c>NUL</c>, <c>COM1</c>) and never ends in a dot or space; two keys differing only in case
/// hash differently, so a case-insensitive volume cannot merge them; and a name is at most ~50 characters.</para>
/// </summary>
internal static class RecordName
{
    private const int SlugChars = 32;

    public static string For(string value)
    {
        var slug = new StringBuilder(SlugChars);
        var dash = false;
        foreach (var c in value)
        {
            if (slug.Length >= SlugChars) break;
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
                dash = false;
            }
            else if (!dash && slug.Length > 0)
            {
                slug.Append('-');
                dash = true;
            }
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 8);
        var head = slug.ToString().TrimEnd('-');
        return head.Length == 0 ? hash : $"{head}-{hash}";
    }
}
