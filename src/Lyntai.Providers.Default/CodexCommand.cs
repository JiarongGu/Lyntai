using Lyntai.Llm.Cli;

namespace Lyntai.Providers.CodexCli;

/// <summary>codex-CLI command resolution for the AGENT SESSION (the provider goes through
/// <see cref="CliProviderEngine"/>, which resolves via its dialect). Keeps this backend's env precedence —
/// ctor override → <c>LYNTAI_PROVIDER_CMD</c> → <c>CODEX_CMD</c> → <c>codex</c> — in one place, expressed
/// against the shared <see cref="CliCommand"/> resolver so the precedence and the quote-aware tokenizer
/// can't drift from every other CLI provider's.</summary>
internal static class CodexCommand
{
    /// <summary>The dialect is the SINGLE declaration of this CLI's default command and env seams — reading
    /// them from it (rather than re-listing them here) is what keeps the session and the provider spawning the
    /// same binary. A second copy would drift silently: every test and the e2e stub drive
    /// <c>LYNTAI_PROVIDER_CMD</c>, the one entry both lists share.</summary>
    private static readonly CodexCliDialect Dialect = new();

    /// <summary>Resolve <paramref name="command"/> (or the env seams, or a plain <c>codex</c>) into the
    /// executable + any prefix args (e.g. the stub script passed to <c>node</c>).</summary>
    public static (string Exe, IReadOnlyList<string> PrefixArgs) Resolve(string? command) =>
        CliCommand.Resolve(command, Dialect.DefaultCommand, Dialect.CommandEnvironmentVariables);
}
