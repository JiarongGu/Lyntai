using Lyntai.Text;

namespace Lyntai.Tests.Text;

/// <summary>The three entry points and which POSTURE each takes — the thing their names do not carry.
///
/// <para>Behaviour under the two readers is covered end to end by <c>TextStructuredExtensionsTests</c>, which
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
    public void The_two_READS_try_a_later_brace_when_the_first_balanced_one_is_not_JSON_and_ExtractObject_keeps_the_first()
    {
        const string prose = """see {score}: {"score":0.8}""";

        Assert.True(JsonExtract.TryParseObject(prose, out var doc));
        using (doc) Assert.Equal(0.8, doc.RootElement.GetProperty("score").GetDouble());
        Assert.True(JsonExtract.TryReadObject(prose, out var json));
        Assert.Equal("""{"score":0.8}""", json);

        Assert.Equal("{score}", JsonExtract.ExtractObject(prose));   // the first-brace contract is unchanged
    }

    [Fact]
    public void A_later_brace_is_tried_only_a_bounded_number_of_times()
    {
        const string valid = """{"ok":true}""";
        var seven = string.Concat(Enumerable.Repeat("{x} ", 7)) + valid;       // the valid object is the eighth brace
        var eight = string.Concat(Enumerable.Repeat("{x} ", 8)) + valid;       // …and here the ninth

        Assert.True(JsonExtract.TryParseObject(seven, out var doc));
        doc.Dispose();
        Assert.True(JsonExtract.TryReadObject(seven, out _));
        Assert.False(JsonExtract.TryParseObject(eight, out _));
        Assert.False(JsonExtract.TryReadObject(eight, out _));
    }

    [Fact]
    public void A_truncated_object_after_prose_braces_still_reaches_a_retry()
    {
        // trying later braces must not turn a cut-off reply into the smaller object inside it
        Assert.False(JsonExtract.TryReadObject("""see {x}: {"cut": {"in":1}, "of""", out _));
        Assert.False(JsonExtract.TryParseObject("""{"cut": {"in":1}, "of""", out _));
    }

    [Fact]
    public void A_TRUNCATED_object_is_refused_rather_than_completed()
    {
        // Missing content, not missing punctuation — so it must still reach a retry.
        Assert.False(JsonExtract.TryReadObject("""{"cut": "of""", out _));
        Assert.False(JsonExtract.TryParseObject("""{"cut": "of""", out _));
    }
}
