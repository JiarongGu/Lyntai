using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Acp;

/// <summary>An <see cref="IAgentSession"/> over the Agent Client Protocol: JSON-RPC 2.0, one frame per line, over
/// the stdio of an agent process started for the turn and ended with it. The counterpart of
/// <see cref="Providers.CodexCli.CodexAgentSession"/> and <see cref="Providers.ClaudeCli.ClaudeAgentSession"/> for
/// any agent that speaks the protocol (<c>claude-agent-acp</c>, <c>codex-acp</c>, a host's own), so one
/// <see cref="IAgentSession"/> drives either door.
/// <para><b>A turn</b> is <c>initialize</c>, then <c>session/new</c> — or, for a
/// <see cref="AgentSessionOptions.ResumeToken"/>, <c>session/resume</c> where the agent states it and
/// <c>session/load</c> where it states that, else a refusal before the prompt (<see cref="ProviderVerdict.Unsupported"/>)
/// — then <c>session/prompt</c>. Its events are the updates <see cref="AgentStreamEvent"/> has a case for, and its
/// failure is read from the agent's own fields: the error's code and <c>data</c>, a typed session failure, a rejected
/// limit frame. The agent's prose never classifies. <see cref="SessionEnded.SessionId"/> is the conversation's
/// resume token.</para>
/// <para><b>Cancelling</b> the token sends <c>session/cancel</c> and gives the agent its grace period to answer
/// before the process is killed; the cancellation then propagates, as on the CLI doors.</para>
/// <para><b>Permission requests</b> follow <see cref="AgentSessionOptions.ToolPolicy"/>: read-only rejects each,
/// write allows each once, never for good. The client offers the agent no filesystem and no terminal.</para></summary>
public sealed class AcpAgentSession : IAgentSession
{
    /// <summary>How long a cancelled turn waits for the agent's answer to <c>session/cancel</c> before the process
    /// is killed, unless the constructor says otherwise.</summary>
    public static readonly TimeSpan DefaultCancelGrace = TimeSpan.FromSeconds(5);

    private static readonly string ClientVersion =
        typeof(AcpAgentSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    private readonly IProcessRunner _runner;
    private readonly LyntaiOptions _options;
    private readonly ILogger _logger;
    private readonly string _command;
    private readonly IReadOnlyList<string> _arguments;
    private readonly IReadOnlyDictionary<string, string?>? _environment;
    private readonly string? _accountHomeVariable;
    private readonly TimeSpan _cancelGrace;
    private readonly TimeProvider _time;

    /// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn. It must implement
    /// <see cref="IProcessRunner.ExchangeLinesAsync"/>, which the shipped <see cref="ProcessRunner"/> does.</param>
    /// <param name="options">Timeout configuration.</param>
    /// <param name="command">The agent's launch command: an executable, or one with prefix arguments
    /// (<c>node "agent.mjs"</c>; quote a path with spaces).</param>
    /// <param name="arguments">Arguments added after the command's own, each a separate entry.</param>
    /// <param name="environment">Extra environment variables for the spawn. A null value REMOVES one the host
    /// process holds from the child.</param>
    /// <param name="accountHomeVariable">The variable the agent reads its configuration home from
    /// (<c>CLAUDE_CONFIG_DIR</c>, <c>CODEX_HOME</c>), which a turn's <see cref="AgentSessionOptions.Account"/> sets.
    /// Null for an agent whose variable is not known: a turn that names an account is then refused.</param>
    /// <param name="cancelGrace">How long a cancelled turn waits for the agent before the process is killed;
    /// null = <see cref="DefaultCancelGrace"/>.</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="time">The clock a usage reading is stamped from; null = the system clock.</param>
    public AcpAgentSession(
        IProcessRunner runner,
        LyntaiOptions options,
        string command,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? accountHomeVariable = null,
        TimeSpan? cancelGrace = null,
        ILogger<AcpAgentSession>? logger = null,
        TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _runner = runner;
        _options = options;
        _command = command;
        _arguments = arguments ?? [];
        _environment = environment;
        _accountHomeVariable = accountHomeVariable;
        _cancelGrace = cancelGrace ?? DefaultCancelGrace;
        _logger = logger ?? NullLogger<AcpAgentSession>.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Run one turn and stream what the agent does.</summary>
    /// <param name="options">The turn. Its <see cref="AgentSessionOptions.WorkingDirectory"/> is the session's
    /// <c>cwd</c> (made absolute; the current directory when null). A blank resume token, an unusable MCP server
    /// and an account with no usable home, or one on an agent whose home variable is unknown, refuse the turn
    /// before anything is spawned.</param>
    /// <param name="ct">Cancels the turn: <c>session/cancel</c>, then the process.</param>
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!AgentMcpServers.TryValidate(options.McpServers, out var mcpRefusal))
        {
            yield return CliAgentLoop.Refused(AgentMcpServers.RefusedSubtype, mcpRefusal);
            yield break;
        }

        IReadOnlyDictionary<string, string> accountEnvironment = new Dictionary<string, string>(0);
        string? accountRefusal = null;
        if (options.Account is not null && _accountHomeVariable is null)
            accountRefusal = "this agent's configuration-home variable is not known, so an account cannot be handed to it.";
        else if (!AgentAccounts.TryMap(options.Account, _accountHomeVariable ?? "", out accountEnvironment, out accountRefusal))
            accountRefusal ??= "the account cannot be handed to the agent.";
        if (accountRefusal is not null)
        {
            yield return CliAgentLoop.Refused(AgentAccounts.RefusedSubtype, accountRefusal);
            yield break;
        }

        if (options.ResumeToken is not null && string.IsNullOrWhiteSpace(options.ResumeToken))
        {
            yield return CliAgentLoop.Refused(AgentResumeToken.RefusedSubtype,
                "a blank resume token names no conversation. Pass the SessionId reported by SessionStarted/SessionEnded, or null to start a fresh session.");
            yield break;
        }

        var tokens = CliCommand.Tokenize(_command);
        if (tokens.Count == 0)
        {
            yield return CliAgentLoop.Refused("command-invalid", "the agent's launch command is blank.");
            yield break;
        }

        var cwd = Path.GetFullPath(options.WorkingDirectory ?? Directory.GetCurrentDirectory());
        var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var reader = new AcpAgentReader(options, cwd, ClientVersion, outbox.Writer, _time);
        reader.Start();

        // the runner is cancelled by this source and never by the caller's token, so a cancelled turn gets to say
        // session/cancel and hear the agent's answer first
        using var kill = new CancellationTokenSource();
        using var onCancel = ct.Register(() =>
        {
            if (reader.TryCancel()) kill.CancelAfter(_cancelGrace);
            else kill.Cancel();
        });

        IAsyncEnumerable<string>? lines = null;
        string? unsupported = null;
        try
        {
            lines = _runner.ExchangeLinesAsync(tokens[0], [.. tokens.Skip(1), .. _arguments],
                outbox.Reader.ReadAllAsync(CancellationToken.None), _options.ResolveTimeout(options.TimeoutSeconds),
                workingDirectory: cwd, environment: AgentAccounts.Merge(_environment, accountEnvironment), ct: kill.Token);
        }
        catch (NotSupportedException ex)
        {
            unsupported = ex.Message;   // a BYO runner without the live-stdin member: said as a refusal, not thrown
        }

        if (lines is null)
        {
            yield return CliAgentLoop.Refused("runner-unsupported", unsupported);
            yield break;
        }

        try
        {
            await foreach (var evt in CliAgentLoop.RunLinesAsync(lines, endAtTerminal: true, reader.Read, _logger, "acp", ct).ConfigureAwait(false))
            {
                // a cancelled caller never receives the agent's answer to its cancel as if it were a result
                ct.ThrowIfCancellationRequested();
                yield return evt;
            }
        }
        finally
        {
            outbox.Writer.TryComplete();
        }

        ct.ThrowIfCancellationRequested();
    }
}
