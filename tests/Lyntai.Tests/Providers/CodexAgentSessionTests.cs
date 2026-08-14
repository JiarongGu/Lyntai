using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Providers;

/// <summary>
/// The `codex` agent session — the second implementer of <see cref="IAgentSession"/>, and therefore the test
/// that the agent-session shape is genuinely neutral rather than claude-shaped.
///
/// <para>Every assertion is labelled MEASURED or INFERRED, because the two halves of this mapping have very
/// different standing. MEASURED = the shape appears in the codex-cli 0.146.0 capture recorded in
/// <c>CodexJsonlParser</c>'s docblock (one real successful turn via the <c>--oss</c> local-model path plus one
/// real failed turn) or in its <c>--help</c>. INFERRED = the shape has NOT been observed here; the reader's
/// tool-step mapping is deliberately written so that an inferred detail being wrong costs exactly two things
/// and no more — no payload is invented or dropped, and the uncertainty stays inside the tool-step half. It
/// does NOT guarantee the right KIND of event (the tool arm is reached by elimination), so a tool step's kind
/// is provisional and only its payload is reliable — see <c>docs/DECISIONS.md</c> D35.</para>
///
/// Driven through <see cref="FakeProcessRunner"/> and the codex-shaped stub — never a real binary.
/// </summary>
public class CodexAgentSessionTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>MEASURED: verbatim from the recorded codex-cli 0.146.0 successful turn.</summary>
    private static readonly string[] MeasuredSuccess =
    [
        """{"type":"thread.started","thread_id":"019fc935-704c-75b2-a660-a85c89a67514"}""",
        """{"type":"turn.started"}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}""",
        """{"type":"turn.completed","usage":{"input_tokens":6489,"cached_input_tokens":11,"cache_write_input_tokens":7,"output_tokens":2,"reasoning_output_tokens":0}}""",
    ];

    /// <summary>MEASURED: the two error-ish lines that appeared in the run which went on to SUCCEED. The
    /// consuming app's hand-rolled parser failed the turn on the bare `error` line — defect #1.</summary>
    private static readonly string[] MeasuredNoisyButSuccessful =
    [
        """{"type":"thread.started","thread_id":"thread-noisy"}""",
        """{"type":"error","message":"Reconnecting... 2/5 (unexpected status 401 Unauthorized …)"}""",
        """{"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata … not found"}}""",
        """{"type":"turn.started"}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}""",
        """{"type":"turn.completed","usage":{"input_tokens":1,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0}}""",
    ];

    private static CodexAgentSession Session(FakeProcessRunner runner, ILogger<CodexAgentSession>? logger = null) =>
        new(runner, new LyntaiOptions(), logger, command: "codex");

    private static CodexAgentSession StubSession() =>
        new(new ProcessRunner(), new LyntaiOptions(), command: CodexCliProviderTests.StubCommand);

    private static AgentSessionOptions Ask(string prompt = "do the thing") =>
        new() { Prompt = prompt, WorkingDirectory = "/project/root" };

    // ── argv: the measured invocation, shared with the provider path ─────────

    [Fact] // MEASURED
    public async Task An_agent_turn_runs_exec_with_json_events_and_the_prompt_on_stdin()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask("say ok")).ToListAsync();

        Assert.Equal(["exec", "--json", "--skip-git-repo-check", "--color", "never", "--sandbox", "read-only", "-"],
            runner.LastArgs);
        Assert.Equal("say ok", runner.LastStdin);
    }

    [Fact] // MEASURED — defect #2: works in a dev git repo, breaks in a shipped bundle
    public async Task The_git_repo_check_is_skipped_on_the_agent_path_exactly_as_on_the_provider_path()
    {
        // codex refuses to run outside a git repository. The session runs in the CALLER's working directory,
        // which is a repo on a developer's machine and very often is not one in a shipped app — so the flag
        // is required here too. Both paths build their argv from the same source, so it cannot be dropped
        // from one of them.
        var sessionRunner = new FakeProcessRunner(MeasuredSuccess);
        await Session(sessionRunner).StreamAsync(Ask()).ToListAsync();

        var providerRunner = new FakeProcessRunner { RunResult = new ProcessResult(0, "", "") };
        await new CodexCliProvider(providerRunner, new LyntaiOptions(), command: "codex")
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Contains("--skip-git-repo-check", sessionRunner.LastArgs!);
        Assert.Contains("--skip-git-repo-check", providerRunner.LastArgs!);
    }

    [Fact] // MEASURED (`--sandbox` values come from `codex exec --help`); the POLICY mapping is Lyntai's
    public async Task A_write_policy_raises_the_sandbox_to_workspace_write()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask() with { ToolPolicy = AgentToolPolicy.Write }).ToListAsync();

        var args = runner.LastArgs!.ToList();
        Assert.Equal("workspace-write", args[args.IndexOf("--sandbox") + 1]);
    }

    [Fact] // MEASURED
    public async Task A_read_only_policy_keeps_the_read_only_sandbox()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask() with { ToolPolicy = AgentToolPolicy.ReadOnly }).ToListAsync();

        var args = runner.LastArgs!.ToList();
        Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
    }

    [Fact]
    public async Task An_explicit_sandbox_mode_wins_over_the_policy_mapping()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var options = new CodexAgentOptions
        {
            Prompt = "hi",
            ToolPolicy = AgentToolPolicy.ReadOnly,
            SandboxMode = "danger-full-access",
        };

        await Session(runner).StreamAsync(options).ToListAsync();

        var args = runner.LastArgs!.ToList();
        Assert.Equal("danger-full-access", args[args.IndexOf("--sandbox") + 1]);
    }

    [Fact] // MEASURED
    public async Task The_model_is_forwarded()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask() with { Model = "gpt-5-codex" }).ToListAsync();

        var args = runner.LastArgs!.ToList();
        Assert.Equal("gpt-5-codex", args[args.IndexOf("--model") + 1]);
    }

    [Fact]
    public async Task The_prompt_never_appears_in_argv()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask("my secret prompt text")).ToListAsync();

        Assert.DoesNotContain("my secret prompt text", runner.LastArgs!);
    }

    [Fact] // codex exec has no measured append-system-prompt flag, so it travels IN the prompt
    public async Task A_system_prompt_is_prepended_to_the_stdin_prompt()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask("what now?") with { SystemPrompt = "Be concise." }).ToListAsync();

        Assert.Equal("Be concise.\n\nwhat now?", runner.LastStdin);
        Assert.DoesNotContain("--append-system-prompt", runner.LastArgs!);
    }

    [Fact]
    public async Task The_session_runs_in_the_callers_working_directory_not_the_neutral_temp_dir()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Equal("/project/root", runner.LastWorkingDirectory);
        Assert.NotEqual(CliProviderEngine.NeutralWorkingDirectory, runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task A_portable_installs_environment_reaches_the_spawn()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var session = new CodexAgentSession(runner, new LyntaiOptions(), command: "codex",
            environment: new Dictionary<string, string> { ["CODEX_HOME"] = "portable/home" });

        await session.StreamAsync(Ask()).ToListAsync();

        Assert.Equal("portable/home", runner.LastEnvironment?["CODEX_HOME"]);
    }

    // ── the measured JSONL → AgentStreamEvent mapping ────────────────────────

    [Fact] // MEASURED
    public async Task A_measured_successful_turn_yields_started_text_usage_and_an_ok_terminal()
    {
        var events = await Session(new FakeProcessRunner(MeasuredSuccess)).StreamAsync(Ask()).ToListAsync();

        Assert.Contains(events, e => e is SessionStarted { SessionId: "019fc935-704c-75b2-a660-a85c89a67514" });
        Assert.Contains(events, e => e is TextDelta { Text: "ok" });
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("ok", ended.FinalText);
        Assert.Equal("019fc935-704c-75b2-a660-a85c89a67514", ended.SessionId);
    }

    [Fact] // MEASURED — defect #1: a bare `error` line failed a turn that had SUCCEEDED
    public async Task Non_terminal_noise_does_not_fail_a_turn_that_succeeded()
    {
        var events = await Session(new FakeProcessRunner(MeasuredNoisyButSuccessful)).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("ok", ended.FinalText);
    }

    [Fact] // MEASURED — turn.failed is the only terminal failure, and its wording is classified
    public async Task A_turn_failure_is_terminal_and_classified()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"thread-fail"}""",
            """{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer"}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.True(ended.IsError);
        Assert.Equal(LlmVerdict.AuthFailed, ended.Verdict);   // not a bare Failed
        Assert.Contains("401", ended.Diagnostic);
        Assert.Equal("thread-fail", ended.SessionId);
    }

    [Fact] // MEASURED usage fields; the absent model id is a measured ABSENCE, never guessed
    public async Task The_final_usage_carries_the_measured_cache_fields_and_no_model()
    {
        var events = await Session(new FakeProcessRunner(MeasuredSuccess)).StreamAsync(Ask()).ToListAsync();

        var usage = events.OfType<UsageFinal>().Single();
        Assert.Equal(6489, usage.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
        Assert.Equal(11, usage.CacheReadTokens);     // cached_input_tokens
        Assert.Equal(7, usage.CacheCreateTokens);    // cache_write_input_tokens
        Assert.Null(usage.Model);                    // codex's thread events carry no model id
    }

    [Fact] // codex has no per-turn usage tick — the absence is measured, not an omission
    public async Task No_live_usage_ticks_are_invented()
    {
        var events = await Session(new FakeProcessRunner(MeasuredSuccess)).StreamAsync(Ask()).ToListAsync();

        Assert.DoesNotContain(events, e => e is UsageLive);
    }

    [Fact]
    public async Task RunAsync_folds_to_the_agent_message_text()
    {
        var result = await Session(new FakeProcessRunner(MeasuredSuccess)).RunAsync(Ask());

        Assert.Equal("ok", result.FinalText);
        Assert.Equal("019fc935-704c-75b2-a660-a85c89a67514", result.SessionId);
        Assert.Equal(LlmVerdict.Ok, result.Verdict);
        Assert.Equal(6489, result.Usage?.InputTokens);
    }

    [Fact] // the codex PROVIDER concatenates every agent_message; the session must agree
    public async Task Multiple_agent_messages_concatenate_into_the_final_text()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"one "}}""",
            """{"type":"item.completed","item":{"id":"b","type":"agent_message","text":"two"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Equal(2, events.OfType<TextDelta>().Count());
        Assert.Equal("one two", events.OfType<SessionEnded>().Single().FinalText);
    }

    // ── the INFERRED tool-step mapping ───────────────────────────────────────

    [Fact] // INFERRED: item.started has NOT been observed here
    public async Task A_started_then_completed_tool_item_becomes_a_correlated_call_and_result()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.started","item":{"id":"item_3","type":"command_execution","command":"ls -la"}}""",
            """{"type":"item.completed","item":{"id":"item_3","type":"command_execution","command":"ls -la","exit_code":0,"aggregated_output":"a\nb"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var call = events.OfType<ToolCall>().Single();
        Assert.Equal("command_execution", call.Name);       // codex's OWN item type, never a Lyntai-invented name
        Assert.Equal("item_3", call.CallId);
        var result = events.OfType<ToolResult>().Single();
        Assert.Equal("item_3", result.CallId);              // correlated by the item id
        Assert.False(result.IsError);
    }

    [Fact] // INFERRED: if item.started turns out not to exist, the step must still be VISIBLE
    public async Task A_tool_item_seen_only_at_completion_still_yields_a_call_so_the_step_is_visible()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"id":"item_4","type":"mcp_tool_call","server":"fs","tool":"read"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Single(events.OfType<ToolCall>());     // synthesised from the completion
        Assert.Single(events.OfType<ToolResult>());
        Assert.Equal("mcp_tool_call", events.OfType<ToolCall>().Single().Name);
    }

    [Theory] // INFERRED: the failure signals a tool item might carry
    [InlineData("""{"id":"i","type":"command_execution","exit_code":2}""", true)]
    [InlineData("""{"id":"i","type":"command_execution","status":"failed"}""", true)]
    [InlineData("""{"id":"i","type":"command_execution","exit_code":0}""", false)]
    [InlineData("""{"id":"i","type":"command_execution"}""", false)]
    public async Task A_failed_tool_item_is_reported_as_an_error_result(string item, bool expectedIsError)
    {
        var runner = new FakeProcessRunner([
            $$"""{"type":"item.completed","item":{{item}}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Equal(expectedIsError, events.OfType<ToolResult>().Single().IsError);
    }

    [Fact] // INFERRED: the item type name AND its text field
    public async Task A_reasoning_item_becomes_thinking_not_a_tool_step()
    {
        var runner = new FakeProcessRunner([
            """{"type":"item.completed","item":{"id":"r","type":"reasoning","text":"thinking out loud"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Equal("thinking out loud", events.OfType<Thinking>().Single().Text);
        Assert.Empty(events.OfType<ToolCall>());
    }

    [Fact] // the payload is codex's own object verbatim — Lyntai invents no argument schema
    public async Task Tool_arguments_and_results_are_codexs_own_item_json_verbatim()
    {
        var runner = new FakeProcessRunner([
            """{"type":"item.completed","item":{"id":"i","type":"web_search","query":"lyntai"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Contains("\"query\":\"lyntai\"", events.OfType<ToolCall>().Single().ArgumentsJson);
        Assert.Contains("\"query\":\"lyntai\"", events.OfType<ToolResult>().Single().Content);
    }

    [Fact] // an unknown envelope type must be ignored, never fail a turn
    public async Task An_unknown_envelope_line_is_ignored()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.updated","item":{"id":"x","type":"agent_message","text":"partial"}}""",
            """{"type":"some.future.event","payload":{}}""",
            "2026-08-03T19:57:43.038887Z ERROR codex_api::endpoint: failed to connect",
            "{ not json",
            """{"type":"item.completed","item":{"id":"x","type":"agent_message","text":"final"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.Equal("final", ended.FinalText);          // the partial update was NOT double-counted
        Assert.Single(events.OfType<TextDelta>());
    }

    // ── resume, and the one refusal it leaves ────────────────────────────────

    [Fact] // MEASURED: `codex exec resume --help` on codex-cli 0.146.0 (2026-08-05)
    public async Task A_resume_token_continues_the_named_thread_instead_of_starting_a_fresh_one()
    {
        // `Usage: codex exec resume [OPTIONS] [SESSION_ID] [PROMPT]` — `resume` is a SUBCOMMAND of `exec`, the
        // session id is the FIRST positional and the `-` stdin marker is the SECOND. That ORDER is what the
        // measurement bought: an id that landed in the prompt slot would be answered as a question, which on
        // this CLI costs a turn instead of an error. Options precede both positionals, as they do on the
        // measured fresh invocation and in the CLI's own usage line.
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner)
            .StreamAsync(Ask("carry on") with { ResumeToken = "019fc935-704c-75b2-a660-a85c89a67514" })
            .ToListAsync();

        Assert.Equal([
            "exec", "resume", "--json", "--skip-git-repo-check", "--color", "never", "--sandbox", "read-only",
            "019fc935-704c-75b2-a660-a85c89a67514", "-",
        ], runner.LastArgs);
        Assert.Equal("carry on", runner.LastStdin);   // the prompt still travels on stdin, never argv
    }

    [Fact] // the resume path is a THIRD chance to lose the flag whose absence only breaks shipped apps
    public async Task A_resumed_turn_carries_exactly_the_same_flags_as_a_fresh_one()
    {
        var fresh = new FakeProcessRunner(MeasuredSuccess);
        var resumed = new FakeProcessRunner(MeasuredSuccess);
        var ask = Ask() with { ToolPolicy = AgentToolPolicy.Write, Model = "gpt-5-codex" };

        await Session(fresh).StreamAsync(ask).ToListAsync();
        await Session(resumed).StreamAsync(ask with { ResumeToken = "t-9" }).ToListAsync();

        // Both argvs come from CodexExecArgs, so the resumed one IS the fresh one plus two tokens: the
        // subcommand after `exec`, and the [SESSION_ID] positional before [PROMPT]. Sandbox, model and
        // --skip-git-repo-check therefore cannot be present on one path and missing from the other.
        var expected = fresh.LastArgs!.ToList();
        expected.Insert(1, "resume");
        expected.Insert(expected.Count - 1, "t-9");
        Assert.Equal(expected, resumed.LastArgs!.ToList());
        Assert.Contains("--skip-git-repo-check", resumed.LastArgs!);
    }

    [Fact] // resume changes the argv and nothing else: the JSONL half is untouched
    public async Task A_resumed_turn_reports_the_session_id_codex_announced_not_the_token_it_was_given()
    {
        var events = await Session(new FakeProcessRunner(MeasuredSuccess))
            .StreamAsync(Ask() with { ResumeToken = "an-older-thread" }).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.Equal("ok", ended.FinalText);
        // the id is read from codex's own thread.started; echoing the requested token back would report a
        // REQUEST as an observation (and codex may fork a resumed thread onto a new id).
        Assert.Equal("019fc935-704c-75b2-a660-a85c89a67514", ended.SessionId);
    }

    [Theory]
    [InlineData("--last")]   // codex's OWN resume flag — forwarded, it silently resumes the WRONG thread
    [InlineData("-i")]       // an option that takes a value would swallow the next argument
    [InlineData("   ")]      // blank: neither a session id nor an honest "start fresh"
    public async Task A_resume_token_the_cli_would_read_as_an_option_is_refused_without_spawning(string token)
    {
        // The token is free-form and opaque to Lyntai, and ArgumentList stops SHELL injection, not codex's own
        // parser reading a data slot as an option. Refusing costs nothing; forwarding resumes someone else's
        // thread and looks like it worked.
        var runner = new FakeProcessRunner(MeasuredSuccess);

        var events = await Session(runner).StreamAsync(Ask() with { ResumeToken = token }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.True(ended.IsError);
        Assert.Equal(LlmVerdict.Unsupported, ended.Verdict);
        Assert.Equal("resume-token-invalid", ended.Subtype);   // the TOKEN is bad, not the capability
        Assert.Contains("session id", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);   // never spawned: no turn is billed to find out
    }

    [Fact]
    public async Task Disallowed_tools_are_reported_as_unhonoured_rather_than_dropped_silently()
    {
        var logs = new List<string>();
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner, new CapturingLogger<CodexAgentSession>(logs))
            .StreamAsync(Ask() with { DisallowedTools = ["Bash"] }).ToListAsync();

        Assert.Contains(logs, l => l.Contains("DisallowedTools", StringComparison.Ordinal));
        Assert.DoesNotContain("--disallowed-tools", runner.LastArgs!);   // codex has no such flag to invent
    }

    // ── the host application's own MCP servers (CLI14) ───────────────────────

    /// <summary>MEASURED turn-free against codex-cli 0.146.0 (2026-08-05): `codex exec --help` documents
    /// `-c, --config &lt;key=value&gt;` with a dotted path and a TOML value, and driving `codex mcp list` /
    /// `codex mcp get` — which READ configuration and spend no turn — with these exact overrides reported
    /// back the registered server, its command, its args, its env and (for HTTP) its url and
    /// bearer_token_env_var.</summary>
    private static AgentMcpServer Stdio() => AgentMcpServer.Stdio(
        "app-tools", @"C:\Program Files\app\mcp.exe", ["--serve"],
        new Dictionary<string, string> { ["APP_WORKSPACE"] = "/w" });

    [Fact]
    public async Task App_mcp_servers_are_rendered_as_measured_config_overrides()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask() with { McpServers = [Stdio()] }).ToListAsync();

        var argv = runner.LastArgs!;
        // the VALUE is TOML, so backslashes are escaped: codex's documented fallback for a value that fails
        // to parse as TOML is to use the raw string, which would change the path SILENTLY rather than fail
        Assert.Contains(@"mcp_servers.app-tools.command=""C:\\Program Files\\app\\mcp.exe""", argv);
        Assert.Contains(@"mcp_servers.app-tools.args=[""--serve""]", argv);
        Assert.Contains(@"mcp_servers.app-tools.env={ ""APP_WORKSPACE"" = ""/w"" }", argv);
        Assert.Equal(3, argv.Count(a => a == "-c"));
    }

    [Fact]
    public async Task Every_config_override_is_an_OPTION_and_precedes_the_stdin_positional()
    {
        // on this CLI a flag that lands after the `-` is read as PROMPT text, and a swallowed flag is a
        // SPENT TURN rather than an error — which is why the overrides are passed into CodexExecArgs
        // rather than appended to the argv it returns
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask() with { McpServers = [Stdio()] }).ToListAsync();

        var argv = runner.LastArgs!.ToList();
        var stdinMarker = argv.LastIndexOf("-");
        Assert.Equal(argv.Count - 1, stdinMarker);
        for (var i = 0; i < argv.Count; i++)
        {
            if (argv[i] == "-c") Assert.True(i < stdinMarker, $"the -c at {i} must precede the `-` at {stdinMarker}");
        }
    }

    [Fact]
    public async Task A_resumed_turn_carries_the_SAME_mcp_overrides_as_a_fresh_one()
    {
        // the reason the overrides go through CodexExecArgs: a second copy of the argv is a second chance to
        // lose a flag, and an agent that has its tools on turn 1 and not on turn 2 is the worst version
        var fresh = new FakeProcessRunner(MeasuredSuccess);
        var resumed = new FakeProcessRunner(MeasuredSuccess);
        var options = Ask() with { McpServers = [Stdio()] };

        await Session(fresh).StreamAsync(options).ToListAsync();
        await Session(resumed).StreamAsync(options with { ResumeToken = "019fc935-704c-75b2-a660-a85c89a67514" })
            .ToListAsync();

        var overridesOf = (IReadOnlyList<string> argv) => argv.Where(a => a.StartsWith("mcp_servers.", StringComparison.Ordinal));
        Assert.Equal(overridesOf(fresh.LastArgs!), overridesOf(resumed.LastArgs!));
    }

    [Fact]
    public async Task An_http_servers_bearer_token_travels_in_the_ENVIRONMENT_never_in_argv()
    {
        // MEASURED: codex accepts only `bearer_token_env_var` — the NAME of a variable — and never a literal
        // token. That is the shape to want anyway: argv is readable by any process that can list processes.
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var server = AgentMcpServer.Http("remote", "https://tools.example.invalid/mcp", "s3cret-token");

        await Session(runner).StreamAsync(Ask() with { McpServers = [server] }).ToListAsync();

        Assert.Contains(@"mcp_servers.remote.url=""https://tools.example.invalid/mcp""", runner.LastArgs!);
        Assert.Contains(@"mcp_servers.remote.bearer_token_env_var=""LYNTAI_MCP_BEARER_REMOTE""", runner.LastArgs!);
        Assert.DoesNotContain(runner.LastArgs!, a => a.Contains("s3cret-token", StringComparison.Ordinal));
        Assert.Equal("s3cret-token", runner.LastEnvironment?["LYNTAI_MCP_BEARER_REMOTE"]);
    }

    [Fact]
    public async Task The_turns_bearer_variables_are_MERGED_with_the_installs_own_environment()
    {
        // a portable install passes its own CODEX_HOME at construction; adding a per-turn variable must not
        // replace it, or the session would silently start reading the machine-wide install's credentials
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var session = new CodexAgentSession(runner, new LyntaiOptions(), command: "codex",
            environment: new Dictionary<string, string> { ["CODEX_HOME"] = "portable/home" });

        await session.StreamAsync(Ask() with
        {
            McpServers = [AgentMcpServer.Http("remote", "https://tools.example.invalid/mcp", "tok")],
        }).ToListAsync();

        Assert.Equal("portable/home", runner.LastEnvironment?["CODEX_HOME"]);
        Assert.Equal("tok", runner.LastEnvironment?["LYNTAI_MCP_BEARER_REMOTE"]);
    }

    [Fact]
    public async Task No_app_servers_means_no_overrides_and_no_environment_of_its_own()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);

        await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.DoesNotContain("-c", runner.LastArgs!);
        Assert.Null(runner.LastEnvironment);   // the ctor's dictionary is passed through untouched (here, none)
    }

    [Theory]
    [InlineData("has.dot")]        // a dot opens a nested TOML table — a DIFFERENT server than the one named
    [InlineData("has space")]
    [InlineData("has\"quote")]
    [InlineData("")]
    public async Task A_server_name_the_backend_would_misread_refuses_the_turn_without_spawning(string name)
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var server = AgentMcpServer.Stdio(name, "mcp.exe");

        var events = await Session(runner).StreamAsync(Ask() with { McpServers = [server] }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(LlmVerdict.Unsupported, ended.Verdict);
        Assert.Equal("mcp-server-invalid", ended.Subtype);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_server_that_cannot_be_rendered_refuses_rather_than_running_without_its_tools()
    {
        // the failure this whole seam exists to prevent: an agent that starts, answers, and simply cannot do
        // the thing it was embedded to do — with no error anywhere
        var runner = new FakeProcessRunner(MeasuredSuccess);
        var noCommand = new AgentMcpServer { Name = "app", Transport = McpTransport.Stdio };

        var events = await Session(runner).StreamAsync(Ask() with { McpServers = [noCommand] }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal("mcp-server-invalid", ended.Subtype);
        Assert.Contains("Command", ended.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Two_servers_under_one_name_refuse_rather_than_silently_collapsing()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess);
        AgentMcpServer[] servers = [AgentMcpServer.Stdio("app", "a.exe"), AgentMcpServer.Stdio("APP", "b.exe")];

        var events = await Session(runner).StreamAsync(Ask() with { McpServers = servers }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal("mcp-server-invalid", ended.Subtype);
        Assert.Empty(runner.Calls);
    }

    // ── process faults (the shared guarded-stream contract) ──────────────────

    [Fact]
    public async Task A_process_failure_yields_an_error_terminal()
    {
        var runner = new FakeProcessRunner(throwsAfterLines: new ProcessRunException("codex", 1, "boom stderr"));

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.True(ended.IsError);
        Assert.Contains("boom stderr", ended.Diagnostic);
    }

    [Fact]
    public async Task A_timeout_yields_a_timeout_terminal()
    {
        var runner = new FakeProcessRunner(throwsAfterLines: new ProcessTimeoutException("codex", TimeSpan.FromSeconds(30)));

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Timeout, ended.Verdict);
        Assert.True(ended.IsError);
    }

    [Fact]
    public async Task No_output_yields_a_failed_terminal()
    {
        var events = await Session(new FakeProcessRunner([])).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Failed, ended.Verdict);
        Assert.Contains("no output", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // "printed nothing" and "answered then died" are different bugs — say which one happened
    public async Task Content_without_a_terminal_reports_an_unterminated_turn_not_no_output()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"half an answer"}}""",
            // …and the process ends here: no turn.completed, no turn.failed
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Failed, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Contains("never terminated", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no output produced", ended.Diagnostic);
        // and the partial text is NOT dressed up as an answer (the fold's documented invariant)
        Assert.Null(ended.FinalText);
    }

    [Fact] // the IN-BAND double terminal: codex reports failure at exit 0, so two terminals are printable
    public async Task A_second_in_band_terminal_never_adds_a_second_ending()
    {
        var runner = new FakeProcessRunner([
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"done"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
            // anything after the terminal may add events but must never re-end the session
            """{"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized"}}""",
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);   // the FIRST terminal wins
        Assert.False(ended.IsError);
        Assert.Equal("done", ended.FinalText);
    }

    [Fact]
    public async Task The_readers_terminal_wins_and_is_never_duplicated_by_a_later_process_fault()
    {
        var runner = new FakeProcessRunner(MeasuredSuccess, new ProcessRunException("codex", 1, "boom"));

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var terminals = events.OfType<SessionEnded>().ToList();
        Assert.Single(terminals);
        Assert.Equal(LlmVerdict.Ok, terminals[0].Verdict);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_a_fabricated_terminal()
    {
        using var cts = new CancellationTokenSource();
        var received = new List<AgentStreamEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in Session(new FakeProcessRunner(MeasuredSuccess)).StreamAsync(Ask(), cts.Token))
            {
                received.Add(e);
                cts.Cancel();
            }
        });

        Assert.NotEmpty(received);
        Assert.DoesNotContain(received, e => e is SessionEnded);
    }

    // ── real spawns, against the codex-shaped stub ───────────────────────────

    [Fact]
    public async Task An_agent_turn_against_the_stub_round_trips_over_a_real_spawn()
    {
        var result = await StubSession().RunAsync(new AgentSessionOptions { Prompt = "hello codex" });

        Assert.Equal(LlmVerdict.Ok, result.Verdict);
        Assert.Equal("codex stub reply: hello codex", result.FinalText);
        Assert.Equal(6489, result.Usage?.InputTokens);
        Assert.Equal(12, result.Usage?.CacheReadTokens);
        Assert.NotNull(result.SessionId);
    }

    [Fact]
    public async Task An_in_band_stub_failure_at_exit_zero_is_classified_over_a_real_spawn()
    {
        var result = await StubSession().RunAsync(new AgentSessionOptions { Prompt = "AUTH_ERROR" });

        Assert.Equal(LlmVerdict.AuthFailed, result.Verdict);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Stub_noise_does_not_fail_the_run_over_a_real_spawn()
    {
        var result = await StubSession().RunAsync(new AgentSessionOptions { Prompt = "NOISY please answer" });

        Assert.Equal(LlmVerdict.Ok, result.Verdict);
        Assert.Contains("codex stub reply", result.FinalText);
    }

    // ── DI wiring ────────────────────────────────────────────────────────────

    [Fact]
    public void The_builder_extension_registers_it_as_the_agent_session()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        services.AddLyntai(b => b.AddCodexCliAgentSession());
        using var sp = services.BuildServiceProvider();

        Assert.IsType<CodexAgentSession>(sp.GetRequiredService<IAgentSession>());
    }

    [Fact] // a chat UI that offers BOTH backends must be able to reach either one
    public void Both_cli_agent_sessions_coexist_keyed_by_provider_id()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        services.AddLyntai(b => b.AddClaudeCliAgentSession().AddCodexCliAgentSession());
        using var sp = services.BuildServiceProvider();

        Assert.IsType<ClaudeAgentSession>(sp.GetRequiredKeyedService<IAgentSession>(ClaudeCliProvider.ProviderId));
        Assert.IsType<CodexAgentSession>(sp.GetRequiredKeyedService<IAgentSession>(CodexCliProvider.ProviderId));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (level >= LogLevel.Warning) sink.Add(fmt(state, ex));
        }
    }
}
