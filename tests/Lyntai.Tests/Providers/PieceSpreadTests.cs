using Lyntai.Inference;

namespace Lyntai.Tests.Providers;

/// <summary><see cref="InputSegmentation.MaxPiecesPerInput"/>'s rule — keep that many pieces, the first at the
/// start and the last at the tail, the rest evenly between — and <see cref="InputSegmentation.MinDocumentShare"/>'s
/// rounding, each stated ONCE on the public record every segmenting provider applies.</summary>
public class PieceSpreadTests
{
    public static TheoryData<int, int, int[]> Cases => new()
    {
        { 4, 1, [0] },
        { 4, 2, [0, 3] },
        { 4, 3, [0, 2, 3] },           // 1.5 rounds up
        { 4, 4, [0, 1, 2, 3] },
        { 4, 9, [0, 1, 2, 3] },        // a cap above the count keeps everything
        { 10, 4, [0, 3, 6, 9] },
        { 7, 3, [0, 3, 6] },
        { 100, 3, [0, 50, 99] },
        { 1, 1, [0] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Spread_keeps_the_pieces_the_rule_names(int count, int cap, int[] kept)
    {
        Assert.Equal(kept, InputSegmentation.Spread(Enumerable.Range(0, count).ToList(), cap));
    }

    [Fact]
    public void No_cap_keeps_every_piece_as_the_same_list()
    {
        List<int> pieces = [0, 1, 2, 3, 4];

        Assert.Same(pieces, InputSegmentation.Spread(pieces, null));
    }

    [Fact]
    public void A_cap_under_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputSegmentation.Spread(new[] { 1, 2 }, 0));
    }

    [Theory]
    [InlineData(60, 0.8, 48)]          // decimal, not a binary 48.000…01 that rounds to 49
    [InlineData(10, 0.5, 5)]
    [InlineData(7, 0.5, 4)]            // rounded up
    [InlineData(100, 1e-30, 1)]        // never below one unit
    public void DocumentShare_rounds_up_in_decimal_and_keeps_at_least_one(int window, double share, int expected)
    {
        Assert.Equal(expected, InputSegmentation.DocumentShare(window, share));
    }
}
