using Lyntai.Inference;
using Lyntai.Providers.Onnx;
using Lyntai.Text;

namespace Lyntai.Tests.Providers;

/// <summary>The token-window splitter behind the ONNX provider (<c>docs/DECISIONS.md</c> <b>D177</b>): an input
/// past the window becomes windows within it, each ending at a sentence end in its latter half, else before a
/// word start, else at the budget, with a small overlap — and an input within the budget is one window.</summary>
public class TokenSegmenterTests
{
    private static readonly string[] Vocabulary =
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "w", "##c", ".", "!", "?", ";", "。", "！", "？", "；", ","];

    private static readonly TokenBoundaries Boundaries = TokenBoundaries.FromVocabulary(Vocabulary);

    /// <summary>Space-separated token strings as ids, so a case reads as the tokens it holds.</summary>
    private static int[] Ids(string tokens) =>
        [.. tokens.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => Array.IndexOf(Vocabulary, t))];

    private static string Repeat(string tokens, int times) => string.Join(' ', Enumerable.Repeat(tokens, times));

    [Fact]
    public void Continuations_and_sentence_ends_are_read_off_the_vocabulary()
    {
        Assert.True(Boundaries.IsContinuation(Array.IndexOf(Vocabulary, "##c")));
        Assert.False(Boundaries.IsContinuation(Array.IndexOf(Vocabulary, "w")));
        Assert.All(new[] { ".", "!", "?", ";", "。", "！", "？", "；" },
            t => Assert.True(Boundaries.EndsSentence(Array.IndexOf(Vocabulary, t)), t));
        Assert.False(Boundaries.EndsSentence(Array.IndexOf(Vocabulary, ",")));
        Assert.False(Boundaries.IsContinuation(Vocabulary.Length));   // an id past the table is neither
        Assert.False(Boundaries.EndsSentence(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void An_input_within_the_budget_is_ONE_window_of_itself(int count)
    {
        var window = Assert.Single(TokenSegmenter.Windows(Ids(Repeat("w", count)), 10, Boundaries));

        Assert.Equal((0, count), window);
    }

    [Theory]
    [InlineData("w w w w w ##c w .", 7, 10)]
    [InlineData("w ##c ##c w ##c ##c ##c", 5, 20)]
    [InlineData("w", 1, 40)]
    [InlineData("##c", 3, 30)]
    [InlineData(".", 2, 25)]
    [InlineData("w , w ; w 。 w ！", 4, 12)]
    [InlineData("w w w w w w w w w w w w w w w w w w w .", 20, 7)]
    public void The_windows_cover_the_input_in_order_each_within_the_budget(string unit, int budget, int times)
    {
        var ids = Ids(Repeat(unit, times));

        var windows = TokenSegmenter.Windows(ids, budget, Boundaries);

        Assert.True(windows.Count >= 2, "the case should need splitting");
        Assert.Equal(0, windows[0].Start);
        Assert.Equal(ids.Length, windows[^1].End);
        Assert.All(windows, w => Assert.InRange(w.End - w.Start, 1, budget));
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.True(windows[i].Start > windows[i - 1].Start, "every window starts after the one before");
            Assert.True(windows[i].End > windows[i - 1].End, "every window ends after the one before");
            Assert.True(windows[i].Start <= windows[i - 1].End, "no token falls between two windows");
        }
        Assert.Equal(windows, TokenSegmenter.Windows(ids, budget, Boundaries));   // deterministic
    }

    [Fact]
    public void A_sentence_end_in_the_latter_half_beats_a_later_word_start()
    {
        var ids = Ids("w w w w w w . w w w w w w w");

        var first = TokenSegmenter.Windows(ids, 10, Boundaries)[0];

        Assert.Equal((0, 7), first);   // ends after the period, not at the budget
    }

    [Fact]
    public void A_sentence_end_in_the_FIRST_half_is_passed_over_for_a_word_start_in_the_latter()
    {
        var ids = Ids("w w . w w w w w w w w w w w w");

        var first = TokenSegmenter.Windows(ids, 10, Boundaries)[0];

        Assert.Equal((0, 10), first);
    }

    [Fact]
    public void No_window_STARTS_on_a_continuation_or_a_sentence_end_while_a_word_start_is_in_reach()
    {
        var ids = Ids(Repeat("w ##c ##c . w ##c w ##c ##c ##c", 12));

        var windows = TokenSegmenter.Windows(ids, 9, Boundaries);

        Assert.True(windows.Count >= 5);
        Assert.All(windows, w =>
        {
            Assert.False(Boundaries.IsContinuation(ids[w.Start]), $"a window starts on ## at {w.Start}");
            Assert.False(Boundaries.EndsSentence(ids[w.Start]), $"a window starts on a period at {w.Start}");
        });
    }

    [Fact]
    public void Consecutive_windows_OVERLAP_where_a_word_start_lies_in_the_last_15_percent()
    {
        var ids = Ids(Repeat("w", 50));

        var windows = TokenSegmenter.Windows(ids, 20, Boundaries);

        Assert.Equal((0, 20), windows[0]);
        Assert.Equal(17, windows[1].Start);   // 15% of 20 is 3 tokens back
    }

    [Fact]
    public void The_overlap_restarts_after_a_SENTENCE_END_in_it_before_an_earlier_word_start()
    {
        // periods at 35 and 38: the cut lands after the second, and the overlap (34..38) holds a word start
        // at 34 — but the restart takes the sentence that begins after the first period
        var ids = Ids($"{Repeat("w", 35)} . w w . w w w w w w w w w w w w w w w w");

        var windows = TokenSegmenter.Windows(ids, 40, Boundaries);

        Assert.Equal((0, 39), windows[0]);
        Assert.Equal(36, windows[1].Start);
    }

    [Fact]
    public void With_no_boundary_in_the_latter_half_the_cut_is_HARD_at_the_budget_and_nothing_overlaps()
    {
        var ids = Ids($"w {Repeat("##c", 30)}");

        var windows = TokenSegmenter.Windows(ids, 8, Boundaries);

        Assert.Equal(new (int, int)[] { (0, 8), (8, 16), (16, 24), (24, 31) }, windows);
    }

    [Theory]
    [InlineData("w ##c . ; , w", 1)]
    [InlineData(".", 1)]
    [InlineData(".", 3)]
    [InlineData("##c", 2)]
    [InlineData("w", 1)]
    public void Terminates_on_ADVERSARIAL_input_with_at_most_one_window_per_token(string unit, int budget)
    {
        var ids = Ids(Repeat(unit, 40));

        var windows = TokenSegmenter.Windows(ids, budget, Boundaries);

        Assert.InRange(windows.Count, 2, ids.Length);
        Assert.Equal(ids.Length, windows[^1].End);
    }

    [Fact]
    public void A_budget_under_one_token_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenSegmenter.Windows(Ids("w w"), 0, Boundaries));
    }

    [Theory]
    [InlineData(0.0, 20)]    // no overlap: each window starts where the last one ended
    [InlineData(0.15, 17)]
    [InlineData(0.5, 10)]    // half a window back
    public void The_OVERLAP_is_the_share_of_the_window_the_next_one_reaches_back_into(double overlap, int restart)
    {
        var windows = TokenSegmenter.Windows(Ids(Repeat("w", 50)), 20, Boundaries, overlap);

        Assert.Equal((0, 20), windows[0]);
        Assert.Equal(restart, windows[1].Start);
    }

    [Fact]
    public void A_RUN_of_sentence_ends_never_starts_a_window()
    {
        // the run straddles the budget: cutting inside it would start the next window on a period
        var ids = Ids($"{Repeat("w", 8)} . . . {Repeat("w", 10)}");

        var windows = TokenSegmenter.Windows(ids, 10, Boundaries);

        Assert.All(windows, w => Assert.False(Boundaries.EndsSentence(ids[w.Start]), $"a window starts on a period at {w.Start}"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(12)]
    public void Runs_of_sentence_ends_start_no_window_at_any_budget(int budget)
    {
        var ids = Ids(Repeat("w w w w . . .", 12));

        var windows = TokenSegmenter.Windows(ids, budget, Boundaries);

        Assert.All(windows, w => Assert.False(Boundaries.EndsSentence(ids[w.Start]), $"a window starts on a period at {w.Start}"));
    }
}

/// <summary>What the ONNX provider feeds its graph: one encoded row per input, or per WINDOW once
/// <see cref="InputSegmentation"/> says to segment. No record means the tokenizer's own truncation; under a
/// record a text inside the window is still the tokenizer's row, while a pair's query is cut once per call to
/// its share of the window.</summary>
public class WindowedTokenizerTests
{
    internal static readonly List<string> Vocabulary =
    [
        "[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", ".", "?",
        "how", "many", "people", "live", "in", "berlin", "the", "river", "runs", "north", "city", "is", "old",
        "under", "##ground",
    ];

    internal static readonly WordPieceTokenizer Tokenizer = WordPieceTokenizer.FromVocabulary(Vocabulary);

    internal static WindowedTokenizer Windows(int maxTokens, InputSegmentation? segmentation = null) =>
        new(Tokenizer, TokenBoundaries.FromVocabulary(Vocabulary), maxTokens, segmentation);

    /// <summary>A fresh record that segments, with every other value at its default.</summary>
    internal static InputSegmentation Segment => new();

    internal const string Query = "how many people live in berlin?";                 // 7 tokens

    /// <summary><paramref name="sentences"/> sentences of five tokens each.</summary>
    internal static string River(int sentences) =>
        string.Join(' ', Enumerable.Repeat("the river runs north.", sentences));

    /// <summary>A query of exactly <paramref name="tokens"/> tokens.</summary>
    private static string Berlins(int tokens) => string.Join(' ', Enumerable.Repeat("berlin", tokens));

    /// <summary>A pair row's query tokens and document tokens: <c>[CLS] query [SEP] document [SEP]</c>.</summary>
    private static (int[] Query, int[] Document) Sides(WordPieceEncoding row)
    {
        var separator = Array.IndexOf(row.Ids, 3);
        return (row.Ids[1..separator], row.Ids[(separator + 1)..^1]);
    }

    internal static void AssertSame(WordPieceEncoding expected, WordPieceEncoding actual)
    {
        Assert.Equal(expected.Ids, actual.Ids);
        Assert.Equal(expected.AttentionMask, actual.AttentionMask);
        Assert.Equal(expected.TokenTypeIds, actual.TokenTypeIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Texts_within_the_window_encode_EXACTLY_as_the_tokenizer_does(bool segment)
    {
        string[] texts = ["the city is old.", "", River(2), "underground"];

        var batch = Windows(16, segment ? Segment : null).EncodeTexts(texts);

        Assert.Equal([0, 1, 2, 3, 4], batch.First);
        for (var i = 0; i < texts.Length; i++) AssertSame(Tokenizer.Encode(texts[i], 16), batch.Rows[i]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pairs_within_the_window_whose_query_keeps_to_its_share_encode_EXACTLY_as_the_tokenizer_does(
        bool segment)
    {
        string[] documents = ["the city is old.", "", River(4)];

        var batch = Windows(32, segment ? Segment : null).EncodePairs(Query, documents);

        Assert.Equal([0, 1, 2, 3], batch.First);
        for (var i = 0; i < documents.Length; i++)
            AssertSame(Tokenizer.Encode(Query, documents[i], 32), batch.Rows[i]);
    }

    [Fact]
    public void With_NO_segmentation_an_over_long_input_is_TRUNCATED_exactly_as_the_tokenizer_truncates()
    {
        // the provider's default, and the behaviour it had before segmenting existed — including a query
        // long enough to leave the document nothing, which the tokenizer's own pair rule allows
        var texts = Windows(16).EncodeTexts([River(8)]);
        var pairs = Windows(32).EncodePairs(Query, [River(10)]);
        var longQuery = Windows(12).EncodePairs(Berlins(14), [River(6)]);

        AssertSame(Tokenizer.Encode(River(8), 16), Assert.Single(texts.Rows));
        AssertSame(Tokenizer.Encode(Query, River(10), 32), Assert.Single(pairs.Rows));
        AssertSame(Tokenizer.Encode(Berlins(14), River(6), 12), Assert.Single(longQuery.Rows));
    }

    [Fact]
    public void An_explicit_TRUNCATE_is_one_row_per_input_cut_at_the_window()
    {
        var truncate = new InputSegmentation { Overflow = InputOverflow.Truncate };

        var texts = Windows(16, truncate).EncodeTexts([River(8), "the city is old."]);
        var pairs = Windows(32, truncate).EncodePairs(Query, [River(10)]);

        Assert.Equal([0, 1, 2], texts.First);
        AssertSame(Tokenizer.Encode(River(8), 16), texts.Rows[0]);
        AssertSame(Tokenizer.Encode(Query, River(10), 32), Assert.Single(pairs.Rows));   // a short query cuts nothing
    }

    [Fact]
    public void A_text_past_the_window_is_several_rows_that_lose_no_token()
    {
        var text = River(8);                                   // 40 tokens against a 14-token budget
        var content = Tokenizer.EncodeToIds(text);

        var batch = Windows(16, Segment).EncodeTexts([text, "the city is old."]);

        var rows = batch.Rows[batch.First[0]..batch.First[1]];
        Assert.True(rows.Length >= 3, $"{rows.Length} rows");
        Assert.Equal(batch.First[1] + 1, batch.First[2]);      // the short neighbour is still one row
        Assert.All(rows, row =>
        {
            Assert.InRange(row.Ids.Length, 3, 16);
            Assert.Equal(2, row.Ids[0]);                        // [CLS]
            Assert.Equal(3, row.Ids[^1]);                       // [SEP]
            Assert.All(row.AttentionMask, m => Assert.Equal(1, m));
            Assert.All(row.TokenTypeIds, t => Assert.Equal(0, t));
        });

        // every window is a run of the text's own tokens, and together they reach its last one
        var windows = TokenSegmenter.Windows(content, 14, TokenBoundaries.FromVocabulary(Vocabulary));
        Assert.Equal(windows.Count, rows.Length);
        for (var r = 0; r < rows.Length; r++)
        {
            Assert.Equal(content.Skip(windows[r].Start).Take(windows[r].End - windows[r].Start), rows[r].Ids[1..^1]);
            Assert.Equal(windows[r].End - windows[r].Start, batch.Weights[batch.First[0] + r]);
        }
        Assert.Equal(content.Count, windows[^1].End);
    }

    [Fact]
    public void A_document_past_the_window_keeps_the_WHOLE_query_in_every_row()
    {
        var batch = Windows(32, Segment).EncodePairs(Query, [River(10)]);   // 50 tokens against a 22-token budget

        var queryRow = Tokenizer.Encode(Query, string.Empty, 32);   // [CLS] query [SEP] [SEP]
        Assert.True(batch.Rows.Length >= 3, $"{batch.Rows.Length} rows");
        Assert.All(batch.Rows, row =>
        {
            Assert.InRange(row.Ids.Length, 12, 32);
            Assert.Equal(queryRow.Ids[..^1], row.Ids[..(queryRow.Ids.Length - 1)]);
            Assert.Equal(3, row.Ids[^1]);
            // segment 0 through the first [SEP], segment 1 after it — the signal a cross-encoder reads
            Assert.Equal(queryRow.TokenTypeIds[..^1], row.TokenTypeIds[..(queryRow.Ids.Length - 1)]);
            Assert.All(row.TokenTypeIds[(queryRow.Ids.Length - 1)..], t => Assert.Equal(1, t));
        });
    }

    // A 32-token window holds 29 content tokens. At the default MinDocumentShare of 0.5 the document keeps
    // ceil(14.5) = 15 of them, so the query may take 14 before it is cut.

    [Theory]
    [InlineData(13, 13, 16)]
    [InlineData(14, 14, 15)]   // the most a query may take whole
    [InlineData(15, 14, 15)]   // one more, and the query is cut
    [InlineData(28, 14, 15)]   // left alone, this document would have one token per window
    [InlineData(29, 14, 15)]   // left alone, it would have none — where the tokenizer's own rule scores the query only
    [InlineData(60, 14, 15)]
    public void The_QUERY_is_cut_so_the_document_keeps_its_MinDocumentShare_of_the_window(
        int queryTokens, int queryKept, int documentBudget)
    {
        var batch = Windows(32, Segment).EncodePairs(Berlins(queryTokens), [River(10)]);

        Assert.All(batch.Rows, row =>
        {
            var (query, document) = Sides(row);
            Assert.Equal(queryKept, query.Length);
            Assert.InRange(document.Length, 1, documentBudget);
            Assert.True(row.Ids.Length <= 32);
        });
        // 50 document tokens in windows of up to 15: a handful of rows, never one per token
        Assert.InRange(batch.Rows.Length, 4, 8);
    }

    [Theory]
    [InlineData(InputOverflow.Segment)]
    [InlineData(InputOverflow.Truncate)]
    public void Under_a_record_the_query_is_cut_ONCE_per_call_the_same_for_a_short_document_and_a_long_one(
        InputOverflow overflow)
    {
        // a 20-token query beside a 5-token document fits the 29-token budget whole — and is still cut to its
        // 14-token share, because every document of one call is scored against the SAME question
        var batch = Windows(32, new InputSegmentation { Overflow = overflow })
            .EncodePairs(Berlins(20), ["the city is old.", River(10)]);

        var queries = batch.Rows.Select(row => Sides(row).Query).ToList();
        Assert.All(queries, query => Assert.Equal(Tokenizer.EncodeToIds(Berlins(20)).Take(14), query));
        Assert.Equal(Sides(batch.Rows[batch.First[0]]).Query, Sides(batch.Rows[batch.First[1]]).Query);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void MaxPiecesPerInput_keeps_that_many_windows_the_first_at_the_start_and_the_last_at_the_TAIL(int cap)
    {
        var text = River(60);                                   // 300 tokens, some thirty windows of 14
        var content = Tokenizer.EncodeToIds(text);
        var all = TokenSegmenter.Windows(content, 14, TokenBoundaries.FromVocabulary(Vocabulary));

        var rows = Windows(16, new InputSegmentation { MaxPiecesPerInput = cap }).EncodeTexts([text]).Rows;

        Assert.Equal(cap, rows.Length);
        Assert.Equal(content.Take(all[0].End - all[0].Start), rows[0].Ids[1..^1]);
        if (cap > 1) Assert.Equal(content.Skip(all[^1].Start), rows[^1].Ids[1..^1]);
    }

    [Fact]
    public void MaxPiecesPerInput_caps_a_documents_windows_under_the_whole_query()
    {
        var batch = Windows(32, new InputSegmentation { MaxPiecesPerInput = 2 }).EncodePairs(Query, [River(30)]);

        var all = TokenSegmenter.Windows(Tokenizer.EncodeToIds(River(30)), 22, TokenBoundaries.FromVocabulary(Vocabulary));
        Assert.Equal(2, batch.Rows.Length);
        Assert.Equal(all[^1].End - all[^1].Start, Sides(batch.Rows[^1]).Document.Length);
    }

    [Theory]
    [InlineData(null, 1, 1)]   // no cap of its own: the request's
    [InlineData(2, 1, 1)]      // the request narrows the record's
    [InlineData(2, 5, 2)]      // and never widens it
    public void A_requests_piece_cap_narrows_a_documents_windows(int? own, int requested, int expected)
    {
        var batch = Windows(32, new InputSegmentation { MaxPiecesPerInput = own })
            .EncodePairs(Query, [River(30)], requested);

        Assert.Equal(expected, batch.Rows.Length);
    }

    [Fact]
    public void A_vanishing_MinDocumentShare_still_leaves_every_window_one_document_token()
    {
        // 1e-30 is below decimal's range, so the share rounds to nothing unless it is floored at one
        var batch = Windows(32, new InputSegmentation { MinDocumentShare = 1e-30 }).EncodePairs(Berlins(40), [River(2)]);

        Assert.All(batch.Rows, row =>
        {
            var (query, document) = Sides(row);
            Assert.Equal(28, query.Length);
            Assert.Single(document);
        });
    }

    [Fact]
    public void A_larger_MinDocumentShare_cuts_the_query_sooner()
    {
        // 0.8 of 29 is 23.2, so the document keeps 24 and the 7-token query is cut to 5
        var batch = Windows(32, new InputSegmentation { MinDocumentShare = 0.8 }).EncodePairs(Query, ["the city is old."]);

        Assert.Equal(Tokenizer.EncodeToIds(Query).Take(5), Sides(Assert.Single(batch.Rows)).Query);
    }

    [Fact]
    public void An_explicit_TRUNCATE_keeps_the_document_its_share_too()
    {
        var batch = Windows(32, new InputSegmentation { Overflow = InputOverflow.Truncate })
            .EncodePairs(Berlins(40), [River(10)]);

        var (query, document) = Sides(Assert.Single(batch.Rows));
        Assert.Equal(14, query.Length);
        Assert.Equal(Tokenizer.EncodeToIds(River(10)).Take(15), document);
    }

    [Fact]
    public void A_window_too_small_for_the_special_tokens_is_refused_as_the_tokenizer_refuses_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(2, Segment).EncodeTexts(["x"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(3, Segment).EncodePairs("q", ["x"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(2).EncodeTexts(["x"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(3).EncodePairs("q", ["x"]));
    }
}

/// <summary>The two HEADS over a windowed batch, with the graph replaced by a function of the rows it is fed —
/// the half of a call a test can reach without a model on disk.</summary>
public class OnnxWindowedHeadTests
{
    private static readonly int Berlin = WindowedTokenizerTests.Vocabulary.IndexOf("berlin");

    /// <summary>A stand-in cross-encoder: a row scores the number of times the DOCUMENT side says "berlin".</summary>
    private static double[] CountBerlin(WordPieceEncoding[] rows) =>
        [.. rows.Select(r => (double)r.Ids.Where((id, t) => r.TokenTypeIds[t] == 1 && id == Berlin).Count())];

    /// <summary>A stand-in embedder whose vector depends only on the row's own content, never on where the
    /// row sits in a pass.</summary>
    private static float[][] ByContent(WordPieceEncoding[] rows) =>
        [.. rows.Select(r => new float[] { r.Ids[1], r.Ids.Length, 1f })];

    [Fact]
    public void A_document_past_the_window_scores_as_its_BEST_window_not_its_first()
    {
        // the one relevant sentence sits in a MIDDLE window: neither the first nor the last mentions berlin
        var document = $"{WindowedTokenizerTests.River(8)} berlin is old. {WindowedTokenizerTests.River(8)}";
        var fed = new List<WordPieceEncoding[]>();

        var scores = OnnxCrossEncoderHead.Score(
            WindowedTokenizerTests.Windows(32, WindowedTokenizerTests.Segment), WindowedTokenizerTests.Query,
            [document, "the city is old.", "berlin is old."],
            rows => { fed.Add(rows); return CountBerlin(rows); });

        Assert.Equal([1.0, 0.0, 1.0], scores);                 // one per document, in input order
        var rows = Assert.Single(fed);                          // 7 rows fit one pass of 8
        Assert.True(rows.Length >= 5, $"{rows.Length} rows");
        Assert.Equal(0.0, CountBerlin([rows[0]])[0]);
    }

    [Fact]
    public void A_requests_piece_cap_reaches_the_rows_the_graph_is_fed()
    {
        var document = $"{WindowedTokenizerTests.River(8)} berlin is old. {WindowedTokenizerTests.River(8)}";
        var fed = new List<WordPieceEncoding[]>();

        var scores = OnnxCrossEncoderHead.Score(
            WindowedTokenizerTests.Windows(32, WindowedTokenizerTests.Segment), WindowedTokenizerTests.Query,
            [document], rows => { fed.Add(rows); return CountBerlin(rows); }, maxPiecesPerInput: 1);

        Assert.Single(Assert.Single(fed));
        Assert.Equal([0.0], scores);   // the one window kept is the first, which does not mention berlin
    }

    [Fact]
    public void With_NO_segmentation_a_document_past_the_window_is_scored_on_its_first_window_alone()
    {
        var document = $"{WindowedTokenizerTests.River(8)} berlin is old. {WindowedTokenizerTests.River(8)}";

        var scores = OnnxCrossEncoderHead.Score(WindowedTokenizerTests.Windows(32), WindowedTokenizerTests.Query,
            [document], CountBerlin);

        Assert.Equal([0.0], scores);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_fitting_score_call_whose_query_keeps_to_its_share_feeds_the_graph_the_tokenizers_EXACT_rows(
        bool segment)
    {
        string[] documents = ["the city is old.", WindowedTokenizerTests.River(4), ""];
        WordPieceEncoding[]? fed = null;

        OnnxCrossEncoderHead.Score(
            WindowedTokenizerTests.Windows(32, segment ? WindowedTokenizerTests.Segment : null),
            WindowedTokenizerTests.Query, documents,
            rows => { fed = rows; return new double[rows.Length]; });

        Assert.Equal(documents.Length, fed!.Length);
        for (var i = 0; i < documents.Length; i++)
            WindowedTokenizerTests.AssertSame(
                WindowedTokenizerTests.Tokenizer.Encode(WindowedTokenizerTests.Query, documents[i], 32), fed[i]);
    }

    [Fact]
    public void A_text_past_the_window_embeds_as_the_TOKEN_weighted_mean_of_its_windows_unit_vectors()
    {
        WordPieceEncoding[]? fed = null;

        var vectors = OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16, WindowedTokenizerTests.Segment),
            [WindowedTokenizerTests.River(9)],
            rows => { fed = rows; return [.. rows.Select((_, r) => new float[] { (r + 1) * 2f, 2f, 0f })]; });

        var vector = Assert.Single(vectors);
        Assert.True(fed!.Length >= 3, $"{fed.Length} rows");
        var expected = new double[3];
        for (var r = 0; r < fed.Length; r++)
        {
            double[] v = [(r + 1) * 2.0, 2.0, 0.0];
            var length = Math.Sqrt(v.Sum(x => x * x));
            var tokens = fed[r].Ids.Length - 2;                 // the window's own tokens, not [CLS]/[SEP]
            for (var k = 0; k < 3; k++) expected[k] += tokens * v[k] / length;
        }
        var total = Math.Sqrt(expected.Sum(x => x * x));
        for (var k = 0; k < 3; k++) Assert.Equal(expected[k] / total, vector[k], 1e-6);
    }

    [Fact]
    public void A_text_within_the_window_keeps_its_vector_EXACTLY_as_the_head_computed_it()
    {
        var vectors = OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16, WindowedTokenizerTests.Segment),
            ["the city is old.", WindowedTokenizerTests.River(9)],
            rows => [.. rows.Select((_, r) => r == 0 ? new float[] { 1f, 2f, 2f } : [0f, 3f, 4f])]);

        Assert.Equal([1f, 2f, 2f], vectors[0]);                // not normalised, though its neighbour's is
        Assert.Equal(1.0, Math.Sqrt(vectors[1].Sum(x => (double)x * x)), 1e-6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_in_window_embed_feeds_the_graph_EXACTLY_what_the_tokenizer_alone_would(bool segment)
    {
        string[] texts = ["the city is old.", WindowedTokenizerTests.River(2), ""];
        WordPieceEncoding[]? fed = null;

        OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16, segment ? WindowedTokenizerTests.Segment : null), texts,
            rows => { fed = rows; return [.. rows.Select(_ => new float[] { 1f })]; });

        Assert.Equal(texts.Length, fed!.Length);
        for (var i = 0; i < texts.Length; i++)
            WindowedTokenizerTests.AssertSame(WindowedTokenizerTests.Tokenizer.Encode(texts[i], 16), fed[i]);
    }

    // ---- passes: the graph never sees more rows at once than the same call unsegmented, or a small floor ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unsegmented_call_is_ONE_pass_however_many_inputs_it_holds(bool segment)
    {
        var texts = Enumerable.Repeat("the city is old.", 3 * WindowedBatch.MinPassRows).ToArray();
        var embedPasses = new List<int>();
        var scorePasses = new List<int>();
        var windows = WindowedTokenizerTests.Windows(32, segment ? WindowedTokenizerTests.Segment : null);

        OnnxPoolingHead.Embed(windows, texts, rows => { embedPasses.Add(rows.Length); return ByContent(rows); });
        OnnxCrossEncoderHead.Score(windows, WindowedTokenizerTests.Query, texts,
            rows => { scorePasses.Add(rows.Length); return new double[rows.Length]; });

        Assert.Equal([texts.Length], embedPasses);
        Assert.Equal([texts.Length], scorePasses);
    }

    [Fact]
    public void A_segmented_call_runs_in_passes_of_at_most_the_floor_or_its_input_count_and_loses_no_row()
    {
        var windows = WindowedTokenizerTests.Windows(16, WindowedTokenizerTests.Segment);
        string[] texts = [WindowedTokenizerTests.River(60), "the city is old."];
        var passes = new List<int>();

        var vectors = OnnxPoolingHead.Embed(windows, texts, rows => { passes.Add(rows.Length); return ByContent(rows); });

        var batch = windows.EncodeTexts(texts);
        Assert.True(passes.Count >= 4, $"{passes.Count} passes");
        Assert.All(passes, rows => Assert.InRange(rows, 1, Math.Max(WindowedBatch.MinPassRows, texts.Length)));
        Assert.Equal(batch.Rows.Length, passes.Sum());
        // chunking must keep each row's answer paired with its row
        var first = batch.Rows[batch.First[0]..batch.First[1]];
        var expected = Lyntai.Memory.VectorMath.WeightedMeanDirection(
            ByContent(first), [.. batch.Weights[batch.First[0]..batch.First[1]].Select(w => (double)w)]);
        Assert.Equal(expected, vectors[0]);
        Assert.Equal(ByContent([batch.Rows[^1]])[0], vectors[1]);
    }

    [Fact]
    public void A_segmented_SCORE_call_runs_in_passes_and_keeps_every_document_its_best_window()
    {
        var document = $"{WindowedTokenizerTests.River(20)} berlin is old. {WindowedTokenizerTests.River(20)}";
        var passes = new List<int>();

        var scores = OnnxCrossEncoderHead.Score(
            WindowedTokenizerTests.Windows(32, WindowedTokenizerTests.Segment), WindowedTokenizerTests.Query,
            [document, WindowedTokenizerTests.River(30)],
            rows => { passes.Add(rows.Length); return CountBerlin(rows); });

        Assert.Equal([1.0, 0.0], scores);
        Assert.True(passes.Count >= 2, $"{passes.Count} passes");
        Assert.All(passes, rows => Assert.InRange(rows, 1, WindowedBatch.MinPassRows));
    }
}

