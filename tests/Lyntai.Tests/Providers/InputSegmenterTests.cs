using Lyntai.Providers.Http;

namespace Lyntai.Tests.Providers;

/// <summary>The splitter behind <see cref="HttpModelOptions.MaxInputChars"/>: an over-long input becomes
/// pieces within the budget, cut at the best boundary in each window's latter half, never between a surrogate
/// pair, with a small overlap — and an input within the budget is left exactly as it came.</summary>
public class InputSegmenterTests
{
    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"w{i:00}"));

    [Fact]
    public void An_input_within_the_budget_is_ONE_piece_the_input_itself_untrimmed()
    {
        const string input = "  padded, and within the budget  ";

        var piece = Assert.Single(InputSegmenter.Split(input, input.Length));

        Assert.Same(input, piece);
    }

    [Theory]
    [InlineData(40, "prose")]
    [InlineData(16, "cjk")]
    [InlineData(25, "emoji")]
    [InlineData(7, "one-long-word")]
    [InlineData(3, "punctuation")]
    public void The_spans_cover_the_input_in_order_and_the_pieces_are_those_spans_trimmed(int budget, string kind)
    {
        var input = Sample(kind);

        var spans = InputSegmenter.Spans(input, budget);
        var pieces = InputSegmenter.Split(input, budget);

        Assert.True(spans.Count >= 2, $"{kind} should need splitting at {budget}");
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(input.Length, spans[^1].End);
        for (var i = 1; i < spans.Count; i++)
        {
            Assert.True(spans[i].Start > spans[i - 1].Start, "every piece starts after the one before");
            Assert.True(spans[i].Start <= spans[i - 1].End, "no gap between consecutive pieces");
        }
        Assert.All(spans, s => Assert.True(s.End - s.Start <= budget));

        // joined without the overlap, the spans ARE the input
        var joined = string.Concat(spans.Select((s, i) =>
            input[s.Start..(i + 1 < spans.Count ? spans[i + 1].Start : s.End)]));
        Assert.Equal(input, joined);

        var trimmed = spans.Select(s => input[s.Start..s.End].Trim()).Where(p => p.Length > 0);
        Assert.Equal(trimmed, pieces);
    }

    [Theory]
    // a sentence end beats the later whitespace
    [InlineData("one two three four five. six seven eight nine ten eleven twelve thirteen", 40,
        "one two three four five.")]
    // whitespace beats a hard cut
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 40, "aaaaaaaaaaaaaaaaaaaaaaaaa")]
    // a blank line beats a later line break and a later sentence end
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\nyy. zz\nwwvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv", 50,
        "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    // a line break beats a later sentence end
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\nyy. zzvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv", 50,
        "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void The_cut_prefers_a_blank_line_then_a_line_break_then_a_sentence_end_then_whitespace(
        string input, int budget, string firstPiece)
    {
        Assert.Equal(firstPiece, InputSegmenter.Split(input, budget)[0]);
    }

    [Fact]
    public void The_cut_is_searched_only_in_the_windows_LATTER_half()
    {
        // the line break outranks a sentence end but sits in the FIRST half; cutting there would halve the piece
        const string input = "head\none two three four five six. seven eight nine ten eleven twelve";

        Assert.Equal("head\none two three four five six.", InputSegmenter.Split(input, 40)[0]);
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaa 3.14159265358979323846 and more words after it")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaa e.g.fused!tokens?here;joined and more words after it")]
    public void ASCII_punctuation_NOT_followed_by_whitespace_is_no_sentence_end(string input)
    {
        // the only whitespace in the latter half is after the a's; a decimal point or an abbreviation is not a cut
        Assert.Equal(new string('a', 22), InputSegmenter.Split(input, 40)[0]);
    }

    [Fact]
    public void The_restart_prefers_a_sentence_end_over_earlier_whitespace_in_the_tail()
    {
        // the line break puts the cut at 98; the tail [84, 98) holds whitespace at 88 and a sentence end at 94
        var input = new string('a', 87) + " bbbbb. cc\n" + new string('d', 60);

        var spans = InputSegmenter.Spans(input, 100);

        Assert.Equal(98, spans[0].End);
        Assert.Equal(94, spans[1].Start);
    }

    [Fact]
    public void The_restart_takes_the_EARLIEST_boundary_in_the_tail_for_the_most_overlap()
    {
        // the cut is at 95; the tail [81, 95) holds whitespace boundaries at 88 and 92
        var input = new string('a', 87) + " bbb cc\n" + new string('d', 60);

        var spans = InputSegmenter.Spans(input, 100);

        Assert.Equal(95, spans[0].End);
        Assert.Equal(88, spans[1].Start);
    }

    [Fact]
    public void A_pair_that_overruns_a_one_character_budget_leaves_no_empty_span_behind()
    {
        Assert.Equal([(0, 1), (1, 3)], InputSegmenter.Spans("a😀", 1));
    }

    [Fact]
    public void A_CJK_sentence_end_needs_no_whitespace_after_it()
    {
        var pieces = InputSegmenter.Split("这是第一句话。这是第二句话。这是第三句话。这是第四句话。", 16);

        Assert.Equal(["这是第一句话。这是第二句话。", "这是第三句话。这是第四句话。"], pieces);
    }

    [Fact]
    public void A_run_with_no_boundary_is_hard_cut_at_the_budget_with_no_overlap()
    {
        var spans = InputSegmenter.Spans(new string('a', 250), 100);

        Assert.Equal([(0, 100), (100, 200), (200, 250)], spans);
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        // an odd budget puts every hard cut between the halves of a pair
        var input = string.Concat(Enumerable.Repeat("😀", 60));

        var pieces = InputSegmenter.Split(input, 25);

        Assert.All(pieces, p =>
        {
            Assert.False(char.IsLowSurrogate(p[0]), "a piece starts with the second half of a pair");
            Assert.False(char.IsHighSurrogate(p[^1]), "a piece ends with the first half of a pair");
            Assert.True(p.Length <= 25);
        });
        Assert.Equal(input, string.Concat(pieces));
    }

    [Fact]
    public void The_next_piece_restarts_inside_the_last_15_percent_when_a_boundary_is_there()
    {
        var input = Words(30);   // "w00 w01 … w29": whitespace every four characters

        var spans = InputSegmenter.Spans(input, 40);
        var pieces = InputSegmenter.Split(input, 40);

        Assert.True(spans[1].Start < spans[0].End, "the second piece should overlap the first");
        Assert.True(spans[0].End - spans[1].Start <= 0.15 * (spans[0].End - spans[0].Start));
        Assert.Equal(pieces[0].Split(' ')[^1], pieces[1].Split(' ')[0]);
    }

    [Theory]
    [InlineData(0.0, 40)]    // no overlap: each piece starts where the last one ended
    [InlineData(0.15, 36)]   // the default: the last word of the piece before
    [InlineData(0.5, 20)]    // half a window back
    public void The_OVERLAP_is_the_share_of_the_piece_the_next_one_reaches_back_into(double overlap, int restart)
    {
        var spans = InputSegmenter.Spans(Words(30), 40, overlap);   // the first piece ends at 40

        Assert.Equal(40, spans[0].End);
        Assert.Equal(restart, spans[1].Start);
    }

    [Fact]
    public void TRUNCATING_keeps_the_first_piece_segmenting_would_send()
    {
        var input = Words(30);

        Assert.Equal(InputSegmenter.Split(input, 40)[0], InputSegmenter.Truncate(input, 40));
        Assert.Equal("one two three four five.",
            InputSegmenter.Truncate("one two three four five. six seven eight nine ten eleven twelve thirteen", 40));
    }

    [Fact]
    public void Truncating_leaves_an_input_within_the_budget_EXACTLY_as_given()
    {
        const string input = "  padded, and within the budget  ";

        Assert.Same(input, InputSegmenter.Truncate(input, input.Length));
    }

    [Fact]
    public void Truncating_never_splits_a_surrogate_pair()
    {
        var cut = InputSegmenter.Truncate(string.Concat(Enumerable.Repeat("😀", 60)), 25);

        Assert.Equal(24, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    // ---- the COUNT is taken after NFKC normalisation; the text cut and sent is the original ----------------

    [Theory]
    [InlineData("℃")]         // °C
    [InlineData("㎡")]         // m2
    [InlineData("㍿")]         // 株式会社
    [InlineData("㌚")]         // ミリバール
    [InlineData("ﷺ")]         // eighteen characters of Arabic
    public void A_compatibility_character_COUNTS_as_its_NFKC_form(string character)
    {
        var nfkc = character.Normalize(System.Text.NormalizationForm.FormKC).Length;

        Assert.True(nfkc > character.Length, "the case should expand under NFKC");
        Assert.Equal(nfkc, InputSegmenter.Measure(character));
    }

    [Theory]
    [InlineData("plain ASCII text, already normal")]
    [InlineData("é composes to one character")]   // a combining mark, composed by NFKC
    [InlineData("㎡ and ℃ beside 株式会社 and ﷺ")]
    [InlineData("👩‍👩‍👧 a family, 🇯🇵 a flag")]
    [InlineData("ｆｕｌｌ－ｗｉｄｔｈ　ｔｅｘｔ")]
    public void The_count_is_NEVER_below_the_NFKC_length_of_the_whole(string text)
    {
        Assert.True(InputSegmenter.Measure(text) >= text.Normalize(System.Text.NormalizationForm.FormKC).Length);
    }

    [Fact]
    public void An_input_whose_RAW_length_fits_but_whose_NFKC_length_does_not_is_split()
    {
        var input = string.Concat(Enumerable.Repeat("㎡", 15));   // 15 characters, 30 after NFKC

        var pieces = InputSegmenter.Split(input, 20);

        Assert.True(pieces.Count >= 2, $"{pieces.Count} piece(s)");
        Assert.All(pieces, p => Assert.True(InputSegmenter.Measure(p) <= 20, $"a piece counting {InputSegmenter.Measure(p)}"));
        Assert.Equal(input, string.Concat(pieces));   // cut from, and sent as, the ORIGINAL text
    }

    [Fact]
    public void Counting_NFKC_is_LINEAR_in_the_length_of_the_text()
    {
        // 400,000 characters that are not NFKC-normal: re-normalising each candidate piece would be quadratic
        var input = string.Concat(Enumerable.Repeat("㎡ ℃ ", 100_000));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var pieces = InputSegmenter.Split(input, 1_000);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.All(pieces, p => Assert.True(InputSegmenter.Measure(p) <= 1_000));
    }

    [Fact]
    public void A_cut_never_falls_INSIDE_a_text_element()
    {
        // a base with two combining marks is one element: splitting it would count its marks as nothing
        var input = string.Concat(Enumerable.Repeat("á̂", 40));

        var spans = InputSegmenter.Spans(input, 10);

        Assert.All(spans, s => Assert.Equal('a', input[s.Start]));
    }

    // ---- MaxPiecesPerInput: the pieces kept are spread from the first to the LAST ----------------------------

    [Theory]
    [InlineData(1, new[] { 0 })]
    [InlineData(2, new[] { 0, 3 })]
    [InlineData(3, new[] { 0, 2, 3 })]
    [InlineData(4, new[] { 0, 1, 2, 3 })]
    [InlineData(9, new[] { 0, 1, 2, 3 })]
    public void A_piece_cap_keeps_that_many_pieces_the_first_at_the_start_and_the_last_at_the_TAIL(
        int cap, int[] kept)
    {
        var all = InputSegmenter.Split(Words(30), 40);   // four pieces

        var plan = InputSegmenter.Segment([Words(30), "short"], 40, maxPieces: cap);

        Assert.Equal(4, all.Count);
        Assert.Equal(kept.Select(i => all[i]), plan.Pieces.Take(plan.First[1]));
        Assert.Equal("short", plan.Pieces[^1]);
    }

    // ---- a reranker's query, cut ONCE per call to its share of the pair window ------------------------------

    [Fact]
    public void A_query_within_its_share_of_the_pair_window_is_kept_EXACTLY()
    {
        const string query = "where is the needle";

        Assert.Same(query, InputSegmenter.QueryWithin(query, 60, 0.5));
    }

    [Theory]
    [InlineData(506, 0.5, 253)]   // an adopting application's own bound under a 512-token window
    [InlineData(60, 0.5, 30)]
    [InlineData(60, 0.8, 12)]
    public void A_longer_query_keeps_at_most_its_share_cut_at_a_WORD_boundary(int window, double share, int most)
    {
        var query = Words(200);

        var kept = InputSegmenter.QueryWithin(query, window, share);

        Assert.InRange(InputSegmenter.Measure(kept), most / 2, most);
        Assert.StartsWith(kept, query, StringComparison.Ordinal);
        Assert.Equal(' ', query[kept.Length]);   // it ended where a word did
    }

    [Theory]
    [InlineData("long-word", 7)]
    [InlineData("dots", 3)]
    [InlineData("cjk-ends", 3)]
    [InlineData("dot-space", 5)]
    [InlineData("newlines", 2)]
    [InlineData("spaces", 10)]
    [InlineData("bangs", 1)]
    [InlineData("emoji", 3)]
    [InlineData("a-b", 1)]
    public void Adversarial_input_terminates_and_every_piece_fits(string kind, int budget)
    {
        var input = Adversarial(kind);

        var pieces = InputSegmenter.Split(input, budget);

        Assert.NotEmpty(pieces);
        Assert.True(pieces.Count <= input.Length);
        Assert.All(pieces, p => Assert.True(p.Length <= budget, $"a {p.Length}-character piece at {budget}"));
    }

    [Fact]
    public void An_ALL_WHITESPACE_input_over_the_budget_stays_ONE_piece_within_it()
    {
        // every span trims to nothing; dropping them all would leave the input with no answer
        var input = string.Concat(Enumerable.Repeat(" \n\t ", 50));

        var piece = Assert.Single(InputSegmenter.Split(input, 30));

        Assert.Equal(input[..30], piece);
    }

    [Fact]
    public void The_same_input_and_budget_always_give_the_same_pieces()
    {
        var input = Sample("prose");

        Assert.Equal(InputSegmenter.Split(input, 40), InputSegmenter.Split(input, 40));
    }

    [Fact]
    public void A_budget_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputSegmenter.Split("anything", 0));
    }

    [Fact]
    public void Several_inputs_keep_their_order_and_their_piece_ranges()
    {
        var inputs = new[] { "short", Words(30), "  also short  " };

        var plan = InputSegmenter.Segment(inputs, 40);

        Assert.Equal(1, plan.Segmented);
        Assert.Equal("short", plan.Pieces[0]);
        Assert.Equal("  also short  ", plan.Pieces[^1]);   // untouched, like any input within the bound
        Assert.Equal(0, plan.First[0]);
        Assert.Equal(1, plan.First[1]);
        Assert.Equal(plan.Pieces.Count - 1, plan.First[2]);
        Assert.Equal(plan.Pieces.Count, plan.First[3]);
        Assert.Equal(InputSegmenter.Split(inputs[1], 40), plan.Pieces.Skip(1).Take(plan.First[2] - 1));
    }

    [Fact]
    public void With_nothing_over_the_budget_the_inputs_are_sent_as_given()
    {
        var inputs = new[] { "one", " two ", "three\n" };

        var plan = InputSegmenter.Segment(inputs, 40);

        Assert.Same(inputs, plan.Pieces);
        Assert.Equal(0, plan.Segmented);
    }

    private static string Sample(string kind) => kind switch
    {
        "prose" => "The first sentence is here. The second one follows it closely!\nA new line starts; "
            + "then a paragraph ends.\n\nAnother paragraph begins with more words than one piece can hold, "
            + "and it keeps going until the text is long enough to need several pieces.",
        "cjk" => string.Concat(Enumerable.Repeat("这是一句比较长的中文句子。", 6)),
        "emoji" => string.Concat(Enumerable.Repeat("😀", 60)),
        "one-long-word" => new string('x', 100),
        "punctuation" => new string('.', 40),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Adversarial(string kind) => kind switch
    {
        "long-word" => new string('x', 10_000),
        "dots" => new string('.', 5_000),
        "cjk-ends" => new string('。', 5_000),
        "dot-space" => string.Concat(Enumerable.Repeat(". ", 3_000)),
        "newlines" => new string('\n', 4_000),
        "spaces" => new string(' ', 4_000),
        "bangs" => string.Concat(Enumerable.Repeat("!?", 3_000)),
        "emoji" => string.Concat(Enumerable.Repeat("😀", 3_000)),
        "a-b" => string.Concat(Enumerable.Repeat("a b", 3_000)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
