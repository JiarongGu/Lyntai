using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>A consumer's settings file and a tool host's handed as ONE, because the <c>claude</c> CLI applies only the
/// LAST <c>--settings</c> it is given, the earlier file dropped whole rather than merged per key (measured on
/// 2.1.285).</summary>
internal static class ClaudeSettingsJson
{
    // strict on purpose: a file the merge cannot read goes to the CLI unchanged, and the CLI's own reader judges it
    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };

    /// <summary>The value of the last <c>--settings</c> in <paramref name="args"/>, or null when there is none.</summary>
    public static string? LastValue(IReadOnlyList<string> args)
    {
        for (var i = args.Count - 2; i >= 0; i--)
            if (args[i] == "--settings") return args[i + 1];
        return null;
    }

    /// <summary>Rewrite <paramref name="hostFile"/> as <paramref name="consumerFile"/>'s settings with the host's merged
    /// in (<see cref="Merge"/>), or return false — when either is not a fully qualified path to a readable JSON object,
    /// or the write fails. The host file is the tool host's per-call file, written through
    /// <see cref="Agents.McpCliContext.WriteTempFile"/> and deleted when the call ends; rewriting it in place keeps its
    /// owner-only mode.</summary>
    public static bool TryMergeInto(string hostFile, string consumerFile)
    {
        if (Read(consumerFile) is not { } mine || Read(hostFile) is not { } hosts) return false;
        Merge(mine, hosts);
        try
        {
            File.WriteAllText(hostFile, mine.ToJsonString());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Merge <paramref name="theirs"/> into <paramref name="mine"/>: objects key by key, lists joined without
    /// repeating an item, and any other clash — a clash of kinds included — keeps <paramref name="mine"/>'s value.</summary>
    internal static void Merge(JsonObject mine, JsonObject theirs)
    {
        foreach (var (key, value) in theirs)
        {
            if (!mine.TryGetPropertyValue(key, out var own)) mine[key] = value?.DeepClone();
            else if (own is JsonObject ownObject && value is JsonObject other) Merge(ownObject, other);
            else if (own is JsonArray list && value is JsonArray more)
                foreach (var item in more)
                    if (!list.Any(x => JsonNode.DeepEquals(x, item))) list.Add(item?.DeepClone());
        }
    }

    private static JsonObject? Read(string path)
    {
        // a relative path resolves against THIS process's directory, and the CLI's is a different one
        if (!Path.IsPathFullyQualified(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: Strict) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
