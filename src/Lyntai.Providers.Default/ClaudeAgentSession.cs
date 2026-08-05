using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Streaming;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>
/// Spawns the authenticated <c>claude</c> CLI in self-driving (agentic) mode and maps its
/// <c>--output-format stream-json --include-partial-messages</c> output to <see cref="AgentStreamEvent"/>s.
/// Unlike <see cref="ClaudeCliProvider"/> (which always uses a neutral working directory), the session
/// DELIBERATELY runs in the caller's <see cref="AgentSessionOptions.WorkingDirectory"/> — the agent
/// is driving the caller's project.
/// The command resolves from (in order): the ctor override, <c>LYNTAI_PROVIDER_CMD</c>, <c>CLAUDE_CMD</c>,
/// then a plain <c>claude</c> from PATH — same env seams as the provider so tests/e2e can stub it.
/// </summary>
public sealed class ClaudeAgentSession : IAgentSession
{
    private readonly IProcessRunner _runner;
    private readonly LyntaiOptions _options;
    private readonly ILogger _logger;
    private readonly string? _command;
    private readonly IReadOnlyDictionary<string, string>? _environment;

    /// <param name="runner">Spawns the CLI.</param>
    /// <param name="options">Platform options; supplies the timeout resolution.</param>
    /// <param name="logger">Null = no logging.</param>
    /// <param name="command">A PORTABLE <c>claude</c> path; null = the env overrides, then PATH.</param>
    /// <param name="environment">Extra environment variables for every spawn — the same seam
    /// <see cref="ClaudeCliProvider"/> has, and for the same reason: a portable install usually wants its own
    /// <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's state. Without it a
    /// host that passes one to the provider and the session alike (which both methods' docs instruct) had it
    /// honoured for completions and silently dropped for agent turns.</param>
    public ClaudeAgentSession(
        IProcessRunner runner,
        LyntaiOptions options,
        ILogger<ClaudeAgentSession>? logger = null,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _runner = runner;
        _options = options;
        _logger = logger ?? NullLogger<ClaudeAgentSession>.Instance;
        _command = command;
        _environment = environment;
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (exe, prefixArgs) = ClaudeCommand.Resolve(_command);
        var argv = prefixArgs.Concat(ClaudeAgentArgs.Build(options)).ToList();

        var timeout = _options.ResolveTimeout(options.TimeoutSeconds);

        var reader = new StreamJsonAgentReader();
        var sawTerminal = false;
        string? lastSessionId = null;

        var lines = _runner.StreamLinesAsync(exe, argv, stdin: options.Prompt, inactivityTimeout: timeout,
            workingDirectory: options.WorkingDirectory, environment: _environment, ct: ct);
        var e = lines.GetAsyncEnumerator(ct);
        await using (e.ConfigureAwait(false))
        {
            // the guarded loop lives once in Core; the fault→terminal translation lives once in this package
            // (CliAgentTerminal.FromFault), so the codex session answers the same exceptions the same way.
            // No clock here — the inactivity window is the RUNNER's (its timeout arrives as
            // ProcessTimeoutException), so ANY OperationCanceledException is cancellation and PROPAGATES:
            // FromFault returns null for it. The fault terminal captures lastSessionId at FAULT time (the
            // closure reads the loop-mutated local).
            var guarded = GuardedStream.ReadAll<string, SessionEnded>(
                async () => await e.MoveNextAsync().ConfigureAwait(false) ? e.Current : null,
                ex => CliAgentTerminal.FromFault(ex, lastSessionId),
                ct);
            await foreach (var (line, terminal) in guarded.ConfigureAwait(false))
            {
                if (terminal is not null)
                {
                    if (!sawTerminal) yield return terminal;
                    yield break;
                }

                foreach (var evt in reader.Read(line!))
                {
                    if (evt is SessionStarted ss) lastSessionId = ss.SessionId;
                    else if (evt is SessionEnded se)
                    {
                        // SessionEnded is THE single terminal: the first one wins, and anything the CLI
                        // prints after it can add events but never a second ending. Same rule
                        // CodexAgentSession enforces — without it a transcript carrying two `result` lines
                        // ends the session twice, and RunAsync's fold (last-one-wins) reports the SECOND
                        // verdict, so a stray trailing line could turn a finished Ok turn into a failure.
                        if (sawTerminal) continue;
                        sawTerminal = true;
                        lastSessionId = se.SessionId ?? lastSessionId;
                    }
                    yield return evt;
                }
            }
        }

        if (!sawTerminal)
        {
            _logger.LogWarning("ClaudeAgentSession produced no terminal event; session={SessionId}", lastSessionId);
            yield return new SessionEnded(LlmVerdict.Failed, true, null, lastSessionId, null,
                "no output produced (no terminal result)");
        }
    }
}
