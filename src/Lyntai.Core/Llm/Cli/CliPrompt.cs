using System.Text;

namespace Lyntai.Llm.Cli;

/// <summary>Flattens the canonical message list into the single prompt a print-mode CLI takes. Nothing here
/// is backend-specific — a CLI accepts one blob of text, so every dialect gets the same flattening unless it
/// overrides <see cref="ICliProviderDialect.BuildPrompt"/>.</summary>
internal static class CliPrompt
{
    /// <summary>A lone user message passes through verbatim; a multi-message request becomes role-labeled
    /// blocks. A <see cref="LlmRequest.JsonSchema"/> request appends the structured-output instruction
    /// (design §6) — a CLI has no response-format parameter to carry it.</summary>
    public static string Flatten(LlmRequest req)
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
