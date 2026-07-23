using System.Runtime.CompilerServices;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>Tests for ClaudeAgentArgs, ClaudeAgentSession, ClaudeToolCalls, and the DI registration.</summary>
public class ClaudeAgentSessionTests
{
    // ── Fake runner ───────────────────────────────────────────────────────────

    private sealed class FakeAgentRunner : IProcessRunner
    {
        private readonly IReadOnlyList<string> _lines;
        private readonly Exception? _throws;

        public string? LastCommand { get; private set; }
        public IReadOnlyList<string>? LastArgs { get; private set; }
        public string? LastStdin { get; private set; }
        public string? LastWorkingDirectory { get; private set; }

        public FakeAgentRunner(IReadOnlyList<string>? lines = null, Exception? throws = null)
        {
            _lines = lines ?? [];
            _throws = throws;
        }

        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? timeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, CancellationToken ct = default)
            => Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));

        public async IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,
            string? stdin = null, TimeSpan? timeout = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastCommand = command;
            LastArgs = args;
            LastStdin = stdin;
            LastWorkingDirectory = workingDirectory;

            foreach (var line in _lines)
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }

            if (_throws is not null)
            {
                // yield lines first, then throw (as ProcessRunner does: yields lines so far, then throws)
                await Task.Yield();
                throw _throws;
            }
        }
    }

    // ── Shared stream-json fixture ────────────────────────────────────────────

    private static readonly string[] FullTranscript =
    [
        """{"type":"system","session_id":"sess-abc123","model":"claude-opus-4-5"}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"call-1","name":"Read","input":{"file_path":"/x/a.txt"}}],"usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0}}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"call-1","content":"file content here","is_error":false}]}}""",
        """{"type":"result","result":"Done","session_id":"sess-abc123","is_error":false,"usage":{"input_tokens":20,"output_tokens":8,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"subtype":null}""",
    ];

    // ── ClaudeAgentArgs tests ─────────────────────────────────────────────────

    [Fact]
    public void Build_always_includes_base_flags()
    {
        var opts = new AgentSessionOptions { Prompt = "hi" };
        var argv = ClaudeAgentArgs.Build(opts);

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
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "--disallowed-tools flag expected");
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("Edit", dtVal);
        Assert.Contains("Write", dtVal);
        Assert.Contains("NotebookEdit", dtVal);
        Assert.DoesNotContain("--permission-mode", argv);
    }

    [Fact]
    public void Build_write_policy_emits_acceptEdits_and_excludes_edit_write_from_disallowed()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        Assert.Contains("--permission-mode", argv);
        var pmIdx = argv.IndexOf("--permission-mode");
        Assert.Equal("acceptEdits", argv[pmIdx + 1]);

        // disallowed-tools may still be present (for the always-denied flow tools) but must not contain Edit
        var dtIdx = argv.IndexOf("--disallowed-tools");
        if (dtIdx >= 0)
        {
            var dtVal = argv[dtIdx + 1];
            Assert.DoesNotContain("Edit", dtVal);
            Assert.DoesNotContain("Write", dtVal);
        }
    }

    [Fact]
    public void Build_always_denied_flow_tools_are_in_disallowed_for_write_policy()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ToolPolicy = AgentToolPolicy.Write };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0, "headless flow tools should always be disallowed");
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("AskUserQuestion", dtVal);
    }

    [Fact]
    public void Build_system_prompt_emits_append_system_prompt()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", SystemPrompt = "Be concise." };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--append-system-prompt");
        Assert.True(idx >= 0);
        Assert.Equal("Be concise.", argv[idx + 1]);
    }

    [Fact]
    public void Build_resume_token_emits_resume_flag()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", ResumeToken = "sess-xyz" };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--resume");
        Assert.True(idx >= 0);
        Assert.Equal("sess-xyz", argv[idx + 1]);
    }

    [Fact]
    public void Build_model_emits_model_flag()
    {
        var opts = new AgentSessionOptions { Prompt = "hi", Model = "claude-opus-4-5" };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("claude-opus-4-5", argv[idx + 1]);
    }

    [Fact]
    public void Build_settings_path_emits_settings_flag_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", SettingsPath = "/path/to/settings.json" };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--settings");
        Assert.True(idx >= 0);
        Assert.Equal("/path/to/settings.json", argv[idx + 1]);
    }

    [Fact]
    public void Build_mcp_config_emits_mcp_config_flag_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", McpConfigPath = "/path/to/mcp.json" };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--mcp-config");
        Assert.True(idx >= 0);
        Assert.Equal("/path/to/mcp.json", argv[idx + 1]);
    }

    [Fact]
    public void Build_allowed_tools_emits_comma_joined_for_claude_options()
    {
        var opts = new ClaudeAgentOptions { Prompt = "hi", AllowedTools = ["mcp__fs__read", "mcp__fs__write"] };
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var idx = argv.IndexOf("--allowedTools");
        Assert.True(idx >= 0);
        Assert.Equal("mcp__fs__read,mcp__fs__write", argv[idx + 1]);
    }

    [Fact]
    public void Build_prompt_never_appears_in_argv()
    {
        var opts = new AgentSessionOptions { Prompt = "my secret prompt text" };
        var argv = ClaudeAgentArgs.Build(opts);
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
        var argv = ClaudeAgentArgs.Build(opts).ToList();

        var dtIdx = argv.IndexOf("--disallowed-tools");
        Assert.True(dtIdx >= 0);
        var dtVal = argv[dtIdx + 1];
        Assert.Contains("Bash", dtVal);
        // duplicates must be deduplicated
        var bashCount = dtVal.Split(',').Count(t => t == "Bash");
        Assert.Equal(1, bashCount);
    }

    // ── ClaudeAgentSession.StreamAsync tests ──────────────────────────────────

    [Fact]
    public async Task StreamAsync_happy_path_yields_ordered_events_with_correct_session_id()
    {
        var runner = new FakeAgentRunner(FullTranscript);
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

    [Fact]
    public async Task StreamAsync_prompt_goes_over_stdin_not_argv()
    {
        var runner = new FakeAgentRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var opts = new AgentSessionOptions { Prompt = "my prompt text", WorkingDirectory = "/work" };
        await session.StreamAsync(opts).ToListAsync();

        Assert.Equal("my prompt text", runner.LastStdin);
        Assert.DoesNotContain("my prompt text", runner.LastArgs!);
    }

    [Fact]
    public async Task StreamAsync_working_directory_passed_to_runner()
    {
        var runner = new FakeAgentRunner(FullTranscript);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var opts = new AgentSessionOptions { Prompt = "hi", WorkingDirectory = "/project/root" };
        await session.StreamAsync(opts).ToListAsync();

        Assert.Equal("/project/root", runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task RunAsync_fold_returns_final_text_and_session_id()
    {
        var runner = new FakeAgentRunner(FullTranscript);
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
        var runner = new FakeAgentRunner(throws: ex);
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
        var runner = new FakeAgentRunner(throws: ex);
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
        var runner = new FakeAgentRunner([]);
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
        var runner = new FakeAgentRunner(FullTranscript, ex);
        var session = new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude");

        var events = new List<AgentStreamEvent>();
        await foreach (var e in session.StreamAsync(new ClaudeAgentOptions { Prompt = "x", WorkingDirectory = "." }))
            events.Add(e);

        var terminals = events.OfType<SessionEnded>().ToList();
        Assert.Single(terminals);
        Assert.Equal(LlmVerdict.Ok, terminals[0].Verdict);
        Assert.False(terminals[0].IsError);
    }

    [Fact]
    public async Task StreamAsync_cancellation_propagates_as_operation_cancelled()
    {
        using var cts = new CancellationTokenSource();

        // A runner that yields one line then checks cancellation
        var runner = new FakeAgentRunner(FullTranscript);
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
        var runner = new FakeAgentRunner();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b.AddClaudeCliAgentSession());
        using var sp = services.BuildServiceProvider();

        var agentSession = sp.GetRequiredService<IAgentSession>();
        Assert.IsType<ClaudeAgentSession>(agentSession);
    }
}

// Helper extension to collect async enumerable
file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source, CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            list.Add(item);
        return list;
    }
}
