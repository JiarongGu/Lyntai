using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.CodexCli;

/// <summary>
/// Spawns the authenticated OpenAI <c>codex</c> CLI (no API key) and maps its <c>exec --json</c> JSONL to
/// <see cref="LlmReply"/>/<see cref="LlmChunk"/> + verdict. The command resolves from (in order): the ctor
/// override, <c>LYNTAI_PROVIDER_CMD</c>, <c>CODEX_CMD</c>, then a plain <c>codex</c> from PATH.
///
/// Like <see cref="Providers.ClaudeCli.ClaudeCliProvider"/>, this type is only the composition of
/// <see cref="CliProviderEngine"/> (everything generic about driving a CLI) and
/// <see cref="CodexCliDialect"/> (everything specific to codex), plus the declaration of which OPTIONAL
/// capabilities this backend actually has.
///
/// Note what is ABSENT: no <see cref="IProviderVersionInstaller"/>, because <c>codex update</c> takes no
/// version/channel argument — this backend cannot pin a version, so it doesn't claim to. That difference from
/// the claude provider is the capability model working as intended.
/// </summary>
public sealed class CodexCliProvider : ILlmProvider, IProviderInstallation, IProviderUpdater, IProviderAuth
{
    public const string ProviderId = "codex-cli";

    private readonly CliProviderEngine _engine;

    /// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn.</param>
    /// <param name="options">Timeout/model configuration.</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="command">Explicit command, e.g. a PORTABLE <c>codex</c> the host ships or unpacks itself
    /// rather than a global install (quote a path with spaces). Wins over the env seams.</param>
    /// <param name="provisioner">Optional MCP tool host for this provider.</param>
    /// <param name="environment">Extra environment variables for every spawn — a portable install usually
    /// wants its own <c>CODEX_HOME</c> so it neither reads nor mutates the machine-wide install's state.</param>
    /// <param name="dialect">A pre-configured dialect, to change codex-specific behaviour such as
    /// <see cref="CodexCliDialect.SandboxMode"/>. Defaults to a read-only sandbox.</param>
    public CodexCliProvider(
        IProcessRunner runner,
        LyntaiOptions options,
        ILogger<CodexCliProvider>? logger = null,
        string? command = null,
        ICliToolProvisioner? provisioner = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CodexCliDialect? dialect = null)
        => _engine = new CliProviderEngine(dialect ?? new CodexCliDialect(), runner, options, logger, command,
            provisioner, environment);

    /// <inheritdoc/>
    public string Id => ProviderId;

    /// <summary>Whether the <c>codex</c> CLI looks callable — see <see cref="CliProviderEngine.IsAvailable"/>
    /// (a portable copy is checked for presence; a BYO runner is trusted).</summary>
    public bool IsAvailable => _engine.IsAvailable;

    /// <inheritdoc/>
    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        _engine.CompleteAsync(req, ct);

    /// <inheritdoc/>
    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        _engine.StreamAsync(req, ct);

    /// <summary>Report the installed CLI without running a turn (<c>codex --version</c> →
    /// <c>"codex-cli 0.146.0"</c>).</summary>
    /// <remarks><see cref="ProviderProbeResult.Model"/> is null: codex has no turn-free model readout, and the
    /// probe never guesses one.</remarks>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) => _engine.ProbeAsync(ct);

    /// <summary>Run the CLI's OWN updater (<c>codex update</c>) and report what changed: probe → update →
    /// re-probe. It takes no target version — pinning is not available on this backend.</summary>
    public Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default) => _engine.UpdateAsync(ct);

    /// <summary>Report whether the CLI is signed in — and as whom — without running a turn
    /// (<c>codex login status</c>).</summary>
    /// <remarks>codex has NO machine-readable auth readout (no <c>--json</c> on that command), so this parses
    /// prose conservatively: an unrecognized wording reports <c>Authenticated: false</c> with the CLI's own
    /// words in <see cref="ProviderAuthStatus.Detail"/> rather than guessing a signed-in state. Ask
    /// <see cref="ProbeAsync"/> first when you need to tell "signed out" from "couldn't be asked".</remarks>
    public Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default) => _engine.StatusAsync(ct);

    /// <summary>Start the CLI's sign-in flow (<c>codex login</c>), then report the state it left behind.
    /// BLOCKS until the flow completes, fails, or the dialect's 10-minute budget expires; cancelling
    /// <paramref name="ct"/> abandons the wait.</summary>
    /// <remarks>codex takes no account-kind, email or SSO options here, so a
    /// <see cref="ProviderLoginRequest"/> carrying any of them is REFUSED without spawning (rather than
    /// silently ignored). Its credential-reading login modes are out of scope by design — Lyntai never carries
    /// a secret.</remarks>
    public Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest? request = null, CancellationToken ct = default) =>
        _engine.LoginAsync(request, ct);

    /// <summary>Sign the CLI out (<c>codex logout</c>) and report the state it left behind. MUTATES stored
    /// credentials — gate it behind a user action.</summary>
    public Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default) => _engine.LogoutAsync(ct);
}
