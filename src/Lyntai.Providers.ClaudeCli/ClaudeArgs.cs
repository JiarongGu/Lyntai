using System.Text;
using Lyntai.Llm;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the STATIC argv for a `claude` print-mode call. Dynamic content — the prompt —
/// always travels over stdin, never argv (prompts carry newlines and shell metacharacters).</summary>
public static class ClaudeArgs
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

    /// <summary>Flatten the canonical message list into the single stdin prompt: a lone user message
    /// passes through verbatim; multi-message requests become role-labeled blocks. A JsonSchema
    /// request appends the structured-output instruction (design §6).</summary>
    public static string BuildPrompt(LlmRequest req)
    {
        string prompt;
        if (req.Messages.Count == 1 && req.Messages[0].Role == "user")
        {
            prompt = req.Messages[0].Content;
        }
        else
        {
            var sb = new StringBuilder();
            foreach (var m in req.Messages)
                sb.Append('[').Append(m.Role).Append("]\n").Append(m.Content).Append("\n\n");
            prompt = sb.ToString().TrimEnd();
        }

        if (!string.IsNullOrEmpty(req.JsonSchema))
        {
            prompt += "\n\nReply with a single JSON object conforming to this JSON schema, and nothing else:\n"
                + req.JsonSchema;
        }
        return prompt;
    }
}
