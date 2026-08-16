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

    /// <summary>A terminal result carrying <c>is_error</c> is a FAILURE, not an answer.
    /// <para>Present in the released 2.5.0 and every version before it: <c>StreamJsonEventKind</c> had no
    /// failure member at all, so no claude line could ever produce <c>CliOutputEventKind.Failure</c> and the
    /// engine's whole in-band-failure precedence was dead code for this backend — only codex reached it.
    /// A run that printed partial assistant text and then failed returned <c>LlmVerdict.Ok</c> with a
    /// truncated answer labelled complete; with error prose in <c>result</c>, that prose WAS the answer. Even
    /// on a non-zero exit the verdict came from the stderr tail instead of the backend's own words, so
    /// <c>AuthFailed</c>/<c>RateLimited</c> degraded to bare <c>Failed</c> — advance instead of cool, the
    /// exact regression <c>pitfalls.md</c> records as fixed for codex on 2026-08-05. The sibling reader of
    /// the same wire format has always read <c>is_error</c>. Found 2026-08-14.</para></summary>
    [Theory]
    [InlineData("""{"type":"result","is_error":true,"subtype":"error_max_turns","result":"ran out of turns"}""")]
    [InlineData("""{"type":"result","is_error":true,"result":"401 Unauthorized"}""")]
    public void A_result_flagged_is_error_is_a_failure_not_an_answer(string line)
    {
        var evt = StreamJsonParser.Parse(line);

        Assert.Equal(StreamJsonEventKind.Failure, evt.Kind);
        Assert.NotEmpty(evt.Text);   // the backend's own words, which is what gets classified
    }

    [Fact]
    public void A_result_without_is_error_stays_an_ordinary_answer()
    {
        // is_error absent, or explicitly false, is a normal terminal result — the common case, and the one a
        // too-eager failure check would break by failing every successful turn.
        foreach (var line in new[]
        {
            """{"type":"result","result":"the answer"}""",
            """{"type":"result","is_error":false,"result":"the answer"}""",
        })
        {
            var evt = StreamJsonParser.Parse(line);
            Assert.Equal(StreamJsonEventKind.Result, evt.Kind);
            Assert.Equal("the answer", evt.Text);
        }
    }

    [Fact]
    public void Multiple_text_blocks_concatenate()
    {
        const string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"a"},{"type":"tool_use","id":"x"},{"type":"text","text":"b"}]}}""";

        var evt = StreamJsonParser.Parse(line);

        Assert.Equal("ab", evt.Text);
    }
}
