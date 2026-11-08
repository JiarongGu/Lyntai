using System.Text;

namespace Lyntai.Storage;

/// <summary>
/// Orders strings the way SQLite's BINARY and Postgres's <c>COLLATE "C"</c> do over UTF-8: by code point.
/// <see cref="StringComparer.Ordinal"/> orders by UTF-16 unit, which puts a surrogate pair (a code point past
/// U+FFFF) before U+E000 to U+FFFF; UTF-8 bytes put it after.
/// </summary>
internal sealed class Utf8Ordinal : IComparer<string>
{
    public static readonly Utf8Ordinal Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var a = x.AsSpan();
        var b = y.AsSpan();
        while (!a.IsEmpty && !b.IsEmpty)
        {
            // a lone surrogate reads as U+FFFD, what a UTF-8 encoder writes for it; the ordinal compare below
            // still tells two such strings apart
            Rune.DecodeFromUtf16(a, out var ra, out var ca);
            Rune.DecodeFromUtf16(b, out var rb, out var cb);
            if (ra != rb) return ra.Value.CompareTo(rb.Value);
            a = a[ca..];
            b = b[cb..];
        }
        if (a.IsEmpty && b.IsEmpty) return string.CompareOrdinal(x, y);
        return a.IsEmpty ? -1 : 1;
    }
}
