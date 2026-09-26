using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Tests.Fakes;
using Lyntai.Text;
using static Lyntai.Tests.Text.SentencePieceFixture;

namespace Lyntai.Tests.Text;

/// <summary>The owned SentencePiece tokenizer (<c>docs/DECISIONS.md</c> <b>D191</b>): id for id against the golden
/// answers HF <c>tokenizers</c> gave for the committed fixture, and rule by rule on a synthetic
/// <c>tokenizer.json</c> small enough to read.</summary>
public class SentencePieceTokenizerTests
{
    private static readonly SentencePieceTokenizer Fixture = FromFile(TokenizerPath);
    private static readonly Golden Tiny = Load("spm-tiny.golden.json");

    private static SentencePieceTokenizer FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SentencePieceTokenizer.FromTokenizerJson(stream);
    }

    /// <summary>A minimal XLM-R-shaped tokenizer.json with no normalizer; a test replaces the section it varies.
    /// Ids: 0 &lt;s&gt; · 1 &lt;pad&gt; · 2 &lt;/s&gt; · 3 &lt;unk&gt; · 4 ▁a · 5 ▁b · 6 b · 7 ▁ · 8 a · 9 &lt; · 10 s ·
    /// 11 &gt; · 12 ▁a▁b, a piece only an UNSPLIT word can reach.</summary>
    private static JsonObject Synthetic() => new()
    {
        ["added_tokens"] = new JsonArray(Special(0, "<s>"), Special(1, "<pad>"), Special(2, "</s>"), Special(3, "<unk>")),
        ["normalizer"] = null,
        ["pre_tokenizer"] = JsonNode.Parse("""
            {"type":"Sequence","pretokenizers":[{"type":"WhitespaceSplit"},
             {"type":"Metaspace","replacement":"▁","add_prefix_space":true}]}
            """),
        ["post_processor"] = JsonNode.Parse("""{"type":"RobertaProcessing","sep":["</s>",2],"cls":["<s>",0]}"""),
        ["model"] = JsonNode.Parse("""
            {"type":"Unigram","unk_id":3,"vocab":[["<s>",0.0],["<pad>",0.0],["</s>",0.0],["<unk>",0.0],
             ["▁a",-1.0],["▁b",-1.0],["b",-2.0],["▁",-3.0],["a",-3.0],["<",-4.0],["s",-4.0],[">",-4.0],
             ["▁a▁b",-0.5]]}
            """),
    };

    private static JsonObject Special(int id, string content) => new() { ["id"] = id, ["content"] = content, ["special"] = true };

    private static SentencePieceTokenizer LoadJson(JsonObject json) =>
        SentencePieceTokenizer.FromTokenizerJson(new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString())));

    private static JsonNode? Metaspace(string settings) => JsonNode.Parse(
        $$"""{"type":"Sequence","pretokenizers":[{"type":"WhitespaceSplit"},{"type":"Metaspace","replacement":"▁",{{settings}}}]}""");

    [Fact]
    public void Every_golden_input_encodes_to_the_reference_ids()
    {
        var misses = Tiny.Cases.Concat(Tiny.HfOnly)
            .Select(c => (c.Text, c.Ids, Actual: Fixture.EncodeToIds(c.Text).ToArray()))
            .Where(r => !r.Ids.SequenceEqual(r.Actual))
            .Select(r => $"{Escape(r.Text)} -> expected [{string.Join(',', r.Ids)}], got [{string.Join(',', r.Actual)}]")
            .ToList();

        Assert.True(misses.Count == 0, string.Join(Environment.NewLine, misses));
    }

    [Fact]
    public void The_Sequence_pipeline_multilingual_e5_declares_encodes_to_the_reference_ids()
    {
        // the same model under Sequence[Precompiled, Replace(" {2,}" -> " ")] and Metaspace with no WhitespaceSplit
        var tokenizer = FromFile(Path.Combine(SentencePieceFixture.Directory, "spm-tiny-sequence.tokenizer.json"));
        var golden = Load("spm-tiny-sequence.golden.json");

        var misses = golden.Cases.Concat(golden.HfOnly)
            .Select(c => (c.Text, c.Ids, Actual: tokenizer.EncodeToIds(c.Text).ToArray()))
            .Where(r => !r.Ids.SequenceEqual(r.Actual))
            .Select(r => $"{Escape(r.Text)} -> expected [{string.Join(',', r.Ids)}], got [{string.Join(',', r.Actual)}]")
            .ToList();

        Assert.True(misses.Count == 0, string.Join(Environment.NewLine, misses));
    }

    [Theory]
    [InlineData("""{"Regex":"b{2,}"}""", "b", "abbb", new[] { 4, 6 })]      // a run collapses: unreplaced, [4,6,6,6]
    [InlineData("""{"String":"b"}""", "a", "b", new[] { 4 })]               // a literal: unreplaced, [5]
    [InlineData("""{"String":"b"}""", "", "ab", new[] { 4 })]               // an empty content deletes: unreplaced, [4,6]
    public void A_Replace_normalizer_rewrites_every_match_before_pre_tokenizing(
        string pattern, string content, string text, int[] expected)
    {
        var json = Synthetic();
        json["normalizer"] = JsonNode.Parse($$"""{"type":"Replace","pattern":{{pattern}},"content":"{{content}}"}""");

        Assert.Equal(expected, LoadJson(json).EncodeToIds(text));
    }

    [Fact]
    public void A_Sequence_normalizer_applies_its_members_in_order()
    {
        // "b" -> "a" then "a" -> "b": in order, every "b" comes back as "b"; reversed, both would end as "a"
        var json = Synthetic();
        json["normalizer"] = JsonNode.Parse("""
            {"type":"Sequence","normalizers":[{"type":"Replace","pattern":{"String":"b"},"content":"a"},
             {"type":"Replace","pattern":{"String":"a"},"content":"b"}]}
            """);

        Assert.Equal([5, 5], LoadJson(json).EncodeToIds("a b"));
    }

    [Fact]
    public void Typed_special_tokens_stay_TEXT_where_the_reference_would_parse_them()
    {
        Assert.NotEmpty(Tiny.TypedSpecials);
        foreach (var typed in Tiny.TypedSpecials)
        {
            var actual = Fixture.EncodeToIds(typed.Text);
            Assert.Equal(typed.Ids, actual);            // C++ SentencePiece: the characters
            Assert.NotEqual(typed.HfIds, actual);       // HF: a control token — the divergence is deliberate (D191)
        }
    }

    [Fact]
    public void A_pair_frames_as_the_reference_template_does()
    {
        Assert.NotEmpty(Tiny.Pairs);
        foreach (var pair in Tiny.Pairs)
        {
            var encoding = Fixture.Encode(pair.A, pair.B, 512);

            Assert.Equal(pair.Ids, encoding.Ids);
            Assert.Equal(pair.TypeIds, encoding.TokenTypeIds);
            Assert.All(encoding.AttentionMask, m => Assert.Equal(1, m));
        }
    }

    [Fact]
    public void The_XLM_R_layout_is_s_A_s_and_s_A_s_s_B_s_all_in_segment_0()
    {
        Assert.Equal([0, 2], Fixture.Encode(string.Empty).Ids);
        Assert.Equal([0, 2, 2, 2], Fixture.Encode(string.Empty, string.Empty).Ids);

        var framed = Fixture.Frame([7, 8], [9]);

        Assert.Equal([0, 7, 8, 2, 2, 9, 2], framed.Ids);
        Assert.All(framed.TokenTypeIds, t => Assert.Equal(0, t));
    }

    [Fact]
    public void A_BertProcessing_pair_puts_the_second_side_in_segment_1()
    {
        var json = Synthetic();
        json["post_processor"] = JsonNode.Parse("""{"type":"BertProcessing","sep":["</s>",2],"cls":["<s>",0]}""");

        var framed = LoadJson(json).Frame([4], [5]);

        Assert.Equal([0, 4, 2, 5, 2], framed.Ids);
        Assert.Equal([0, 0, 0, 1, 1], framed.TokenTypeIds);
    }

    [Fact]
    public void With_no_post_processor_there_are_no_specials_and_the_second_side_is_segment_1()
    {
        var json = Synthetic();
        json["post_processor"] = null;
        var tokenizer = LoadJson(json);

        Assert.Equal([4], tokenizer.Encode("a", maxTokens: 1).Ids);
        Assert.Equal([0, 1], tokenizer.Frame([4], [5]).TokenTypeIds);
    }

    [Fact]
    public void The_pair_budget_is_spent_on_the_second_text_first()
    {
        // four specials leave three content tokens, and the query takes all three
        var encoding = LoadJson(Synthetic()).Encode("a a a", "b b b", maxTokens: 7);

        Assert.Equal([0, 4, 4, 4, 2, 2, 2], encoding.Ids);
    }

    [Fact]
    public void A_long_text_is_cut_to_the_window_INCLUDING_its_specials()
    {
        var encoding = LoadJson(Synthetic()).Encode(string.Join(' ', Enumerable.Repeat("a", 50)), maxTokens: 6);

        Assert.Equal([0, 4, 4, 4, 4, 2], encoding.Ids);
    }

    [Fact]
    public void A_window_that_cannot_hold_one_content_token_is_refused()
    {
        var tokenizer = LoadJson(Synthetic());

        Assert.Throws<ArgumentOutOfRangeException>(() => tokenizer.Encode("a", maxTokens: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => tokenizer.Encode("a", "b", maxTokens: 4));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t\n ")]
    public void Blank_text_is_no_content_and_the_bare_specials(string text)
    {
        var tokenizer = LoadJson(Synthetic());

        Assert.Empty(tokenizer.EncodeToIds(text));
        Assert.Equal([0, 2], tokenizer.Encode(text).Ids);
    }

    [Fact]
    public void A_lone_surrogate_is_an_unknown_character_not_a_throw()
    {
        Assert.Equal([7, 3], LoadJson(Synthetic()).EncodeToIds("\ud800"));
    }

    [Theory]
    [InlineData("\"prepend_scheme\":\"always\"", new[] { 4, 5 })]
    [InlineData("\"prepend_scheme\":\"never\"", new[] { 8, 6 })]
    [InlineData("\"add_prefix_space\":false,\"prepend_scheme\":\"never\"", new[] { 8, 6 })]
    [InlineData("\"add_prefix_space\":true,\"prepend_scheme\":\"never\"", new[] { 8, 6 })]   // the scheme wins
    public void Metaspace_prepends_by_its_declared_scheme(string settings, int[] expected)
    {
        var json = Synthetic();
        json["pre_tokenizer"] = Metaspace(settings);

        Assert.Equal(expected, LoadJson(json).EncodeToIds("a b"));
    }

    [Fact]
    public void Metaspace_SPLITS_before_every_replacement_when_split_is_absent()
    {
        // "a▁b" -> "▁a▁b" -> ["▁a", "▁b"]; unsplit, the word would reach the "▁a▁b" piece
        Assert.Equal([4, 5], LoadJson(Synthetic()).EncodeToIds("a▁b"));
    }

    [Fact]
    public void Metaspace_keeps_the_word_whole_when_split_is_false()
    {
        var json = Synthetic();
        json["pre_tokenizer"] = Metaspace("\"prepend_scheme\":\"always\",\"split\":false");

        Assert.Equal([12], LoadJson(json).EncodeToIds("a▁b"));
    }

    [Fact]
    public void An_empty_text_reaches_no_step_so_Metaspace_alone_adds_no_meta_space()
    {
        var json = Synthetic();
        json["pre_tokenizer"] = JsonNode.Parse("""{"type":"Metaspace","replacement":"▁","prepend_scheme":"always"}""");
        var tokenizer = LoadJson(json);

        Assert.Empty(tokenizer.EncodeToIds(string.Empty));
        Assert.Equal([7], tokenizer.EncodeToIds(" "));      // a space IS a word to Metaspace alone: "▁"
    }

    [Fact]
    public void With_no_pre_tokenizer_the_whole_text_is_one_word()
    {
        var json = Synthetic();
        json["pre_tokenizer"] = null;

        Assert.Equal([8, 3, 6], LoadJson(json).EncodeToIds("a b"));   // the space is a character with no piece
    }

    [Theory]
    [InlineData("model", """{"type":"BPE","vocab":{},"merges":[]}""", "BPE")]
    [InlineData("model", """{"type":"WordPiece","vocab":{}}""", "WordPiece")]
    [InlineData("model", """{"type":"Unigram","unk_id":3,"byte_fallback":true,"vocab":[["<s>",0.0],["<pad>",0.0],["</s>",0.0],["<unk>",0.0]]}""", "byte_fallback")]
    [InlineData("model", """{"type":"Unigram","vocab":[["a",-1.0]]}""", "unk_id")]
    [InlineData("model", """{"type":"Unigram","unk_id":9,"vocab":[["a",-1.0]]}""", "unk_id")]
    [InlineData("normalizer", """{"type":"NFKC"}""", "NFKC")]
    [InlineData("normalizer", """{"type":"Sequence","normalizers":[{"type":"NFKC"}]}""", "NFKC")]
    [InlineData("normalizer", """{"type":"Replace","pattern":{"Glob":"*"},"content":""}""", "Glob")]
    [InlineData("normalizer", """{"type":"Precompiled","precompiled_charsmap":"%%%"}""", "precompiled_charsmap")]
    [InlineData("pre_tokenizer", """{"type":"ByteLevel","add_prefix_space":false}""", "ByteLevel")]
    [InlineData("pre_tokenizer", """{"type":"Metaspace","replacement":"▁","prepend_scheme":"first"}""", "first")]
    [InlineData("pre_tokenizer", """{"type":"Metaspace","replacement":"▁","add_prefix_space":false}""", "add_prefix_space")]
    [InlineData("post_processor", """{"type":"ByteLevel"}""", "ByteLevel")]
    public void An_unsupported_component_is_refused_BY_NAME(string section, string node, string named)
    {
        var json = Synthetic();
        json[section] = JsonNode.Parse(node);

        var error = Assert.Throws<InvalidDataException>(() => LoadJson(json));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_that_is_not_JSON_is_refused_as_invalid_data()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            SentencePieceTokenizer.FromTokenizerJson(new MemoryStream("{ not json"u8.ToArray())));

        Assert.Contains("tokenizer.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JSON_of_the_wrong_shape_is_refused_as_invalid_data()
    {
        var json = Synthetic();
        json["model"] = JsonNode.Parse("""{"type":"Unigram","unk_id":3,"vocab":[["<s>","not a score"]]}""");

        Assert.Throws<InvalidDataException>(() => LoadJson(json));
    }

    [Fact]
    public void A_missing_tokenizer_json_names_the_file()
    {
        using var scratch = new ScratchDir("spm");

        var error = Assert.Throws<FileNotFoundException>(() => SentencePieceTokenizer.FromModelDirectory(scratch.Path));

        Assert.Contains("tokenizer.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromModelDirectory_reads_the_directory_s_tokenizer_json()
    {
        using var scratch = new ScratchDir("spm");
        File.Copy(TokenizerPath, scratch.Combine("tokenizer.json"));

        var tokenizer = SentencePieceTokenizer.FromModelDirectory(scratch.Path);

        Assert.Equal(Fixture.EncodeToIds("Hello world."), tokenizer.EncodeToIds("Hello world."));
    }

    [Fact]
    public void Pieces_are_the_vocabulary_in_id_order()
    {
        var tokenizer = LoadJson(Synthetic());

        Assert.Equal(13, tokenizer.Pieces.Count);
        Assert.Equal("<s>", tokenizer.Pieces[0]);
        Assert.Equal("▁a", tokenizer.Pieces[4]);
    }
}
