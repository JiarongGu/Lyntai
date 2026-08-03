using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Everything specific to the <c>claude</c> CLI, and nothing else: its command name, its print-mode
/// argv, its stream-json line format, and the self-maintenance subcommands it verifiably has. The generic
/// spawn/verdict/streaming/maintenance behaviour lives in <see cref="CliProviderEngine"/>.
///
/// Public so it can be composed directly (a host wiring its own <see cref="CliProviderEngine"/>, a test
/// asserting the vocabulary) — but the normal entry point is
/// <see cref="ClaudeCliBuilderExtensions.AddClaudeCliProvider"/>.</summary>
/// <remarks>Every maintenance command here was verified against a live CLI (<c>--help</c> on v2.1.220)
/// before being named. That matters more than usual for this backend: it treats an unrecognized token as a
/// PROMPT and answers it, so a guessed subcommand costs tokens on every call while the build stays green.</remarks>
public sealed class ClaudeCliDialect : CliProviderDialectBase
{
    /// <inheritdoc/>
    public override string Id => ClaudeCliProvider.ProviderId;

    /// <inheritdoc/>
    public override string DefaultCommand => "claude";

    /// <summary>The shared stub seam first, then this CLI's own override.</summary>
    public override IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD", "CLAUDE_CMD"];

    /// <summary>Print mode + stream-json, with interactive UI tools disallowed for a library call.</summary>
    public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest request) => ClaudeArgs.Build(request.Model);

    /// <summary>Decode one <c>stream-json</c> line into the engine's vocabulary.</summary>
    public override CliOutputEvent ParseLine(string line)
    {
        var evt = StreamJsonParser.Parse(line);
        return evt.Kind switch
        {
            StreamJsonEventKind.AssistantText => CliOutputEvent.Content(evt.Text),
            StreamJsonEventKind.Result => CliOutputEvent.Result(evt.Text, evt.Usage),
            _ => CliOutputEvent.Ignored,
        };
    }

    /// <summary><c>claude update</c> — the CLI's own updater, with no check-only mode (so "was an update
    /// available?" is answered after the fact, by the version comparison the engine does).</summary>
    public override IReadOnlyList<string>? UpdateArgs => ["update"];

    /// <summary><c>claude auth status --json</c>. <c>--json</c> is passed EXPLICITLY even though it is the
    /// CLI's default: a build predating <c>auth</c> rejects the unknown flag and exits non-zero, whereas a
    /// bare <c>auth status</c> would be taken as a prompt and quietly spend a turn.</summary>
    public override IReadOnlyList<string>? AuthStatusArgs => ["auth", "status", "--json"];

    /// <inheritdoc/>
    public override IReadOnlyList<string>? LogoutArgs => ["auth", "logout"];

    /// <inheritdoc/>
    public override ProviderAuthStatus? ParseAuthStatus(string output) => ClaudeAuthStatusJson.Parse(output);

    /// <summary><c>claude install [stable|latest|&lt;version&gt;] [--force]</c> — the CLI's own installer,
    /// which is how a host PINS a known-good build instead of taking whatever <c>update</c> gives it.</summary>
    public override bool TryBuildInstallArgs(ProviderInstallRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        var target = request?.Version?.Trim();
        if (FlagShaped(target))
        {
            args = [];
            refusal = $"'{target}' is not a version — a target starting with '-' would be read as a flag by the CLI";
            return false;
        }

        List<string> argv = ["install"];
        if (target is { Length: > 0 }) argv.Add(target);
        if (request?.Force == true) argv.Add("--force");
        args = argv;
        refusal = null;
        return true;
    }

    /// <summary><c>claude auth login [--claudeai | --console] [--email &lt;e&gt;] [--sso]</c>. The neutral
    /// <see cref="ProviderLoginRequest.Mode"/> is mapped onto this CLI's two account kinds; anything else is
    /// REFUSED rather than forwarded as an invented flag.</summary>
    public override bool TryBuildLoginArgs(ProviderLoginRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        List<string> argv = ["auth", "login"];
        args = argv;
        refusal = null;

        if (request?.Mode?.Trim() is { Length: > 0 } mode)
        {
            var flag = mode.ToLowerInvariant() switch
            {
                "claudeai" or "claude.ai" or "subscription" => "--claudeai",
                "console" or "api" => "--console",
                _ => null,
            };
            if (flag is null)
            {
                refusal = $"login mode '{mode}' is not an account kind this backend has " +
                    "(claudeai | claude.ai | subscription, or console | api) — pass none for its default";
                return false;
            }
            argv.Add(flag);
        }

        if (request?.Email?.Trim() is { Length: > 0 } email)
        {
            if (FlagShaped(email))
            {
                refusal = $"email '{email}' would be read as a flag by the CLI";
                return false;
            }
            argv.Add("--email");
            argv.Add(email);
        }

        if (request?.Sso == true) argv.Add("--sso");
        return true;
    }
}
