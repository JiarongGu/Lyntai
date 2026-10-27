using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The plan event and the usage reading, read off recorded captures by BOTH agent readers into the
/// same stream shape. Each capture says what it is: REAL (a line the door was seen to emit) or
/// SOURCE-DERIVED (shaped from the door's published event schema or a counted record of one, never captured
/// by this repository) — and neither stands for the other.</summary>
public class AgentEventStreamCompletionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ── captures ─────────────────────────────────────────────────────────────

    /// <summary>SOURCE-DERIVED: a claude tool turn whose to-do list is the plan. The line shapes are the
    /// ones the other fixtures hold; the counts are a counted record of a real two-call turn, where the
    /// streamed messages said output 16 and 4 and the result said 80 — the result is the turn's total.</summary>
    private static readonly string[] ClaudePlanTurn =
    [
        """{"type":"system","subtype":"init","session_id":"conv-1","model":"claude-opus-4-8"}""",
        """{"type":"assistant","message":{"id":"msg_1","content":[{"type":"tool_use","id":"toolu_1","name":"TodoWrite","input":{"todos":[{"content":"Read the streamer","status":"completed","activeForm":"Reading"},{"content":"Cap it","status":"in_progress","activeForm":"Capping"},{"content":"Ship it","status":"pending","activeForm":"Shipping"}]}}],"usage":{"input_tokens":2,"output_tokens":16,"cache_read_input_tokens":17228,"cache_creation_input_tokens":16605}}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"Todos have been modified","is_error":false}]}}""",
        """{"type":"assistant","message":{"id":"msg_2","content":[{"type":"text","text":"Done."}],"usage":{"input_tokens":2,"output_tokens":4,"cache_read_input_tokens":33833,"cache_creation_input_tokens":112}}}""",
        """{"type":"result","subtype":"success","is_error":false,"result":"Done.","session_id":"conv-1","usage":{"input_tokens":4,"output_tokens":80,"cache_read_input_tokens":51061,"cache_creation_input_tokens":16717}}""",
    ];

    /// <summary>SOURCE-DERIVED: a codex turn whose agent keeps a todo list — started, changed, completed — from
    /// codex's published exec event schema (<c>todo_list</c> carries the whole list each time). The usage line
    /// is the REAL 0.146.0 one (<see cref="CodexAgentSessionTests"/>' measured success), all four counts present.</summary>
    private static readonly string[] CodexPlanTurn =
    [
        """{"type":"thread.started","thread_id":"conv-2"}""",
        """{"type":"turn.started"}""",
        """{"type":"item.started","item":{"id":"item_0","type":"todo_list","items":[{"text":"Read the streamer","completed":false},{"text":"Cap it","completed":false}]}}""",
        """{"type":"item.updated","item":{"id":"item_0","type":"todo_list","items":[{"text":"Read the streamer","completed":true},{"text":"Cap it","completed":false}]}}""",
        """{"type":"item.completed","item":{"id":"item_0","type":"todo_list","items":[{"text":"Read the streamer","completed":true},{"text":"Cap it","completed":true}]}}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"Done."}}""",
        """{"type":"turn.completed","usage":{"input_tokens":6489,"cached_input_tokens":11,"cache_write_input_tokens":7,"output_tokens":2,"reasoning_output_tokens":0}}""",
    ];

    private static List<AgentStreamEvent> Fold(Func<string, IReadOnlyList<AgentStreamEvent>> read, string[] lines) =>
        [.. lines.SelectMany(read)];

    private static List<AgentStreamEvent> ClaudeEvents(string[] lines) =>
        Fold(new StreamJsonAgentReader(new FixedClock(Noon)).Read, lines);

    private static List<AgentStreamEvent> CodexEvents(string[] lines) =>
        Fold(new CodexAgentReader(new FixedClock(Noon)).Read, lines);

    // ── the shared stream shape ──────────────────────────────────────────────

    [Fact]
    public void Both_readers_fold_their_captures_into_one_stream_shape()
    {
        foreach (var events in new[] { ClaudeEvents(ClaudePlanTurn), CodexEvents(CodexPlanTurn) })
        {
            var plan = events.OfType<PlanUpdated>().Last();
            Assert.All(plan.Entries, entry => Assert.NotNull(entry.Status));

            var reading = Assert.Single(events.OfType<UsageReading>());
            Assert.Equal(UsageReading.Tokens, reading.Unit);
            Assert.Equal(UsageScope.Turn, reading.Scope);
            Assert.Equal(Noon, reading.MeasuredAt);

            // the reading comes after the older usage events and before the one terminal event, which is last
            var ended = Assert.IsType<SessionEnded>(events[^1]);
            Assert.Single(events.OfType<SessionEnded>());
            Assert.True(events.IndexOf(reading) < events.IndexOf(ended));
            Assert.True(events.IndexOf(events.OfType<UsageFinal>().Single()) < events.IndexOf(reading));
            Assert.Equal(events[0] is SessionStarted s ? s.SessionId : null, ended.SessionId);
        }
    }

    // ── claude ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_claude_todo_list_is_a_plan_beside_its_tool_call()
    {
        var events = ClaudeEvents(ClaudePlanTurn);

        var call = Assert.Single(events.OfType<ToolCall>());
        Assert.Equal("TodoWrite", call.Name);   // the tool call stays: the plan is read beside it

        var plan = Assert.Single(events.OfType<PlanUpdated>());
        Assert.Equal(
            [
                new PlanEntry("Read the streamer", PlanEntryStatus.Completed),
                new PlanEntry("Cap it", PlanEntryStatus.InProgress),
                new PlanEntry("Ship it", PlanEntryStatus.Pending),
            ],
            plan.Entries);
        Assert.True(events.IndexOf(call) < events.IndexOf(plan));
    }

    [Fact]
    public void A_claude_result_is_read_as_the_turns_total_not_a_sum_of_its_messages()
    {
        var reading = Assert.Single(ClaudeEvents(ClaudePlanTurn).OfType<UsageReading>());

        Assert.Equal(new UsageReading(4, 80, 51061, 16717, "tokens", UsageScope.Turn, Noon), reading);
    }

    [Fact]
    public void A_count_the_claude_result_never_sends_is_unknown_not_zero()
    {
        const string line = """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"c","usage":{"input_tokens":9,"output_tokens":3}}""";

        var events = ClaudeEvents([line]);

        var reading = Assert.Single(events.OfType<UsageReading>());
        Assert.Equal((9L, 3L, (long?)null, (long?)null),
            (reading.InputTokens, reading.OutputTokens, reading.CacheReadTokens, reading.CacheCreateTokens));
        // the older event cannot say unknown, which is exactly why the reading exists
        Assert.Equal(0, events.OfType<UsageFinal>().Single().CacheReadTokens);
    }

    [Fact]
    public void A_claude_result_with_no_usage_yields_no_reading_and_the_older_events_are_unchanged()
    {
        var events = ClaudeEvents(["""{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"c"}"""]);

        Assert.Empty(events.OfType<UsageReading>());
        Assert.Empty(events.OfType<UsageFinal>());
        Assert.IsType<SessionEnded>(Assert.Single(events));
    }

    [Fact]
    public void A_streamed_claude_message_is_not_read_as_a_usage_reading()
    {
        var events = ClaudeEvents([ClaudePlanTurn[3]]);

        Assert.Equal(new UsageLive(2, 4, 33833), Assert.Single(events.OfType<UsageLive>()));
        Assert.Empty(events.OfType<UsageReading>());
    }

    [Fact]
    public void A_todo_status_the_library_has_no_word_for_is_null_not_guessed()
    {
        const string line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t","name":"TodoWrite","input":{"todos":[{"content":"Odd","status":"blocked"},{"status":"pending"}]}}]}}""";

        var plan = Assert.Single(ClaudeEvents([line]).OfType<PlanUpdated>());

        Assert.Equal([new PlanEntry("Odd", null)], plan.Entries);   // the entry with no content is skipped
    }

    [Fact]
    public void Another_tool_with_a_todos_argument_is_not_a_plan()
    {
        const string line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t","name":"Read","input":{"todos":[{"content":"x","status":"pending"}]}}]}}""";

        Assert.Empty(ClaudeEvents([line]).OfType<PlanUpdated>());
    }

    // ── codex ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_codex_todo_list_is_a_plan_each_time_it_arrives_and_never_a_tool_step()
    {
        var events = CodexEvents(CodexPlanTurn);

        var plans = events.OfType<PlanUpdated>().ToList();
        Assert.Equal(3, plans.Count);
        Assert.Equal(
            [new PlanEntry("Read the streamer", PlanEntryStatus.Pending), new PlanEntry("Cap it", PlanEntryStatus.Pending)],
            plans[0].Entries);
        Assert.Equal(PlanEntryStatus.Completed, plans[1].Entries[0].Status);
        Assert.All(plans[2].Entries, entry => Assert.Equal(PlanEntryStatus.Completed, entry.Status));
        Assert.Empty(events.OfType<ToolCall>());
        Assert.Empty(events.OfType<ToolResult>());
    }

    [Fact]
    public void A_codex_update_that_is_not_a_plan_is_still_ignored()
    {
        const string line = """{"type":"item.updated","item":{"id":"x","type":"agent_message","text":"partial"}}""";

        Assert.Empty(CodexEvents([line]));
    }

    [Fact]
    public void A_codex_usage_line_is_read_per_turn_with_the_older_events_unchanged()
    {
        var events = CodexEvents(CodexPlanTurn);

        Assert.Equal(new UsageReading(6489, 2, 11, 7, "tokens", UsageScope.Turn, Noon),
            Assert.Single(events.OfType<UsageReading>()));
        Assert.Equal(new UsageFinal(6489, 2, 11, 7, Model: null), Assert.Single(events.OfType<UsageFinal>()));
    }

    [Fact]
    public void A_count_a_codex_build_omits_is_unknown_not_zero()
    {
        const string line = """{"type":"turn.completed","usage":{"input_tokens":5,"output_tokens":1}}""";

        var events = CodexEvents([line]);

        var reading = Assert.Single(events.OfType<UsageReading>());
        Assert.Equal((5L, 1L, (long?)null, (long?)null),
            (reading.InputTokens, reading.OutputTokens, reading.CacheReadTokens, reading.CacheCreateTokens));
        Assert.Equal(0, events.OfType<UsageFinal>().Single().CacheCreateTokens);
    }

    // ── through the session: the doors, with no real binary ─────────────────

    [Fact]
    public async Task The_claude_session_streams_the_plan_and_the_reading()
    {
        var session = new ClaudeAgentSession(new FakeProcessRunner(ClaudePlanTurn), new LyntaiOptions(), command: "claude");

        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "go" }).ToListAsync();

        Assert.Single(events.OfType<PlanUpdated>());
        Assert.Equal(80, Assert.Single(events.OfType<UsageReading>()).OutputTokens);
        Assert.IsType<SessionEnded>(events[^1]);
    }

    [Fact]
    public async Task The_codex_session_streams_the_plan_and_the_reading()
    {
        var session = new CodexAgentSession(new FakeProcessRunner(CodexPlanTurn), new LyntaiOptions(), command: "codex");

        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "go" }).ToListAsync();

        Assert.Equal(3, events.OfType<PlanUpdated>().Count());
        Assert.Equal(6489, Assert.Single(events.OfType<UsageReading>()).InputTokens);
        Assert.IsType<SessionEnded>(events[^1]);
    }
}
