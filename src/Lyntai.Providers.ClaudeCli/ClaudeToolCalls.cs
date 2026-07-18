using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Helper utilities for interpreting claude tool-call argument schemas. Keeps claude's
/// tool-argument conventions out of Core — the app's edit-tracker or file-watcher imports this.</summary>
public static class ClaudeToolCalls
{
    /// <summary>Parse the <c>file_path</c> argument from a claude tool call (e.g. Edit, Write, Read).
    /// Returns null when the argument is absent, the JSON is malformed, or the root is not an object.</summary>
    public static string? FilePathOf(ToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.ArgumentsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return root.TryGetProperty("file_path", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
