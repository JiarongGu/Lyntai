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
}

/// <summary>What the ONNX provider feeds its graph: one encoded row per WINDOW, and which rows belong to which
/// input — with an input inside the window encoded exactly as the tokenizer alone encodes it.</summary>
public class WindowedTokenizerTests
{
    internal static readonly List<string> Vocabulary =
    [
        "[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", ".", "?",
        "how", "many", "people", "live", "in", "berlin", "the", "river", "runs", "north", "city", "is", "old",
        "under", "##ground",
    ];

    internal static readonly WordPieceTokenizer Tokenizer = WordPieceTokenizer.FromVocabulary(Vocabulary);

    internal static WindowedTokenizer Windows(int maxTokens) =>
        new(Tokenizer, TokenBoundaries.FromVocabulary(Vocabulary), maxTokens);

    internal const string Query = "how many people live in berlin?";                 // 7 tokens

    /// <summary><paramref name="sentences"/> sentences of five tokens each.</summary>
    internal static string River(int sentences) =>
        string.Join(' ', Enumerable.Repeat("the river runs north.", sentences));

    internal static void AssertSame(WordPieceEncoding expected, WordPieceEncoding actual)
    {
        Assert.Equal(expected.Ids, actual.Ids);
        Assert.Equal(expected.AttentionMask, actual.AttentionMask);
        Assert.Equal(expected.TokenTypeIds, actual.TokenTypeIds);
    }

    [Fact]
    public void Texts_within_the_window_encode_EXACTLY_as_the_tokenizer_does()
    {
        string[] texts = ["the city is old.", "", River(2), "underground"];

        var batch = Windows(16).EncodeTexts(texts);

        Assert.Equal([0, 1, 2, 3, 4], batch.First);
        for (var i = 0; i < texts.Length; i++) AssertSame(Tokenizer.Encode(texts[i], 16), batch.Rows[i]);
    }

    [Fact]
    public void Pairs_within_the_window_encode_EXACTLY_as_the_tokenizer_does()
    {
        string[] documents = ["the city is old.", "", River(4)];

        var batch = Windows(32).EncodePairs(Query, documents);

        Assert.Equal([0, 1, 2, 3], batch.First);
        for (var i = 0; i < documents.Length; i++)
            AssertSame(Tokenizer.Encode(Query, documents[i], 32), batch.Rows[i]);
    }

    [Fact]
    public void A_text_past_the_window_is_several_rows_that_lose_no_token()
    {
        var text = River(8);                                   // 40 tokens against a 14-token budget
        var content = Tokenizer.EncodeToIds(text);

        var batch = Windows(16).EncodeTexts([text, "the city is old."]);

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
        var batch = Windows(32).EncodePairs(Query, [River(10)]);   // 50 tokens against a 22-token budget

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

    [Fact]
    public void A_query_that_leaves_NO_room_falls_back_to_the_tokenizers_own_truncation()
    {
        // 12 tokens of window, 9 left after the specials, and a 14-token query: nothing to segment into
        var longQuery = "how many people live in berlin how many people live in berlin now?";
        string[] documents = [River(6), "the city is old."];

        var batch = Windows(12).EncodePairs(longQuery, documents);

        Assert.Equal([0, 1, 2], batch.First);
        for (var i = 0; i < documents.Length; i++)
            AssertSame(Tokenizer.Encode(longQuery, documents[i], 12), batch.Rows[i]);
    }

    [Fact]
    public void A_window_too_small_for_the_special_tokens_is_refused_as_the_tokenizer_refuses_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(2).EncodeTexts(["x"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Windows(3).EncodePairs("q", ["x"]));
    }
}

/// <summary>The two HEADS over a window-segmented batch, with the graph replaced by a function of the rows it
/// is fed — the half of a call a test can reach without a model on disk.</summary>
public class OnnxWindowedHeadTests
{
    private static readonly int Berlin = WindowedTokenizerTests.Vocabulary.IndexOf("berlin");

    /// <summary>A stand-in cross-encoder: a row scores the number of times the DOCUMENT side says "berlin".</summary>
    private static double[] CountBerlin(WordPieceEncoding[] rows) =>
        [.. rows.Select(r => (double)r.Ids.Where((id, t) => r.TokenTypeIds[t] == 1 && id == Berlin).Count())];

    [Fact]
    public void A_document_past_the_window_scores_as_its_BEST_window_not_its_first()
    {
        // the one relevant sentence sits in a MIDDLE window: neither the first nor the last mentions berlin
        var document = $"{WindowedTokenizerTests.River(8)} berlin is old. {WindowedTokenizerTests.River(8)}";
        var fed = new List<WordPieceEncoding[]>();

        var scores = OnnxCrossEncoderHead.Score(WindowedTokenizerTests.Windows(32), WindowedTokenizerTests.Query,
            [document, "the city is old.", "berlin is old."],
            rows => { fed.Add(rows); return CountBerlin(rows); });

        Assert.Equal([1.0, 0.0, 1.0], scores);                 // one per document, in input order
        var rows = Assert.Single(fed);
        Assert.True(rows.Length >= 5, $"{rows.Length} rows");   // every window reached the graph, in ONE pass
        Assert.Equal(0.0, CountBerlin([rows[0]])[0]);
    }

    [Fact]
    public void An_in_window_call_feeds_the_graph_EXACTLY_what_the_tokenizer_alone_would()
    {
        string[] documents = ["the city is old.", WindowedTokenizerTests.River(4), ""];
        WordPieceEncoding[]? fed = null;

        OnnxCrossEncoderHead.Score(WindowedTokenizerTests.Windows(32), WindowedTokenizerTests.Query, documents,
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

        var vectors = OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16), [WindowedTokenizerTests.River(9)],
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
        var vectors = OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16),
            ["the city is old.", WindowedTokenizerTests.River(9)],
            rows => [.. rows.Select((_, r) => r == 0 ? new float[] { 1f, 2f, 2f } : [0f, 3f, 4f])]);

        Assert.Equal([1f, 2f, 2f], vectors[0]);                // not normalised, though its neighbour's is
        Assert.Equal(1.0, Math.Sqrt(vectors[1].Sum(x => (double)x * x)), 1e-6);
    }

    [Fact]
    public void An_in_window_embed_feeds_the_graph_EXACTLY_what_the_tokenizer_alone_would()
    {
        string[] texts = ["the city is old.", WindowedTokenizerTests.River(2), ""];
        WordPieceEncoding[]? fed = null;

        OnnxPoolingHead.Embed(WindowedTokenizerTests.Windows(16), texts,
            rows => { fed = rows; return [.. rows.Select(_ => new float[] { 1f })]; });

        Assert.Equal(texts.Length, fed!.Length);
        for (var i = 0; i < texts.Length; i++)
            WindowedTokenizerTests.AssertSame(WindowedTokenizerTests.Tokenizer.Encode(texts[i], 16), fed[i]);
    }
}
