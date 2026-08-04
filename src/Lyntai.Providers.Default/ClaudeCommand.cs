using Lyntai.Llm.Cli;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>claude-CLI command resolution for the AGENT SESSION (the provider goes through
/// <see cref="CliProviderEngine"/>, which resolves via its dialect). Keeps this package's env precedence —
/// ctor override → <c>LYNTAI_PROVIDER_CMD</c> → <c>CLAUDE_CMD</c> → <c>claude</c> — in one place, expressed
/// against the shared <see cref="CliCommand"/> resolver so the precedence and the quote-aware tokenizer
/// can't drift from every other CLI provider's.</summary>
internal static class ClaudeCommand
{
    /// <summary>The env seams consulted, in order, when no explicit command is given.</summary>
    private static readonly string[] EnvironmentVariables = ["LYNTAI_PROVIDER_CMD", "CLAUDE_CMD"];

    /// <summary>Resolve <paramref name="command"/> (or the env seams, or a plain <c>claude</c>) into the
    /// executable + any prefix args (e.g. the stub script passed to <c>node</c>).</summary>
    public static (string Exe, IReadOnlyList<string> PrefixArgs) Resolve(string? command) =>
        CliCommand.Resolve(command, "claude", EnvironmentVariables);

    /// <summary>Split a command line into tokens, honoring double-quoted spans (paths with spaces).</summary>
    public static List<string> Tokenize(string commandLine) => CliCommand.Tokenize(commandLine);
}
