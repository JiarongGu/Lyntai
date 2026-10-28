using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Inference;

namespace Lyntai.Tests.Providers;

/// <summary>
/// The Agent Client Protocol door, through the stand-in agent (<c>devtools/scripts/acp-stub.mjs</c>) at zero tokens
/// and through scripted frames.
///
/// <para>Every capture is labelled. <b>REAL</b> = a frame recorded from a real <c>codex-acp</c> 2.1.1 handshake
/// (<c>initialize</c> and a logged-out <c>session/new</c>; no prompt was sent and nothing was signed in).
/// <b>SOURCE-DERIVED</b> = read from the published protocol schema or from the shipped code of
/// <c>codex-acp</c> 2.1.1 and <c>claude-agent-acp</c> 0.84.0, never recorded on a turn: no prompt was taken against a
/// real agent, so the update, usage, limit and typed-failure shapes below are held by the stand-in and by nothing
/// else (<c>docs/agent-provider-layer.md</c> §6). Neither stands for the other.</para>
/// </summary>
[Collection(AcpStandIn.Collection)]
public class AcpAgentSessionTests
{
    // ── captures ─────────────────────────────────────────────────────────────

    /// <summary>REAL: codex-acp 2.1.1's answer to <c>initialize</c>, protocol version 1. It states both
    /// <c>loadSession</c> and <c>sessionCapabilities.resume</c>, and <c>mcpCapabilities.http</c>.</summary>
    private const string RealCodexInitialize =
        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentInfo":{"name":"@agentclientprotocol/codex-acp","title":"Codex","version":"2.1.1"},"agentCapabilities":{"auth":{"logout":{}},"providers":{},"loadSession":true,"promptCapabilities":{"embeddedContext":true,"image":true},"sessionCapabilities":{"resume":{},"list":{},"close":{},"delete":{},"fork":{},"additionalDirectories":{},"subagents":{}},"mcpCapabilities":{"acp":false,"http":true,"sse":false},"_meta":{"authStatus":{}}},"authMethods":[{"id":"api-key","name":"API Key","description":"Use an API key to authenticate","_meta":{"api-key":{"provider":"openai"}}},{"id":"chat-gpt","name":"ChatGPT","description":"Use ChatGPT to authenticate"}],"_meta":{"steering":{"supported":true},"jetbrains":{"air":{"version":1,"goal":{"version":1,"controlMethod":"_session/goal","actions":["set","pause","resume","clear"]},"capabilities":["sessionFailure","diffPatch","agentFileChangeReport","nativeSubagentSessions","asyncTasks","recommendedValue","rawInputRendering","planContentDelta"]}}}}}""";

    /// <summary>REAL: codex-acp 2.1.1's answer to <c>session/new</c> under an empty home — the id is the request's
    /// (2), the code is ACP's auth-required, and there is no <c>data</c>.</summary>
    private const string RealCodexAuthRequired =
        """{"jsonrpc":"2.0","id":2,"error":{"code":-32000,"message":"Authentication required"}}""";

    // ── fixtures ─────────────────────────────────────────────────────────────

    private static string StubCommand =>
        $"node \"{Path.Combine(TestPaths.DevtoolsDir("scripts"), "acp-stub.mjs")}\"";

    private static string NewLog() =>
        Path.Combine(TestPaths.TestScratchDir, $"acp-{Guid.NewGuid():N}.jsonl");

    private static AcpAgentSession Stub(
        string? caps = null, string? log = null, bool loggedOut = false, TimeSpan? grace = null,
        TimeProvider? time = null, string? homeVariable = null)
    {
        var env = new Dictionary<string, string?>();
        if (caps is not null) env["LYNTAI_ACP_STUB_CAPS"] = caps;
        if (log is not null) env["LYNTAI_ACP_STUB_LOG"] = log;
        if (loggedOut) env["LYNTAI_ACP_STUB_AUTH"] = "out";
        return new AcpAgentSession(new ProcessRunner(), new LyntaiOptions(), StubCommand, environment: env,
            accountHomeVariable: homeVariable, cancelGrace: grace, time: time);
    }

    private static AcpAgentSession Scripted(FakeProcessRunner runner, string? homeVariable = null) =>
        new(runner, new LyntaiOptions(), "agent", accountHomeVariable: homeVariable);

    private static AgentSessionOptions Ask(string prompt = "do the thing") =>
        new() { Prompt = prompt, WorkingDirectory = TestPaths.TestScratchDir };

    private static async Task<List<AgentStreamEvent>> Run(IAgentSession session, AgentSessionOptions options) =>
        await session.StreamAsync(options).ToListAsync();

    private static SessionEnded Ended(IEnumerable<AgentStreamEvent> events) =>
        Assert.IsType<SessionEnded>(events.Last());

    private static List<JsonNode> Sent(string log) =>
        File.ReadAllLines(log).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!).ToList();

    private static JsonNode Request(IEnumerable<JsonNode> frames, string method) =>
        frames.Single(f => f["method"]?.GetValue<string>() == method);

    private static string Method(JsonNode frame) => frame["method"]?.GetValue<string>() ?? "";

    /// <summary>A scripted agent: for each method the client sends, the lines it answers with.</summary>
    private static FakeProcessRunner Agent(Dictionary<string, Func<JsonNode, string[]>> byMethod) => new()
    {
        ExchangeHandler = line =>
        {
            var frame = JsonNode.Parse(line)!;
            return byMethod.TryGetValue(Method(frame), out var reply) ? reply(frame) : [];
        },
    };

    // ── the handshake and a fresh turn ───────────────────────────────────────

    [Fact] // SOURCE-DERIVED
    public async Task A_fresh_turn_initializes_opens_a_session_prompts_and_ends_with_the_answer()
    {
        var log = NewLog();

        var events = await Run(Stub(log: log), Ask("say ok"));

        Assert.Equal("acp-stub-session-1", Assert.IsType<SessionStarted>(events[0]).SessionId);
        Assert.Equal("acp stub reply: say ok", Assert.IsType<TextDelta>(events[1]).Text);
        var ended = Ended(events);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("acp-stub-session-1", ended.SessionId);
        Assert.Equal("acp stub reply: say ok", ended.FinalText);
        Assert.Null(ended.Failure);
        Assert.Equal(["initialize", "session/new", "session/prompt"], Sent(log).Select(Method));
    }

    [Fact] // SOURCE-DERIVED: ACP's initialize request; the AIR capability is what lets codex-acp say a failure as a field
    public async Task The_initialize_request_declines_fs_and_terminal_and_declares_typed_failures()
    {
        var log = NewLog();

        await Run(Stub(log: log), Ask());

        var initialize = Request(Sent(log), "initialize")["params"]!;
        Assert.Equal(1, initialize["protocolVersion"]!.GetValue<int>());
        var capabilities = initialize["clientCapabilities"]!;
        Assert.False(capabilities["fs"]!["readTextFile"]!.GetValue<bool>());
        Assert.False(capabilities["fs"]!["writeTextFile"]!.GetValue<bool>());
        Assert.False(capabilities["terminal"]!.GetValue<bool>());
        Assert.Equal("sessionFailure", capabilities["_meta"]!["jetbrains"]!["air"]!["capabilities"]![0]!.GetValue<string>());
        Assert.Equal("lyntai", initialize["clientInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_session_opens_in_the_working_directory_as_an_absolute_path()
    {
        var log = NewLog();

        await Run(Stub(log: log), Ask() with { WorkingDirectory = "." });

        var cwd = Request(Sent(log), "session/new")["params"]!["cwd"]!.GetValue<string>();
        Assert.True(Path.IsPathRooted(cwd));
        Assert.Equal(Directory.GetCurrentDirectory(), cwd);
    }

    [Fact]
    public async Task The_prompt_goes_as_a_text_block_and_a_system_prompt_as_a_leading_one()
    {
        var log = NewLog();

        await Run(Stub(log: log), Ask("the request") with { SystemPrompt = "be brief" });

        var blocks = Request(Sent(log), "session/prompt")["params"]!["prompt"]!.AsArray();
        Assert.Equal(["be brief", "the request"], blocks.Select(b => b!["text"]!.GetValue<string>()));
        Assert.All(blocks, b => Assert.Equal("text", b!["type"]!.GetValue<string>()));
    }

    // ── load or resume, by what the agent states ─────────────────────────────

    [Fact] // SOURCE-DERIVED
    public async Task A_resume_token_continues_by_session_resume_where_the_agent_states_it()
    {
        var log = NewLog();

        var events = await Run(Stub(log: log), Ask("go on") with { ResumeToken = "conv-1" });

        var methods = Sent(log).Select(Method).ToList();
        Assert.Equal(["initialize", "session/resume", "session/prompt"], methods);
        Assert.Equal("conv-1", Request(Sent(log), "session/resume")["params"]!["sessionId"]!.GetValue<string>());
        Assert.Equal("conv-1", Assert.IsType<SessionStarted>(events[0]).SessionId);
        Assert.Equal("conv-1", Ended(events).SessionId);
    }

    [Fact] // SOURCE-DERIVED: a load replays the whole conversation as updates before it answers
    public async Task A_resume_token_continues_by_session_load_where_that_is_all_the_agent_states_and_drops_its_replay()
    {
        var log = NewLog();

        var events = await Run(Stub(caps: "load", log: log), Ask("go on") with { ResumeToken = "conv-1" });

        Assert.Equal(["initialize", "session/load", "session/prompt"], Sent(log).Select(Method));
        Assert.DoesNotContain(events, e => e is TextDelta { Text: "an earlier answer" });
        var ended = Ended(events);
        Assert.Equal("acp stub reply: go on", ended.FinalText);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
    }

    [Fact] // SOURCE-DERIVED
    public async Task A_resume_token_is_refused_before_the_prompt_where_the_agent_states_neither()
    {
        var log = NewLog();

        var events = await Run(Stub(caps: "", log: log), Ask() with { ResumeToken = "conv-1" });

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.Equal(AgentProvider.OptionRefusedSubtype, ended.Subtype);
        Assert.Contains("resume", ended.Diagnostic);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        Assert.DoesNotContain(Sent(log), f => Method(f) == "session/prompt");
    }

    [Fact] // SOURCE-DERIVED: a conversation the agent does not hold
    public async Task A_conversation_the_agent_refuses_to_resume_ends_the_turn_before_the_prompt()
    {
        var log = NewLog();

        var events = await Run(Stub(log: log), Ask() with { ResumeToken = "unknown-session" });

        var ended = Ended(events);
        Assert.True(ended.IsError);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32002", ended.Failure.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
        Assert.DoesNotContain(Sent(log), f => Method(f) == "session/prompt");
    }

    [Fact]
    public async Task A_blank_resume_token_is_refused_without_a_spawn()
    {
        var runner = new FakeProcessRunner();

        var events = await Run(Scripted(runner), Ask() with { ResumeToken = "  " });

        Assert.Equal(AgentResumeTokenSubtype, Ended(events).Subtype);
        Assert.Empty(runner.Calls);
    }

    private const string AgentResumeTokenSubtype = "resume-token-invalid";

    // ── the updates of a turn, mapped ────────────────────────────────────────

    [Fact] // SOURCE-DERIVED
    public async Task A_turns_updates_map_to_the_event_stream_in_the_order_the_agent_sent_them()
    {
        var events = await Run(Stub(), Ask("PLAN_TURN"));

        Assert.Collection(events,
            e => Assert.IsType<SessionStarted>(e),
            e => Assert.Equal("I will read the file, then run it.", Assert.IsType<Thinking>(e).Text),
            e =>
            {
                var plan = Assert.IsType<PlanUpdated>(e);
                Assert.Equal(
                    [("Read hello.txt", PlanEntryStatus.Completed), ("Run the script", PlanEntryStatus.InProgress), ("Report", PlanEntryStatus.Pending)],
                    plan.Entries.Select(p => (p.Content, p.Status)));
            },
            e =>
            {
                var call = Assert.IsType<ToolCall>(e);
                Assert.Equal(("read", "call_1"), (call.Name, call.CallId));
                Assert.Contains("Read hello.txt", call.ArgumentsJson);   // the payload is the update's own object
            },
            e =>
            {
                var result = Assert.IsType<ToolResult>(e);
                Assert.Equal("call_1", result.CallId);
                Assert.False(result.IsError);
                Assert.Contains("hello", result.Content);
            },
            e => Assert.Equal(("execute", "call_2"), (Assert.IsType<ToolCall>(e).Name, Assert.IsType<ToolCall>(e).CallId)),
            e => Assert.True(Assert.IsType<ToolResult>(e).IsError),
            e => Assert.Equal("It read hello and the script failed.", Assert.IsType<TextDelta>(e).Text),
            e => Assert.IsType<UsageFinal>(e),
            e => Assert.IsType<UsageReading>(e),
            e => Assert.Equal(ProviderVerdict.Ok, Assert.IsType<SessionEnded>(e).Verdict));
    }

    [Fact] // SOURCE-DERIVED: codex-acp's PromptResponse.usage — its inputTokens exclude the cache read
    public async Task A_prompts_usage_is_a_nullable_reading_and_a_final_count()
    {
        var clock = FixedLocalClock.Wednesday;

        var events = await Run(Stub(time: clock), Ask());

        var final = events.OfType<UsageFinal>().Single();
        Assert.Equal((6489L, 2L, 11L, 0L, (string?)null), (final.InputTokens, final.OutputTokens, final.CacheReadTokens, final.CacheCreateTokens, final.Model));
        var reading = events.OfType<UsageReading>().Single();
        Assert.Equal((6489L, 2L, 11L), (reading.InputTokens, reading.OutputTokens, reading.CacheReadTokens));
        Assert.Null(reading.CacheCreateTokens);   // cachedWriteTokens was not sent: unknown, never 0
        Assert.Equal((UsageReading.Tokens, UsageScope.Turn, clock.GetUtcNow()), (reading.Unit, reading.Scope, reading.MeasuredAt));
    }

    [Fact] // SOURCE-DERIVED: a usage_update carries context figures, not counts
    public async Task Update_kinds_the_stream_has_no_case_for_yield_no_event()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ =>
            [
                Update("user_message_chunk", """{"content":{"type":"text","text":"an echo"}}"""),
                Update("available_commands_update", """{"availableCommands":[]}"""),
                Update("current_mode_update", """{"currentModeId":"agent"}"""),
                Update("usage_update", """{"used":51200,"size":200000}"""),
                Update("agent_message_chunk", """{"content":{"type":"image","data":"AAAA","mimeType":"image/png"}}"""),
                "this line is not JSON",
                Update("agent_message_chunk", """{"content":{"type":"text","text":"done"}}"""),
                PromptEnded("end_turn"),
            ],
        });

        var events = await Run(Scripted(runner), Ask());

        Assert.Equal(["SessionStarted", "TextDelta", "SessionEnded"], events.Select(e => e.GetType().Name));
    }

    [Fact] // SOURCE-DERIVED: a tool_call that arrives already finished, and an update for a call never seen start
    public async Task A_finished_tool_is_visible_even_when_its_start_was_not_seen()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ =>
            [
                Update("tool_call", """{"toolCallId":"a","title":"Search","kind":"search","status":"completed"}"""),
                Update("tool_call_update", """{"toolCallId":"b","status":"failed"}"""),
                Update("tool_call_update", """{"toolCallId":"b","status":"failed"}"""),
                PromptEnded("end_turn"),
            ],
        });

        var events = await Run(Scripted(runner), Ask());

        Assert.Equal(["SessionStarted", "ToolCall", "ToolResult", "ToolCall", "ToolResult", "ToolResult", "SessionEnded"],
            events.Select(e => e.GetType().Name));
        Assert.Equal("tool", events.OfType<ToolCall>().Last().Name);   // no kind and no title said which
    }

    // ── permission requests and everything else the agent asks of the client ─

    [Fact] // SOURCE-DERIVED
    public async Task A_read_only_turn_rejects_every_permission_request()
    {
        var events = await Run(Stub(), Ask("PERMISSION_TURN") with { ToolPolicy = AgentToolPolicy.ReadOnly });

        Assert.Equal("permission: reject-once", Ended(events).FinalText);
    }

    [Fact] // SOURCE-DERIVED
    public async Task A_write_turn_allows_a_permission_request_once_and_never_for_good()
    {
        var events = await Run(Stub(), Ask("PERMISSION_TURN") with { ToolPolicy = AgentToolPolicy.Write });

        Assert.Equal("permission: allow-once", Ended(events).FinalText);
    }

    [Fact] // SOURCE-DERIVED
    public async Task A_permission_request_with_no_acceptable_option_is_answered_cancelled()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            [""] = _ => [PromptEnded("end_turn")],   // the client's answer to the request below
            ["session/prompt"] = _ =>
            [
                """{"jsonrpc":"2.0","id":"p1","method":"session/request_permission","params":{"sessionId":"s1","toolCall":{"toolCallId":"c"},"options":[{"optionId":"always","name":"Always","kind":"allow_always"}]}}""",
            ],
        });

        await Run(Scripted(runner), Ask() with { ToolPolicy = AgentToolPolicy.Write }).WaitAsync(TimeSpan.FromSeconds(10));

        var answer = JsonNode.Parse(runner.Exchanged.Single(l => l.Contains("\"p1\"")))!;
        Assert.Equal("p1", answer["id"]!.GetValue<string>());
        Assert.Equal("cancelled", answer["result"]!["outcome"]!["outcome"]!.GetValue<string>());
    }

    [Fact] // SOURCE-DERIVED: the client declined fs and terminal, and says so rather than leave the agent waiting
    public async Task A_request_the_client_does_not_implement_is_answered_method_not_found()
    {
        var events = await Run(Stub(), Ask("FS_TURN"));

        Assert.Equal("fs: -32601", Ended(events).FinalText);
    }

    // ── what the agent is handed ─────────────────────────────────────────────

    [Fact] // SOURCE-DERIVED: ACP's stdio McpServer shape
    public async Task A_stdio_mcp_server_is_handed_over_in_the_protocols_shape()
    {
        var log = NewLog();
        var server = AgentMcpServer.Stdio("app-tools", "app-mcp", ["--db", "x"], new Dictionary<string, string> { ["TOKEN"] = "t" });

        await Run(Stub(log: log), Ask() with { McpServers = [server] });

        var sent = Request(Sent(log), "session/new")["params"]!["mcpServers"]![0]!;
        Assert.Equal("app-tools", sent["name"]!.GetValue<string>());
        Assert.Equal("app-mcp", sent["command"]!.GetValue<string>());
        Assert.Equal(["--db", "x"], sent["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal("TOKEN", sent["env"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("t", sent["env"]![0]!["value"]!.GetValue<string>());
    }

    [Fact] // SOURCE-DERIVED: an http server needs the agent's mcpCapabilities.http
    public async Task An_http_mcp_server_is_handed_over_where_the_agent_states_http_and_refused_where_it_does_not()
    {
        var log = NewLog();
        var options = Ask() with { McpServers = [AgentMcpServer.Http("app", "https://app.test/mcp", "secret")] };

        var accepted = await Run(Stub(caps: "http", log: log), options);
        var refused = await Run(Stub(), options);

        var sent = Request(Sent(log), "session/new")["params"]!["mcpServers"]![0]!;
        Assert.Equal("http", sent["type"]!.GetValue<string>());
        Assert.Equal("Authorization", sent["headers"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Bearer secret", sent["headers"]![0]!["value"]!.GetValue<string>());
        Assert.Equal(ProviderVerdict.Ok, Ended(accepted).Verdict);
        var ended = Ended(refused);
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.Equal(AgentProvider.OptionRefusedSubtype, ended.Subtype);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
    }

    [Fact]
    public async Task An_unusable_mcp_server_is_refused_without_a_spawn()
    {
        var runner = new FakeProcessRunner();

        var events = await Run(Scripted(runner), Ask() with { McpServers = [AgentMcpServer.Stdio("bad name", "x")] });

        Assert.Equal("mcp-server-invalid", Ended(events).Subtype);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task An_account_reaches_the_child_in_the_agents_home_variable()
    {
        var runner = new FakeProcessRunner();
        var account = new AgentAccount { Provider = "acp", Name = "work", Home = "/accounts/work" };

        await Run(Scripted(runner, "AGENT_HOME"), Ask() with { Account = account });

        Assert.Equal("/accounts/work", Assert.Single(runner.Calls).Environment!["AGENT_HOME"]);
    }

    [Fact]
    public async Task An_account_is_refused_without_a_spawn_where_the_agents_home_variable_is_not_known()
    {
        var runner = new FakeProcessRunner();
        var account = new AgentAccount { Provider = "acp", Name = "work", Home = "/accounts/work" };

        var events = await Run(Scripted(runner), Ask() with { Account = account });

        Assert.Equal("account-invalid", Ended(events).Subtype);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task The_agent_is_launched_by_its_command_arguments_and_environment_in_the_working_directory()
    {
        var runner = new FakeProcessRunner();
        var session = new AcpAgentSession(runner, new LyntaiOptions(), "node \"agent dir/agent.mjs\"", ["--acp"],
            new Dictionary<string, string?> { ["KEEP"] = "1", ["DROP"] = null });

        await Run(session, Ask());

        var call = Assert.Single(runner.Calls);
        Assert.Equal("node", call.Command);
        Assert.Equal(["agent dir/agent.mjs", "--acp"], call.Args);
        Assert.Equal(TestPaths.TestScratchDir, call.WorkingDirectory);
        Assert.Equal("1", call.Environment!["KEEP"]);
        Assert.Null(call.Environment["DROP"]);
        Assert.True(call.Environment.ContainsKey("DROP"));
    }

    // ── a protocol the door does not speak ───────────────────────────────────

    [Fact]
    public async Task An_agent_answering_another_protocol_version_is_refused_before_a_session_opens()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => ["""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":2,"agentCapabilities":{}}}"""],
        });

        var events = await Run(Scripted(runner), Ask());

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.Contains("version 2", ended.Diagnostic);
        Assert.Equal(["initialize"], runner.Exchanged.Select(l => Method(JsonNode.Parse(l)!)));
    }

    [Fact] // REAL: both captures are codex-acp 2.1.1's own
    public async Task A_real_codex_acp_initialize_is_read_for_what_it_states_and_the_conversation_resumes_by_resume()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [RealCodexInitialize],
            ["session/resume"] = _ => ["""{"jsonrpc":"2.0","id":2,"result":{}}"""],
            ["session/prompt"] = _ => [PromptEnded("end_turn")],
        });

        var events = await Run(Scripted(runner), Ask() with { ResumeToken = "conv-1" });

        Assert.Equal(["initialize", "session/resume", "session/prompt"], runner.Exchanged.Select(l => Method(JsonNode.Parse(l)!)));
        Assert.Equal(ProviderVerdict.Ok, Ended(events).Verdict);
    }

    [Fact] // REAL: a logged-out codex-acp answers session/new -32000 and nothing else
    public async Task A_real_codex_acp_that_wants_a_sign_in_reads_as_an_access_failure_with_nothing_sent()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [RealCodexInitialize],
            ["session/new"] = _ => [RealCodexAuthRequired],
        });

        var events = await Run(Scripted(runner), Ask());

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.Equal("Authentication required", ended.Diagnostic);
        Assert.Equal(AgentFailureKind.Access, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32000", ended.Failure.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
        Assert.DoesNotContain(runner.Exchanged, l => Method(JsonNode.Parse(l)!) == "session/prompt");
    }

    // ── a failed turn, read from the agent's own fields ──────────────────────

    [Fact] // SOURCE-DERIVED: claude-agent-acp's errorKind is the SDK message's category
    public async Task An_error_with_data_is_read_from_its_data_field()
    {
        var events = await Run(Stub(), Ask("FORCE_ERROR"));

        var ended = Ended(events);
        Assert.True(ended.IsError);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.Equal("jsonrpc-error", ended.Subtype);
        Assert.Equal("Internal error: stub turn failure", ended.Diagnostic);
        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
        Assert.Equal("acp.data.errorKind=server_error", ended.Failure.By);
        Assert.Equal(AgentDelivery.Taken, ended.Failure.Delivery);   // the agent answered the prompt, and ran no tool
    }

    [Fact] // SOURCE-DERIVED: claude-agent-acp forwards the CLI's rejected rate_limit_info as a usage_update's _meta
    public async Task A_claude_limit_reads_as_a_limit_that_resets_when_the_agents_frame_says()
    {
        var events = await Run(Stub(), Ask("LIMIT_CLAUDE"));

        var ended = Ended(events);
        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal("acp.usage_update._claude/rateLimit.rejected", failure.By);
        Assert.Equal(new AgentRestriction(RestrictionCause.Quota, RestrictionSubject.Account,
            RestrictionRecovery.ResetAt(DateTimeOffset.FromUnixTimeSeconds(1893456000))), failure.Restriction);
    }

    [Fact] // SOURCE-DERIVED: codex-acp's limit has the message "Internal error" and the sentence in data.message
    public async Task A_codex_limit_reads_its_error_info_and_its_reset_from_the_sentence_in_data()
    {
        var events = await Run(Stub(time: FixedLocalClock.Wednesday), Ask("LIMIT_CODEX"));

        var ended = Ended(events);
        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.Equal("Internal error: You’ve hit your usage limit. Try again at 4:05 PM.", ended.Diagnostic);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal("acp.data.codexErrorInfo=usageLimitExceeded", failure.By);
        Assert.Equal(RestrictionCause.Quota, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Account, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.ResetAt, failure.Restriction.Recovery.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 16, 5, 0, TimeSpan.FromHours(11)), failure.Restriction.Recovery.At);
    }

    [Fact] // SOURCE-DERIVED: what codex-acp does when the client did not declare typed failures
    public async Task A_limit_sentence_the_agent_said_as_text_never_classifies_the_turn()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ =>
            [
                Update("agent_message_chunk", """{"content":{"type":"text","text":"You’ve hit your usage limit. Try again at 4:05 PM."}}"""),
                PromptEnded("end_turn"),
            ],
        });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.False(ended.IsError);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Null(ended.Failure);
    }

    [Fact] // SOURCE-DERIVED: codex-acp ends a failed turn end_turn, and says the failure as a field the client declared
    public async Task A_turn_the_agent_ended_end_turn_with_a_typed_failure_is_a_failed_turn()
    {
        var events = await Run(Stub(time: FixedLocalClock.Wednesday), Ask("TYPED_FAILURE"));

        var ended = Ended(events);
        Assert.True(ended.IsError);
        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.Equal("session-failure", ended.Subtype);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.StartsWith("acp.sessionFailure.category=limit", failure.By);
        Assert.Equal(RecoveryKind.ResetAt, failure.Restriction!.Recovery.Kind);
    }

    [Fact] // SOURCE-DERIVED: the AIR category "limit" also covers a spent context window
    public async Task A_typed_limit_whose_title_states_no_allowance_is_not_taken_for_one()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ =>
            [
                PromptEnded("end_turn", ""","_meta":{"jetbrains":{"air":{"version":1,"sessionFailure":{"id":"t:error","revision":1,"category":"limit","severity":"error","title":"The conversation is too long for the model.","actions":["new_session"]}}}}"""),
            ],
        });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("acp.sessionFailure.category=limit", ended.Failure.By);
    }

    [Theory] // SOURCE-DERIVED: AIR's categories, read by kind
    [InlineData("access", AgentFailureKind.Access)]
    [InlineData("connection", AgentFailureKind.Transient)]
    [InlineData("service", AgentFailureKind.Transient)]
    [InlineData("request", AgentFailureKind.Fatal)]
    [InlineData("unknown", AgentFailureKind.Fatal)]
    public async Task A_typed_failure_reads_by_its_category(string category, AgentFailureKind kind)
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ =>
            [
                PromptEnded("end_turn", ""","_meta":{"jetbrains":{"air":{"sessionFailure":{"category":"CATEGORY","severity":"error","title":"it failed"}}}}""".Replace("CATEGORY", category)),
            ],
        });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.True(ended.IsError);
        Assert.Equal(kind, ended.Failure!.Kind);
    }

    [Fact] // SOURCE-DERIVED: a retry notice is a warning, not an ending
    public async Task A_typed_warning_mid_turn_does_not_fail_the_turn()
    {
        var events = await Run(Stub(), Ask("AIR_WARNING"));

        Assert.Equal(ProviderVerdict.Ok, Ended(events).Verdict);
    }

    [Theory] // SOURCE-DERIVED: codex's error info as a word, and as an object holding an HTTP status
    [InlineData("\"unauthorized\"", AgentFailureKind.Access, "acp.data.codexErrorInfo=unauthorized")]
    [InlineData("\"rateLimitExceeded\"", AgentFailureKind.Limit, "acp.data.codexErrorInfo=rateLimitExceeded")]
    [InlineData("\"serverOverloaded\"", AgentFailureKind.Transient, "acp.data.codexErrorInfo=serverOverloaded")]
    [InlineData("\"contextWindowExceeded\"", AgentFailureKind.Fatal, "acp.data.codexErrorInfo=contextWindowExceeded")]
    [InlineData("{\"httpConnectionFailed\":{\"httpStatusCode\":null}}", AgentFailureKind.Transient, "acp.data.codexErrorInfo.httpConnectionFailed")]
    [InlineData("{\"responseStreamConnectionFailed\":{\"httpStatusCode\":429}}", AgentFailureKind.Limit, "acp.data.codexErrorInfo.responseStreamConnectionFailed")]
    [InlineData("{\"responseStreamConnectionFailed\":{\"httpStatusCode\":401}}", AgentFailureKind.Access, "acp.data.codexErrorInfo.responseStreamConnectionFailed")]
    public async Task A_codex_error_info_reads_by_its_kind(string info, AgentFailureKind kind, string by)
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ => ["""{"jsonrpc":"2.0","id":3,"error":{"code":-32603,"message":"Internal error","data":{"message":"it failed","codexErrorInfo":INFO}}}""".Replace("INFO", info)],
        });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.Equal(kind, ended.Failure!.Kind);
        Assert.Equal(by, ended.Failure.By);
    }

    [Fact] // the sentence is read only where no field said
    public async Task An_error_info_that_says_nothing_leaves_the_door_to_read_the_sentence()
    {
        var runner = Agent(new()
        {
            ["initialize"] = _ => [Initialized()],
            ["session/new"] = _ => [OpenedId(2)],
            ["session/prompt"] = _ => ["""{"jsonrpc":"2.0","id":3,"error":{"code":-32603,"message":"Internal error","data":{"message":"Selected model is at capacity. Please try a different model.","codexErrorInfo":"other"}}}"""],
        });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32603:sentence:model-capacity", ended.Failure.By);
    }

    [Fact] // SOURCE-DERIVED: a prompt answered -32000 (an expired login), after the agent had taken the session
    public async Task An_access_error_on_the_prompt_reads_as_access_with_the_prompt_taken()
    {
        var ended = Ended(await Run(Stub(), Ask("AUTH_PROMPT")));

        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.Equal(AgentFailureKind.Access, ended.Failure!.Kind);
        Assert.Equal("acp.error.code=-32000", ended.Failure.By);
        Assert.Equal(AgentDelivery.Taken, ended.Failure.Delivery);
    }

    [Fact] // REAL shape: a logged-out agent answers session/new -32000, and no session opens
    public async Task A_logged_out_agent_fails_before_a_session_opens_and_nothing_was_sent()
    {
        var log = NewLog();

        var events = await Run(Stub(loggedOut: true, log: log), Ask());

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        Assert.DoesNotContain(Sent(log), f => Method(f) == "session/prompt");
    }

    [Theory] // SOURCE-DERIVED: the protocol's stop reasons the stream has no success for
    [InlineData("MAX_TOKENS", "max_tokens", ProviderVerdict.Failed)]
    [InlineData("REFUSAL", "refusal", ProviderVerdict.Refused)]
    public async Task A_stop_reason_that_is_not_an_answer_fails_the_turn(string marker, string reason, ProviderVerdict verdict)
    {
        var ended = Ended(await Run(Stub(), Ask(marker)));

        Assert.True(ended.IsError);
        Assert.Equal(verdict, ended.Verdict);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal($"acp.stopReason={reason}", ended.Failure.By);
        Assert.Null(ended.FinalText);
    }

    // ── a process that dies ──────────────────────────────────────────────────

    [Theory] // the delivery of a turn the process died in is what the turn had shown
    [InlineData("DIE_EARLY", AgentDelivery.Taken)]
    [InlineData("DIE_MID", AgentDelivery.Taken)]
    [InlineData("DIE_AFTER_TOOL", AgentDelivery.Uncertain)]
    public async Task A_process_that_dies_mid_turn_ends_it_with_the_delivery_the_turn_showed(string marker, AgentDelivery delivery)
    {
        var ended = Ended(await Run(Stub(), Ask(marker)));

        Assert.True(ended.IsError);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("process.exit", ended.Failure.By);
        Assert.Equal(delivery, ended.Failure.Delivery);
    }

    [Fact]
    public async Task An_agent_that_exits_before_answering_initialize_ends_the_turn_never_sent()
    {
        var runner = Agent(new() { ["initialize"] = _ => null! });

        var ended = Ended(await Run(Scripted(runner), Ask()));

        Assert.True(ended.IsError);
        Assert.Equal("no-terminal-event", ended.Failure!.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
    }

[Fact]    public async Task A_runner_without_the_live_stdin_member_refuses_the_turn_rather_than_throwing()    {        var session = new AcpAgentSession(new LinesOnlyRunner(), new LyntaiOptions(), "agent");        var ended = Ended(await Run(session, Ask()));        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);        Assert.Equal("runner-unsupported", ended.Subtype);        Assert.Contains(nameof(IProcessRunner.ExchangeLinesAsync), ended.Diagnostic);        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);    }    private sealed class LinesOnlyRunner : IProcessRunner    {        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) =>            Task.FromResult(new ProcessResult(0, "", ""));        public IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,            string? stdin = null, TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null,            string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null,            CancellationToken ct = default) => AsyncEnumerable.Empty<string>();    }
    // ── cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_sends_session_cancel_and_lets_the_agent_answer_it_before_the_cancellation_propagates()
    {
        var log = NewLog();
        using var cts = new CancellationTokenSource();
        var events = new List<AgentStreamEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in Stub(log: log).StreamAsync(Ask("SLOW_TURN"), cts.Token))
            {
                events.Add(e);
                if (e is TextDelta) cts.Cancel();
            }
        });

        var cancel = Assert.Single(Sent(log), f => Method(f) == "session/cancel");
        Assert.Equal("acp-stub-session-1", cancel["params"]!["sessionId"]!.GetValue<string>());
        Assert.False(cancel.AsObject().ContainsKey("id"));   // a notification: it has no answer to wait for
        Assert.DoesNotContain(events, e => e is SessionEnded);   // the agent's cancelled answer is not handed over as a result
    }

    [Fact]
    public async Task An_agent_that_ignores_cancel_is_killed_when_its_grace_runs_out()
    {
        var log = NewLog();
        using var cts = new CancellationTokenSource();
        var started = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in Stub(log: log, grace: TimeSpan.FromMilliseconds(400)).StreamAsync(Ask("IGNORE_CANCEL"), cts.Token))
            {
                if (e is TextDelta) cts.Cancel();
            }
        });

        Assert.Contains(Sent(log), f => Method(f) == "session/cancel");
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Cancelling_before_a_prompt_is_in_flight_ends_the_process_without_a_cancel_frame()
    {
        // the agent answers initialize and then says nothing, so the turn is still opening its session
        var runner = Agent(new() { ["initialize"] = _ => [Initialized()] });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Scripted(runner).StreamAsync(Ask(), cts.Token)) { }
        });

        Assert.DoesNotContain(runner.Exchanged, l => Method(JsonNode.Parse(l)!) == "session/cancel");
    }

    // ── helpers over the wire ────────────────────────────────────────────────

    private static string Initialized(string capabilities = """{"loadSession":true,"sessionCapabilities":{"resume":{}}}""") =>
        $$$"""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentCapabilities":{{{capabilities}}},"authMethods":[]}}""";

    private static string OpenedId(int id) => $$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{"sessionId":"s1"}}""";

    private static string Update(string kind, string body) =>
        """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s1","update":{"sessionUpdate":"KIND",BODY}}}""".Replace("KIND", kind).Replace("BODY", body[1..^1]);

    private static string PromptEnded(string stopReason, string extra = "") =>
        $$$"""{"jsonrpc":"2.0","id":3,"result":{"stopReason":"{{{stopReason}}}"{{{extra}}}}}""";
}

/// <summary>The stand-in suite starts a node process per turn, dozens in a burst, and runs alone for that reason:
/// beside it, a test that times a 250 ms window on the thread pool fails for the load, not for its code.</summary>
[CollectionDefinition(Collection, DisableParallelization = true)]
public sealed class AcpStandIn
{
    public const string Collection = "acp-stand-in";
}
