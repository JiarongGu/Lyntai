using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Helper utilities for interpreting claude tool-call argument schemas. Keeps claude's
/// tool-argument conventions out of Core — the app's edit-tracker or file-watcher imports this.</summary>
public static class ClaudeToolCalls
{
    /// <summary>Parse the path argument from a claude write-tool call. Checks the three write-tool path
    /// argument names in order — <c>file_path</c> (Edit/Write/Read), then <c>notebook_path</c>
    /// (NotebookEdit), then <c>path</c> — so an edit-tracker built from the agent stream doesn't silently
    /// miss NotebookEdit (or any <c>path</c>-arg tool) writes. Returns null when none is present, the JSON
    /// is malformed, or the root is not an object.</summary>
    public static string? FilePathOf(ToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.ArgumentsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return StringArg(root, "file_path") ?? StringArg(root, "notebook_path") ?? StringArg(root, "path");
        }
        catch
        {
            return null;
        }
    }

    private static string? StringArg(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
