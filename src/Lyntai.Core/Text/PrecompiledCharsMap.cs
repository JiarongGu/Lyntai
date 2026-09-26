using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Lyntai.Text;

/// <summary>SentencePiece's compiled normalization rules — the <c>precompiled_charsmap</c> a
/// <c>tokenizer.json</c> carries — applied the way HF <c>tokenizers</c> applies them.
///
/// <para>The blob is <c>[u32 LE trie length in bytes][trie: u32 units][replacements, each NUL-terminated]</c>:
/// a Darts double-array trie over UTF-8 bytes whose leaf value is an offset into the replacements.</para>
///
/// <para><b>Matching follows the reference, including where it looks wrong.</b> An extended grapheme cluster
/// under 6 UTF-8 bytes with a key prefixing it is replaced WHOLE by the SHORTEST such key's replacement; any
/// other cluster is looked up one code point at a time. C++ SentencePiece matches the longest key instead, so
/// the two part on stacked combining marks; a "fix" would disagree with every model trained through HF.</para></summary>
internal sealed class PrecompiledCharsMap
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly uint[] _trie;
    private readonly byte[] _replacements;

    private PrecompiledCharsMap(uint[] trie, byte[] replacements) => (_trie, _replacements) = (trie, replacements);

    /// <summary>Read a decoded charsmap blob.</summary>
    /// <exception cref="InvalidDataException">The blob is truncated, its trie is not whole 4-byte units, or its
    /// replacements are not UTF-8.</exception>
    public static PrecompiledCharsMap Parse(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length < 4) throw Malformed("is shorter than its own length prefix");
        var trieBytes = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        if (trieBytes == 0 || trieBytes % 4 != 0 || trieBytes > (uint)(blob.Length - 4))
            throw Malformed($"declares a {trieBytes}-byte trie in {blob.Length - 4} bytes");

        var trie = new uint[trieBytes / 4];
        for (var i = 0; i < trie.Length; i++)
            trie[i] = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(4 + (i * 4)));

        var replacements = blob[(4 + (int)trieBytes)..];
        try
        {
            _ = StrictUtf8.GetCharCount(replacements);
        }
        catch (DecoderFallbackException)
        {
            throw Malformed("carries replacements that are not UTF-8");
        }
        return new PrecompiledCharsMap(trie, replacements);
    }

    /// <summary><paramref name="text"/> with every rule applied.</summary>
    public string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new StringBuilder(text.Length);
        var clusters = StringInfo.GetTextElementEnumerator(text);
        while (clusters.MoveNext())
        {
            var cluster = clusters.GetTextElement();
            if (Encoding.UTF8.GetByteCount(cluster) < 6 && Transform(cluster) is { } whole)
            {
                output.Append(whole);
                continue;
            }

            foreach (var rune in cluster.EnumerateRunes())
            {
                var single = rune.ToString();
                output.Append(Transform(single) ?? single);
            }
        }
        return output.ToString();
    }

    /// <summary>The replacement for the shortest key prefixing <paramref name="chunk"/>, or null when none does.</summary>
    private string? Transform(string chunk)
    {
        var offset = ShortestKeyValue(Encoding.UTF8.GetBytes(chunk));
        if (offset < 0 || offset > _replacements.Length) return null;
        var end = Array.IndexOf(_replacements, (byte)0, offset);
        return Encoding.UTF8.GetString(_replacements, offset, (end < 0 ? _replacements.Length : end) - offset);
    }

    // The reference's common-prefix search, stopped at its FIRST (shortest) leaf. A position past the array is a
    // miss: a well-formed trie never reaches one, and a malformed one must not throw on every call.
    private int ShortestKeyValue(ReadOnlySpan<byte> key)
    {
        var node = Offset(_trie[0]);
        foreach (var c in key)
        {
            if (c == 0) break;
            node ^= c;
            if (node >= (uint)_trie.Length) return -1;
            var unit = _trie[node];
            if (Label(unit) != c) return -1;
            node ^= Offset(unit);
            if (node >= (uint)_trie.Length) return -1;
            if (HasLeaf(unit)) return Value(_trie[node]);
        }
        return -1;
    }

    private static bool HasLeaf(uint unit) => ((unit >> 8) & 1) == 1;

    private static int Value(uint unit) => (int)(unit & 0x7FFFFFFFu);

    private static uint Label(uint unit) => unit & 0x800000FFu;

    private static uint Offset(uint unit) => (unit >> 10) << (int)((unit & (1u << 9)) >> 6);

    private static InvalidDataException Malformed(string why) =>
        new($"The tokenizer's precompiled_charsmap {why}, so its normalization rules cannot be read.");
}
