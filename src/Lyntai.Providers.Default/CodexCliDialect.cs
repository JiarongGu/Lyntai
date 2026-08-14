using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Providers.CodexCli;

/// <summary>Everything specific to the OpenAI <c>codex</c> CLI, and nothing else. The generic
/// spawn/verdict/streaming/maintenance behaviour lives in <see cref="CliProviderEngine"/>.</summary>
/// <remarks>
/// <para>Every command and flag here was MEASURED against codex-cli 0.146.0 (2026-08-04) via <c>--help</c>,
/// plus a real successful turn (through <c>--oss</c> + a local model, so no tokens were spent) and a real
/// failed one. That is not ceremony: <c>codex</c>'s usage is <c>codex [OPTIONS] [PROMPT]</c>, so an
/// unrecognized SUBCOMMAND is taken as a prompt and starts a turn — the same trap the claude CLI has.
/// (Unrecognized FLAGS are safe: codex is clap-based and errors out.)</para>
/// <para>Three measured details drive the argv below, and each would break the provider if guessed wrong:
/// <c>--skip-git-repo-check</c> is REQUIRED because the engine spawns from a neutral temp directory and codex
/// otherwise refuses to run outside a git repository; <c>--sandbox read-only</c> keeps a text completion from
/// editing the caller's disk; and the prompt is read from stdin when the positional argument is <c>-</c>.</para>
/// <para>Deliberately NOT claimed: <see cref="ICliProviderDialect.TryBuildInstallArgs"/> stays refused —
/// <c>codex update</c> takes no version/channel argument, so this backend genuinely cannot pin a version, and
/// <see cref="CodexCliProvider"/> therefore does not implement <c>IProviderVersionInstaller</c> at all.</para>
/// </remarks>
public sealed class CodexCliDialect : CliProviderDialectBase
{
    /// <inheritdoc/>
    public override string Id => CodexCliProvider.ProviderId;

    /// <inheritdoc/>
    public override string DefaultCommand => "codex";

    /// <summary>The shared stub seam first, then this CLI's own override.</summary>
    public override IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD", "CODEX_CMD"];

    /// <summary>The sandbox policy for a completion. <c>read-only</c> by default: this seam is a TEXT
    /// completion, so the agent must not edit the caller's working tree to produce one. Raise it only if you
    /// deliberately want codex acting on disk (<c>workspace-write</c>, <c>danger-full-access</c>).</summary>
    public string SandboxMode { get; init; } = CodexExecArgs.ReadOnlySandbox;

    /// <summary><c>codex exec --json ... -</c>: non-interactive, JSONL events on stdout, prompt from stdin.
    /// Built by <see cref="CodexExecArgs"/>, shared with <see cref="CodexAgentSession"/> so a flag (notably
    /// <c>--skip-git-repo-check</c>) can never go missing from one of the two paths.</summary>
    public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest request) =>
        CodexExecArgs.Build(SandboxMode, request.Model);

    /// <inheritdoc/>
    public override CliOutputEvent ParseLine(string line) => CodexJsonlParser.Parse(line);

    /// <summary><c>codex update</c> — measured, and it takes no target, so there is nothing to pin.</summary>
    public override IReadOnlyList<string>? UpdateArgs => ["update"];

    /// <summary><c>codex login status</c> — prose, not JSON (this CLI has no <c>--json</c> for it).</summary>
    public override IReadOnlyList<string>? AuthStatusArgs => ["login", "status"];

    /// <summary><c>codex logout</c> — a top-level command here, not <c>auth logout</c>.</summary>
    public override IReadOnlyList<string>? LogoutArgs => ["logout"];

    /// <inheritdoc/>
    public override ProviderAuthStatus? ParseAuthStatus(string output) => CodexAuthStatusText.Parse(output);

    /// <summary><c>codex login</c> — the browser/account flow. The credential-reading modes codex also offers
    /// (<c>--with-api-key</c> / <c>--with-access-token</c>, which read a secret from stdin) are deliberately
    /// NOT exposed: Lyntai does not carry or store credentials (<c>docs/DECISIONS.md</c> D20), and
    /// <see cref="ProviderLoginRequest"/> has no field for one. Use the CLI directly for those.</summary>
    public override bool TryBuildLoginArgs(ProviderLoginRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        args = [];
        refusal = null;

        if (request?.Mode?.Trim() is { Length: > 0 } mode)
        {
            refusal = $"login mode '{mode}' is not an account kind this backend has — codex login takes no " +
                "account-kind flag, and its --with-api-key / --with-access-token modes read a SECRET from " +
                "stdin, which Lyntai deliberately never carries; run `codex login` yourself for those";
            return false;
        }
        if (request?.Sso == true)
        {
            refusal = "codex login has no SSO flag";
            return false;
        }
        if (request?.Email?.Trim() is { Length: > 0 })
        {
            refusal = "codex login takes no --email hint";
            return false;
        }

        args = ["login"];
        return true;
    }
}
