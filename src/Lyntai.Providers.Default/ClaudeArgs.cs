using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the STATIC argv for a `claude` print-mode call — the one part of an invocation that is
/// this CLI's own vocabulary. Dynamic content — the prompt — always travels over stdin, never argv (prompts
/// carry newlines and shell metacharacters); the engine delivers it per
/// <see cref="Lyntai.Llm.Cli.ICliProviderDialect.PromptDelivery"/>.</summary>
internal static class ClaudeArgs
{
    public static IReadOnlyList<string> Build(string? model)
    {
        var args = new List<string>
        {
            "-p",                                   // print mode, prompt from stdin
            "--output-format", "stream-json",
            "--verbose",
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
