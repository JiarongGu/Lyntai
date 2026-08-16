using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>Tests for ClaudeAgentArgs, ClaudeAgentSession, ClaudeToolCalls, and the DI registration.
/// The process seam is the shared <see cref="FakeProcessRunner"/> (scripted stream lines, yield-then-throw).</summary>
public class ClaudeAgentSessionTests
{
    // ── Shared stream-json fixture ────────────────────────────────────────────

    private static readonly string[] FullTranscript =
    [
        """{"type":"system","session_id":"sess-abc123","model":"claude-opus-4-5"}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"call-1","name":"Read","input":{"file_path":"/x/a.txt"}}],"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0}}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"call-1","content":"file content here","is_error":false}]}}""",
        """{"type":"result","result":"Done","session_id":"sess-abc123","is_error":false,"usage":{"input_tokens":20,"output_tokens":8,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"subtype":null}""",
    ];

    // ── ClaudeAgentArgs tests ─────────────────────────────────────────────────

    /// <summary>Build the argv with a RECORDING temp-file writer, so a test can assert what would have been
    /// written to the <c>--mcp-config</c> document without touching the disk — the same shape
    /// <see cref="Tools.ClaudeCliMcpDialectTests"/> uses for the tool-host path. The returned path is
    /// deterministic so argv assertions can name it.</summary>
    private static IReadOnlyList<string> Args(
        AgentSessionOptions options, List<(string Kind, string Content)>? written = null) =>
        ClaudeAgentArgs.Build(options, (kind, content) =>
        {
            written?.Add((kind, content));
            return $"<temp:{kind}>";
        });

    [Fact]
    public void Build_always_includes_base_flags()
    {
        var opts = new AgentSessionOptions { Prompt = "hi" };
        var argv = Args(opts);

        Assert.Contains("-p", argv);
        Assert.Contains("--output-format", argv);
        Assert.Contains("stream-json", argv);
        Assert.Contains("--verbose", argv);
        Assert.Contains("--include-partial-messages", argv);
    }

    [Fact]
    public void Build_readonly_policy_includes_edit_write_notebookedit_in_disallowed()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.ReadOnly };
        var argv = Args(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "--disallowed-tools flag expected");
        // Split on the comma and compare whole NAMES. Asserting `Contains("Edit", dtVal)` against the joined
        // string is satisfied by "NotebookEdit", so dropping "Edit" from ReadOnlyDenied left this test green
        // while a ReadOnly agent could edit the caller's disk (found 2026-08-14 by the whole-codebase review).
        var denied = argv[dtIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("Edit", denied);
        Assert.Contains("Write", denied);
        Assert.Contains("NotebookEdit", denied);
        Assert.DoesNotContain("--permission-mode", argv);
    }

    [Fact]
    public void Build_write_policy_emits_acceptEdits_and_excludes_edit_write_from_disallowed()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write };
        var argv = Args(opts).ToList();

        Assert.Contains("--permission-mode", argv);
        var pmIdx = argv.IndexOf("--permission-mode");
        Assert.Equal("acceptEdits", argv[pmIdx + 1]);

        // disallowed-tools IS present under Write (the always-denied flow tools go there — see the sibling
        // fact below), so this is asserted rather than guarded by an `if`: wrapped in a conditional, the only
        // check that ReadOnlyDenied is NOT applied here would silently stop running the day AlwaysDenied
        // emptied. Whole-name comparison for the same reason as the ReadOnly fact above — a substring
        // `DoesNotContain("Edit", …)` fails spuriously the moment any denied tool contains "Edit".
        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "--disallowed-tools carries the always-denied flow tools under Write");
        var denied = argv[dtIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.DoesNotContain("Edit", denied);
        Assert.DoesNotContain("Write", denied);
    }

    [Fact]
    public void Build_always_denied_flow_tools_are_in_disallowed_for_write_policy()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write };
        var argv = Args(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "headless flow tools should always be disallowed");
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("AskUserQuestion", dtVal);
    }

    [Fact]
    public void Build_system_prompt_emits_append_system_prompt()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", SystemPrompt = "Be concise." };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--append-system-prompt");
        Assert.True(idx >= 0);
        Assert.Equal("Be concise.", argv[idx + 1]);
    }

    [Fact]
    public void Build_resume_token_emits_resume_flag()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ResumeToken = "sess-xyz" };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--resume");
        Assert.True(idx >= 0);
        Assert.Equal("sess-xyz", argv[idx + 1]);
    }

    [Fact]
    public void Build_model_emits_model_flag()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", Model = "claude-opus-4-5" };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("claude-opus-4-5", argv[idx + 1]);
    }

    [Fact]
    public void Build_settings_path_emits_settings_flag_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", SettingsPath = "/path/to/settings.json" };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--settings");
        Assert.True(idx >= 0);
        Assert.Equal("/path/to/settings.json", argv[idx + 1]);
    }

    [Fact]
    public void Build_mcp_config_emits_mcp_config_flag_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", McpConfigPath = "/path/to/mcp.json" };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--mcp-config");
        Assert.True(idx >= 0);
        Assert.Equal("/path/to/mcp.json", argv[idx + 1]);
    }

    [Fact]
    public void Build_allowed_tools_emits_comma_joined_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", AllowedTools = ["mcp__fs__read", "mcp__fs__write"] };
        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--allowedTools");
        Assert.True(idx >= 0);
        Assert.Equal("mcp__fs__read,mcp__fs__write", argv[idx + 1]);
    }

    [Fact]
    public void Build_prompt_never_appears_in_argv()
    {
        var opts = new AgentSessionOptions { Prompt = "my secret prompt text" };
        var argv = Args(opts);
        Assert.DoesNotContain("my secret prompt text", argv);
    }

    [Fact]
    public void Build_custom_disallowed_tools_merged_with_always_denied()
    {
        var opts = new AgentSessionOptions
        {
            Prompt = "hi",
            DisallowedTools = ["Bash", "Bash"] // duplicates should be removed
        };
        var argv = Args(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0);
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("Bash", dtVal);
        // duplicates must be deduplicated
        var bashCount = dtVal.Split(',').Count(t => t == "Bash");
        Assert.Equal(1, bashCount);
    }

    // ── CLI14: the host application's own MCP servers ─────────────────────────

    /// <summary>MEASURED turn-free against the installed claude CLI (2026-08-05): `claude --help` documents
    /// `--mcp-config &lt;configs...&gt;` as "Load MCP servers from JSON files or strings (space-separated)",
    /// and this document shape was confirmed by placing it as a project `.mcp.json` and reading back what
    /// `claude mcp list` — which reads configuration and spends no turn — had parsed.</summary>
    private static AgentMcpServer Stdio() => AgentMcpServer.Stdio(
        "app-tools", @"C:\Program Files\app\mcp.exe", ["--serve"],
        new Dictionary<string, string> { ["APP_WORKSPACE"] = "/w" });

    [Fact]
    public void App_mcp_servers_are_written_to_a_config_document_and_pointed_at()
    {
        List<(string Kind, string Content)> written = [];
        var opts = new AgentSessionOptions { Prompt = "hi", McpServers = [Stdio()] };

        var argv = Args(opts, written).ToList();

        var idx = argv.IndexOf("--mcp-config");
        Assert.True(idx >= 0, "--mcp-config expected");
        Assert.Equal("<temp:mcp>", argv[idx + 1]);
        var (kind, content) = Assert.Single(written);
        Assert.Equal("mcp", kind);
        Assert.Contains("\"mcpServers\"", content);
        Assert.Contains("\"app-tools\"", content);
        Assert.Contains("\"stdio\"", content);
        Assert.Contains(@"C:\\Program Files\\app\\mcp.exe", content);   // JSON-escaped, not mangled
        Assert.Contains("\"--serve\"", content);
        Assert.Contains("\"APP_WORKSPACE\"", content);
    }

    [Fact]
    public void An_http_server_carries_its_token_as_an_authorization_header_in_the_FILE_not_argv()
    {
        // argv is readable by any process that can list processes, which is why the document goes to an
        // owner-only file rather than the inline-string form --mcp-config also accepts
        List<(string Kind, string Content)> written = [];
        var server = AgentMcpServer.Http("remote", "https://tools.example.invalid/mcp", "s3cret-token");
        var opts = new AgentSessionOptions { Prompt = "hi", McpServers = [server] };

        var argv = Args(opts, written).ToList();

        Assert.DoesNotContain(argv, a => a.Contains("s3cret-token", StringComparison.Ordinal));
        Assert.Contains("Bearer s3cret-token", written.Single().Content);
        Assert.Contains("\"http\"", written.Single().Content);
    }

    [Fact]
    public void A_callers_own_mcp_config_path_is_kept_ALONGSIDE_the_rendered_one()
    {
        // MEASURED: the flag takes a LIST ("space-separated"), which is what lets both coexist. Displacing
        // the caller's config would silently remove servers they had already wired up.
        var opts = new ClaudeAgentOptions
        {
            Prompt = "hi",
            McpConfigPath = "/app/mine.json",
            McpServers = [Stdio()],
        };

        var argv = Args(opts).ToList();

        var idx = argv.IndexOf("--mcp-config");
        Assert.Equal(1, argv.Count(a => a == "--mcp-config"));   // one flag, two values
        Assert.Equal("/app/mine.json", argv[idx + 1]);           // the caller's, first
        Assert.Equal("<temp:mcp>", argv[idx + 2]);
    }

    [Fact]
    public void A_callers_mcp_config_path_alone_is_unchanged_by_this_feature()
    {
        List<(string Kind, string Content)> written = [];
        var opts = new ClaudeAgentOptions { Prompt = "hi", McpConfigPath = "/app/mine.json" };

        var argv = Args(opts, written).ToList();

        var idx = argv.IndexOf("--mcp-config");
        Assert.Equal("/app/mine.json", argv[idx + 1]);
        Assert.Empty(written);   // nothing rendered, so nothing written and nothing to clean up
    }

    [Fact]
    public void No_app_servers_means_no_flag_and_no_file()
    {
        List<(string Kind, string Content)> written = [];

        var argv = Args(new AgentSessionOptions { Prompt = "hi" }, written).ToList();

        Assert.DoesNotContain("--mcp-config", argv);
        Assert.Empty(written);
    }

    [Fact]
    public void Naming_a_server_does_not_pre_approve_its_tools()
    {
        // reachable is not permitted: auto-allow-listing an app's servers would be a silent change of
        // security posture, so the caller keeps AllowedTools / SkipAllPermissions
        var argv = Args(new AgentSessionOptions { Prompt = "hi", McpServers = [Stdio()] }).ToList();

        Assert.DoesNotContain("--allowedTools", argv);
        Assert.DoesNotContain("--dangerously-skip-permissions", argv);
    }

    [Fact]
    public async Task An_unusable_server_refuses_the_turn_without_spawning()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");
        var opts = new AgentSessionOptions { Prompt = "hi", McpServers = [AgentMcpServer.Stdio("has.dot", "x.exe")] };

        var events = await session.StreamAsync(opts).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.Equal(LlmVerdict.Unsupported, ended.Verdict);
        Assert.Equal("mcp-server-invalid", ended.Subtype);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task The_rendered_config_file_is_DELETED_when_the_turn_ends()
    {
        // it carries whatever secrets the caller's servers need, so it must not outlive the turn — this is
        // the real CliTempFile path (the session writes it), not the recording writer above
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");
        var opts = new AgentSessionOptions { Prompt = "hi", McpServers = [Stdio()] };

        await session.StreamAsync(opts).ToListAsync();

        var idx = runner.LastArgs!.ToList().IndexOf("--mcp-config");
        var path = runner.LastArgs![idx + 1];
        Assert.False(File.Exists(path), $"the config file should have been deleted: {path}");
    }

    // ── CLI1: headless skip-all-permissions ───────────────────────────────────

    [Fact]
    public void Build_skip_all_permissions_emits_dangerous_flag()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", SkipAllPermissions = true };
        var argv = Args(opts).ToList();
        Assert.Contains("--dangerously-skip-permissions", argv);
    }

    [Fact]
    public void Build_skip_all_permissions_suppresses_permission_mode_even_for_write_policy()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write, SkipAllPermissions = true };
        var argv = Args(opts).ToList();

        Assert.Contains("--dangerously-skip-permissions", argv);
        Assert.DoesNotContain("--permission-mode", argv); // the CLI rejects combining the two
    }

    [Fact]
    public void Build_skip_all_permissions_suppresses_allowed_tools()
    {
        var opts = new ClaudeAgentOptions
        {
            Prompt = "hi",
            SkipAllPermissions = true,
            AllowedTools = ["mcp__fs__read", "mcp__fs__write"],
        };
        var argv = Args(opts).ToList();

        Assert.Contains("--dangerously-skip-permissions", argv);
        Assert.DoesNotContain("--allowedTools", argv); // conflicting with the bypass — suppressed
    }

    [Fact]
    public void Build_skip_all_permissions_still_honors_always_denied_and_caller_disallowed()
    {
        var opts = new ClaudeAgentOptions
        {
            Prompt = "hi",
            SkipAllPermissions = true,
            DisallowedTools = ["Bash"],
        };
        var argv = Args(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "the always-denied set + caller disallowed are honored even when bypassing");
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("AskUserQuestion", dtVal); // always-denied flow tool
        Assert.Contains("Bash", dtVal);            // caller-supplied
    }

    [Fact]
    public void Build_skip_all_permissions_with_readonly_policy_keeps_write_tools_disallowed()
    {
        // the documented invariant: the ReadOnly denial is still honored under the bypass — skipping
        // permission PROMPTS must not re-enable the write tools the policy disallows
        var opts = new ClaudeAgentOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.ReadOnly, SkipAllPermissions = true };
        var argv = Args(opts).ToList();

        Assert.Contains("--dangerously-skip-permissions", argv);
        Assert.DoesNotContain("--permission-mode", argv); // the CLI rejects combining the two

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "ReadOnly denial must survive the bypass");
        // Whole NAMES, not substrings — see Build_readonly_policy_includes_edit_write_notebookedit_in_disallowed.
        // This is the sharpest of the three: it is what pins that the permission BYPASS does not re-enable the
        // write tools, and a substring match let the "Edit" half of that promise be deleted silently.
        var denied = argv[dtIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("Edit", denied);
        Assert.Contains("Write", denied);
        Assert.Contains("NotebookEdit", denied);
    }

    [Fact]
    public void Build_without_skip_all_permissions_does_not_emit_dangerous_flag()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write };
        var argv = Args(opts).ToList();

        Assert.DoesNotContain("--dangerously-skip-permissions", argv);
        Assert.Contains("--permission-mode", argv); // the normal acceptEdits path is unaffected
    }

    // ── ClaudeAgentSession.StreamAsync tests ──────────────────────────────────

    [Fact]
    public async Task StreamAsync_happy_path_yields_ordered_events_with_correct_session_id()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var opts = new AgentSessionOptions { Prompt = "do the thing", WorkingDirectory = "/work/dir" };
        var events = await session.StreamAsync(opts).ToListAsync();

        // Must contain SessionStarted, ToolCall, ToolResult, SessionEnded
        Assert.Contains(events, e => e is SessionStarted { SessionId: "sess-abc123" });
        Assert.Contains(events, e => e is ToolCall { Name: "Read" });
        Assert.Contains(events, e => e is ToolResult);
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal("sess-abc123", ended.SessionId);
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
    }

    // The provider seam has carried `environment` since portable installs shipped, and both Add* methods'
    // docs instruct a host to "pass the same value to both". The agent session took no such value at all, so
    // a portable install's CLAUDE_CONFIG_DIR was honoured for completions and SILENTLY DROPPED for agent
    // turns — the agent then reading and mutating the machine-wide install's state, which is the exact thing
    // a portable install exists to avoid. Mirrors CodexAgentSessionTests.A_portable_installs_environment_-
    // reaches_the_spawn.
    [Fact]
    public async Task A_portable_installs_environment_reaches_the_spawn()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude",
            environment: new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = "portable/config" });

        await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();

        Assert.Equal("portable/config", runner.LastEnvironment?["CLAUDE_CONFIG_DIR"]);
    }

    [Fact]
    public async Task StreamAsync_prompt_goes_over_stdin_not_argv()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var opts = new AgentSessionOptions { Prompt = "my prompt text", WorkingDirectory = "/work" };
        await session.StreamAsync(opts).ToListAsync();

        Assert.Equal("my prompt text", runner.LastStdin);
        Assert.DoesNotContain("my prompt text", runner.LastArgs!);
    }

    [Fact]
    public async Task StreamAsync_working_directory_passed_to_runner()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var opts = new AgentSessionOptions { Prompt = "hi", WorkingDirectory = "/project/root" };
        await session.StreamAsync(opts).ToListAsync();

        Assert.Equal("/project/root", runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task RunAsync_fold_returns_final_text_and_session_id()
    {
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var result = await session.RunAsync(new AgentSessionOptions { Prompt = "fold me" });

        Assert.Equal("Done", result.FinalText);
        Assert.Equal("sess-abc123", result.SessionId);
        Assert.Equal(LlmVerdict.Ok, result.Verdict);
    }

    [Fact]
    public async Task StreamAsync_process_run_exception_yields_error_terminal_event()
    {
        var ex = new ProcessRunException("claude", 1, "boom stderr");
        var runner = new FakeProcessRunner(throwsAfterLines: ex);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.True(ended.IsError);
        Assert.Contains("boom stderr", ended.Diagnostic);
    }

    [Fact]
    public async Task StreamAsync_process_timeout_yields_timeout_terminal_event()
    {
        var ex = new ProcessTimeoutException("claude", TimeSpan.FromSeconds(30));
        var runner = new FakeProcessRunner(throwsAfterLines: ex);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.True(ended.IsError);
        Assert.Equal(LlmVerdict.Timeout, ended.Verdict);
    }

    [Fact]
    public async Task StreamAsync_no_output_yields_failed_terminal_event()
    {
        // Fake runner yields zero lines and exits cleanly (no exception)
        var runner = new FakeProcessRunner([]);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();

        var ended = events.OfType<SessionEnded>().Single();
        Assert.True(ended.IsError);
        Assert.Equal(LlmVerdict.Failed, ended.Verdict);
        Assert.Contains("no output", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reader_terminal_wins_and_is_not_duplicated_when_process_then_exits_nonzero()
    {
        // The result line makes the reader emit SessionEnded(Ok); the process then throws. The
        // !sawTerminal guard must suppress a second terminal — exactly one SessionEnded, and it's the Ok one.
        var ex = new ProcessRunException("claude", 1, "boom stderr");
        var runner = new FakeProcessRunner(FullTranscript, ex);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var events = new List<AgentStreamEvent>();
        await foreach (var e in session.StreamAsync(new ClaudeAgentOptions { Prompt = "x", WorkingDirectory = "." }))
            events.Add(e);

        var terminals = events.OfType<SessionEnded>().ToList();
        Assert.Single(terminals);
        Assert.Equal(LlmVerdict.Ok, terminals[0].Verdict);
        Assert.False(terminals[0].IsError);
    }

    [Fact] // T5: CALLER cancellation MID-stream propagates — no fabricated terminal after the committed events
    public async Task StreamAsync_mid_stream_caller_cancellation_propagates_without_a_bogus_terminal()
    {
        using var cts = new CancellationTokenSource();
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var received = new List<AgentStreamEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }, cts.Token))
            {
                received.Add(e);
                cts.Cancel(); // bail right after the FIRST event
            }
        });

        Assert.NotEmpty(received);                               // the committed events arrived
        Assert.DoesNotContain(received, e => e is SessionEnded); // no fabricated terminal for a cancelled CALLER
    }

    [Fact]
    public async Task StreamAsync_cancellation_propagates_as_operation_cancelled()
    {
        using var cts = new CancellationTokenSource();

        // A runner that yields one line then checks cancellation
        var runner = new FakeProcessRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        cts.Cancel(); // cancel immediately

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }, cts.Token))
            {
                // consume until cancelled
            }
        });
    }

    // ── ClaudeToolCalls tests ─────────────────────────────────────────────────

    [Fact]
    public void FilePathOf_returns_file_path_when_present()
    {
        var call = new ToolCall("Edit", """{"file_path":"/x/a.txt","old_string":"a","new_string":"b"}""", "call-1");
        Assert.Equal("/x/a.txt", ClaudeToolCalls.FilePathOf(call));
    }

    [Fact]
    public void FilePathOf_returns_null_when_no_file_path()
    {
        var call = new ToolCall("Bash", """{"command":"ls"}""", "call-2");
        Assert.Null(ClaudeToolCalls.FilePathOf(call));
    }

    [Fact]
    public void FilePathOf_returns_null_for_malformed_json()
    {
        var call = new ToolCall("Edit", "not-valid-json", "call-3");
        Assert.Null(ClaudeToolCalls.FilePathOf(call));
    }

    [Fact]
    public void FilePathOf_returns_notebook_path_when_no_file_path()
    {
        var call = new ToolCall("NotebookEdit", """{"notebook_path":"/x/n.ipynb","new_source":"print(1)"}""", "call-4");
        Assert.Equal("/x/n.ipynb", ClaudeToolCalls.FilePathOf(call));
    }

    [Fact]
    public void FilePathOf_returns_path_when_no_file_or_notebook_path()
    {
        var call = new ToolCall("SomeWriteTool", """{"path":"/x/p.txt"}""", "call-5");
        Assert.Equal("/x/p.txt", ClaudeToolCalls.FilePathOf(call));
    }

    [Fact]
    public void FilePathOf_file_path_wins_over_notebook_path_and_path()
    {
        var call = new ToolCall("Edit", """{"file_path":"/x/a.txt","notebook_path":"/x/n.ipynb","path":"/x/p.txt"}""", "call-6");
        Assert.Equal("/x/a.txt", ClaudeToolCalls.FilePathOf(call));
    }

    // ── DI test ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddClaudeCliAgentSession_registers_IAgentSession_as_ClaudeAgentSession()
    {
        var runner = new FakeProcessRunner();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b.AddClaudeCliAgentSession());
        using var sp = services.BuildServiceProvider();

        var agentSession = sp.GetRequiredService<IAgentSession>();
        Assert.IsType<ClaudeAgentSession>(agentSession);
    }
}
