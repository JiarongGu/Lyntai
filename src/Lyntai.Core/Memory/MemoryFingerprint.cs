using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Memory;

/// <summary>The digest a receipt is checked against: SHA-256 over the length-framed fields a call stated, so two fields that
/// concatenate alike (<c>"ab","c"</c> and <c>"a","bc"</c>) cannot collide. A null field is framed apart from an empty one, because
/// "stated nothing" and "stated the empty string" are different calls.
/// <para>Only what the CALLER stated goes in. A value the engine resolves per call — its clock, an id it derives — would differ
/// between a call and its repeat and refuse every retry.</para></summary>
internal static class MemoryFingerprint
{
    public static string Of(MemoryOperationKind kind, params object?[] stated)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, kind.ToString());
        foreach (var field in stated) Add(hash, Text(field));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // a metadata bag compares by content, in key order
    public static string? Bag(IReadOnlyDictionary<string, string>? bag) =>
        bag is not { Count: > 0 }
            ? null
            : string.Join('\n', bag.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key.Length}:{p.Key}={p.Value.Length}:{p.Value}"));

    private static string? Text(object? field) => field switch
    {
        null => null,
        string s => s,
        DateTimeOffset at => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => field.ToString(),
    };

    private static void Add(IncrementalHash hash, string? field)
    {
        var bytes = field is null ? [] : Encoding.UTF8.GetBytes(field);
        hash.AppendData(Encoding.ASCII.GetBytes((field is null ? -1 : bytes.Length).ToString(CultureInfo.InvariantCulture) + ":"));
        hash.AppendData(bytes);
    }
}
