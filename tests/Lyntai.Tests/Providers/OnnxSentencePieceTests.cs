using Lyntai.Inference;
using Lyntai.Providers.Onnx;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Text;

namespace Lyntai.Tests.Providers;

/// <summary>The ONNX provider over a SentencePiece export (<c>docs/DECISIONS.md</c> <b>D191</b>): which tokenizer a
/// model directory selects, and the rows it builds in the model's OWN layout — XLM-R's
/// <c>&lt;s&gt; q &lt;/s&gt;&lt;/s&gt; d &lt;/s&gt;</c> with every segment 0 — rather than BERT's.</summary>
public class OnnxSentencePieceTests : IDisposable
{
    private readonly ScratchDir _scratch = new("onnx-spm");

    public void Dispose() => _scratch.Dispose();

    private string WithTokenizerJson()
    {
        File.Copy(SentencePieceFixture.TokenizerPath, _scratch.Combine("tokenizer.json"));
        return _scratch.Path;
    }

    private (ITransformerTokenizer Tokenizer, WindowedTokenizer Windows) Load(int maxTokens)
    {
        var (tokenizer, boundaries) = TransformerTokenizer.FromModelDirectory(WithTokenizerJson());
        return (tokenizer, new WindowedTokenizer(tokenizer, boundaries, maxTokens, new InputSegmentation()));
    }

    private static string River(int sentences) => string.Join(' ', Enumerable.Repeat("the river runs north.", sentences));

    [Fact]
    public void A_directory_with_only_tokenizer_json_loads_SentencePiece()
    {
        Assert.IsType<SentencePieceRows>(TransformerTokenizer.FromModelDirectory(WithTokenizerJson()).Tokenizer);
    }

    [Fact]
    public void Vocab_txt_WINS_so_every_WordPiece_deployment_is_unchanged()
    {
        WithTokenizerJson();
        File.WriteAllLines(_scratch.Combine("vocab.txt"), ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "alpha"]);

        Assert.IsType<WordPieceRows>(TransformerTokenizer.FromModelDirectory(_scratch.Path).Tokenizer);
    }

    [Fact]
    public void A_directory_with_neither_names_BOTH_files()
    {
        var error = Assert.Throws<FileNotFoundException>(() => TransformerTokenizer.FromModelDirectory(_scratch.Path));

        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
        Assert.Contains("tokenizer.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tokenizer_json_that_is_not_Unigram_is_refused_naming_its_model()
    {
        File.WriteAllText(_scratch.Combine("tokenizer.json"), """{"model":{"type":"BPE","vocab":{},"merges":[]}}""");

        var error = Assert.Throws<InvalidDataException>(() => TransformerTokenizer.FromModelDirectory(_scratch.Path));

        Assert.Contains("BPE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tokenizer_that_frames_NO_special_tokens_is_refused_for_a_transformer()
    {
        // pooling reads row zero as the classification token, and an empty text would be a zero-width row
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SentencePieceFixture.TokenizerPath))!.AsObject();
        json["post_processor"] = null;
        File.WriteAllText(_scratch.Combine("tokenizer.json"), json.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => TransformerTokenizer.FromModelDirectory(_scratch.Path));

        Assert.Contains("post-processor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_within_the_window_encodes_EXACTLY_as_the_tokenizer_does()
    {
        var (tokenizer, windows) = Load(64);

        var row = Assert.Single(windows.EncodeTexts([River(1)]).Rows);

        Assert.Equal(tokenizer.Encode(River(1), 64).Ids, row.Ids);
        Assert.Equal(0, row.Ids[0]);
        Assert.Equal(2, row.Ids[^1]);
    }

    [Fact]
    public void A_segmented_text_frames_every_window_in_the_model_s_specials_within_the_window()
    {
        var (_, windows) = Load(16);

        var batch = windows.EncodeTexts([River(12)]);

        Assert.True(batch.Rows.Length > 1);
        Assert.All(batch.Rows, row =>
        {
            Assert.InRange(row.Ids.Length, 3, 16);
            Assert.Equal(0, row.Ids[0]);
            Assert.Equal(2, row.Ids[^1]);
        });
    }

    [Fact]
    public void A_pair_row_is_the_XLM_R_layout_and_a_segmented_document_keeps_every_row_in_the_window()
    {
        var (_, windows) = Load(24);

        var batch = windows.EncodePairs("where does the river run?", [River(12)]);

        Assert.True(batch.Rows.Length > 1, "the document should need several windows");
        Assert.All(batch.Rows, row =>
        {
            Assert.InRange(row.Ids.Length, 6, 24);
            Assert.Equal(0, row.Ids[0]);
            Assert.Equal(2, row.Ids[^1]);
            Assert.All(row.TokenTypeIds, t => Assert.Equal(0, t));   // type_vocab_size 1: a 1 is out of range
            var firstSeparator = Array.IndexOf(row.Ids, 2);
            Assert.Equal(2, row.Ids[firstSeparator + 1]);            // </s></s> between query and document
        });
    }

    [Fact]
    public void An_unsegmented_pair_is_exactly_the_tokenizer_s_own_pair()
    {
        var (tokenizer, boundaries) = TransformerTokenizer.FromModelDirectory(WithTokenizerJson());
        var windows = new WindowedTokenizer(tokenizer, boundaries, 32, segmentation: null);

        var row = Assert.Single(windows.EncodePairs("where does the river run?", [River(12)]).Rows);

        Assert.Equal(tokenizer.Encode("where does the river run?", River(12), 32).Ids, row.Ids);
    }

    [Fact]
    public void SentencePiece_boundaries_a_leading_meta_space_starts_a_word_and_a_sentence_end_wins()
    {
        var boundaries = TokenBoundaries.FromPieces(["<s>", "▁the", "re", ".", "▁.", "。", "▁", "!"]);

        Assert.False(boundaries.IsContinuation(1));
        Assert.True(boundaries.IsContinuation(2));
        Assert.True(boundaries.EndsSentence(3));
        Assert.False(boundaries.IsContinuation(3));   // a sentence end is not also a continuation
        Assert.True(boundaries.EndsSentence(4));
        Assert.True(boundaries.EndsSentence(5));
        Assert.False(boundaries.IsContinuation(6));   // a bare meta space starts a word
        Assert.False(boundaries.EndsSentence(6));
        Assert.True(boundaries.EndsSentence(7));
    }
}
