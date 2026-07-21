using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Tests.Providers;

/// <summary>Unit tests for <see cref="StreamJsonAgentReader"/> — the stateful per-run translator
/// that maps claude stream-json lines to <see cref="AgentStreamEvent"/>s.</summary>
public class StreamJsonAgentReaderTests
{
    // ── system/init ───────────────────────────────────────────────────────────

    [Fact]
    public void SystemInit_yields_SessionStarted_with_correct_id()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"system","subtype":"init","session_id":"abc-123","model":"claude-opus-4-8","tools":["Read","Edit"]}""";

        var events = reader.Read(line).ToList();

        var evt = Assert.Single(events);
        var started = Assert.IsType<SessionStarted>(evt);
        Assert.Equal("abc-123", started.SessionId);
    }

    // ── stream_event (partial deltas) ────────────────────────────────────────

    [Fact]
    public void StreamEvent_text_delta_yields_TextDelta()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hel"}}}""";

        var events = reader.Read(line).ToList();

        var evt = Assert.Single(events);
        var delta = Assert.IsType<TextDelta>(evt);
        Assert.Equal("Hel", delta.Text);
    }

    [Fact]
    public void StreamEvent_thinking_delta_yields_Thinking()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Hmm"}}}""";

        var events = reader.Read(line).ToList();

        var evt = Assert.Single(events);
        var thinking = Assert.IsType<Thinking>(evt);
        Assert.Equal("Hmm", thinking.Text);
    }

    [Fact]
    public void StreamEvent_unknown_delta_type_yields_nothing()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"unknown_delta","value":"x"}}}""";

        var events = reader.Read(line).ToList();

        Assert.Empty(events);
    }

    [Fact]
    public void StreamEvent_non_content_block_delta_yields_nothing()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"stream_event","event":{"type":"message_start","message":{}}}""";

        var events = reader.Read(line).ToList();

        Assert.Empty(events);
    }

    // ── assistant (complete message) ─────────────────────────────────────────

    [Fact]
    public void Assistant_with_text_and_tool_use_yields_ToolCall_and_UsageLive_but_no_TextDelta()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"assistant","message":{"model":"claude-opus-4-8","content":[{"type":"text","text":"I'll read it."},{"type":"tool_use","id":"toolu_1","name":"Read","input":{"file_path":"/x/a.txt"}}],"usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":50,"cache_creation_input_tokens":10}}}""";

        var events = reader.Read(line).ToList();

        // Must have exactly a ToolCall and a UsageLive; no TextDelta
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e is TextDelta);

        var toolCall = Assert.Single(events.OfType<ToolCall>());
        Assert.Equal("Read", toolCall.Name);
        Assert.Equal("toolu_1", toolCall.CallId);
        // ArgumentsJson should contain file_path
        Assert.Contains("file_path", toolCall.ArgumentsJson);
        Assert.Contains("/x/a.txt", toolCall.ArgumentsJson);

        var usage = Assert.Single(events.OfType<UsageLive>());
        Assert.Equal(100, usage.Input);
        Assert.Equal(20, usage.Output);
        Assert.Equal(50, usage.CacheRead);
    }

    [Fact]
    public void Assistant_updates_model_for_subsequent_UsageFinal()
    {
        var reader = new StreamJsonAgentReader();
        const string initLine = """{"type":"system","subtype":"init","session_id":"sess-1","model":"claude-opus-4-8"}""";
        // Consume the init line to set model
        _ = reader.Read(initLine).ToList();

        const string resultLine = """{"type":"result","subtype":"success","is_error":false,"result":"Done.","session_id":"sess-1","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":50,"cache_creation_input_tokens":10}}""";
        var events = reader.Read(resultLine).ToList();

        var final = Assert.Single(events.OfType<UsageFinal>());
        Assert.Equal("claude-opus-4-8", final.Model);
    }

    [Fact]
    public void Assistant_without_usage_yields_only_ToolCalls()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_2","name":"Edit","input":{"x":1}}]}}""";

        var events = reader.Read(line).ToList();

        Assert.Single(events);
        Assert.IsType<ToolCall>(events[0]);
        Assert.DoesNotContain(events, e => e is UsageLive);
    }

    // ── user (tool results) ──────────────────────────────────────────────────

    [Fact]
    public void User_tool_result_string_content_yields_ToolResult()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"file body","is_error":false}]}}""";

        var events = reader.Read(line).ToList();

        var evt = Assert.Single(events);
        var result = Assert.IsType<ToolResult>(evt);
        Assert.Equal("toolu_1", result.CallId);
        Assert.Equal("file body", result.Content);
        Assert.False(result.IsError);
    }

    [Fact]
    public void User_tool_result_array_content_yields_ToolResult_with_concatenated_text()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_2","content":[{"type":"text","text":"part A"},{"type":"text","text":" part B"}],"is_error":false}]}}""";

        var events = reader.Read(line).ToList();

        var evt = Assert.Single(events);
        var result = Assert.IsType<ToolResult>(evt);
        Assert.Equal("toolu_2", result.CallId);
        Assert.Equal("part A part B", result.Content);
        Assert.False(result.IsError);
    }

    [Fact]
    public void User_tool_result_is_error_true()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_3","content":"error detail","is_error":true}]}}""";

        var events = reader.Read(line).ToList();

        var result = Assert.IsType<ToolResult>(Assert.Single(events));
        Assert.True(result.IsError);
    }

    // ── result (terminal) ────────────────────────────────────────────────────

    [Fact]
    public void Result_success_yields_UsageFinal_then_SessionEnded_Ok()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"Done.","session_id":"abc-123","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":50,"cache_creation_input_tokens":10}}""";

        var events = reader.Read(line).ToList();

        Assert.Equal(2, events.Count);

        var final = Assert.IsType<UsageFinal>(events[0]);
        Assert.Equal(100, final.Input);
        Assert.Equal(20, final.Output);
        Assert.Equal(50, final.CacheRead);
        Assert.Equal(10, final.CacheCreate);

        var ended = Assert.IsType<SessionEnded>(events[1]);
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("success", ended.Subtype);
        Assert.Equal("abc-123", ended.SessionId);
        Assert.Equal("Done.", ended.FinalText);
        Assert.Null(ended.Diagnostic);
    }

    [Fact]
    public void Result_error_yields_UsageFinal_then_SessionEnded_Failed_with_model_from_init()
    {
        var reader = new StreamJsonAgentReader();

        // Feed a system/init first so _model is set
        const string initLine = """{"type":"system","subtype":"init","session_id":"sess-9","model":"claude-opus-4-8"}""";
        _ = reader.Read(initLine).ToList();

        const string resultLine = """{"type":"result","subtype":"error_max_turns","is_error":true,"session_id":"sess-9","usage":{"input_tokens":200,"output_tokens":30,"cache_read_input_tokens":0,"cache_creation_input_tokens":5}}""";
        var events = reader.Read(resultLine).ToList();

        Assert.Equal(2, events.Count);

        var final = Assert.IsType<UsageFinal>(events[0]);
        Assert.Equal("claude-opus-4-8", final.Model);  // remembered from init
        Assert.Equal(5, final.CacheCreate);

        var ended = Assert.IsType<SessionEnded>(events[1]);
        Assert.Equal(LlmVerdict.Failed, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Equal("error_max_turns", ended.Subtype);
        Assert.Equal("sess-9", ended.SessionId);
        Assert.Null(ended.FinalText);  // no "result" key in error line
        Assert.Null(ended.Diagnostic);  // reader never sets diagnostic
    }

    [Fact]
    public void Result_without_usage_yields_only_SessionEnded()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s1"}""";

        var events = reader.Read(line).ToList();

        Assert.Single(events);
        Assert.IsType<SessionEnded>(events[0]);
    }

    [Fact]
    public void Result_with_empty_result_falls_back_to_last_assistant_text()
    {
        var reader = new StreamJsonAgentReader();
        // Assistant text arrives as a complete message block (which the reader does NOT re-emit),
        // then the terminal result carries an empty result string (truncation / older CLI / variant).
        const string assistantLine = """{"type":"assistant","message":{"content":[{"type":"text","text":"The answer is 42."}]}}""";
        _ = reader.Read(assistantLine).ToList();

        const string resultLine = """{"type":"result","subtype":"success","is_error":false,"result":"","session_id":"s3"}""";
        var events = reader.Read(resultLine).ToList();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal("The answer is 42.", ended.FinalText);
    }

    [Fact]
    public void Result_with_nonempty_result_wins_over_last_assistant_text()
    {
        var reader = new StreamJsonAgentReader();
        _ = reader.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"draft"}]}}""").ToList();

        const string resultLine = """{"type":"result","subtype":"success","is_error":false,"result":"final","session_id":"s4"}""";
        var events = reader.Read(resultLine).ToList();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal("final", ended.FinalText);
    }

    // ── tolerant / error handling ────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{malformed json}")]
    [InlineData("""{"type":"unknown_type","data":"x"}""")]
    public void Malformed_or_unknown_lines_yield_nothing_and_never_throw(string line)
    {
        var reader = new StreamJsonAgentReader();

        var events = reader.Read(line).ToList();  // must not throw

        Assert.Empty(events);
    }

    // ── state is preserved across multiple Read calls ────────────────────────

    [Fact]
    public void Model_from_assistant_line_appears_in_UsageFinal()
    {
        var reader = new StreamJsonAgentReader();

        // Assistant line carries model
        const string assistantLine = """{"type":"assistant","message":{"model":"claude-sonnet-4-8","content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}],"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}""";
        _ = reader.Read(assistantLine).ToList();

        const string resultLine = """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s2","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}""";
        var events = reader.Read(resultLine).ToList();

        var final = Assert.Single(events.OfType<UsageFinal>());
        Assert.Equal("claude-sonnet-4-8", final.Model);
    }
}
