using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory;

/// <summary>The <c>graph</c> and <c>link</c> lines of a portable file, written and read without a store: what an
/// export does with content a UTF-8 file cannot spell, and what an import accepts.</summary>
public class GraphPortableLinesTests
{
    // built from the code point, so the source holds no replacement character for check-encoding to flag
    private static readonly string Replacement = char.ConvertFromUtf32(0xFFFD);

    private static readonly DateTimeOffset Created = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static GraphNode Node(long id, string content = "fact", string scope = "s", string headline = "head",
        double stability = 2, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, "engine", "t", scope, headline, content, MemoryGrade.Associative, Created, 0, stability, 0, 0, 0, metadata);

    private static MemoryGraphExport Export(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphLink>? links = null,
        IReadOnlyDictionary<long, IReadOnlyList<string>>? subjects = null) =>
        new(nodes, links ?? [], subjects ?? new Dictionary<long, IReadOnlyList<string>>());

    private static async Task<string> WriteAsync(MemoryGraphExport export)
    {
        using var writer = new StringWriter();
        await GraphPortableLines.WriteAsync(writer, "engine", export, CancellationToken.None);
        return writer.ToString();
    }

    private static async Task<List<GraphPortableLines.CarriedNode>> ReadAsync(string jsonl, string? scope = null) =>
        GraphPortableLines.Read(await MemoryPortableFormat.ReadAsync(new StringReader(jsonl), CancellationToken.None),
            "engine", scope).Nodes;

    [Fact]
    public async Task A_lone_surrogate_is_written_as_the_replacement_character_in_every_text_field()
    {
        var export = Export(
            [Node(1, content: "a\uD800", scope: "s\uDC00", headline: "h\uD800x",
                metadata: new Dictionary<string, string> { ["k\uD800"] = "v\uDFFF" })],
            subjects: new Dictionary<long, IReadOnlyList<string>> { [1] = ["sub\uD800"] });

        var jsonl = await WriteAsync(export);
        var read = Assert.Single(await ReadAsync(jsonl)).Node;

        Assert.Equal("a" + Replacement, read.Content);
        Assert.Equal("s" + Replacement, read.Scope);
        Assert.Equal("h" + Replacement + "x", read.Headline);
        Assert.Equal("v" + Replacement, read.Metadata!["k" + Replacement]);
        Assert.Equal(["sub" + Replacement], read.Subjects);
    }

    [Fact]
    public async Task A_pair_is_not_a_lone_surrogate_and_travels_whole()
    {
        var jsonl = await WriteAsync(Export([Node(1, content: "a\U0001F600b")]));

        Assert.Equal("a\U0001F600b", Assert.Single(await ReadAsync(jsonl)).Node.Content);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_non_finite_number_is_refused_before_the_first_line_is_written(double bad)
    {
        // the bad value is on the SECOND node, so a writer that checks as it goes has already written the first
        var export = Export([Node(1), Node(2, stability: bad)]);
        using var writer = new StringWriter();

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => GraphPortableLines.WriteAsync(writer, "engine", export, CancellationToken.None));

        Assert.Contains("Nothing was written", error.Message, StringComparison.Ordinal);
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public async Task A_non_finite_link_weight_is_refused_before_the_first_line_is_written()
    {
        var export = Export([Node(1), Node(2)], [new GraphLink(1, 2, "about", double.NaN, 0, 0, 0, 0)]);
        using var writer = new StringWriter();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => GraphPortableLines.WriteAsync(writer, "engine", export, CancellationToken.None));

        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public async Task An_entry_with_an_empty_scope_and_headline_round_trips_and_is_accepted_at_import()
    {
        var jsonl = await WriteAsync(Export([Node(1, scope: "", headline: "")]));

        var read = Assert.Single(await ReadAsync(jsonl)).Node;

        Assert.Equal("", read.Scope);
        Assert.Equal("", read.Headline);
        Assert.Equal("fact", read.Content);
    }

    [Fact]
    public async Task A_line_with_no_scope_at_all_is_still_refused()
    {
        var jsonl = (await WriteAsync(Export([Node(1)]))).Replace("\"scope\":\"s\",", "", StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(jsonl));
    }

    [Fact]
    public async Task A_round_trip_keeps_every_field_it_wrote()
    {
        var export = Export(
            [Node(1, content: "first", metadata: new Dictionary<string, string> { ["source"] = "owner" }), Node(2, content: "second", stability: 7.5)],
            [new GraphLink(1, 2, "about", 2, 1, 2, 3, 0.5)],
            new Dictionary<long, IReadOnlyList<string>> { [2] = ["db"] });

        var lines = GraphPortableLines.Read(
            await MemoryPortableFormat.ReadAsync(new StringReader(await WriteAsync(export)), CancellationToken.None), "engine", null);

        Assert.Equal(["first", "second"], lines.Nodes.Select(n => n.Node.Content));
        Assert.Equal(7.5, lines.Nodes[1].Node.Stability);
        Assert.Equal("owner", lines.Nodes[0].Node.Metadata!["source"]);
        Assert.Equal(["db"], lines.Nodes[1].Node.Subjects);
        var link = Assert.Single(lines.Links);
        Assert.Equal(new GraphLink(1, 2, "about", 2, 1, 2, 3, 0.5), link);
    }
}
