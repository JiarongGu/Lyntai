using Lyntai.Inference;
using Lyntai.Inference.Cli;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the STATIC argv for a `claude` print-mode call — the one part of an invocation that is
/// this CLI's own vocabulary. Dynamic content — the prompt — always travels over stdin, never argv (prompts
/// carry newlines and shell metacharacters); the engine delivers it per
/// <see cref="Lyntai.Inference.Cli.ICliBackend.PromptDelivery"/>.</summary>
internal static class ClaudeArgs
{
    /// <summary>The print-mode prefix every headless <c>claude</c> invocation opens with: <c>-p</c> (print
    /// mode, prompt from stdin) plus the stream-json output format both readers parse. Shared with
    /// <see cref="ClaudeAgentArgs"/> so the two claude paths can't drift on it. The DENY lists deliberately
    /// stay per-path — the agent run denies the flow tools that would hang it, which a completion has no
    /// reason to name.</summary>
    internal static readonly string[] PrintMode = ["-p", "--output-format", "stream-json", "--verbose"];

    public static IReadOnlyList<string> Build(string? model, ClaudeCompletionOptions? completion = null)
    {
        // no interactive UI tools from a library call — always, and first, so a caller only ever ADDS denials
        List<string> denied = ["AskUserQuestion"];
        foreach (var tool in completion?.DisallowedTools ?? [])
            if (!denied.Contains(tool, StringComparer.Ordinal)) denied.Add(tool);

        var args = new List<string>(PrintMode) { "--disallowed-tools", string.Join(",", denied) };
        AddScope(args, completion?.SettingSources, completion?.StrictMcpConfig == true);
        if (!string.IsNullOrEmpty(model))
        {
            args.Add("--model");
            args.Add(model);
        }
        return args;
    }

    /// <summary>What a spawn loads beyond what it is given — <c>--setting-sources</c> and <c>--strict-mcp-config</c>
    /// — shared by the completion and agent paths so the two cannot drift. Nothing is added when neither is
    /// set.</summary>
    internal static void AddScope(List<string> args, IReadOnlyList<string>? settingSources, bool strictMcpConfig)
    {
        if (settingSources is not null)
        {
            args.Add("--setting-sources");
            args.Add(string.Join(",", settingSources));   // an EMPTY list is an empty value: load no source
        }
        if (strictMcpConfig) args.Add("--strict-mcp-config");
    }

    /// <summary>A <c>SettingSources</c> list, copied — or refused where an entry is one the CLI would misread: null,
    /// empty, holding a comma or whitespace (the value is ONE comma-joined token), or flag-shaped. An unknown NAME is
    /// the CLI's to refuse, and it does before any turn.</summary>
    /// <exception cref="ArgumentException">An entry the CLI would misread.</exception>
    internal static IReadOnlyList<string>? CheckSettingSources(IReadOnlyList<string>? sources, string name)
    {
        if (sources is null) return null;
        foreach (var source in sources)
            if (string.IsNullOrEmpty(source) || source.StartsWith('-')
                || source.Any(c => c == ',' || char.IsWhiteSpace(c)))
                throw new ArgumentException(
                    $"'{source}' is not a setting source the CLI can be handed: one name each (user, project, local), "
                    + "never empty, flag-shaped, or holding a comma or whitespace.", name);
        return [.. sources];
    }

    // The PROMPT is not built here: flattening a message list into one blob of text is the same for every
    // CLI, so it lives in Core as CliBackendBase.BuildPrompt (CliPrompt.Flatten).
}
