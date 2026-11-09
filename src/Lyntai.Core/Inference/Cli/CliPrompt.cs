using System.Text;
using Lyntai.Inference;

namespace Lyntai.Inference.Cli;

/// <summary>Flattens the canonical message list into the single prompt a print-mode CLI takes. Nothing here
/// is backend-specific — a CLI accepts one blob of text, so every backend gets the same flattening unless it
/// overrides <see cref="ICliBackend.BuildPrompt"/>.</summary>
internal static class CliPrompt
{
    /// <summary>A lone user message passes through verbatim; a multi-message request becomes role-labeled
    /// blocks. A <see cref="TextRequest.JsonSchema"/> request appends the structured-output instruction
    /// (design §6) — a CLI has no response-format parameter to carry it.</summary>
    public static string Flatten(TextRequest req)
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
            {
                // a tool result names the call it answers, and an assistant turn lists the calls it made: without
                // either, a replayed tool loop reads as an empty [assistant] followed by unattributed results
                sb.Append('[').Append(m.Role);
                if (m.ToolCallId is { Length: > 0 } answered) sb.Append(' ').Append(answered);
                sb.Append("]\n").Append(m.Content).Append('\n');
                foreach (var call in m.ToolCalls ?? [])
                    sb.Append("[call ").Append(call.Id).Append("] ").Append(call.Name).Append(' ').Append(call.ArgumentsJson).Append('\n');
                sb.Append('\n');
            }
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
