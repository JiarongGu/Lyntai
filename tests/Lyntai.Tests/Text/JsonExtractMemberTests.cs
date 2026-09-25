using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Tests.Text;

/// <summary>The three hand-walking helpers: two member reads that never throw on an element of the wrong kind,
/// and the one-object writer.</summary>
public class JsonExtractMemberTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("""{"id":"abc"}""", "abc")]
    [InlineData("""{"id":""}""", null)]          // empty is absent
    [InlineData("""{"id":12}""", null)]          // a number is not a string
    [InlineData("""{"other":"abc"}""", null)]
    [InlineData("""["abc"]""", null)]            // not an object: TryGetProperty would THROW here
    [InlineData("\"abc\"", null)]
    public void StringProperty_reads_only_a_non_empty_string_member_of_an_object(string json, string? expected) =>
        Assert.Equal(expected, JsonExtract.StringProperty(Parse(json), "id"));

    [Theory]
    [InlineData("""{"id":"abc"}""", "abc")]
    [InlineData("""{"id":12345}""", "12345")]    // an accepted numeric id must not read as rejected
    [InlineData("""{"id":1.5e3}""", "1.5e3")]    // its JSON spelling, untouched
    [InlineData("""{"id":true}""", null)]
    [InlineData("""{"id":null}""", null)]
    [InlineData("""{"id":""}""", null)]
    [InlineData("""[12345]""", null)]
    public void ScalarProperty_reads_a_string_or_number_member_as_text(string json, string? expected) =>
        Assert.Equal(expected, JsonExtract.ScalarProperty(Parse(json), "id"));

    [Fact]
    public void WriteObject_wraps_the_members_it_is_handed_in_one_object()
    {
        var json = JsonExtract.WriteObject(writer =>
        {
            writer.WriteString("kind", "image");
            writer.WriteNumber("n", 1);
        });

        Assert.Equal("""{"kind":"image","n":1}""", json);
        Assert.Equal("{}", JsonExtract.WriteObject(_ => { }));
    }
}
