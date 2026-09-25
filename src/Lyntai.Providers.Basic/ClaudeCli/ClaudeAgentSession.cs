using Lyntai.Inference;
using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Lyntai.Providers.Basic;

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
    // the backend is the single declaration of this CLI's default command and env seams, shared with the provider
    private static readonly ClaudeCliBackend Backend = new();

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
    /// <see cref="ClaudeCliProvider"/> has, for the same reason: a portable install usually wants its own
    /// <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's state. Pass the
    /// provider and the session the same value.</param>
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

    /// <summary>Run one claude turn and stream what the agent does.</summary>
    /// <param name="options">The turn. <see cref="AgentSessionOptions.McpServers"/> is rendered into a
    /// <c>--mcp-config</c> document written to an owner-only temp file and DELETED when the turn ends —
    /// alongside a <c>ClaudeAgentOptions.McpConfigPath</c> the caller supplied, never instead of it. An
    /// entry that cannot be rendered REFUSES the turn (a single <see cref="SessionEnded"/> with
    /// <see cref="ProviderVerdict.Unsupported"/>) rather than being dropped, because an agent that silently lost
    /// the tools it exists to use looks like a working agent. So does a
    /// <see cref="AgentSessionOptions.ResumeToken"/> the CLI would read as an OPTION rather than an id: blank,
    /// or starting with <c>-</c>.</param>
    /// <param name="ct">Cancels the turn and kills the process tree.</param>
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!AgentMcpServers.TryValidate(options.McpServers, out var mcpRefusal))
        {
            yield return CliAgentLoop.Refused(AgentMcpServers.RefusedSubtype, mcpRefusal);
            yield break;
        }

        // written before the spawn, deleted in the finally below — the file carries whatever secrets the
        // caller's servers need (a bearer token, a stdio server's env), so it must not outlive the turn
        var tempFiles = new List<string>();
        string Write(string kind, string content)
        {
            var path = CliTempFile.Write(kind, content);
            tempFiles.Add(path);
            return path;
        }

        if (!ClaudeAgentArgs.TryBuild(options, Write, out var agentArgs, out var refusal))
        {
            yield return CliAgentLoop.Refused(AgentResumeToken.RefusedSubtype, refusal);
            yield break;
        }

        try
        {
            var (exe, prefixArgs) = CliCommand.Resolve(_command, Backend);
            var reader = new StreamJsonAgentReader();
            var turn = CliAgentLoop.RunAsync(_runner, exe, [.. prefixArgs, .. agentArgs], options.Prompt,
                _options.ResolveTimeout(options.TimeoutSeconds), options, _environment, reader.Read, _logger,
                "claude", ct);
            await foreach (var evt in turn.ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            foreach (var path in tempFiles) CliTempFile.TryDelete(path);
        }
    }
}
