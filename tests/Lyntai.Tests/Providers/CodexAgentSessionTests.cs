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
/// tool-step mapping is deliberately written so that an inferred detail being wrong degrades (fewer events)
/// rather than lies (a wrong event).</para>
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

    // ── the honest refusals ──────────────────────────────────────────────────

    [Fact]
    public async Task A_resume_token_is_refused_without_spawning()
    {
        // codex's resume shape is UNMEASURED here, and `codex [OPTIONS] [PROMPT]` reads an unrecognized
        // subcommand as a PROMPT — a guessed `exec resume <id>` would silently spend a turn answering the
        // thread id. Ignoring the token instead would silently start a FRESH session and lose the history.
        var runner = new FakeProcessRunner(MeasuredSuccess);

        var events = await Session(runner).StreamAsync(Ask() with { ResumeToken = "thread-123" }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.True(ended.IsError);
        Assert.Equal(LlmVerdict.Unsupported, ended.Verdict);
        Assert.Contains("resume", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);   // never spawned
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
