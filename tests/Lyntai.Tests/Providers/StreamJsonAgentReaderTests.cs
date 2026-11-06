using Lyntai.Inference;
using Lyntai.Agents;
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

        Assert.Equal(2, events.Count);
        Assert.Equal("abc-123", Assert.IsType<SessionStarted>(events[0]).SessionId);
        Assert.IsType<AgentLoaded>(events[1]);
    }

    [Fact]
    public void SystemInit_reads_what_was_loaded_from_bare_names_and_from_objects()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"system","subtype":"init","session_id":"s","tools":["Read","Bash"],"mcp_servers":[{"name":"daoris","status":"connected"},{"name":"idle"},{"status":"x"}],"skills":[{"name":"review"},"plan",""],"plugins":[{"name":"p1","path":"/p"}]}""";

        var loaded = Assert.Single(reader.Read(line).OfType<AgentLoaded>());

        Assert.Equal(["Read", "Bash"], loaded.Tools);
        Assert.Equal([new AgentLoadedServer("daoris", "connected"), new AgentLoadedServer("idle", null)], loaded.McpServers);
        Assert.Equal(["review", "plan"], loaded.Skills);
        Assert.Equal(["p1"], loaded.Plugins);
    }

    [Fact]
    public void SystemInit_with_none_of_the_four_lists_reads_no_loaded_event_and_a_missing_list_reads_empty()
    {
        var reader = new StreamJsonAgentReader();

        Assert.Empty(reader.Read("""{"type":"system","subtype":"init","session_id":"a"}""").OfType<AgentLoaded>());

        var loaded = Assert.Single(reader.Read("""{"type":"system","subtype":"init","session_id":"b","tools":["Read"]}""").OfType<AgentLoaded>());
        Assert.Empty(loaded.McpServers);
        Assert.Empty(loaded.Skills);
        Assert.Empty(loaded.Plugins);
    }

    [Fact]
    public void A_later_system_frame_is_no_loaded_reading()
    {
        var reader = new StreamJsonAgentReader();

        var events = reader.Read("""{"type":"system","subtype":"thinking_tokens","session_id":"s","tools":["Read"]}""");

        Assert.DoesNotContain(events, e => e is AgentLoaded);
    }

    [Fact]
    public void Progress_system_events_do_not_restart_the_session()
    {
        // The shape claude 2.1.280 emits: `system/init`, then `system/thinking_tokens` PROGRESS events
        // carrying the same session_id. A SessionStarted per event would make a consumer that persists the
        // stream store one "session started" per progress tick.
        var reader = new StreamJsonAgentReader();
        string[] lines =
        [
            """{"type":"system","subtype":"init","session_id":"sess-1","model":"claude-haiku-4-5"}""",
            """{"type":"system","subtype":"thinking_tokens","estimated_tokens":120,"estimated_tokens_delta":120,"session_id":"sess-1"}""",
            """{"type":"system","subtype":"thinking_tokens","estimated_tokens":310,"estimated_tokens_delta":190,"session_id":"sess-1"}""",
        ];

        var started = lines.SelectMany(reader.Read).OfType<SessionStarted>().ToList();

        Assert.Equal("sess-1", Assert.Single(started).SessionId);
    }

    [Fact]
    public void A_changed_session_id_is_announced_again()
    {
        var reader = new StreamJsonAgentReader();

        var started = new[]
            {
                """{"type":"system","subtype":"init","session_id":"sess-1"}""",
                """{"type":"system","subtype":"init","session_id":"sess-2"}""",
            }
            .SelectMany(reader.Read).OfType<SessionStarted>().Select(s => s.SessionId).ToList();

        Assert.Equal(["sess-1", "sess-2"], started);
    }

    [Fact]
    public void A_later_system_frame_that_only_carries_a_new_id_is_not_an_announcement()
    {
        var reader = new StreamJsonAgentReader();

        var started = new[]
            {
                """{"type":"system","subtype":"init","session_id":"sess-1"}""",
                """{"type":"system","subtype":"thinking_tokens","estimated_tokens":1,"session_id":"sess-1"}""",
                """{"type":"system","subtype":"thinking_tokens","estimated_tokens":9,"session_id":"sess-2"}""",
            }
            .SelectMany(reader.Read).OfType<SessionStarted>().Select(s => (s.SessionId, s.Announced)).ToList();

        Assert.Equal([("sess-1", true), ("sess-2", false)], started);
    }

    [Fact]
    public void A_first_system_frame_that_names_no_subtype_is_the_agents_own_start_and_a_default_is_announced()
    {
        var reader = new StreamJsonAgentReader();

        var first = Assert.IsType<SessionStarted>(Assert.Single(reader.Read("""{"type":"system","session_id":"s"}""")));

        Assert.True(first.Announced);
        Assert.True(new SessionStarted("x").Announced);
    }

    // ── every allowance report is an event (AG42) ────────────────────────────

    // SOURCE-DERIVED from the field names the rejected frame tests already carry (status, resetsAt, rateLimitType, isUsingOverage)
    [Fact]
    public void An_allowed_warning_and_rejected_frame_are_each_an_event_with_the_window_as_an_allowance_window_states_it()
    {
        var reader = new StreamJsonAgentReader();

        var reports = new[]
            {
                """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1792000000,"rateLimitType":"five_hour","isUsingOverage":false}}""",
                """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed_warning","resetsAt":1792400000,"rateLimitType":"seven_day_opus"}}""",
                """{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1792400000,"rateLimitType":"seven_day","limitScope":"group_pool"}}""",
                """{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1792000000,"overageResetsAt":1795000000,"rateLimitType":"overage","isUsingOverage":true}}""",
            }
            .SelectMany(reader.Read).OfType<AgentAllowanceReport>().ToList();

        Assert.Equal(["allowed", "allowed_warning", "rejected", "rejected"], reports.Select(r => r.Status).ToArray());
        Assert.Equal((AgentAllowanceWindow.FiveHour, TimeSpan.FromHours(5), (string?)null), (reports[0].Window!.Name, reports[0].Window!.Length, reports[0].Window!.Scope));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792000000), reports[0].Window!.ResetsAt);
        Assert.Equal((false, null), (reports[0].IsUsingOverage, reports[1].IsUsingOverage));
        Assert.Equal((AgentAllowanceWindow.Weekly, "opus"), (reports[1].Window!.Name, reports[1].Window!.Scope));
        Assert.Equal("group_pool", reports[2].LimitScope);
        Assert.Equal(("overage", (TimeSpan?)null, true), (reports[3].Window!.Name, reports[3].Window!.Length, reports[3].IsUsingOverage));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000), reports[3].OverageResetsAt);
        Assert.All(reports, r => Assert.Null(r.Window!.UsedPercent));
    }

    // SOURCE-DERIVED from a consumer's recorded frame (claude 2.1.287, one headless turn; the moments invented): a use is a
    // fraction from 0 to 1 in unifiedWindows, and the overage fields are words
    private const string RecordedFrame =
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1792000000,"rateLimitType":"five_hour","overageStatus":"rejected","overageDisabledReason":"member_zero_credit_limit","isUsingOverage":false,"unifiedWindows":{"five_hour":{"utilization":0.88,"resetsAt":1792000000},"seven_day":{"utilization":0.14,"resetsAt":1792400000}}}}""";

    [Fact]
    public void A_recorded_frame_states_the_named_windows_use_as_a_percent_and_the_overage_words()
    {
        var report = Assert.Single(new StreamJsonAgentReader().Read(RecordedFrame).OfType<AgentAllowanceReport>());

        Assert.Equal((88.0, 1.0), (report.Window!.UsedPercent, report.Window.UsedStep));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792000000), report.Window.ResetsAt);
        Assert.Equal(("rejected", "member_zero_credit_limit", false), (report.OverageStatus, report.OverageDisabledReason, report.IsUsingOverage));
    }

    [Theory]
    [InlineData("""{"status":"allowed","rateLimitType":"seven_day","unifiedWindows":{"seven_day":{"utilization":0.375,"resetsAt":1792400000}}}""", 37.5, 0.1, 1792400000L)]
    [InlineData("""{"status":"allowed","rateLimitType":"seven_day","utilization":0.5}""", 50.0, 10.0, null)]
    [InlineData("""{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":1,"resetsAt":1792000000}}}""", 100.0, 100.0, 1792000000L)]
    [InlineData("""{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":5e-1}}}""", 50.0, null, null)]
    [InlineData("""{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":88}}}""", null, null, null)]
    [InlineData("""{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":"0.5"}}}""", null, null, null)]
    [InlineData("""{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":[1]}""", null, null, null)]
    public void A_use_is_kept_as_the_digits_state_it_and_a_use_out_of_range_or_not_a_number_is_left_out(
        string info, double? percent, double? step, long? reset)
    {
        var report = Assert.Single(new StreamJsonAgentReader().Read($$"""{"type":"rate_limit_event","rate_limit_info":{{info}}}""").OfType<AgentAllowanceReport>());

        Assert.Equal((percent, step), (report.Window!.UsedPercent, report.Window.UsedStep));
        Assert.Equal(reset is { } r ? DateTimeOffset.FromUnixTimeSeconds(r) : (DateTimeOffset?)null, report.Window.ResetsAt);
    }

    [Fact]
    public void A_frame_with_no_reset_and_one_with_no_window_are_kept_in_part_and_the_rejected_frame_still_ends_a_failing_turn_as_before()
    {
        var reader = new StreamJsonAgentReader();

        var events = new[]
            {
                """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour"}}""",
                """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}""",
                """{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1792000000,"rateLimitType":"five_hour"}}""",
                """{"type":"result","subtype":"success","is_error":true,"result":"limit reached","session_id":"s"}""",
            }
            .SelectMany(reader.Read).ToList();

        var reports = events.OfType<AgentAllowanceReport>().ToList();
        Assert.Null(reports[0].Window!.ResetsAt);
        Assert.Null(reports[1].Window);
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(AgentFailureKind.Limit, ended.Failure!.Kind);
        Assert.Equal(RecoveryKind.ResetAt, ended.Failure.Restriction!.Recovery.Kind);
        Assert.Empty(new StreamJsonAgentReader().Read("""{"type":"rate_limit_event"}"""));
    }

    // ── a turn's own report: denials and usage by model (AG33) ───────────────

    // SOURCE-DERIVED: the shape of a host's record of a result frame (values invented, field names theirs)
    private const string ThreeModelResult =
        """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s","usage":{"input_tokens":30,"output_tokens":9},"modelUsage":{"claude-opus-4-8":{"inputTokens":20,"outputTokens":5,"cacheReadInputTokens":100,"cacheCreationInputTokens":7,"contextWindow":200000,"costUSD":0.5},"claude-haiku-4-5":{"inputTokens":10,"outputTokens":4,"contextWindow":200000,"costUSD":0.01},"claude-sub-1":{"inputTokens":1}},"permission_denials":[{"tool_name":"Bash","tool_use_id":"toolu_1","tool_input":{"command":"rm"}},{"tool_name":"Edit","tool_use_id":"toolu_2","reason":"read-only turn"},{"tool_input":{}}]}""";

    [Fact]
    public void A_results_usage_by_model_marks_the_sessions_model_and_leaves_the_others_as_rows_with_no_cost()
    {
        var reader = new StreamJsonAgentReader(new FixedTime());
        _ = reader.Read("""{"type":"system","subtype":"init","session_id":"s","model":"claude-opus-4-8"}""").ToList();

        var usage = Assert.Single(reader.Read(ThreeModelResult).OfType<AgentModelUsage>());

        Assert.Equal(
            [("claude-opus-4-8", 20L, 5L, 100L, 7L, 200000L, true), ("claude-haiku-4-5", 10L, 4L, null, null, 200000L, false),
             ("claude-sub-1", 1L, null, null, null, null, false)],
            usage.Models.Select(m => (m.Model, m.InputTokens, m.OutputTokens, m.CacheReadTokens, m.CacheCreateTokens, m.ContextWindow, m.IsSessionModel)).ToArray());
        Assert.DoesNotContain("0.5", usage.ToString());
    }

    [Fact]
    public void A_single_model_result_and_one_with_no_model_usage_each_read_as_they_are()
    {
        var reader = new StreamJsonAgentReader();
        var one = Assert.Single(reader.Read(
            """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s","modelUsage":{"m1":{"inputTokens":3,"outputTokens":4}}}""")
            .OfType<AgentModelUsage>());

        Assert.Equal(("m1", 3L, 4L, false), (one.Models[0].Model, one.Models[0].InputTokens, one.Models[0].OutputTokens, one.Models[0].IsSessionModel));
        Assert.Empty(new StreamJsonAgentReader().Read("""{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s"}""").OfType<AgentModelUsage>());
    }

    [Fact]
    public void A_denied_tool_call_is_an_event_with_the_reason_only_where_the_field_gives_one()
    {
        var denied = new StreamJsonAgentReader().Read(ThreeModelResult).OfType<AgentToolDenied>().ToList();

        Assert.Equal([new AgentToolDenied("Bash", "toolu_1", null), new AgentToolDenied("Edit", "toolu_2", "read-only turn")], denied);
    }

    [Fact]
    public void The_report_comes_before_the_terminal_and_does_not_change_it()
    {
        var events = new StreamJsonAgentReader().Read(ThreeModelResult);

        Assert.IsType<SessionEnded>(events[^1]);
        Assert.Equal(1, events.OfType<SessionEnded>().Count());
        Assert.Equal(ProviderVerdict.Ok, ((SessionEnded)events[^1]).Verdict);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    }

    // ── frames no case took (AG32) ───────────────────────────────────────────

    [Fact]
    public void Three_unknown_types_and_a_flood_of_one_leave_three_entries_with_an_exact_count_and_one_bounded_sample()
    {
        var reader = new StreamJsonAgentReader();
        var flood = "{\"type\":\"telemetry\",\"blob\":\"" + new string('x', 2000) + "\"}";
        string[] lines = [..Enumerable.Repeat(flood, 500), """{"type":"surprise","n":1}""", """{"type":"stream_event","event":{"type":"novel_event"}}""",
            """{"type":"system","subtype":"init","session_id":"s"}""", """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s"}"""];

        var events = lines.SelectMany(reader.Read).ToList();
        var unread = reader.Unread();

        Assert.Equal([("telemetry", (string?)null, 500), ("surprise", null, 1), ("stream_event", "novel_event", 1)],
            unread.Select(u => (u.Type, u.Subtype, u.Count)).ToArray());
        Assert.All(unread, u => Assert.True(u.Sample.Length <= 512));
        Assert.Equal(512, unread[0].Sample.Length);
        Assert.Equal(2, events.Count);   // the start and the terminal: nothing was added for the unread ones
    }

    [Fact]
    public void A_stream_with_nothing_unknown_leaves_unread_empty_and_the_entries_are_capped_at_sixteen()
    {
        var reader = new StreamJsonAgentReader();
        _ = FullTurn.SelectMany(reader.Read).ToList();
        Assert.Empty(reader.Unread());

        for (var i = 0; i < 40; i++) _ = reader.Read("{\"type\":\"kind" + i + "\"}");
        Assert.Equal(16, reader.Unread().Count);
        Assert.Equal("kind0", reader.Unread()[0].Type);
    }

    private static readonly string[] FullTurn =
    [
        """{"type":"system","subtype":"init","session_id":"s"}""",
        """{"type":"stream_event","event":{"type":"message_start"}}""",
        """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"hi"}}}""",
        """{"type":"stream_event","event":{"type":"message_stop"}}""",
        """{"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}""",
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}""",
        """{"type":"result","subtype":"success","is_error":false,"result":"hi","session_id":"s"}""",
    ];

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
    public void Assistant_with_text_and_tool_use_and_no_partial_deltas_yields_its_words_then_ToolCall_and_UsageLive()
    {
        var reader = new StreamJsonAgentReader();
        const string line = """{"type":"assistant","message":{"model":"claude-opus-4-8","content":[{"type":"text","text":"I'll read it."},{"type":"tool_use","id":"toolu_1","name":"Read","input":{"file_path":"/x/a.txt"}}],"usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":50,"cache_creation_input_tokens":10}}}""";

        var events = reader.Read(line).ToList();

        // no stream_event delta came before it, so its words are the message's own, ahead of its call
        Assert.Equal(3, events.Count);
        Assert.Equal("I'll read it.", Assert.IsType<TextDelta>(events[0]).Text);

        var toolCall = Assert.Single(events.OfType<ToolCall>());
        Assert.Equal("Read", toolCall.Name);
        Assert.Equal("toolu_1", toolCall.CallId);
        // ArgumentsJson should contain file_path
        Assert.Contains("file_path", toolCall.ArgumentsJson);
        Assert.Contains("/x/a.txt", toolCall.ArgumentsJson);

        var usage = Assert.Single(events.OfType<UsageLive>());
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(50, usage.CacheReadTokens);
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

        Assert.Equal(3, events.Count);

        var final = Assert.IsType<UsageFinal>(events[0]);
        Assert.Equal(100, final.InputTokens);
        Assert.Equal(20, final.OutputTokens);
        Assert.Equal(50, final.CacheReadTokens);
        Assert.Equal(10, final.CacheCreateTokens);

        // the usage reading rides between the older event and the terminal (AgentEventStreamCompletionTests)
        Assert.IsType<UsageReading>(events[1]);

        var ended = Assert.IsType<SessionEnded>(events[2]);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
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

        Assert.Equal(3, events.Count);

        var final = Assert.IsType<UsageFinal>(events[0]);
        Assert.Equal("claude-opus-4-8", final.Model);  // remembered from init
        Assert.Equal(5, final.CacheCreateTokens);

        Assert.IsType<UsageReading>(events[1]);

        var ended = Assert.IsType<SessionEnded>(events[2]);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Equal("error_max_turns", ended.Subtype);
        Assert.Equal("sess-9", ended.SessionId);
        Assert.Null(ended.FinalText);  // no "result" key in error line
        Assert.Null(ended.Diagnostic);  // reader never sets diagnostic
    }

    /// <summary>A FAILED turn does not hand back its pre-error partial text as the answer.
    /// <para>The fallback that substitutes the last assistant text for an empty terminal result is gated on
    /// <c>is_error</c>; ungated, a run that streamed some text and then failed returns that text as
    /// <c>FinalText</c>. The fold one layer up applies the identical gate — "a terminal that IS an error must
    /// NOT be dressed up as a partial success … those callers would then consume garbage instead of
    /// retrying" — but cannot fire once this reader has filled the field. The codex twin returns null here
    /// too.</para>
    /// <para>An error fact that feeds no assistant text before the failure cannot catch this: the fallback
    /// has nothing to substitute, and <c>FinalText</c> is null either way.</para>
    /// </summary>
    [Fact]
    public void A_failed_turn_does_not_return_its_partial_text_as_the_answer()
    {
        var reader = new StreamJsonAgentReader();
        _ = reader.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"half an answ"}]}}""").ToList();

        var events = reader.Read(
            """{"type":"result","subtype":"error_max_turns","is_error":true,"session_id":"s"}""").ToList();

        var ended = Assert.IsType<SessionEnded>(events.Last());
        Assert.True(ended.IsError);
        Assert.Null(ended.FinalText);
    }

    /// <summary>A failed turn's verdict is CLASSIFIED from the backend's own words, not hard-coded.
    /// <para>A bare <c>isError ? Failed : Ok</c> skips <c>ProviderVerdictClassifier</c>, so an expired login
    /// reports <c>Failed</c> and a host switching on the verdict retries immediately instead of prompting to
    /// re-authenticate. <c>AuthFailed</c> cools the host; <c>Failed</c> merely advances.</para></summary>
    [Fact]
    public void A_failed_turn_classifies_an_auth_error_rather_than_reporting_a_bare_failure()
    {
        var reader = new StreamJsonAgentReader();

        var events = reader.Read(
            """{"type":"result","is_error":true,"result":"401 Unauthorized: please run claude auth login","session_id":"s"}""")
            .ToList();

        var ended = Assert.IsType<SessionEnded>(events.Last());
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
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

    // ── a subagent's lines ───────────────────────────────────────────────────

    // SOURCE-DERIVED: the shape of a host's capture of an ASYNCHRONOUS, un-nested, successful subagent, in which
    // every line the child produces arrives on the session's stdout with `parent_tool_use_id` set to the `Task`
    // call that spawned it. A synchronous subagent, a nested one and a failed one were not seen there.
    private const string SessionPlan =
        """{"type":"assistant","message":{"model":"claude-opus-4-8","content":[{"type":"tool_use","id":"plan-1","name":"TodoWrite","input":{"todos":[{"content":"survey","status":"in_progress"}]}}]},"parent_tool_use_id":null}""";
    private const string SessionSpawn =
        """{"type":"assistant","message":{"model":"claude-opus-4-8","content":[{"type":"tool_use","id":"task-1","name":"Task","input":{"prompt":"look"}}],"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0}},"parent_tool_use_id":null}""";
    private const string ChildRead =
        """{"type":"assistant","message":{"model":"claude-haiku-4-5","content":[{"type":"tool_use","id":"read-1","name":"Read","input":{"file_path":"a.txt"}}],"usage":{"input_tokens":900,"output_tokens":40,"cache_read_input_tokens":300}},"parent_tool_use_id":"task-1"}""";
    private const string ChildPlan =
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"plan-2","name":"TodoWrite","input":{"todos":[{"content":"read a.txt","status":"pending"}]}}]},"parent_tool_use_id":"task-1"}""";
    private const string ChildResult =
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"read-1","content":"hello"}]},"parent_tool_use_id":"task-1"}""";
    private const string ChildText =
        """{"type":"assistant","message":{"content":[{"type":"text","text":"the child's report"}]},"error":"max_output_tokens","parent_tool_use_id":"task-1"}""";
    private const string ChildDelta =
        """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"child"}},"parent_tool_use_id":"task-1"}""";

    [Fact]
    public void A_subagents_calls_and_results_name_the_call_that_spawned_it()
    {
        var reader = new StreamJsonAgentReader();

        var spawn = Assert.IsType<ToolCall>(Assert.Single(reader.Read(SessionSpawn).OfType<ToolCall>()));
        var read = Assert.IsType<ToolCall>(Assert.Single(reader.Read(ChildRead).OfType<ToolCall>()));
        var result = Assert.IsType<ToolResult>(Assert.Single(reader.Read(ChildResult)));

        Assert.Null(spawn.ParentCallId);
        Assert.Equal("Read", read.Name);
        Assert.Equal("task-1", read.ParentCallId);
        Assert.Equal("read-1", result.CallId);
        Assert.Equal("task-1", result.ParentCallId);
    }

    [Fact]
    public void A_line_with_no_parent_field_or_a_null_one_is_the_sessions_own()
    {
        var reader = new StreamJsonAgentReader();

        var call = Assert.Single(reader.Read(
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}""").OfType<ToolCall>());
        var nullField = Assert.Single(reader.Read(SessionSpawn).OfType<ToolCall>());
        var result = Assert.IsType<ToolResult>(Assert.Single(reader.Read(
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"x"}]},"parent_tool_use_id":null}""")));

        Assert.Null(call.ParentCallId);
        Assert.Null(nullField.ParentCallId);
        Assert.Null(result.ParentCallId);
    }

    [Fact]
    public void A_subagents_todo_list_does_not_replace_the_sessions_plan()
    {
        var reader = new StreamJsonAgentReader();

        var plans = new[] { SessionPlan, ChildPlan }.SelectMany(reader.Read).OfType<PlanUpdated>().ToList();

        var plan = Assert.Single(plans);
        Assert.Equal("survey", Assert.Single(plan.Entries).Content);
    }

    [Fact]
    public void A_subagents_usage_does_not_tick_as_the_turns()
    {
        var reader = new StreamJsonAgentReader();

        var live = new[] { SessionSpawn, ChildRead }.SelectMany(reader.Read).OfType<UsageLive>().ToList();

        Assert.Equal(10, Assert.Single(live).InputTokens);
    }

    [Fact]
    public void A_subagents_text_and_error_do_not_become_the_turns_fallback_or_failure()
    {
        var reader = new StreamJsonAgentReader();
        _ = new[] { SessionSpawn, ChildRead, ChildResult, ChildText }.SelectMany(reader.Read).ToList();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(reader.Read(
            """{"type":"result","subtype":"success","is_error":false,"result":"","session_id":"s"}""")));

        Assert.True(string.IsNullOrEmpty(ended.FinalText));
        Assert.Null(ended.Failure);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
    }

    [Fact]
    public void A_subagents_complete_message_yields_each_text_and_thinking_block_with_the_call_that_spawned_it()
    {
        var reader = new StreamJsonAgentReader();

        var events = reader.Read(
            """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"hm","signature":"x"},{"type":"text","text":"found it"},{"type":"tool_use","id":"read-2","name":"Read","input":{}},{"type":"text","text":"and more"},{"type":"text","text":""}],"usage":{"input_tokens":5,"output_tokens":5}},"parent_tool_use_id":"task-1"}""");

        Assert.Equal(
            [new Thinking("hm") { ParentCallId = "task-1" }, new TextDelta("found it") { ParentCallId = "task-1" },
             new ToolCall("Read", "{}", "read-2") { ParentCallId = "task-1" }, new TextDelta("and more") { ParentCallId = "task-1" }],
            events);
    }

    [Fact]
    public void A_synchronous_child_with_no_task_still_carries_its_words_and_its_end_is_the_calls_result()
    {
        var reader = new StreamJsonAgentReader();

        var all = new[] { SessionSpawn, ChildText,
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"task-1","content":"the child's report"}]},"parent_tool_use_id":null}""" }
            .SelectMany(reader.Read).ToList();

        Assert.Equal("task-1", Assert.Single(all.OfType<TextDelta>()).ParentCallId);
        Assert.Empty(all.OfType<AgentTaskStarted>());
        Assert.Equal("task-1", Assert.Single(all.OfType<ToolResult>()).CallId);
    }

    [Fact]
    public void The_sessions_own_whole_message_yields_its_words_and_no_thinking_event()
    {
        var reader = new StreamJsonAgentReader();

        var events = reader.Read(
            """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"own"},{"type":"text","text":"own words"}]},"parent_tool_use_id":null}""");

        Assert.Equal("own words", Assert.IsType<TextDelta>(Assert.Single(events)).Text);
    }

    [Fact]
    public void Words_a_partial_delta_streamed_are_not_yielded_again_by_the_message_that_carries_them()
    {
        var reader = new StreamJsonAgentReader();

        var events = FullTurn.SelectMany(reader.Read).OfType<TextDelta>().ToList();

        Assert.Equal(["hi"], events.Select(e => e.Text));
    }

    [Fact]
    public void A_later_message_with_no_deltas_is_read_whole_even_after_an_earlier_one_streamed()
    {
        var reader = new StreamJsonAgentReader();
        string[] lines =
        [
            """{"type":"stream_event","event":{"type":"message_start"}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"one"}}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"one"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"two"}]}}""",
        ];

        var words = lines.SelectMany(reader.Read).OfType<TextDelta>().Select(e => e.Text).ToList();

        Assert.Equal(["one", "two"], words);
    }

    // ── a clean end with no result frame ─────────────────────────────────────

    [Fact]
    public void A_clean_exit_after_an_answer_with_no_result_frame_ends_the_turn_on_the_last_text()
    {
        var reader = new StreamJsonAgentReader();
        _ = reader.Read("""{"type":"system","subtype":"init","session_id":"s"}""").ToList();
        _ = reader.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"the answer"}]}}""").ToList();

        var ended = reader.ReadEnd(0);

        Assert.NotNull(ended);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("no-result-frame", ended.Subtype);
        Assert.Equal("the answer", ended.FinalText);
        Assert.Equal("s", ended.SessionId);
    }

    [Fact]
    public void A_clean_exit_with_a_tool_call_never_answered_stays_the_loops_failed_turn()
    {
        var reader = new StreamJsonAgentReader();
        _ = reader.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"looking"},{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}""").ToList();

        Assert.Null(reader.ReadEnd(0));

        _ = reader.Read("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"ok"}]}}""").ToList();

        Assert.NotNull(reader.ReadEnd(0));
    }

    [Fact]
    public void A_clean_exit_with_nothing_read_or_after_a_result_or_a_nonzero_exit_adds_no_ending()
    {
        Assert.Null(new StreamJsonAgentReader().ReadEnd(0));

        var withResult = new StreamJsonAgentReader();
        _ = FullTurn.SelectMany(withResult.Read).ToList();
        Assert.Null(withResult.ReadEnd(0));

        var crashed = new StreamJsonAgentReader();
        _ = crashed.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"half"}]}}""").ToList();
        Assert.Null(crashed.ReadEnd(1));
    }

    [Fact]
    public void A_clean_exit_after_an_assistant_message_that_was_an_api_error_ends_failed_by_that_error()
    {
        var reader = new StreamJsonAgentReader();
        _ = reader.Read("""{"type":"assistant","message":{"content":[{"type":"text","text":"Please run /login"}]},"error":"authentication_failed"}""").ToList();

        var ended = reader.ReadEnd(0);

        Assert.NotNull(ended);
        Assert.True(ended.IsError);
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.NotNull(ended.Failure);
    }

    [Fact]
    public void A_subagents_text_stream_is_not_the_sessions_answer_text()
    {
        var reader = new StreamJsonAgentReader();

        Assert.Empty(reader.Read(ChildDelta));
    }

    [Fact]
    public void A_subagents_model_is_not_the_turns_reported_model()
    {
        var reader = new StreamJsonAgentReader();
        _ = new[] { SessionSpawn, ChildRead }.SelectMany(reader.Read).ToList();

        var final = Assert.Single(reader.Read(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s","usage":{"input_tokens":1,"output_tokens":1}}""")
            .OfType<UsageFinal>());

        Assert.Equal("claude-opus-4-8", final.Model);
    }

    // ── background work (task_* lines) ───────────────────────────────────────

    // SOURCE-DERIVED: the shape of a host's capture of a turn that backgrounds two commands. The first ends before
    // the result; the second is still running at the result and is ended by the binary after it. A subagent's
    // `task_started` (task_type local_agent, no description) is the shape of the same host's lineage capture. A
    // synchronous subagent and a failed task were not seen there.
    private static readonly string[] BackgroundTurn =
    [
        """{"type":"system","subtype":"init","session_id":"sess-bg","cwd":"/work","tools":["Bash"]}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_fetch","name":"Bash","input":{"command":"git fetch origin","run_in_background":true}}]},"parent_tool_use_id":null}""",
        """{"type":"system","subtype":"task_started","task_id":"b0fetch","tool_use_id":"toolu_fetch","description":"git fetch","is_backgrounded":true,"task_type":"local_bash"}""",
        """{"type":"system","subtype":"task_updated","task_id":"b0fetch","patch":{"status":"completed","end_time":1760950801000}}""",
        """{"type":"system","subtype":"task_notification","task_id":"b0fetch","tool_use_id":"toolu_fetch","status":"completed","output_file":"/tasks/b0fetch.output","summary":"git fetch"}""",
        """{"type":"system","subtype":"task_started","task_id":"b1gates","tool_use_id":"toolu_gates","description":"npm run gates","is_backgrounded":true,"task_type":"local_bash"}""",
        """{"type":"system","subtype":"background_tasks_changed","tasks":[{"task_id":"b1gates","task_type":"local_bash","description":"npm run gates"}]}""",
        """{"type":"result","subtype":"success","is_error":false,"result":"running in the background","session_id":"sess-bg"}""",
        """{"type":"system","subtype":"task_updated","task_id":"b1gates","patch":{"status":"killed","end_time":1760950827000}}""",
        """{"type":"system","subtype":"task_notification","task_id":"b1gates","tool_use_id":"toolu_gates","status":"stopped","output_file":"/tasks/b1gates.output","summary":"npm run gates"}""",
    ];

    [Fact]
    public void A_started_task_carries_its_id_kind_description_and_the_call_that_started_it()
    {
        var reader = new StreamJsonAgentReader();

        var started = BackgroundTurn.SelectMany(reader.Read).OfType<AgentTaskStarted>().ToList();

        Assert.Equal(
            [new AgentTaskStarted("b0fetch", "local_bash", "git fetch", "toolu_fetch"),
             new AgentTaskStarted("b1gates", "local_bash", "npm run gates", "toolu_gates")],
            started);
    }

    [Fact]
    public void A_subagents_task_names_the_call_its_lines_name_as_their_parent()
    {
        var reader = new StreamJsonAgentReader();

        var started = Assert.IsType<AgentTaskStarted>(Assert.Single(reader.Read(
            """{"type":"system","subtype":"task_started","task_id":"a9b1","task_type":"local_agent","spawn_depth":1,"tool_use_id":"task-1","subagent_type":"general-purpose"}""")));
        var child = Assert.Single(reader.Read(ChildRead).OfType<ToolCall>());

        Assert.Equal("local_agent", started.Kind);
        Assert.Null(started.Description);
        Assert.Equal(started.CallId, child.ParentCallId);
    }

    [Fact]
    public void An_ended_task_carries_the_agents_status_word_once()
    {
        var reader = new StreamJsonAgentReader();

        var ended = BackgroundTurn.SelectMany(reader.Read).OfType<AgentTaskEnded>().ToList();

        Assert.Equal([new AgentTaskEnded("b0fetch", "completed"), new AgentTaskEnded("b1gates", "stopped")], ended);
    }

    [Fact]
    public void A_task_still_open_at_the_result_is_ended_after_the_one_terminal()
    {
        var reader = new StreamJsonAgentReader();

        var events = BackgroundTurn.SelectMany(reader.Read).ToList();

        var terminal = events.FindIndex(e => e is SessionEnded);
        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Equal("running in the background", ended.FinalText);
        Assert.Single(events.OfType<AgentTaskStarted>(), s => s.TaskId == "b1gates");
        Assert.DoesNotContain(events.Take(terminal), e => e is AgentTaskEnded { TaskId: "b1gates" });
        Assert.Equal(new AgentTaskEnded("b1gates", "stopped"), Assert.IsType<AgentTaskEnded>(events[^1]));
    }

    [Fact]
    public void A_task_line_never_reaches_the_sessions_own_events()
    {
        var reader = new StreamJsonAgentReader();

        var events = BackgroundTurn.SelectMany(reader.Read).ToList();

        // the session is announced once, by its init; a task line carrying another id announces nothing
        Assert.Equal("sess-bg", Assert.Single(events.OfType<SessionStarted>()).SessionId);
        Assert.Empty(reader.Read(
            """{"type":"system","subtype":"task_started","task_id":"x","session_id":"other","description":"d"}""").OfType<SessionStarted>());
        Assert.DoesNotContain(events, e => e is TextDelta or Thinking or PlanUpdated or UsageLive);
    }

    [Theory]
    [InlineData("""{"type":"system","subtype":"task_started","description":"no id"}""")]
    [InlineData("""{"type":"system","subtype":"task_started","task_id":"","description":"empty id"}""")]
    [InlineData("""{"type":"system","subtype":"task_notification","status":"completed"}""")]
    [InlineData("""{"type":"system","subtype":"task_progress","task_id":"a9b1","description":"Reading README.md"}""")]
    [InlineData("""{"type":"system","subtype":"task_updated","task_id":"a9b1","patch":{"status":"completed"}}""")]
    public void A_task_line_that_names_no_task_or_says_nothing_new_yields_nothing(string line)
    {
        Assert.Empty(new StreamJsonAgentReader().Read(line));
    }

    [Fact]
    public void A_task_line_missing_its_optional_fields_reads_them_as_null()
    {
        var reader = new StreamJsonAgentReader();

        Assert.Equal(new AgentTaskStarted("t1", null, null, null),
            Assert.Single(reader.Read("""{"type":"system","subtype":"task_started","task_id":"t1"}""")));
        Assert.Equal(new AgentTaskEnded("t1", null),
            Assert.Single(reader.Read("""{"type":"system","subtype":"task_notification","task_id":"t1"}""")));
    }
}
