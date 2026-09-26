using Lyntai.Text;
using static Lyntai.Tests.Text.SentencePieceFixture;

namespace Lyntai.Tests.Text;

/// <summary>SentencePiece's compiled normalizer, against what HF <c>tokenizers</c> produced from the same
/// charsmap — including the grapheme rule that looks like a bug and is the reference.</summary>
public class PrecompiledCharsMapTests
{
    private static readonly PrecompiledCharsMap Map = PrecompiledCharsMap.Parse(Charsmap());
    private static readonly Golden Tiny = Load("spm-tiny.golden.json");

    [Fact]
    public void Normalizes_every_golden_input_exactly_as_the_reference_does()
    {
        var misses = Tiny.Cases.Concat(Tiny.HfOnly)
            .Select(c => (c.Text, Expected: c.Normalized!, Actual: Map.Normalize(c.Text)))
            .Where(r => r.Expected != r.Actual)
            .Select(r => $"{Escape(r.Text)} -> expected {Escape(r.Expected)}, got {Escape(r.Actual)}")
            .ToList();

        Assert.True(misses.Count == 0, string.Join(Environment.NewLine, misses));
    }

    [Fact]
    public void A_short_cluster_is_replaced_WHOLE_by_its_shortest_key_even_when_that_drops_a_mark()
    {
        // "e" + two acutes is ONE 5-byte cluster and "e"+acute is a key, so the second acute goes with it —
        // the reference's rule, and the one input where it and C++ SentencePiece part ways.
        Assert.Equal("é", Map.Normalize("é́"));
    }

    [Fact]
    public void A_cluster_of_six_bytes_or_more_is_looked_up_one_code_point_at_a_time()
    {
        // 7 bytes: no whole lookup, and neither "e" nor a lone acute is a key
        Assert.Equal("é́́", Map.Normalize("é́́"));
    }

    [Fact]
    public void A_key_can_expand_and_a_key_can_delete()
    {
        Assert.Equal("fiTM", Map.Normalize("ﬁ™"));
        Assert.Equal("gone", Map.Normalize("\u0001gone"));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 8, 0, 0, 0, 1, 2 })]            // claims 8 trie bytes, carries 2
    [InlineData(new byte[] { 3, 0, 0, 0, 1, 2, 3, 0 })]      // not a whole number of 4-byte units
    [InlineData(new byte[] { 4, 0, 0, 0, 0, 0, 0, 0, 0xFF })] // a replacement that is not UTF-8
    public void A_truncated_or_malformed_blob_is_refused_rather_than_misread(byte[] blob)
    {
        var error = Assert.Throws<InvalidDataException>(() => PrecompiledCharsMap.Parse(blob));

        Assert.Contains("precompiled_charsmap", error.Message, StringComparison.Ordinal);
    }
}
