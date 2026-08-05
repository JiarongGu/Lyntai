using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>
/// Spawns the authenticated `claude` CLI (no API key) and maps its stream-json to
/// <see cref="LlmReply"/>/<see cref="LlmChunk"/> + verdict. The command resolves from (in order): the ctor
/// override, <c>LYNTAI_PROVIDER_CMD</c>, <c>CLAUDE_CMD</c>, then a plain <c>claude</c> from PATH — the env
/// seams are what let tests/e2e point at the deterministic stub.
///
/// Everything generic about driving a CLI backend lives in <see cref="CliProviderEngine"/>; everything
/// specific to THIS CLI lives in <see cref="ClaudeCliDialect"/>. This type is the composition of the two,
/// plus the declaration of which OPTIONAL capabilities the claude CLI actually has — which is why it is a
/// dozen forwarding members rather than a second copy of the spawn/verdict/streaming rules.
/// </summary>
public sealed class ClaudeCliProvider : ILlmProvider, IProviderInstallation, IProviderUpdater,
    IProviderVersionInstaller, IProviderAuth
{
    /// <summary>The router-facing id this provider answers to — name it in a candidate list, or in
    /// <c>UseDefaultCandidates</c>, to route to the claude CLI. Also the value of <see cref="Id"/>.</summary>
    public const string ProviderId = "claude-cli";

    private readonly CliProviderEngine _engine;

    /// <param name="runner">Process execution — BYO to sandbox, audit or remote the spawn.</param>
    /// <param name="options">Timeout/model configuration.</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="command">Explicit command, e.g. a PORTABLE <c>claude</c> the host ships or unpacks
    /// itself rather than a global PATH install (quote a path with spaces). Wins over the env seams.</param>
    /// <param name="provisioner">Optional MCP tool host for this provider.</param>
    /// <param name="environment">Extra environment variables for every spawn — a portable install usually
    /// wants its own <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's
    /// state (the maintenance seams honour it too, so a probe/auth check reports the PORTABLE state).</param>
    public ClaudeCliProvider(
        IProcessRunner runner,
        LyntaiOptions options,
        ILogger<ClaudeCliProvider>? logger = null,
        string? command = null,
        ICliToolProvisioner? provisioner = null,
        IReadOnlyDictionary<string, string>? environment = null)
        => _engine = new CliProviderEngine(new ClaudeCliDialect(), runner, options, logger, command, provisioner, environment);

    /// <inheritdoc/>
    public string Id => ProviderId;

    /// <summary>Whether the <c>claude</c> CLI looks callable — the resolved command on the local PATH for the
    /// built-in runner, optimistically true for a BYO <see cref="IProcessRunner"/> (which resolves commands in
    /// its own environment). See <see cref="CliProviderEngine.IsAvailable"/>.</summary>
    public bool IsAvailable => _engine.IsAvailable;

    /// <inheritdoc/>
    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        _engine.CompleteAsync(req, ct);

    /// <inheritdoc/>
    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        _engine.StreamAsync(req, ct);

    /// <summary>Report the installed CLI without running a turn (<c>claude --version</c>).</summary>
    /// <remarks><see cref="ProviderProbeResult.Model"/> is null against today's CLI — it has no turn-free way
    /// to report its resolved model, and the probe never guesses one. Read the model the CLI ACTUALLY used
    /// from <see cref="UsageFinal.Model"/> on an agent run; this fills in only if a future build labels one on
    /// its version line.</remarks>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) => _engine.ProbeAsync(ct);

    /// <summary>Run the CLI's OWN updater (<c>claude update</c>) and report what changed: probe → update →
    /// re-probe. The CLI has no check-only mode, so "an update was available" is reported after the fact as
    /// <see cref="ProviderUpdateResult.Updated"/> (the version moved). For a NAMED version, see
    /// <see cref="InstallAsync"/>.</summary>
    public Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default) => _engine.UpdateAsync(ct);

    /// <summary>Install a NAMED version of the CLI through its own installer
    /// (<c>claude install [stable|latest|&lt;version&gt;] [--force]</c>), so a host can PIN a known-good build.
    /// Reports the same <see cref="ProviderUpdateResult"/> as an update — including <c>Updated</c> for a
    /// DOWNGRADE, which is a legitimate outcome of pinning.</summary>
    /// <remarks>This drives the installer the CLI already ships; Lyntai still never downloads or stores a
    /// binary itself (<c>docs/DECISIONS.md</c> D26). A <see cref="ProviderInstallRequest.Version"/> the CLI
    /// would read as a flag is refused without spawning anything.</remarks>
    public Task<ProviderUpdateResult> InstallAsync(ProviderInstallRequest? request = null, CancellationToken ct = default) =>
        _engine.InstallAsync(request, ct);

    /// <summary>Report whether the CLI is signed in — and as whom — without running a turn
    /// (<c>claude auth status --json</c>).</summary>
    /// <remarks>The parsed state WINS over the exit code: a signed-out CLI may report its state and still exit
    /// non-zero, and that is an answer, not a broken backend. An output shape this build can't read reports
    /// <c>Authenticated: false</c> with the raw text in <see cref="ProviderAuthStatus.Detail"/> — it never
    /// guesses a signed-in state.</remarks>
    public Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default) => _engine.StatusAsync(ct);

    /// <summary>Start the CLI's sign-in flow (<c>claude auth login</c>), then report the state it left behind.
    /// BLOCKS until the flow completes, fails, or the dialect's 10-minute budget expires — the CLI opens a
    /// browser and waits, so a UI should show a spinner rather than poll <see cref="StatusAsync"/>. Cancelling
    /// <paramref name="ct"/> abandons the wait (and kills the process tree).</summary>
    /// <remarks><see cref="ProviderAuthResult.Succeeded"/> means the command reported success;
    /// <see cref="ProviderAuthResult.Status"/> is the authority on whether the backend is actually signed in.
    /// An unrecognized <see cref="ProviderLoginRequest.Mode"/> is REFUSED without spawning anything — a
    /// free-form account kind must never become an invented CLI flag.</remarks>
    public Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest? request = null, CancellationToken ct = default) =>
        _engine.LoginAsync(request, ct);

    /// <summary>Sign the CLI out (<c>claude auth logout</c>) and report the state it left behind. MUTATES the
    /// CLI's stored credentials — gate it behind a user action.</summary>
    public Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default) => _engine.LogoutAsync(ct);
}
