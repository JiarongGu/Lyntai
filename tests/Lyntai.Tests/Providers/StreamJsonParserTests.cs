using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Tests.Providers;

public class StreamJsonParserTests
{
    // fixture lines captured from the provider stub (the same shape the real CLI emits)
    private const string SystemLine = """{"type":"system","subtype":"init","session_id":"stub-1a"}""";
    private const string AssistantLine = """{"type":"assistant","message":{"content":[{"type":"text","text":"stub reply: hello"}]}}""";
    private const string ResultLine = """{"type":"result","result":"stub reply: hello","usage":{"input_tokens":1200,"output_tokens":340,"cache_read_input_tokens":800},"total_cost_usd":0.012}""";

    [Fact]
    public void System_init_is_other()
    {
        Assert.Equal(StreamJsonEventKind.Other, StreamJsonParser.Parse(SystemLine).Kind);
    }

    [Fact]
    public void Assistant_line_yields_text()
    {
        var evt = StreamJsonParser.Parse(AssistantLine);

        Assert.Equal(StreamJsonEventKind.AssistantText, evt.Kind);
        Assert.Equal("stub reply: hello", evt.Text);
    }

    [Fact]
    public void Result_line_yields_text_usage_and_cost()
    {
        var evt = StreamJsonParser.Parse(ResultLine);

        Assert.Equal(StreamJsonEventKind.Result, evt.Kind);
        Assert.Equal("stub reply: hello", evt.Text);
        Assert.NotNull(evt.Usage);
        Assert.Equal(1200, evt.Usage.InputTokens);
        Assert.Equal(340, evt.Usage.OutputTokens);
        Assert.Equal(800, evt.Usage.CacheReadTokens);
        Assert.Equal(0.012, evt.Usage.CostUsd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"unknown"}""")]
    [InlineData("""{"no_type":true}""")]
    [InlineData("[1,2,3]")]
    public void Malformed_or_unknown_lines_are_other_never_throw(string line)
    {
        Assert.Equal(StreamJsonEventKind.Other, StreamJsonParser.Parse(line).Kind);
    }

    [Fact]
    public void Multiple_text_blocks_concatenate()
    {
        const string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"a"},{"type":"tool_use","id":"x"},{"type":"text","text":"b"}]}}""";

        var evt = StreamJsonParser.Parse(line);

        Assert.Equal("ab", evt.Text);
    }
}
