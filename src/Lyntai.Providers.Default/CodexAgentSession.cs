using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.CodexCli;

/// <summary>
/// Spawns the authenticated OpenAI <c>codex</c> CLI in self-driving (agentic) mode and maps its
/// <c>exec --json</c> JSONL to <see cref="AgentStreamEvent"/>s — the codex counterpart of
/// <see cref="Providers.ClaudeCli.ClaudeAgentSession"/>, so one <see cref="IAgentSession"/> drives either.
/// Unlike <see cref="CodexCliProvider"/> (always a neutral working directory), the session DELIBERATELY runs
/// in the caller's <see cref="AgentSessionOptions.WorkingDirectory"/> — the agent is driving the caller's
/// project. <c>--skip-git-repo-check</c> is still passed: that directory is a git repository on a
/// developer's machine and frequently is not one in a shipped app.
/// The command resolves from the ctor override, <c>LYNTAI_PROVIDER_CMD</c>, <c>CODEX_CMD</c>, then
/// <c>codex</c> on PATH — same env seams as the provider, so tests can stub it.
/// </summary>
/// <remarks>
/// <para><b>Half of this backend's event mapping is inferred — know which half before you rely on it.</b>
/// Treat a tool step's KIND as provisional and its PAYLOAD as reliable; switch on
/// <see cref="ToolCall.Name"/>. <c>CodexAgentReader</c>'s own summary states what is MEASURED, what is
/// INFERRED, and how far the inference is bounded.</para>
/// <para><b>Two neutral options codex cannot honour</b>, each handled explicitly:
/// <see cref="AgentSessionOptions.DisallowedTools"/> is logged as unhonoured (codex's tool gate is the
/// sandbox, not a deny list), and <see cref="AgentSessionOptions.SystemPrompt"/> travels as a leading block
/// of the prompt, since <c>codex exec</c> has no measured flag for one.
/// <see cref="AgentSessionOptions.ResumeToken"/> IS honoured — see <see cref="StreamAsync"/> for the one
/// token shape it refuses.</para>
/// </remarks>
public sealed class CodexAgentSession : IAgentSession
{
    private readonly IProcessRunner _runner;
    private readonly LyntaiOptions _options;
    private readonly ILogger _logger;
    private readonly string? _command;
    private readonly IReadOnlyDictionary<string, string>? _environment;

    /// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn.</param>
    /// <param name="options">Timeout configuration.</param>
    /// <param name="logger">Optional diagnostics. Also where an unhonourable option is reported.</param>
    /// <param name="command">Explicit command, e.g. a PORTABLE <c>codex</c> the host ships or unpacks itself
    /// rather than a global install (quote a path with spaces). Wins over the env seams.</param>
    /// <param name="environment">Extra environment variables for the spawn — a portable install usually
    /// wants its own <c>CODEX_HOME</c> so it neither reads nor mutates the machine-wide install's state.</param>
    public CodexAgentSession(
        IProcessRunner runner,
        LyntaiOptions options,
        ILogger<CodexAgentSession>? logger = null,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _runner = runner;
        _options = options;
        _logger = logger ?? NullLogger<CodexAgentSession>.Instance;
        _command = command;
        _environment = environment;
    }

    /// <summary>Run one codex turn and stream what the agent does.</summary>
    /// <param name="options">The turn. A non-null <see cref="AgentSessionOptions.ResumeToken"/> CONTINUES the
    /// thread it names: the argv becomes <c>codex exec resume … &lt;SESSION_ID&gt; -</c> (<c>resume</c> is a
    /// real subcommand of <c>exec</c>, the id is its first POSITIONAL, and the <c>-</c> stdin marker takes
    /// the PROMPT position after it — see
    /// <see cref="CodexExecArgs.TryBuildResume"/>). The one token shape still REFUSED without spawning — a
    /// single <see cref="SessionEnded"/> with <see cref="LlmVerdict.Unsupported"/> — is one the CLI would read
    /// as an OPTION rather than as an id (blank, or starting with <c>-</c> such as its own <c>--last</c>),
    /// because forwarding it would silently resume the WRONG thread.
    /// <para>Whether codex re-announces <c>thread.started</c> on a resumed turn is NOT measured, so a resumed
    /// turn's <see cref="SessionEnded.SessionId"/> may be null where a fresh one's is set. Nothing is
    /// fabricated to cover that: a caller resuming already holds the id it passed in, and echoing it back
    /// would report a request as an observation (the same reason <see cref="UsageFinal.Model"/> is null
    /// here).</para></param>
    /// <param name="ct">Cancels the turn and kills the process tree.</param>
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!AgentMcpServers.TryValidate(options.McpServers, out var mcpRefusal))
        {
            yield return new SessionEnded(LlmVerdict.Unsupported, true, "mcp-server-invalid", null, null, mcpRefusal);
            yield break;
        }

        if (!CodexAgentArgs.TryBuild(options, out var agentArgs, out var mcpEnvironment, out var refusal))
        {
            yield return new SessionEnded(LlmVerdict.Unsupported, true, "resume-token-invalid", null, null, refusal);
            yield break;
        }

        if (options.DisallowedTools.Count > 0)
        {
            _logger.LogWarning(
                "CodexAgentSession cannot honour DisallowedTools ({Count} entry/entries dropped) — codex has " +
                "no per-tool deny list; its gate is the --sandbox mode, set from ToolPolicy (or " +
                "CodexAgentOptions.SandboxMode).", options.DisallowedTools.Count);
        }

        var (exe, prefixArgs) = CodexCommand.Resolve(_command);
        var argv = prefixArgs.Concat(agentArgs).ToList();
        var timeout = _options.ResolveTimeout(options.TimeoutSeconds);

        var reader = new CodexAgentReader();
        var sawTerminal = false;
        var sawContent = false;

        var lines = _runner.StreamLinesAsync(exe, argv, stdin: CodexAgentArgs.BuildPrompt(options),
            inactivityTimeout: timeout, workingDirectory: options.WorkingDirectory,
            environment: MergeEnvironment(_environment, mcpEnvironment), ct: ct);
        var e = lines.GetAsyncEnumerator(ct);
        await using (e.ConfigureAwait(false))
        {
            // the guarded loop lives once in Core; the fault→terminal translation lives once in this package
            // (CliAgentTerminal.FromFault), so the claude session answers the same exceptions the same way.
            // No clock here — the inactivity window is the RUNNER's (its timeout arrives as
            // ProcessTimeoutException), so ANY OperationCanceledException is cancellation and PROPAGATES:
            // FromFault returns null for it. The fault terminal reads the reader's thread id at FAULT time.
            var guarded = GuardedStream.ReadAll<string, SessionEnded>(
                async () => await e.MoveNextAsync().ConfigureAwait(false) ? e.Current : null,
                ex => CliAgentTerminal.FromFault(ex, reader.ThreadId),
                ct);
            await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    // codex reports failure IN BAND and exits 0, so the reader's terminal is authoritative;
                    // a later non-zero exit must not add a second one.
                    if (!sawTerminal) yield return terminal;
                    yield break;
                }

                foreach (var evt in reader.Read(line!))
                {
                    // SessionEnded is THE single terminal: the first one wins and anything the CLI prints
                    // after it can add events but never a second ending.
                    if (evt is SessionEnded)
                    {
                        if (sawTerminal) continue;
                        sawTerminal = true;
                    }
                    else if (evt is TextDelta or ToolCall or ToolResult or Thinking)
                    {
                        sawContent = true;
                    }
                    yield return evt;
                }
            }
        }

        if (!sawTerminal)
        {
            // Two different problems, so two different diagnostics: a CLI that printed nothing at all (a bad
            // invocation, a missing binary behind a BYO runner) versus one that answered and then died before
            // turn.completed (a crash or a kill mid-turn). Collapsing both into "no output produced" sends
            // whoever is debugging to look in the wrong place.
            var diagnostic = sawContent
                ? "the turn never terminated: codex streamed output but no turn.completed/turn.failed arrived " +
                  "before the process ended"
                : "no output produced (no terminal result)";
            _logger.LogWarning("CodexAgentSession produced no terminal event ({Diagnostic}); session={SessionId}",
                diagnostic, reader.ThreadId);
            yield return new SessionEnded(LlmVerdict.Failed, true, null, reader.ThreadId, null, diagnostic);
        }
    }

    /// <summary>The ctor's environment (a portable install's <c>CODEX_HOME</c>, say) plus anything this TURN
    /// needs — currently the bearer tokens of HTTP MCP servers, which codex reads only from a variable it
    /// names. The turn's own entries win on a collision, and the ctor's dictionary is returned untouched
    /// when there is nothing to add.</summary>
    private static IReadOnlyDictionary<string, string>? MergeEnvironment(
        IReadOnlyDictionary<string, string>? standing, IReadOnlyDictionary<string, string> perTurn)
    {
        if (perTurn.Count == 0) return standing;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (standing is not null)
        {
            foreach (var (key, value) in standing) merged[key] = value;
        }
        foreach (var (key, value) in perTurn) merged[key] = value;
        return merged;
    }
}
