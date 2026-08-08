using System.Buffers;
using System.Text;
using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Memory;

/// <summary>The model-facing half of graph memory: a pair of tools that let an agent open on a cheap index
/// and then dig where it turns out to need depth.
/// <para>Tool names are prefixed per engine (<c>project_recall</c>, <c>project_expand</c>) rather than a
/// single multiplexed tool taking an engine argument. Fewer tools would read better, but it would let the
/// model consult the WRONG memory — the same accuracy failure the authoritative/associative split exists to
/// prevent — and a wrong memory is worse than a missing one.</para>
/// <para>INTERNAL: consumers reach these through <c>AddMemoryTools</c> and never construct one, so this
/// type is an implementation detail rather than surface. It is exposed to the tests via
/// <c>InternalsVisibleTo</c>.</para></summary>
internal static class MemoryTools
{
    /// <summary>Separates the engine from the id in a reference the model echoes back. Engine names are
    /// caller-chosen and may contain <c>/</c>; ids are numeric or hex, so parsing on the LAST occurrence is
    /// unambiguous whatever the engine is called.</summary>
    internal const string RefSeparator = "::";

    /// <summary>Sanitize an engine name into something a tool name may contain — hierarchical member names
    /// carry <c>/</c>, which no provider accepts.</summary>
    internal static string ToolPrefix(string engineName)
    {
        var sb = new StringBuilder(engineName.Length);
        foreach (var ch in engineName)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        return sb.ToString().Trim('_');
    }

    internal static string Format(MemoryRef reference) =>
        string.Concat(reference.Engine, RefSeparator, reference.Id);

    internal static MemoryRef? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var at = text.LastIndexOf(RefSeparator, StringComparison.Ordinal);
        return at <= 0 || at + RefSeparator.Length >= text.Length
            ? null
            : new MemoryRef(text[..at], text[(at + RefSeparator.Length)..]);
    }

    /// <summary>Render a recall as the observation fed back to the model. Hand-written with
    /// <see cref="Utf8JsonWriter"/> — no reflection serialization, so this stays AOT-clean.</summary>
    internal static string Render(MemoryRecall recall)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("ran", recall.Ran.ToString());
            writer.WriteStartArray("items");
            foreach (var item in recall.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("ref", Format(item.Reference));
                writer.WriteString("headline", item.Headline);
                // ALWAYS written, null when withheld: an explicit null tells the model there is more text
                // to fetch, which is the affordance that makes it call expand. Omitting the key is silent.
                writer.WriteString("content", item.Content);
                writer.WriteString("grade", item.Grade.ToString());
                writer.WriteNumber("links", item.Degree);
                writer.WriteNumber("retrievability", Math.Round(item.Retrievability, 3));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string? ReadString(string argumentsJson, string name)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static int? ReadInt(string argumentsJson, string name)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(name, out var value) &&
                   value.TryGetInt32(out var n)
                ? n
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The recall tool for <paramref name="engine"/> — returns headlines, not full text.</summary>
    internal static ITool Recall(IMemoryEngine engine, string prefix, string taskKey, string? scope) =>
        new FunctionTool(
            $"{prefix}_recall",
            async (argumentsJson, ct) =>
            {
                var (task, defaultScope) = MemoryToolScope.Resolve(taskKey, scope);
                var recall = await engine.RecallAsync(new MemoryQuery(
                    task,
                    ReadString(argumentsJson, "scope") ?? defaultScope,
                    ReadString(argumentsJson, "query"),
                    ReadInt(argumentsJson, "limit")), ct).ConfigureAwait(false);
                return Render(recall);
            },
            $"Search remembered context in '{engine.Name}'. Returns short headlines with a 'ref' for each — " +
            $"call {prefix}_expand with a ref to read the full text and what it is linked to. Items marked " +
            "Authoritative are exact facts and already carry their full content.",
            """
            {"type":"object","properties":{
              "query":{"type":"string","description":"What to look for. Omit for the most recently used."},
              "scope":{"type":"string","description":"Optional variant to search within."},
              "limit":{"type":"integer","description":"Maximum items to return."}
            }}
            """);

    /// <summary>The expand tool for <paramref name="engine"/> — full text plus neighbours.</summary>
    internal static ITool Expand(IMemoryEngine engine, string prefix) =>
        new FunctionTool(
            $"{prefix}_expand",
            async (argumentsJson, ct) =>
            {
                var reference = Parse(ReadString(argumentsJson, "ref"));
                if (reference is not MemoryRef target)
                    return """{"error":"pass the 'ref' of an item returned by the matching recall tool"}""";
                if (engine is not IExpandableMemory expandable)
                    return """{"error":"this memory cannot be expanded"}""";

                var recall = await expandable
                    .ExpandAsync(target, hops: ReadInt(argumentsJson, "hops") ?? 1, ct: ct)
                    .ConfigureAwait(false);
                return Render(recall);
            },
            $"Read the full text of one item in '{engine.Name}' and the headlines of what it is linked to. " +
            $"Pass the 'ref' from a {prefix}_recall result.",
            """
            {"type":"object","required":["ref"],"properties":{
              "ref":{"type":"string","description":"The 'ref' of an item from the matching recall tool."},
              "hops":{"type":"integer","description":"How far to walk from it. Defaults to 1."}
            }}
            """);
}
