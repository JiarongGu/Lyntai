using Lyntai.Text;

namespace Lyntai.Tests.Text;

/// <summary>The three entry points and which POSTURE each takes — the thing their names do not carry.
///
/// <para>Behaviour under the two readers is covered end to end by <c>LlmStructuredExtensionsTests</c>, which
/// exercises them through the front door and the repair path. What is here is the split itself: that
/// leniency reaches exactly the two reads and never the grader, and the one place extraction and leniency
/// disagree.</para></summary>
public class JsonExtractTests
{
    [Theory]
    [InlineData("""{"ok": true,}""")]
    [InlineData("{\"ok\": true // yes\n}")]
    public void The_two_READS_tolerate_punctuation_and_the_GRADER_does_not(string text)
    {
        // StructureScorer grades whether a model emitted well-formed JSON. A grader that accepted a trailing
        // comma would score malformed output as perfect, which is why the leniency is not shared.
        Assert.True(JsonExtract.TryParseObject(text, out var doc));
        doc.Dispose();
        Assert.True(JsonExtract.TryReadObject(text, out var json));
        Assert.True(JsonExtract.IsValid(json));     // …and what a read HANDS BACK is strict either way

        Assert.False(JsonExtract.IsValid(text));    // the grader, on the same bytes
    }

    [Fact]
    public void An_object_that_already_parses_strictly_comes_back_BYTE_FOR_BYTE()
    {
        // Re-serializing unconditionally would reformat every reply to buy nothing, and a caller handed the
        // model's own spacing keeps it.
        const string exact = """{"b": 2,   "a": [1, 2]}""";

        Assert.True(JsonExtract.TryReadObject($"Sure:\n```json\n{exact}\n```", out var json));
        Assert.Equal(exact, json);
    }

    [Fact]
    public void A_block_comment_containing_a_BRACE_defeats_extraction_which_is_the_safe_direction()
    {
        // The one place the scan and the parser disagree: ExtractObject tracks strings but not comments, so
        // it closes on the `}` inside the comment and hands the parser a fragment. Pinned because the class
        // doc claims it — an unverified caveat is worth no more than an absent one — and because the
        // DIRECTION is what makes it tolerable: the caller retries rather than receiving a truncated object.
        const string awkward = """{/* closes } here */"ok": true}""";

        Assert.False(JsonExtract.TryReadObject(awkward, out var json));
        Assert.Null(json);

        // …while the same comment WITHOUT a brace is repaired without a round trip.
        Assert.True(JsonExtract.TryReadObject("""{/* fine */"ok": true}""", out var repaired));
        Assert.True(JsonExtract.IsValid(repaired));
    }

    [Fact]
    public void A_TRUNCATED_object_is_refused_rather_than_completed()
    {
        // Missing content, not missing punctuation — so it must still reach a retry.
        Assert.False(JsonExtract.TryReadObject("""{"cut": "of""", out _));
        Assert.False(JsonExtract.TryParseObject("""{"cut": "of""", out _));
    }
}
