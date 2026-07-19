using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Llm;
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

    public ClaudeAgentSession(
        IProcessRunner runner,
        LyntaiOptions options,
        ILogger<ClaudeAgentSession>? logger = null,
        string? command = null)
    {
        _runner = runner;
        _options = options;
        _logger = logger ?? NullLogger<ClaudeAgentSession>.Instance;
        _command = command;
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (exe, prefixArgs) = ResolveCommand();
        var argv = prefixArgs.Concat(ClaudeAgentArgs.Build(options)).ToList();

        var timeout = _options.ResolveTimeout(options.TimeoutSeconds);

        var reader = new StreamJsonAgentReader();
        var sawTerminal = false;
        string? lastSessionId = null;

        var lines = _runner.StreamLinesAsync(exe, argv, stdin: options.Prompt, timeout: timeout,
            workingDirectory: options.WorkingDirectory, ct: ct);
        var e = lines.GetAsyncEnumerator(ct);
        await using (e.ConfigureAwait(false))
        {
            while (true)
            {
                string? line = null;
                SessionEnded? terminal = null;
                try
                {
                    line = await e.MoveNextAsync().ConfigureAwait(false) ? e.Current : null;
                }
                catch (OperationCanceledException) { throw; }
                catch (ProcessTimeoutException ex)
                {
                    terminal = new SessionEnded(LlmVerdict.Timeout, true, "timeout", lastSessionId, null, ex.Message);
                }
                catch (ProcessRunException ex)
                {
                    terminal = new SessionEnded(
                        LlmVerdictClassifier.FromErrorText(ex.StdErrTail), true, null,
                        lastSessionId, null, $"exit {ex.ExitCode}: {ex.StdErrTail}");
                }
                catch (Exception ex)
                {
                    terminal = new SessionEnded(LlmVerdict.Failed, true, null, lastSessionId, null, $"spawn failed: {ex.Message}");
                }

                if (terminal is not null)
                {
                    if (!sawTerminal) yield return terminal;
                    yield break;
                }
                if (line is null) break;

                foreach (var evt in reader.Read(line))
                {
                    if (evt is SessionStarted ss) lastSessionId = ss.SessionId;
                    else if (evt is SessionEnded se)
                    {
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

    private (string Exe, IReadOnlyList<string> PrefixArgs) ResolveCommand()
    {
        var cmd = _command
            ?? Environment.GetEnvironmentVariable("LYNTAI_PROVIDER_CMD")
            ?? Environment.GetEnvironmentVariable("CLAUDE_CMD")
            ?? "claude";
        var tokens = ClaudeCliProvider.Tokenize(cmd);
        return tokens.Count == 0 ? ("claude", []) : (tokens[0], tokens.Skip(1).ToList());
    }
}
