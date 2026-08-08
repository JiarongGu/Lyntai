using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Memory;

/// <summary>Derives a stable <see cref="MemoryRef.Id"/> for an engine whose store returns no identifier on
/// write. The hash covers the same fields those stores dedup on, so the id a write returns is the id a
/// later recall reports for the same entry.</summary>
internal static class MemoryContentId
{
    /// <summary>Length-framed so ("ab", "c") and ("a", "bc") cannot collide — the same framing rule the
    /// response cache's key uses.</summary>
    public static string For(string taskKey, string scope, string content)
    {
        var framed = $"{taskKey.Length}:{taskKey}|{scope.Length}:{scope}|{content.Length}:{content}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)));
    }
}
