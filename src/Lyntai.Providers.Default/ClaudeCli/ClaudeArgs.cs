using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the STATIC argv for a `claude` print-mode call — the one part of an invocation that is
/// this CLI's own vocabulary. Dynamic content — the prompt — always travels over stdin, never argv (prompts
/// carry newlines and shell metacharacters); the engine delivers it per
/// <see cref="Lyntai.Llm.Cli.ICliProviderDialect.PromptDelivery"/>.</summary>
internal static class ClaudeArgs
{
    /// <summary>The print-mode prefix every headless <c>claude</c> invocation opens with: <c>-p</c> (print
    /// mode, prompt from stdin) plus the stream-json output format both readers parse. Shared with
    /// <see cref="ClaudeAgentArgs"/> so the two claude paths can't drift on it. The DENY lists deliberately
    /// stay per-path — the agent run denies the flow tools that would hang it, which a completion has no
    /// reason to name.</summary>
    internal static readonly string[] PrintMode = ["-p", "--output-format", "stream-json", "--verbose"];

    public static IReadOnlyList<string> Build(string? model)
    {
        var args = new List<string>(PrintMode)
        {
            "--disallowed-tools", "AskUserQuestion", // no interactive UI tools from a library call
        };
        if (!string.IsNullOrEmpty(model))
        {
            args.Add("--model");
            args.Add(model);
        }
        return args;
    }

    // The PROMPT is not built here: flattening a message list into one blob of text is the same for every
    // CLI, so it lives in Core as CliProviderDialectBase.BuildPrompt (CliPrompt.Flatten).
}
