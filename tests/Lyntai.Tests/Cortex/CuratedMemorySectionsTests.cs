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

    [Fact]
    public void Filters_by_task_and_scope_when_requested()
    {
        CuratedMemory E(long id, string kind, string content, string? task, string? scope) =>
            new(id, kind, content, Source: null, Enabled: true, T0.AddSeconds(id), T0.AddSeconds(id), task, scope);

        var entries = new[]
        {
            E(1, "glossary", "zh term",  task: "translation", scope: "lang:zh"),
            E(2, "glossary", "any lang", task: "translation", scope: null),   // null scope → every scope
            E(3, "rules",    "meta rule", task: "metadata",   scope: null),
            E(4, "persona",  "be terse",  task: null,         scope: null),    // null task → every task
        };

        var text = CuratedMemorySections.Compose(entries, task: "translation", scopes: ["lang:zh"]);

        Assert.Contains("zh term", text);
        Assert.Contains("any lang", text);
        Assert.Contains("be terse", text);       // universal (null task) row
        Assert.DoesNotContain("meta rule", text); // other task filtered out
    }
}
