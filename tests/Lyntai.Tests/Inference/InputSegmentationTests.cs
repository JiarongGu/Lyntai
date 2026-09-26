using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>The one configuration every provider with a window shares for an input longer than that window
/// (<c>docs/DECISIONS.md</c> <b>D177</b>): segment or truncate, how much consecutive windows overlap, and how
/// much of a reranker pair's window the document keeps.</summary>
public class InputSegmentationTests
{
    [Fact]
    public void A_new_record_SEGMENTS_with_a_15_percent_overlap_and_keeps_a_document_half_the_window()
    {
        var segmentation = new InputSegmentation();

        Assert.Equal(InputOverflow.Segment, segmentation.Overflow);
        Assert.Equal(0.15, segmentation.Overlap);
        Assert.Equal(0.5, segmentation.MinDocumentShare);
    }

    [Fact]
    public void A_new_record_puts_NO_cap_on_the_pieces_of_an_input()
    {
        Assert.Null(new InputSegmentation().MaxPiecesPerInput);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void A_piece_cap_of_one_or_more_is_accepted(int cap)
    {
        var segmentation = new InputSegmentation { MaxPiecesPerInput = cap };
        Assert.Equal(cap, segmentation.MaxPiecesPerInput);

        segmentation.MaxPiecesPerInput = null;   // back to unbounded
        Assert.Null(segmentation.MaxPiecesPerInput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_piece_cap_under_one_is_refused(int cap)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new InputSegmentation { MaxPiecesPerInput = cap });

        Assert.Equal(nameof(InputSegmentation.MaxPiecesPerInput), ex.ParamName);
    }

    [Theory]
    [InlineData(InputOverflow.Segment)]
    [InlineData(InputOverflow.Truncate)]
    public void Either_overflow_is_accepted(InputOverflow overflow)
    {
        Assert.Equal(overflow, new InputSegmentation { Overflow = overflow }.Overflow);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void An_UNDEFINED_overflow_is_refused_rather_than_read_differently_by_each_provider(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new InputSegmentation { Overflow = (InputOverflow)value });

        Assert.Equal(nameof(InputSegmentation.Overflow), ex.ParamName);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void An_overlap_from_none_to_half_a_window_is_accepted(double overlap)
    {
        Assert.Equal(overlap, new InputSegmentation { Overlap = overlap }.Overlap);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_overlap_outside_none_to_half_a_window_is_refused(double overlap)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new InputSegmentation { Overlap = overlap });

        Assert.Equal(nameof(InputSegmentation.Overlap), ex.ParamName);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.99)]
    public void A_document_share_strictly_between_none_and_all_is_accepted(double share)
    {
        Assert.Equal(share, new InputSegmentation { MinDocumentShare = share }.MinDocumentShare);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void A_document_share_of_none_or_all_of_the_window_is_refused(double share)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new InputSegmentation { MinDocumentShare = share });

        Assert.Equal(nameof(InputSegmentation.MinDocumentShare), ex.ParamName);
    }

    // ---- a REQUEST may narrow the piece cap, never widen it ------------------------------------------------

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, 2, 2)]      // a record with no cap: the request's is the only one
    [InlineData(3, null, 3)]      // a request asking nothing keeps the record's
    [InlineData(3, 2, 2)]         // the request narrows it
    [InlineData(2, 5, 2)]         // and never widens it
    public void A_requests_piece_cap_is_the_lower_of_its_own_and_the_records(int? own, int? requested, int? expected) =>
        Assert.Equal(expected, new InputSegmentation { MaxPiecesPerInput = own }.MaxPiecesFor(requested));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_request_piece_cap_under_one_is_refused(int cap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRequest("q", ["d"]) { MaxPiecesPerInput = cap });
}
