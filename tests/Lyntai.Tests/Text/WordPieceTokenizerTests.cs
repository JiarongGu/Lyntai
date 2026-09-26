using System.Text;
using Microsoft.ML.Tokenizers;
using Lyntai.Tests.Fakes;

// Microsoft.ML.Tokenizers ships a WordPieceTokenizer of its own, so the type under test is aliased rather
// than imported. BertTokenizer, not that type, is the reference: it runs the FULL pipeline (clean, CJK,
// lowercase, strip accents, punctuation, then WordPiece).
using WordPieceTokenizer = Lyntai.Text.WordPieceTokenizer;
using TokenEncoding = Lyntai.Text.TokenEncoding;

namespace Lyntai.Tests.Text;

/// <summary>The WordPiece tokenizer the static vector backend owns.
///
/// <para><b>Why the library writes its own.</b> <c>Microsoft.ML.Tokenizers</c> costs 325,896 B and drags
/// <c>Google.Protobuf</c> (489,568 B) for the SentencePiece models this vector backend never loads — 812 KB of
/// closure for one WordPiece call. Owning it lets the static vector backend live in the dependency-free
/// <c>Lyntai.Providers.Basic</c> and keep that package's trim/AOT claim.</para>
///
/// <para><b>The risk that buys, and how it is pinned.</b> A tokenizer that disagrees by one rule produces
/// finite, plausible, WRONG vectors — the failure mode no smoke test sees. So the load-bearing test here is
/// not any single rule below: it is
/// <see cref="Produces_the_SAME_ids_as_the_Microsoft_ML_Tokenizers_implementation_it_replaces"/>, which
/// compares every id against <c>Microsoft.ML.Tokenizers</c>. That package stays referenced by the TEST
/// project for exactly that reason and by nothing in <c>src/</c>.</para>
/// </summary>
public class WordPieceTokenizerTests
{
    /// <summary>A BERT vocabulary needs its special tokens present, and WordPiece needs [UNK].</summary>
    private static List<string> Vocabulary(params string[] words) =>
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", .. words];

    private static WordPieceTokenizer Tokenizer(IReadOnlyList<string> vocabulary) =>
        WordPieceTokenizer.FromVocabulary(vocabulary);

    [Fact]
    public void Encodes_a_whole_word_to_its_own_row()
    {
        var tokenizer = Tokenizer(Vocabulary("alpha", "beta"));

        Assert.Equal([5, 6], tokenizer.EncodeToIds("alpha beta"));
    }

    [Fact]
    public void Adds_NO_CLS_or_SEP_because_the_table_was_built_without_them()
    {
        // Pinned because it is invisible if it changes: a model2vec table is a mean over the rows its
        // CONTENT tokens select, so bracketing would fold two more rows into every vector.
        var tokenizer = Tokenizer(Vocabulary("alpha"));

        Assert.Equal([5], tokenizer.EncodeToIds("alpha"));
    }

    [Fact]
    public void Lowercases_because_the_model_declares_do_lower_case()
    {
        var tokenizer = Tokenizer(Vocabulary("alpha"));

        Assert.Equal([5], tokenizer.EncodeToIds("ALPHA"));
    }

    [Fact]
    public void Takes_the_LONGEST_matching_prefix_then_continuation_pieces()
    {
        // Greedy longest-match-first is the whole of WordPiece. "playing" must not become [UNK] while
        // "play" and "##ing" both sit in the vocabulary.
        var tokenizer = Tokenizer(Vocabulary("play", "##ing", "pla"));

        Assert.Equal([5, 6], tokenizer.EncodeToIds("playing"));
    }

    [Fact]
    public void An_unmatchable_word_is_ONE_unk_not_one_per_character()
    {
        // The dangerous alternative is per-character fallback, which floods the mean with [UNK]'s row
        // proportionally to word length rather than once.
        var tokenizer = Tokenizer(Vocabulary("alpha"));

        Assert.Equal([1], tokenizer.EncodeToIds("zzzz"));
    }

    [Fact]
    public void A_word_whose_TAIL_cannot_be_matched_is_unk_ENTIRELY_not_its_matched_prefix()
    {
        // WordPiece is all-or-nothing per word: if any piece fails, the whole word is [UNK]. Emitting the
        // matched prefix instead would silently change what a vector means.
        var tokenizer = Tokenizer(Vocabulary("play", "##ing"));

        Assert.Equal([1], tokenizer.EncodeToIds("playzz"));
    }

    [Fact]
    public void Splits_on_PUNCTUATION_so_a_trailing_comma_is_not_part_of_the_word()
    {
        var tokenizer = Tokenizer(Vocabulary("alpha", ",", "beta"));

        Assert.Equal([5, 6, 7], tokenizer.EncodeToIds("alpha,beta"));
    }

    [Fact]
    public void Gives_each_CJK_character_its_own_token()
    {
        // tokenize_chinese_chars: true. Without it "中文" is one unmatched word and the whole phrase
        // collapses to a single [UNK] — which is how a Chinese corpus silently stops being searchable.
        var tokenizer = Tokenizer(Vocabulary("中", "文"));

        Assert.Equal([5, 6], tokenizer.EncodeToIds("中文"));
    }

    [Fact]
    public void Strips_ACCENTS_because_strip_accents_follows_do_lower_case()
    {
        var tokenizer = Tokenizer(Vocabulary("cafe"));

        Assert.Equal([5], tokenizer.EncodeToIds("café"));
    }

    [Fact]
    public void A_word_past_the_length_cap_is_unk_rather_than_scanned_quadratically()
    {
        // 100 characters is BERT's own max_input_chars_per_word. The cap is not cosmetic: longest-match
        // over a pathological token is quadratic, so a single 10,000-character "word" would dominate a run.
        var tokenizer = Tokenizer(Vocabulary("a", "##a"));

        Assert.Equal([1], tokenizer.EncodeToIds(new string('a', 101)));
    }

    [Fact]
    public void Treats_every_whitespace_kind_as_a_separator()
    {
        var tokenizer = Tokenizer(Vocabulary("alpha", "beta"));

        Assert.Equal([5, 6], tokenizer.EncodeToIds("alpha\t\n\r beta"));
    }

    [Fact]
    public void An_empty_or_whitespace_text_yields_no_ids_at_all()
    {
        var tokenizer = Tokenizer(Vocabulary("alpha"));

        Assert.Empty(tokenizer.EncodeToIds(""));
        Assert.Empty(tokenizer.EncodeToIds("   \t "));
    }

    /// <summary>Inputs where <c>Microsoft.ML.Tokenizers</c> departs from the reference BERT pipeline, so
    /// matching it would mean reproducing a defect. Each is asserted in
    /// <see cref="Corrects_the_four_rules_the_previous_tokenizer_got_wrong"/> rather than merely skipped —
    /// an unexplained exclusion list is how a real disagreement gets filed as a known difference.</summary>
    private static readonly string[] WhereTheReferenceImplementationIsWrong =
        ["alpha\tbeta", "alpha\nbeta", "alpha\rbeta"];

    /// <summary>The load-bearing test: every id must match <c>Microsoft.ML.Tokenizers</c>, wherever it
    /// follows the reference pipeline.
    ///
    /// <para>The corpus hits the rules that differ BETWEEN implementations rather than the ones everybody
    /// agrees on — casing, accents, CJK, punctuation runs, digits, subword splits and unmatchable words —
    /// because a tokenizer that agrees on plain English and diverges on the rest is exactly the one that
    /// ships.</para></summary>
    [Fact]
    public void Produces_the_SAME_ids_as_the_Microsoft_ML_Tokenizers_implementation_it_replaces()
    {
        var vocabulary = Vocabulary(
            "alpha", "beta", "play", "##ing", "##ed", "cafe", "中", "文", ",", ".", "!", "-",
            "the", "quick", "brown", "fox", "1", "2", "##3", "don", "'", "t", "un", "##able", "s");

        // stripAccents: false MATCHES THE REFERENCE'S DEFAULT, and is not this library's shipped setting.
        // BertTokenizer.Create leaves accents on; a BERT model's own tokenizer_config.json declares
        // strip_accents: null, which the reference pipeline reads as "follow do_lower_case" = ON. The two
        // disagree, so this test pins the PIPELINE by holding the options equal, and
        // Model2VecProviderTests pins which options the model actually gets.
        var ours = WordPieceTokenizer.FromVocabulary(vocabulary, stripAccents: false);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', vocabulary)));

        // DECLARED as Tokenizer, not BertTokenizer, and that is not a style choice. BertTokenizer shadows
        // EncodeToIds with a `new` method that adds [CLS]/[SEP]; the base one does not, and the unbracketed
        // call is the behaviour being reproduced — typing it BertTokenizer here compares against a DIFFERENT
        // tokenizer and every id shifts by two positions.
        Tokenizer theirs = BertTokenizer.Create(stream);

        string[] corpus =
        [
            "alpha beta",
            "ALPHA Beta",
            "playing played",
            "café",
            "中文 alpha",
            "alpha, beta. the!",
            "alpha,,,beta",
            "don't",
            "unable",
            "123",
            "the quick brown fox",
            "  alpha   beta  ",
            "alpha\u00A0beta", // a NO-BREAK space: escaped, because written raw it reads as the first row
            "zzzz alpha",
            "-alpha-",
            "ALPHA中文beta",
        ];

        // Every mismatch at once, not the first: a tokenizer diverging on one rule usually diverges on
        // several, and finding them one run at a time hides how big the disagreement is.
        var divergences = new List<string>();
        // (the whitespace separators the reference gets wrong are not here: the next test asserts them)
        foreach (var text in corpus)
        {
            var expected = theirs.EncodeToIds(text);
            var actual = ours.EncodeToIds(text);
            if (!expected.SequenceEqual(actual))
                divergences.Add(
                    $"'{text}' -> reference [{string.Join(',', expected)}], ours [{string.Join(',', actual)}]");
        }

        Assert.True(divergences.Count == 0, string.Join(Environment.NewLine, divergences));
    }

    /// <summary>The places this tokenizer deliberately does NOT reproduce its predecessor — each a
    /// departure from the reference BERT pipeline rather than a preference, and each one text SILENTLY
    /// LOST rather than mis-split.
    ///
    /// <para><b>It asserts the predecessor's wrong answer as well as ours</b>, so if
    /// <c>Microsoft.ML.Tokenizers</c> ever fixes one this test fails and the exclusion can be deleted —
    /// where a one-sided assertion would keep passing and the note would rot.</para></summary>
    [Fact]
    public void Corrects_the_four_rules_the_previous_tokenizer_got_wrong()
    {
        var vocabulary = Vocabulary("alpha", "beta", "cafe", "$", "=");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', vocabulary)));
        Tokenizer theirs = BertTokenizer.Create(stream);
        var ours = WordPieceTokenizer.FromVocabulary(vocabulary);

        // 1. TAB, NEWLINE and CARRIAGE RETURN separate words. The reference maps them to a space while
        //    cleaning; the predecessor treats them as word characters, so every line break in a document
        //    fuses two words into one unmatchable token and the pair embeds as a single [UNK].
        foreach (var text in WhereTheReferenceImplementationIsWrong)
        {
            Assert.Equal([1], theirs.EncodeToIds(text));
            Assert.Equal([5, 6], ours.EncodeToIds(text));
        }

        // 2. ACCENTS are stripped. A BERT tokenizer_config declares strip_accents: null, which the
        //    reference reads as "follow do_lower_case" — on, for every model this vector backend loads.
        Assert.Equal([1], theirs.EncodeToIds("café"));
        Assert.Equal([7], ours.EncodeToIds("café"));

        // 3. ASCII SYMBOLS are punctuation. The reference splits on 33-47/58-64/91-96/123-126 explicitly
        //    BECAUSE $ ^ + = | < > are Unicode symbols (Sc/Sk/Sm) rather than punctuation (P*). Testing
        //    only the categories drops all seven, though each has its own vocabulary row — so a price, a
        //    comparison and a query string lose characters the model was trained to see.
        Assert.Empty(theirs.EncodeToIds("$"));
        Assert.Equal([8], ours.EncodeToIds("$"));
        Assert.Equal([5, 6], theirs.EncodeToIds("alpha=beta"));
        Assert.Equal([5, 9, 6], ours.EncodeToIds("alpha=beta"));

        // 4. An unmatchable SYMBOL is [UNK], not nothing. Dropping it is the worse direction: [UNK] is a
        //    row the table has an opinion about, where a dropped character silently shortens the mean.
        Assert.Empty(theirs.EncodeToIds("\U0001F600"));
        Assert.Equal([1], ours.EncodeToIds("\U0001F600"));
    }
}

/// <summary><see cref="WordPieceTokenizer.Encode"/> — the TRANSFORMER path, which is the one thing the
/// static vector backend must never get.
///
/// <para>A <c>model2vec</c> table is a mean over content rows, so bracketing it with <c>[CLS]</c>/<c>[SEP]</c>
/// shifts every vector. A BERT graph is the opposite: it takes those tokens plus an attention mask and a
/// segment id as three separate tensors, and omitting any of them does not fail — it returns a plausible,
/// wrong vector.</para></summary>
public class WordPieceEncodeTests
{
    private static List<string> Vocabulary(params string[] words) =>
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", .. words];

    private static WordPieceTokenizer Tokenizer(params string[] words) =>
        WordPieceTokenizer.FromVocabulary(Vocabulary(words));

    [Fact]
    public void Brackets_the_content_with_CLS_and_SEP()
    {
        var encoding = Tokenizer("alpha", "beta").Encode("alpha beta");

        Assert.Equal([2, 5, 6, 3], encoding.Ids);
    }

    [Fact]
    public void Attends_to_every_token_it_emits()
    {
        // The mask exists to mark PADDING, which is added when a batch is assembled rather than here — so a
        // single encoding is all ones. A zero anywhere in this array would silently drop a real token.
        var encoding = Tokenizer("alpha", "beta").Encode("alpha beta");

        Assert.Equal(encoding.Ids.Length, encoding.AttentionMask.Length);
        Assert.All(encoding.AttentionMask, m => Assert.Equal(1, m));
    }

    [Fact]
    public void Marks_every_token_as_SEGMENT_ZERO_because_one_text_is_one_segment()
    {
        // Segment ids are what a cross-encoder uses to tell a query from a document, and what llama.cpp's
        // GGUF conversion zeroes (docs/task-archive.md Part 215). A single-sequence vector backend legitimately sends zeros;
        // the point of emitting the tensor at all is that the graph asks for it.
        var encoding = Tokenizer("alpha", "beta").Encode("alpha beta");

        Assert.Equal(encoding.Ids.Length, encoding.TokenTypeIds.Length);
        Assert.All(encoding.TokenTypeIds, t => Assert.Equal(0, t));
    }

    [Fact]
    public void Truncates_to_maxTokens_INCLUDING_the_two_special_tokens()
    {
        // The off-by-two that matters: a 512-position model rejects 513, so reserving room for [CLS]/[SEP]
        // is the difference between a long document embedding and the whole call throwing.
        var encoding = Tokenizer("alpha").Encode(string.Join(' ', Enumerable.Repeat("alpha", 50)), maxTokens: 8);

        Assert.Equal(8, encoding.Ids.Length);
        Assert.Equal(2, encoding.Ids[0]);
        Assert.Equal(3, encoding.Ids[^1]);
        Assert.Equal(6, encoding.Ids.Count(id => id == 5));
    }

    [Fact]
    public void An_EMPTY_text_is_still_a_valid_sequence_rather_than_nothing()
    {
        // [CLS][SEP] is what BERT does with an empty string. Returning an empty tensor instead would make
        // the graph fail on a batch containing one blank document.
        var encoding = Tokenizer("alpha").Encode("");

        Assert.Equal([2, 3], encoding.Ids);
    }

    [Fact]
    public void Rejects_a_maxTokens_too_small_to_hold_the_special_tokens()
    {
        // Silently returning [CLS][SEP] for maxTokens: 2 would embed every document identically.
        Assert.Throws<ArgumentOutOfRangeException>(() => Tokenizer("alpha").Encode("alpha", maxTokens: 2));
    }

    // ---- the PAIR overload: what a cross-encoder scores -----------------------------------------------
    // Vocabulary() puts the specials first, so [CLS] is 2 and [SEP] is 3 — the same literals the
    // single-text tests above assert with.

    [Fact]
    public void A_pair_is_CLS_a_SEP_b_SEP_with_the_segments_distinguishable()
    {
        var encoding = Tokenizer("red", "blue").Encode("red", "blue");

        Assert.Equal([2, 5, 3, 6, 3], encoding.Ids);
        // segment 0 covers [CLS] a [SEP]; segment 1 covers b [SEP] — the whole signal a cross-encoder reads,
        // and exactly what llama.cpp's GGUF conversion zeroes (docs/task-archive.md Part 215)
        Assert.Equal([0, 0, 0, 1, 1], encoding.TokenTypeIds);
        Assert.All(encoding.AttentionMask, m => Assert.Equal(1, m));
    }

    [Fact]
    public void Truncation_takes_from_the_DOCUMENT_and_leaves_the_query_whole()
    {
        // Losing the tail of a long document costs some evidence; losing the tail of the query changes the
        // question. So b yields first — the asymmetric rule a reranker wants, stated rather than inherited.
        var document = string.Join(' ', Enumerable.Repeat("blue", 40));

        var encoding = Tokenizer("red", "blue").Encode("red", document, maxTokens: 8);

        Assert.Equal(8, encoding.Ids.Length);
        Assert.Equal([2, 5, 3, 6, 6, 6, 6, 3], encoding.Ids);   // the query survives intact
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 1], encoding.TokenTypeIds);
    }

    [Fact]
    public void A_query_too_long_for_the_budget_is_shortened_and_the_document_gets_nothing()
    {
        var query = string.Join(' ', Enumerable.Repeat("red", 40));

        var encoding = Tokenizer("red", "blue").Encode(query, "blue", maxTokens: 6);

        Assert.Equal([2, 5, 5, 5, 3, 3], encoding.Ids);          // both separators adjacent: b kept nothing
        Assert.Equal([0, 0, 0, 0, 0, 1], encoding.TokenTypeIds);
    }

    [Fact]
    public void An_empty_side_still_yields_a_valid_pair()
    {
        var encoding = Tokenizer("red").Encode("", "");

        Assert.Equal([2, 3, 3], encoding.Ids);
        Assert.Equal([0, 0, 1], encoding.TokenTypeIds);
    }

    [Fact]
    public void A_budget_with_no_room_for_content_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tokenizer("red").Encode("a", "b", maxTokens: 3));
    }

    [Fact]
    public void Frame_brackets_given_ids_exactly_as_Encode_brackets_its_own()
    {
        var tokenizer = Tokenizer("alpha", "beta", "gamma");
        const string a = "alpha beta", b = "gamma alpha";

        AssertSame(tokenizer.Encode(a, 64), tokenizer.Frame(tokenizer.EncodeToIds(a)));
        AssertSame(tokenizer.Encode(a, b, 64), tokenizer.Frame(tokenizer.EncodeToIds(a), tokenizer.EncodeToIds(b)));
    }

    [Fact]
    public void Frame_never_truncates_because_the_caller_already_fit_the_window()
    {
        int[] content = [.. Enumerable.Repeat(5, 600)];

        var framed = Tokenizer("alpha").Frame(content);

        Assert.Equal([2, .. content, 3], framed.Ids);
        Assert.All(framed.AttentionMask, m => Assert.Equal(1, m));
        Assert.All(framed.TokenTypeIds, t => Assert.Equal(0, t));
    }

    [Fact]
    public void A_framed_pair_puts_the_second_side_and_its_separator_in_segment_1()
    {
        var framed = Tokenizer("alpha", "beta").Frame([5], [6, 6]);

        Assert.Equal([2, 5, 3, 6, 6, 3], framed.Ids);
        Assert.Equal([0, 0, 0, 1, 1, 1], framed.TokenTypeIds);
    }

    private static void AssertSame(TokenEncoding expected, TokenEncoding actual)
    {
        Assert.Equal(expected.Ids, actual.Ids);
        Assert.Equal(expected.AttentionMask, actual.AttentionMask);
        Assert.Equal(expected.TokenTypeIds, actual.TokenTypeIds);
    }
}

/// <summary><see cref="WordPieceTokenizer.FromModelDirectory"/> — the entry point that takes its rules from
/// the model instead of from the caller's guess.</summary>
public class WordPieceTokenizerFromModelDirectoryTests : IDisposable
{
    private readonly ScratchDir _scratch = new("wordpiece");

    private string Dir => _scratch.Path;

    public void Dispose() => _scratch.Dispose();

    private string WriteVocabulary(params string[] words)
    {
        File.WriteAllLines(Path.Combine(Dir, "vocab.txt"),
            ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", .. words]);
        return Dir;
    }

    [Fact]
    public void Honours_the_rules_the_model_DECLARES_rather_than_the_defaults()
    {
        // do_lower_case: false is the discriminating case — under the default it would match, so a
        // tokenizer that ignored the config would pass a test written with the default.
        WriteVocabulary("alpha");
        File.WriteAllText(Path.Combine(Dir, "tokenizer_config.json"), "{\"do_lower_case\": false}");

        var tokenizer = WordPieceTokenizer.FromModelDirectory(Dir);

        Assert.Equal([5], tokenizer.EncodeToIds("alpha"));
        Assert.Equal([1], tokenizer.EncodeToIds("ALPHA"));
    }

    [Fact]
    public void Falls_back_to_the_reference_defaults_when_the_model_ships_no_config()
    {
        // A hand-assembled model directory is the common case and is not worth refusing to load over.
        var tokenizer = WordPieceTokenizer.FromModelDirectory(WriteVocabulary("alpha"));

        Assert.Equal([5], tokenizer.EncodeToIds("ALPHA"));
    }

    [Fact]
    public void A_missing_vocabulary_names_the_file_rather_than_null_referencing()
    {
        var error = Assert.Throws<FileNotFoundException>(() => WordPieceTokenizer.FromModelDirectory(Dir));

        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>The owned tokenizer against a REAL vocabulary, which the synthetic fixture cannot stand in for.
///
/// <para>A fixture vocabulary is 30 tokens chosen to exercise known rules; a real export is 29,528 rows of
/// PRUNED, overlapping word pieces, where a wrong longest-match still finds <i>something</i> and returns
/// finite, plausible, wrong vectors. Equality over that vocabulary is the check that has somewhere to
/// fail.</para>
///
/// <para>Skipped without <c>LYNTAI_STATIC_MODEL_DIR</c>. Point it at a `potion-*` or `static-retrieval-*`
/// directory.</para></summary>
public class WordPieceTokenizerLiveTests
{
    private static string? ModelDirectory => Environment.GetEnvironmentVariable("LYNTAI_STATIC_MODEL_DIR");

    [SkippableFact]
    public void Matches_the_reference_implementation_over_a_REAL_pruned_vocabulary()
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory),
            "set LYNTAI_STATIC_MODEL_DIR to a model2vec directory");

        var vocabularyPath = Path.Combine(ModelDirectory!, "vocab.txt");
        Skip.IfNot(File.Exists(vocabularyPath), $"no vocab.txt in {ModelDirectory}");

        // Options held equal to the reference's DEFAULTS, and the corpus avoids all four classes the
        // fixture test pins as reference defects — no tab/newline/CR, no accents, no ASCII symbol outside
        // the Unicode punctuation categories. So anything failing here is this implementation's own bug.
        var ours = WordPieceTokenizer.FromVocabulary(File.ReadAllLines(vocabularyPath), stripAccents: false);
        using var stream = File.OpenRead(vocabularyPath);
        Tokenizer theirs = BertTokenizer.Create(stream);

        string[] corpus =
        [
            "the weather forecast for tomorrow",
            "My wife Alice works at an architecture firm in Copenhagen.",
            "unbelievably antidisestablishmentarianism",
            "COVID-19 vaccination rates rose 12.5% between Q3 and Q4 2021.",
            "don't  can't  won't — it's Alice's",
            "user@example.com https://example.com/a/b",
            "中文测试 mixed with English",
            "naive resume facade",
            "supercalifragilisticexpialidocious",
            "1234567890 3.14159 -42",
            "Hello,world!No spaces?After punctuation.",
            "    leading and trailing    ",
            "ALL CAPS SHOUTING TEXT",
            "symbols % & * ( ) { } [ ] \\ / that BOTH agree are punctuation",
        ];

        var divergences = new List<string>();
        foreach (var text in corpus)
        {
            var expected = theirs.EncodeToIds(text);
            var actual = ours.EncodeToIds(text);
            if (!expected.SequenceEqual(actual))
                divergences.Add($"'{text}' -> reference [{string.Join(',', expected)}], "
                                + $"ours [{string.Join(',', actual)}]");
        }

        Assert.True(divergences.Count == 0, string.Join(Environment.NewLine, divergences));
    }

}
