using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Delivery on both CLI doors, by fault injection on the process stand-in: a process that dies before
/// any event never showed the prompt was read; one that dies after the door answered had taken it; one that dies
/// after a tool step had begun may have acted. The rule is the shared loop's, so the two doors are held to the
/// same table.</summary>
public class AgentDeliveryTests
{
    private static readonly Dictionary<string, string[]> Answered = new()
    {
        ["claude"] =
        [
            """{"type":"system","session_id":"s-1","model":"m"}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"half"}}}""",
        ],
        ["codex"] =
        [
            """{"type":"thread.started","thread_id":"s-1"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"half"}}""",
        ],
    };

    private static readonly Dictionary<string, string[]> AfterATool = new()
    {
        ["claude"] =
        [
            .. Answered["claude"],
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{}}]}}""",
        ],
        ["codex"] =
        [
            .. Answered["codex"],
            """{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"x","status":"in_progress"}}""",
        ],
    };

    private static async Task<SessionEnded> Run(string backend, FakeProcessRunner runner, AgentSessionOptions? options = null)
    {
        IAgentSession session = backend == "claude"
            ? new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude")
            : new CodexAgentSession(runner, new LyntaiOptions(), command: "codex");
        var events = await session.StreamAsync(options ?? new AgentSessionOptions { Prompt = "hi" }).ToListAsync();
        return events.OfType<SessionEnded>().Single();
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_process_that_dies_before_any_event_was_never_sent(string backend)
    {
        var ended = await Run(backend, new FakeProcessRunner([], new ProcessRunException(backend, 1, "boom")));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("process.exit", ended.Failure.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_process_that_dies_mid_stream_had_taken_the_prompt(string backend)
    {
        var ended = await Run(backend, new FakeProcessRunner(Answered[backend], new ProcessRunException(backend, 1, "boom")));

        Assert.Equal(AgentDelivery.Taken, ended.Failure!.Delivery);
        Assert.Equal("s-1", ended.SessionId);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_process_that_dies_after_a_tool_call_is_uncertain(string backend)
    {
        var ended = await Run(backend, new FakeProcessRunner(AfterATool[backend], new ProcessRunException(backend, 1, "boom")));

        Assert.Equal(AgentDelivery.Uncertain, ended.Failure!.Delivery);
    }

    [Theory] // the same table for a stream that simply ends with no terminal
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_stream_that_ends_with_no_terminal_is_judged_by_what_it_showed(string backend)
    {
        var nothing = await Run(backend, new FakeProcessRunner([]));
        var answered = await Run(backend, new FakeProcessRunner(Answered[backend]));
        var tooled = await Run(backend, new FakeProcessRunner(AfterATool[backend]));

        Assert.Equal(AgentDelivery.NeverSent, nothing.Failure!.Delivery);
        Assert.Equal(AgentDelivery.Taken, answered.Failure!.Delivery);
        Assert.Equal(AgentDelivery.Uncertain, tooled.Failure!.Delivery);
        Assert.All(new[] { nothing, answered, tooled }, e => Assert.Equal("no-terminal-event", e.Failure!.By));
    }

    [Theory] // a stalled child is transient; it is the harness's fault, never the door's words
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_inactivity_timeout_after_the_door_answered_is_transient_and_taken(string backend)
    {
        var ended = await Run(backend, new FakeProcessRunner(Answered[backend],
            new ProcessTimeoutException(backend, TimeSpan.FromSeconds(30))));

        Assert.Equal(ProviderVerdict.Timeout, ended.Verdict);
        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
        Assert.Equal("process.inactivity-timeout", ended.Failure.By);
        Assert.Equal(AgentDelivery.Taken, ended.Failure.Delivery);
    }

    [Theory] // stderr may name an access failure and nothing else
    [InlineData("claude", "Invalid API key · Please run /login", AgentFailureKind.Access, ProviderVerdict.AuthFailed)]
    [InlineData("codex", "unexpected status 401 Unauthorized", AgentFailureKind.Access, ProviderVerdict.AuthFailed)]
    [InlineData("claude", "429 Too Many Requests", AgentFailureKind.Fatal, ProviderVerdict.RateLimited)]
    [InlineData("codex", "overloaded_error", AgentFailureKind.Fatal, ProviderVerdict.Failed)]
    public async Task Stderr_names_an_access_failure_and_nothing_else(string backend, string stderr, AgentFailureKind kind, ProviderVerdict verdict)
    {
        var ended = await Run(backend, new FakeProcessRunner([], new ProcessRunException(backend, 1, stderr)));

        Assert.Equal(kind, ended.Failure!.Kind);
        // the verdict of a process fault is still its stderr's read, as before this layer: the kind is the
        // narrower fact, so a stderr that says 429 is a Fatal failure that benches the host
        Assert.Equal(verdict, ended.Verdict);
    }

    [Theory] // an in-band failure after a tool step began: the failure is the door's, the delivery uncertain
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_in_band_failure_after_a_tool_call_is_uncertain_whatever_its_kind(string backend)
    {
        var terminal = backend == "claude"
            ? """{"type":"result","subtype":"success","is_error":true,"api_error_status":529,"result":"API Error: 529 overloaded_error"}"""
            : """{"type":"turn.failed","error":{"message":"Selected model is at capacity. Please try a different model."}}""";

        var ended = await Run(backend, new FakeProcessRunner([.. AfterATool[backend], terminal]));

        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
        Assert.Equal(AgentDelivery.Uncertain, ended.Failure.Delivery);
    }

    [Theory] // …and before any tool step it is taken, the door having answered
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_in_band_failure_after_the_door_answered_had_taken_the_prompt(string backend)
    {
        var terminal = backend == "claude"
            ? """{"type":"result","subtype":"success","is_error":true,"api_error_status":429,"result":"You've hit your limit"}"""
            : """{"type":"turn.failed","error":{"message":"Your workspace is out of credits. Ask your workspace owner to refill in order to continue."}}""";

        var ended = await Run(backend, new FakeProcessRunner([.. Answered[backend], terminal]));

        Assert.Equal(AgentFailureKind.Limit, ended.Failure!.Kind);
        Assert.Equal(AgentDelivery.Taken, ended.Failure.Delivery);
    }

    [Theory] // refused before anything was spawned: the one case where nothing could have been sent
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_turn_refused_before_the_spawn_was_never_sent(string backend)
    {
        var runner = new FakeProcessRunner(Answered[backend]);

        var ended = await Run(backend, runner, new AgentSessionOptions { Prompt = "hi", ResumeToken = "--last" });

        Assert.Empty(runner.Calls);
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
    }

    [Theory] // a good turn has no failure to stamp
    [InlineData("claude", """{"type":"result","subtype":"success","is_error":false,"result":"ok"}""")]
    [InlineData("codex", """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""")]
    public async Task A_good_turn_carries_no_failure(string backend, string terminal)
    {
        var ended = await Run(backend, new FakeProcessRunner([.. Answered[backend], terminal]));

        Assert.False(ended.IsError);
        Assert.Null(ended.Failure);
    }
}
