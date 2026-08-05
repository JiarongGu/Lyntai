using Lyntai.Llm.Cli;

namespace Lyntai.Providers.CodexCli;

/// <summary>codex-CLI command resolution for the AGENT SESSION (the provider goes through
/// <see cref="CliProviderEngine"/>, which resolves via its dialect). Keeps this backend's env precedence —
/// ctor override → <c>LYNTAI_PROVIDER_CMD</c> → <c>CODEX_CMD</c> → <c>codex</c> — in one place, expressed
/// against the shared <see cref="CliCommand"/> resolver so the precedence and the quote-aware tokenizer
/// can't drift from every other CLI provider's.</summary>
internal static class CodexCommand
{
    /// <summary>The env seams consulted, in order, when no explicit command is given. Deliberately the same
    /// list <see cref="CodexCliDialect.CommandEnvironmentVariables"/> names, so pointing the stub at one seam
    /// redirects the provider and the session alike.</summary>
    private static readonly string[] EnvironmentVariables = ["LYNTAI_PROVIDER_CMD", "CODEX_CMD"];

    /// <summary>Resolve <paramref name="command"/> (or the env seams, or a plain <c>codex</c>) into the
    /// executable + any prefix args (e.g. the stub script passed to <c>node</c>).</summary>
    public static (string Exe, IReadOnlyList<string> PrefixArgs) Resolve(string? command) =>
        CliCommand.Resolve(command, "codex", EnvironmentVariables);
}
