using Lyntai.Providers.Http;
using Lyntai.Providers.Onnx;

namespace Lyntai.Tests.Providers;

/// <summary><see cref="Lyntai.Inference.InputSegmentation.MaxPiecesPerInput"/>'s rule — keep that many pieces,
/// the first at the start and the last at the tail, the rest evenly between — held by BOTH packages that apply
/// it. Neither can reach the other's internals, so each keeps its own few lines, and this one table holds
/// both to the same answer.</summary>
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
    public void The_HTTP_segmenter_keeps_the_same_pieces_as_the_rule(int count, int cap, int[] kept)
    {
        Assert.Equal(kept, InputSegmenter.Spread(Enumerable.Range(0, count).ToList(), cap));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_ONNX_segmenter_keeps_the_same_windows_as_the_rule(int count, int cap, int[] kept)
    {
        Assert.Equal(kept, TokenSegmenter.Spread(Enumerable.Range(0, count).ToList(), cap));
    }

    [Fact]
    public void No_cap_keeps_every_piece_on_both()
    {
        List<int> pieces = [0, 1, 2, 3, 4];

        Assert.Same(pieces, InputSegmenter.Spread(pieces, null));
        Assert.Same(pieces, TokenSegmenter.Spread(pieces, null));
    }
}
