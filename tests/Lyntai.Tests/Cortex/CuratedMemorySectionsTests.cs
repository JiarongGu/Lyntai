using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Cortex;

public class CuratedMemorySectionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static CuratedMemory Entry(long id, string kind, string content, bool enabled = true) =>
        new(id, kind, content, Source: null, enabled, T0.AddSeconds(id), T0.AddSeconds(id));

    [Fact]
    public void Composes_per_kind_sections_in_order()
    {
        var text = CuratedMemorySections.Compose([
            Entry(1, "persona", "be terse"),
            Entry(2, "glossary", "DEK = data encryption key"),
            Entry(3, "persona", "cite sources"),
        ]);

        Assert.Equal(
            "## glossary\n- DEK = data encryption key\n\n## persona\n- be terse\n- cite sources",
            text);
    }

    [Fact]
    public void Drops_disabled_entries()
    {
        var text = CuratedMemorySections.Compose([
            Entry(1, "persona", "keep me"),
            Entry(2, "persona", "hide me", enabled: false),
        ]);
        Assert.Equal("## persona\n- keep me", text);
    }

    [Fact]
    public void Empty_or_all_disabled_yields_empty_string()
    {
        Assert.Equal("", CuratedMemorySections.Compose([]));
        Assert.Equal("", CuratedMemorySections.Compose([Entry(1, "k", "x", enabled: false)]));
    }

    [Fact]
    public void Custom_header_and_bullet()
    {
        var text = CuratedMemorySections.Compose(
            [Entry(1, "notes", "hello")],
            header: k => $"### {k.ToUpperInvariant()}",
            bullet: "* ");
        Assert.Equal("### NOTES\n* hello", text);
    }
}
