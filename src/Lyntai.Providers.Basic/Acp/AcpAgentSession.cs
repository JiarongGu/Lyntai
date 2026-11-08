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
/// limit frame, each through the door's <see cref="IAcpExtensionReader"/>s. The agent's prose never classifies.
/// <see cref="SessionEnded.SessionId"/> is the conversation's resume token.</para>
/// <para><b>Cancelling</b> the token sends <c>session/cancel</c> and gives the agent its grace period to answer
/// before the process is killed; the cancellation then propagates, as on the CLI doors.</para>
/// <para><b>Permission requests</b> follow <see cref="AgentSessionOptions.ToolPolicy"/>: read-only rejects each,
/// write allows each once, never for good, unless a write turn has an <see cref="IAcpPermissionApprover"/>, which
/// answers each instead. The client offers the agent no filesystem and no terminal.</para></summary>
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
    private readonly AgentAccountMapping? _accounts;
    private readonly TimeSpan _cancelGrace;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<IAcpExtensionReader> _extensions;
    private readonly IAcpPermissionApprover? _permissions;
    private readonly AcpPermissionWait _permissionWait;

    /// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn. It must implement
    /// <see cref="IProcessRunner.ExchangeLinesAsync(string, IReadOnlyList{string}, IAsyncEnumerable{string}, TimeSpan?, TimeSpan?, string?, IReadOnlyDictionary{string, string?}?, CancellationToken)"/>, which the shipped <see cref="ProcessRunner"/> does.</param>
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
    /// <param name="accountVariables">The other variables the agent reads for a key or a gateway's address, which an
    /// account's <see cref="AgentAccount.Environment"/> may set. Needs <paramref name="accountHomeVariable"/>; none
    /// means an account entry is refused.</param>
    /// <param name="extensions">What reads the agent's own extensions (<see cref="IAcpExtensionReader"/>), and exactly
    /// what the door reads: a reader left out is not consulted. Null = <see cref="AcpExtensionReaders.Default"/>; an
    /// empty set reads none.</param>
    /// <param name="permissionApprover">Who answers the agent's permission requests in a write turn
    /// (<see cref="IAcpPermissionApprover"/>); null = the tool policy answers them. A session built without one
    /// is the door that states <see cref="AgentAbilities.PermissionRequests"/> unsupported.</param>
    /// <param name="permissionWait">How long a request waits for the approver (<see cref="AcpPermissionWait"/>). Asking
    /// the approver's wait needs an approver and a runner whose <see cref="IProcessRunner.CanPauseInactivity"/> is true.</param>
    /// <exception cref="ArgumentException">The approver's wait with no approver.</exception>
    /// <exception cref="InvalidOperationException">The approver's wait on a runner that cannot be told to pause its inactivity
    /// window, which would end a turn the person is still deciding.</exception>
    public AcpAgentSession(
        IProcessRunner runner,
        LyntaiOptions options,
        string command,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? accountHomeVariable = null,
        TimeSpan? cancelGrace = null,
        ILogger<AcpAgentSession>? logger = null,
        TimeProvider? time = null,
        IEnumerable<string>? accountVariables = null,
        IEnumerable<IAcpExtensionReader>? extensions = null,
        IAcpPermissionApprover? permissionApprover = null,
        AcpPermissionWait permissionWait = AcpPermissionWait.HalfTheWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (permissionWait != AcpPermissionWait.HalfTheWindow)
        {
            if (permissionApprover is null)
                throw new ArgumentException("A wait that is the approver's needs an approver to hold the request.", nameof(permissionWait));
            if (!runner.CanPauseInactivity)
                throw new InvalidOperationException(
                    "The permission wait is the approver's, so the process runner must be told not to read it as a silent agent, and "
                    + $"{runner.GetType().Name} cannot be (IProcessRunner.CanPauseInactivity is false). Use a runner that can, or the default wait.");
        }
        _permissions = permissionApprover;
        _permissionWait = permissionWait;
        _extensions = extensions is null ? AcpExtensionReaders.Default : extensions.ToList();
        if (_extensions.Contains(null!)) throw new ArgumentException("A reader is null.", nameof(extensions));
        _runner = runner;
        _options = options;
        _command = command;
        _arguments = arguments ?? [];
        _environment = environment;
        _accounts = accountHomeVariable is null ? null : new AgentAccountMapping(accountHomeVariable, accountVariables);
        _cancelGrace = cancelGrace ?? DefaultCancelGrace;
        _logger = logger ?? NullLogger<AcpAgentSession>.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Run one turn and stream what the agent does.</summary>
    /// <param name="options">The turn. Its <see cref="AgentSessionOptions.WorkingDirectory"/> is the session's
    /// <c>cwd</c> (made absolute; the current directory when null). A blank resume token, an unusable MCP server
    /// and an account with no usable home or an entry the agent does not read, or one on an agent whose home
    /// variable is unknown, refuse the turn
    /// before anything is spawned.</param>
    /// <param name="ct">Cancels the turn: <c>session/cancel</c>, then the process.</param>
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!TryPrepare(options, out var launch, out var refusal))
        {
            yield return refusal!;
            yield break;
        }

        var tokens = launch!.Tokens;
        var cwd = launch.Cwd;
        var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var inactivity = launch.Inactivity;
        // a held request is the person's wait: its clock is the approver's, so the runner's window is paused while one is held
        var pause = _permissionWait == AcpPermissionWait.AsLongAsApproverHolds ? new InactivityPause() : null;
        var reader = new AcpAgentReader(options, cwd, ClientVersion, outbox.Writer, _time, _extensions, _permissions, inactivity, _logger, pause);
        reader.Start();

        // the runner is cancelled by this source and never by the caller's token, so a cancelled turn gets to say
        // session/cancel and hear the agent's answer first
        using var kill = new CancellationTokenSource();
        using var onCancel = ct.Register(() =>
        {
            if (reader.TryCancel()) kill.CancelAfter(_cancelGrace);
            else kill.Cancel();
        });

        var watch = new PromptWriteWatch(reader);
        IAsyncEnumerable<string>? lines = null;
        string? unsupported = null;
        try
        {
            var stdin = watch.Frames(outbox.Reader.ReadAllAsync(CancellationToken.None));
            lines = pause is null
                ? _runner.ExchangeLinesAsync(tokens[0], [.. tokens.Skip(1), .. _arguments], stdin, inactivity,
                    workingDirectory: cwd, environment: launch.Environment, ct: kill.Token)
                : _runner.ExchangeLinesAsync(tokens[0], [.. tokens.Skip(1), .. _arguments], stdin, pause, inactivity,
                    workingDirectory: cwd, environment: launch.Environment, ct: kill.Token);
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
            await foreach (var evt in CliAgentLoop.RunLinesAsync(
                lines, endAtTerminal: true, reader.Read, reader.Unread, _logger, "acp", announcesBeforeWrite: true, () => watch.Written, () => reader.PromptAnswered, ct).ConfigureAwait(false))
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

    /// <summary>What a spawn needs, once its options have passed every check made before a spawn.</summary>
    internal sealed record AcpLaunch(
        IReadOnlyList<string> Tokens, string Cwd, TimeSpan Inactivity, IReadOnlyDictionary<string, string?>? Environment);

    // the refusals made before anything is spawned, for a turn and for a run's open alike
    private bool TryPrepare(AgentSessionOptions options, out AcpLaunch? launch, out SessionEnded? refusal)
    {
        launch = null;
        if (!AgentMcpServers.TryValidate(options.McpServers, out var mcpRefusal))
        {
            refusal = CliAgentLoop.Refused(AgentMcpServers.RefusedSubtype, mcpRefusal);
            return false;
        }

        if (!AgentAccounts.TryCheckTurn(options.Environment, out var environmentRefusal))
        {
            refusal = CliAgentLoop.Refused(AgentAccounts.EnvironmentRefusedSubtype, environmentRefusal);
            return false;
        }

        IReadOnlyDictionary<string, string> accountEnvironment = new Dictionary<string, string>(0);
        string? accountRefusal = null;
        if (options.Account is not null && _accounts is null)
            accountRefusal = "this agent's configuration-home variable is not known, so an account cannot be handed to it.";
        else if (options.Account is not null
            && !AgentAccounts.TryMap(options.Account, _accounts!, out accountEnvironment, out accountRefusal))
            accountRefusal ??= "the account cannot be handed to the agent.";
        if (accountRefusal is not null)
        {
            refusal = CliAgentLoop.Refused(AgentAccounts.RefusedSubtype, accountRefusal);
            return false;
        }

        if (options.ResumeToken is not null && string.IsNullOrWhiteSpace(options.ResumeToken))
        {
            refusal = CliAgentLoop.Refused(AgentResumeToken.RefusedSubtype,
                "a blank resume token names no conversation. Pass the SessionId reported by SessionStarted/SessionEnded, or null to start a fresh session.");
            return false;
        }

        var tokens = CliCommand.Tokenize(_command);
        if (tokens.Count == 0)
        {
            refusal = CliAgentLoop.Refused("command-invalid", "the agent's launch command is blank.");
            return false;
        }

        refusal = null;
        launch = new AcpLaunch(
            tokens, Path.GetFullPath(options.WorkingDirectory ?? Directory.GetCurrentDirectory()), _options.ResolveAgentTimeout(options.TimeoutSeconds),
            AgentAccounts.Merge(AgentAccounts.MergeTurn(_environment, options.Environment), accountEnvironment));
        return true;
    }

    /// <summary>Open a run kept across turns on this door's agent (<see cref="AcpAgentRun"/>). A refusal made before a spawn, a
    /// runner that cannot pause its inactivity window, and an open the agent fails are each a run already ended.</summary>
    internal async Task<IAgentRun> OpenRunAsync(
        AgentSessionOptions options, AgentRunOptions? run, bool queuedWords, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!TryPrepare(options, out var launch, out var refusal)) return AcpAgentRun.AlreadyOver(refusal!);
        if (!_runner.CanPauseInactivity)
            return AcpAgentRun.AlreadyOver(CliAgentLoop.Refused("runner-unsupported",
                "a run is idle between turns and its process's inactivity window must be paused meanwhile, and this IProcessRunner cannot be told to (CanPauseInactivity is false)."));

        var host = new AcpAgentRun.Host(_runner, launch!, _arguments, _cancelGrace, _time, _extensions, _permissions, _permissionWait, _logger, ClientVersion);
        return await AcpAgentRun.OpenAsync(host, options, run ?? new AgentRunOptions(), queuedWords, ct).ConfigureAwait(false);
    }
}

/// <summary>Whether the prompt's frame was written to the agent: the runner asks for the frame after one only once it has
/// written the one before, so a frame is written when the line after it is asked for.</summary>
internal sealed class PromptWriteWatch(AcpAgentReader reader)
{
    private int _written;

    // an agent that answered the prompt had it, whether or not the writer has come back for the next line yet
    public bool Written => Volatile.Read(ref _written) == 1 || reader.PromptAnswered;

    // the runner's cancellation of its writer must reach the channel read, as it did before this wrapper stood between them
    public async IAsyncEnumerable<string> Frames(
        IAsyncEnumerable<string> frames, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var index = 0;
        await foreach (var frame in frames.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return frame;
            if (index == reader.PromptFrame) Volatile.Write(ref _written, 1);
            index++;
        }
    }
}
