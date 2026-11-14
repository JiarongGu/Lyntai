using Lyntai.Inference;
using Lyntai.Inference.Cli;
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

    // A subagent's lines reach the session's stdout with `parent_tool_use_id` set; its words are not the answer
    // (the agent reader drops them the same way).
    [Fact]
    public void A_subagents_assistant_text_is_not_the_answer()
    {
        const string child = """{"type":"assistant","parent_tool_use_id":"toolu_1","message":{"content":[{"type":"text","text":"sub says hi"}]}}""";
        const string own = """{"type":"assistant","parent_tool_use_id":null,"message":{"content":[{"type":"text","text":"own words"}]}}""";

        Assert.Equal(StreamJsonEventKind.Other, StreamJsonParser.Parse(child).Kind);
        Assert.Equal("own words", StreamJsonParser.Parse(own).Text);
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

    [Fact]
    public void An_assistant_line_carrying_an_error_is_not_the_answer()
    {
        // the CLI prints its login prompt as an assistant message flagged `error`, then ends with an is_error
        // result; reading the prompt as content would commit the stream before the failure arrives
        var evt = StreamJsonParser.Parse(
            """{"type":"assistant","error":"authentication_failed","message":{"content":[{"type":"text","text":"Please run /login"}]}}""");

        Assert.Equal(StreamJsonEventKind.Other, evt.Kind);
    }

    [Fact]
    public void A_cost_that_does_not_fit_a_double_is_no_cost_and_the_result_is_kept()
    {
        var evt = StreamJsonParser.Parse("""{"type":"result","result":"ok","usage":{"input_tokens":1,"output_tokens":2},"total_cost_usd":1e999}""");

        Assert.Equal(StreamJsonEventKind.Result, evt.Kind);
        Assert.Equal("ok", evt.Text);
        Assert.Null(evt.Usage!.CostUsd);
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
    /// <para>Without it no claude line can produce <c>CliOutputEventKind.Failure</c>, and the engine's
    /// in-band-failure precedence never runs for this backend: a run that printed partial text and then failed
    /// returns <c>ProviderVerdict.Ok</c> with a truncated answer labelled complete, and a non-zero exit takes
    /// its verdict from the stderr tail instead of the backend's own words, so <c>AuthFailed</c> and
    /// <c>RateLimited</c> degrade to <c>Failed</c> — advance instead of cool, the shape <c>pitfalls.md</c>
    /// records for codex.</para></summary>
    [Theory]
    [InlineData("""{"type":"result","is_error":true,"subtype":"error_max_turns","result":"ran out of turns"}""")]
    [InlineData("""{"type":"result","is_error":true,"result":"401 Unauthorized"}""")]
    public void A_result_flagged_is_error_is_a_failure_not_an_answer(string line)
    {
        var evt = StreamJsonParser.Parse(line);

        Assert.Equal(StreamJsonEventKind.Failure, evt.Kind);
        Assert.NotEmpty(evt.Text);   // the backend's own words, which is what gets classified
    }

    /// <summary>The RECORDED fact: claude 2.1.325 (haiku, <c>--max-turns 1</c>) printed this on stdout, stderr empty,
    /// exit 1. Trimmed only of its per-model usage blocks, the usage object's detail and an account timestamp.</summary>
    internal const string RecordedMaxTurnsResult = """{"duration_api_ms":2258,"stop_reason":"tool_use","session_id":"62bdcb70-d56d-4163-b2c9-40e391e4bb1e","total_cost_usd":0.006008830000000001,"usage":{"input_tokens":2,"cache_creation_input_tokens":28850,"cache_read_input_tokens":16813,"output_tokens":141},"permission_denials":[],"terminal_reason":"max_turns","fast_mode_state":"off","fast_mode_disabled_reason":"sdk_opt_in_required","safety_stops":0,"is_error":true,"num_turns":2,"subtype":"error_max_turns","errors":["Reached maximum number of turns (1)"],"type":"result","duration_ms":4941,"uuid":"38dc495e-1909-49e7-a585-b1e5e5e6fb04","queued_turn_count":0,"result_index":0}""";

    [Fact] // 2.1.325 pairs the failing subtype with is_error:true, so the rule below is defence; this pins the real line
    public void The_recorded_max_turns_result_is_a_failure_naming_its_subtype()
    {
        var evt = StreamJsonParser.Parse(RecordedMaxTurnsResult);

        Assert.Equal(StreamJsonEventKind.Failure, evt.Kind);
        Assert.Equal("error_max_turns", evt.Text);
    }

    /// <summary>A failing <c>subtype</c> is a failure whatever <c>is_error</c> says. SYNTHETIC: no capture shows
    /// <c>is_error:false</c> beside an <c>error_*</c> subtype (the recorded 2.1.325 line pairs it with <c>true</c>);
    /// the fixture is the case the rule defends against.</summary>
    [Theory]
    [InlineData("""{"type":"result","subtype":"error_max_turns","is_error":false,"result":"partial text"}""")]
    [InlineData("""{"type":"result","subtype":"error_during_execution","result":"partial text"}""")]
    public void An_error_subtype_without_is_error_is_a_failure(string line)
    {
        var evt = StreamJsonParser.Parse(line);

        Assert.Equal(StreamJsonEventKind.Failure, evt.Kind);
        Assert.Equal("partial text", evt.Text);
    }

    /// <summary>SYNTHETIC: a subtype this reader has never seen, that does not begin with <c>error</c>, stays an
    /// answer — only the prefix reads as failure, so a new success-shaped subtype cannot fail a healthy turn.</summary>
    [Fact]
    public void An_unknown_subtype_without_is_error_is_an_answer()
    {
        var evt = StreamJsonParser.Parse("""{"type":"result","subtype":"success_with_notes","is_error":false,"result":"the answer"}""");

        Assert.Equal(StreamJsonEventKind.Result, evt.Kind);
        Assert.Equal("the answer", evt.Text);
    }

    [Fact] // a failed turn was still billed: the backend hands the engine the result's usage beside its words
    public void A_failed_result_keeps_its_usage()
    {
        const string line = """{"type":"result","is_error":true,"subtype":"error_during_execution","result":"overloaded","total_cost_usd":0.05,"usage":{"input_tokens":120,"output_tokens":7,"cache_read_input_tokens":30}}""";

        var evt = new ClaudeCliBackend().ParseLine(line);

        Assert.Equal(CliOutputEventKind.Failure, evt.Kind);
        Assert.Equal("overloaded", evt.Text);
        Assert.Equal(new TextUsage(120, 7, 30, 0.05), evt.Usage);
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
