using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Agents;

/// <summary>The recorder folds a turn's stream into a transcript turn: the host's words verbatim, the agent's last
/// message, a count per tool, the last plan, the session id and the end. Scripted streams only.</summary>
public class AgentTranscriptRecorderTests
{
    private static readonly AgentConversationSource Claude = new("claude-cli", "cli");

    private static AgentSessionOptions Turn(string prompt, string? system = null) =>
        new() { Prompt = prompt, SystemPrompt = system };

    private static SessionEnded Ended(string? sessionId = "s-1", string? finalText = null) =>
        new(ProviderVerdict.Ok, false, "success", sessionId, finalText, null);

    private static AgentTranscript Record(AgentSessionOptions options, params AgentStreamEvent[] events)
    {
        var recorder = new AgentTranscriptRecorder();
        recorder.BeginTurn(Claude, options);
        foreach (var e in events) recorder.Add(e);
        return recorder.Transcript;
    }

    [Fact]
    public void The_hosts_words_are_kept_verbatim_with_the_session_and_the_end()
    {
        var prompt = "  把登录页面改成深色主题\r\n  keep   the spacing  ";
        var t = Record(Turn(prompt, "Be brief."), new SessionStarted("s-1"), new TextDelta("done"), Ended("s-2")).Turns.Single();

        Assert.Equal(prompt, t.Prompt);
        Assert.Equal("Be brief.", t.SystemPrompt);
        Assert.Equal("s-2", t.SessionId);
        Assert.Equal(ProviderVerdict.Ok, t.Verdict);
        Assert.Null(t.FailureKind);
        Assert.Equal(Claude, t.Source);
    }

    [Fact]
    public void The_last_message_is_the_terminals_final_text_else_the_text_after_the_last_tool_step()
    {
        var withFinal = Record(Turn("go"), new TextDelta("early"), Ended(finalText: "the answer")).Turns.Single();
        Assert.Equal("the answer", withFinal.LastMessage);

        var streamed = Record(Turn("go"),
            new TextDelta("before the tool"), new ToolCall("Read", "{}", "c1"), new ToolResult("c1", "ok", false),
            new TextDelta("after "), new TextDelta("the tool"), Ended()).Turns.Single();
        Assert.Equal("after the tool", streamed.LastMessage);

        var endedOnATool = Record(Turn("go"), new TextDelta("said"), new ToolCall("Edit", "{}", "c1"), Ended()).Turns.Single();
        Assert.Null(endedOnATool.LastMessage);
    }

    [Fact]
    public void Thinking_is_never_recorded_and_a_diagnostic_is_never_a_message()
    {
        var t = Record(Turn("go"), new Thinking("secret reasoning"),
            new SessionEnded(ProviderVerdict.Failed, true, null, "s-1", null, "stderr tail with a token"))
            .Turns.Single();

        Assert.Null(t.LastMessage);
        Assert.DoesNotContain("secret", t.ToString());
        Assert.DoesNotContain("token", t.ToString());
    }

    [Fact]
    public void Tools_are_counted_in_first_called_order_and_a_failed_result_is_charged_to_its_call()
    {
        var t = Record(Turn("go"),
            new ToolCall("Read", "{}", "a"), new ToolCall("Edit", "{}", "b"), new ToolCall("Read", "{}", "c"),
            new ToolResult("b", "denied", true), new ToolResult("c", "fine", false), new ToolResult("a", "bad", true),
            new ToolCall("Bash", "{}"), new ToolResult(null, "exit 1", true),
            Ended()).Turns.Single();

        Assert.Equal(
            [new ToolUseSummary("Read", 2, 1), new ToolUseSummary("Edit", 1, 1), new ToolUseSummary("Bash", 1, 1)],
            t.Tools);
    }

    [Fact]
    public void The_plan_is_the_last_one_the_turn_sent()
    {
        var first = new PlanUpdated([new PlanEntry("a", PlanEntryStatus.Pending)]);
        var last = new PlanUpdated([new PlanEntry("a", PlanEntryStatus.Completed), new PlanEntry("b", null)]);

        Assert.Equal(last.Entries, Record(Turn("go"), first, last, Ended()).Turns.Single().Plan);
        Assert.Null(Record(Turn("go"), Ended()).Turns.Single().Plan);
    }

    [Fact]
    public void A_failed_terminal_records_its_kind_and_a_stream_with_no_terminal_records_none()
    {
        var failed = Record(Turn("go"),
            new SessionEnded(ProviderVerdict.RateLimited, true, null, null, null, "limit")
            { Failure = new AgentFailure(AgentFailureKind.Limit, "test") }).Turns.Single();
        Assert.Equal(AgentFailureKind.Limit, failed.FailureKind);
        Assert.Equal(ProviderVerdict.RateLimited, failed.Verdict);

        var abandoned = Record(Turn("go"), new SessionStarted("s-9"), new TextDelta("half")).Turns.Single();
        Assert.Null(abandoned.Verdict);
        Assert.Equal("s-9", abandoned.SessionId);
        Assert.Equal("half", abandoned.LastMessage);
    }

    [Fact]
    public void A_second_session_start_is_a_retried_attempt_and_replaces_the_failed_ones_words()
    {
        var t = Record(Turn("go"),
            new SessionStarted("s-1"), new TextDelta("first try"), new ToolCall("Read", "{}", "a"),
            new SessionStarted("s-1"), new TextDelta("second try"), Ended()).Turns.Single();

        Assert.Equal("second try", t.LastMessage);
        Assert.Equal(1, t.Tools.Single().Calls);
    }

    [Fact]
    public void Turns_accumulate_oldest_first_and_beginning_one_closes_the_open_one()
    {
        var recorder = new AgentTranscriptRecorder();
        recorder.BeginTurn(Claude, Turn("one"));
        recorder.Add(new TextDelta("partial"));
        recorder.BeginTurn(Claude, Turn("two"));
        recorder.Add(Ended());

        var turns = recorder.Transcript.Turns;
        Assert.Equal(["one", "two"], turns.Select(t => t.Prompt));
        Assert.Null(turns[0].Verdict);
        Assert.Equal(ProviderVerdict.Ok, turns[1].Verdict);
    }

    [Fact]
    public void An_event_with_no_open_turn_is_refused()
    {
        var recorder = new AgentTranscriptRecorder();
        Assert.Throws<InvalidOperationException>(() => recorder.Add(new TextDelta("x")));

        recorder.BeginTurn(Claude, Turn("go"));
        recorder.Add(Ended());
        Assert.Throws<InvalidOperationException>(() => recorder.Add(new TextDelta("after the end")));
    }

    [Fact]
    public async Task The_decorator_forwards_every_event_unchanged_and_records_the_turn()
    {
        AgentStreamEvent[] events = [new SessionStarted("s-1"), new TextDelta("hi"), Ended()];
        var recorder = new AgentTranscriptRecorder();
        var session = new RecordingAgentSession(new ScriptedAgentSession(events), recorder, Claude);

        var seen = await session.StreamAsync(Turn("hello")).ToListAsync();

        Assert.Equal(events, seen);
        var turn = recorder.Transcript.Turns.Single();
        Assert.Equal("hello", turn.Prompt);
        Assert.Equal("hi", turn.LastMessage);
    }
}
