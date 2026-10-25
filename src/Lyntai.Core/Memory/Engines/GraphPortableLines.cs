using System.Text.Json;

namespace Lyntai.Memory.Engines;

/// <summary>The <c>graph</c> and <c>link</c> lines of an <see cref="IPortableMemory"/> file, written and read for
/// <see cref="GraphMemoryEngine"/>. The schema is <c>docs/memory.md</c> §Carry a memory to another store; every age is
/// relative to the exporting engine's head, as <see cref="IPortableMemoryGraphStore"/> reads and writes it.</summary>
internal static class GraphPortableLines
{
    public static async Task WriteAsync(TextWriter destination, string engine, MemoryGraphExport export,
        CancellationToken ct)
    {
        foreach (var node in export.Nodes)
        {
            ct.ThrowIfCancellationRequested();
            var subjects = export.Subjects.GetValueOrDefault(node.Id) ?? [];
            await MemoryPortableFormat.WriteLineAsync(destination, MemoryPortableFormat.Graph, engine, w =>
            {
                w.WriteNumber("id", node.Id);
                w.WriteString("taskKey", node.TaskKey);
                w.WriteString("scope", node.Scope);
                w.WriteString("headline", node.Headline);
                w.WriteString("content", node.Content);
                w.WriteString("grade", node.Grade.ToString());
                if (node.Metadata is { Count: > 0 } metadata)
                {
                    w.WriteStartObject("metadata");
                    foreach (var key in metadata.Keys.Order(StringComparer.Ordinal)) w.WriteString(key, metadata[key]);
                    w.WriteEndObject();
                }
                // a non-finite signal has no JSON spelling; every reader coerces one to neutral, so it travels as absent
                var signals = node.Signals.Values.Where(p => double.IsFinite(p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
                if (signals.Count > 0)
                {
                    w.WriteStartObject("signals");
                    foreach (var (name, value) in signals) w.WriteNumber(name, value);
                    w.WriteEndObject();
                }
                if (subjects.Count > 0)
                {
                    w.WriteStartArray("subjects");
                    foreach (var subject in subjects) w.WriteStringValue(subject);
                    w.WriteEndArray();
                }
                w.WriteString("createdAt", node.CreatedAt);
                w.WriteNumber("recallCount", node.RecallCount);
                w.WriteNumber("stability", node.Stability);
                w.WriteNumber("difficulty", node.Difficulty);
                w.WriteNumber("provenanceRetrievability", node.ProvenanceRetrievability);
                w.WriteNumber("provenanceSalience", node.ProvenanceSalience);
                Ages(w, node.Age, node.OrdinalAge, node.VolumeAge, node.ElapsedAge);
                // valid time only where there is some, so a memory that never stated any exports as it always did
                if (node.ValidFrom is { } from) w.WriteString("validFrom", from);
                if (node.ValidTo is { } to) w.WriteString("validTo", to);
                if (node.SupersededAt is { } at) w.WriteString("supersededAt", at);
                if (node.SupersededBy is { } by) w.WriteNumber("supersededBy", by);
            }, ct, node.Validity is null ? MemoryPortableFormat.Version : MemoryPortableFormat.TemporalVersion).ConfigureAwait(false);
        }
        foreach (var link in export.Links)
        {
            ct.ThrowIfCancellationRequested();
            await MemoryPortableFormat.WriteLineAsync(destination, MemoryPortableFormat.Link, engine, w =>
            {
                w.WriteNumber("from", link.From);
                w.WriteNumber("to", link.To);
                w.WriteString("kind", link.Kind);
                w.WriteNumber("weight", link.Weight);
                Ages(w, link.Age, link.OrdinalAge, link.VolumeAge, link.ElapsedAge);
            }, ct).ConfigureAwait(false);
        }
    }

    private static void Ages(Utf8JsonWriter w, double age, double ordinal, double volume, double elapsed)
    {
        w.WriteNumber("age", age);
        w.WriteNumber("ordinalAge", ordinal);
        w.WriteNumber("volumeAge", volume);
        w.WriteNumber("elapsedAge", elapsed);
    }

    /// <summary>Every line checked, before anything is written: graph lines keyed by the source id they carry, and
    /// link lines in that same id space.</summary>
    public static (List<CarriedNode> Nodes, List<GraphLink> Links) Read(
        IReadOnlyList<MemoryPortableLine> lines, string engine, string? scope)
    {
        var nodes = new List<CarriedNode>();
        var links = new List<GraphLink>();
        var ids = new HashSet<long>();
        foreach (var line in lines)
        {
            if (line.Record == MemoryPortableFormat.Link)
            {
                links.Add(new GraphLink(MemoryPortableFormat.Whole(line, "from"), MemoryPortableFormat.Whole(line, "to"),
                    MemoryPortableFormat.String(line.Body, "kind") ?? "", MemoryPortableFormat.Number(line, "weight"),
                    MemoryPortableFormat.Number(line, "age"), MemoryPortableFormat.Number(line, "ordinalAge"),
                    MemoryPortableFormat.Number(line, "volumeAge"), MemoryPortableFormat.Number(line, "elapsedAge")));
                continue;
            }
            if (line.Record != MemoryPortableFormat.Graph)
                throw MemoryPortableFormat.Refuse(line.Number, $"is a '{line.Record}' record, and engine '{engine}' takes "
                    + $"only '{MemoryPortableFormat.Graph}' and '{MemoryPortableFormat.Link}' records");

            var id = MemoryPortableFormat.Whole(line, "id");
            if (!ids.Add(id)) throw MemoryPortableFormat.Refuse(line.Number, $"repeats id {id}");
            var grade = MemoryPortableFormat.String(line.Body, "grade") switch
            {
                nameof(MemoryGrade.Associative) => MemoryGrade.Associative,
                nameof(MemoryGrade.Authoritative) => MemoryGrade.Authoritative,
                _ => throw MemoryPortableFormat.Refuse(line.Number,
                    $"carries no \"grade\" of {nameof(MemoryGrade.Associative)} or {nameof(MemoryGrade.Authoritative)}"),
            };
            var recallCount = MemoryPortableFormat.Whole(line, "recallCount", nonNegative: true);
            nodes.Add(new CarriedNode(id, new GraphNodeImport(
                scope ?? Required(line, "scope"), Required(line, "headline"), Required(line, "content"), grade,
                (line.Body.TryGetProperty("createdAt", out var created) && created.TryGetDateTimeOffset(out var at)
                    ? at : throw MemoryPortableFormat.Refuse(line.Number, "carries no \"createdAt\" time")).ToUniversalTime(),
                recallCount > int.MaxValue ? int.MaxValue : (int)recallCount,
                MemoryPortableFormat.Number(line, "stability", nonNegative: true), MemoryPortableFormat.Number(line, "difficulty"),
                Signals(line), Optional(line, "provenanceRetrievability"), Optional(line, "provenanceSalience"),
                MemoryPortableFormat.Map(line, "metadata"), Subjects(line),
                MemoryPortableFormat.Number(line, "age"), MemoryPortableFormat.Number(line, "ordinalAge"),
                MemoryPortableFormat.Number(line, "volumeAge"), MemoryPortableFormat.Number(line, "elapsedAge"))
            {
                ValidFrom = Instant(line, "validFrom"), ValidTo = Instant(line, "validTo"),
                SupersededAt = Instant(line, "supersededAt"),
            }, line.Body.TryGetProperty("supersededBy", out _) ? MemoryPortableFormat.Whole(line, "supersededBy") : null));
            // a successor with no instant would name who ended an entry and not when, which no reading can use
            if (nodes[^1].SupersededBy is not null && nodes[^1].Node.SupersededAt is null)
                throw MemoryPortableFormat.Refuse(line.Number, "carries a \"supersededBy\" and no \"supersededAt\"");
        }
        return (nodes, links);
    }

    /// <summary>One <c>graph</c> line read: the entry under the source's <paramref name="Id"/>, whose end lands with
    /// it, and the successor that ended it — named afterwards through
    /// <see cref="ITemporalMemoryGraphStore.SupersedeAsync"/>, because the successor's id in THIS store is known
    /// only then.</summary>
    /// <param name="Id">The entry's id in the source, read only to resolve links and successors.</param>
    /// <param name="Node">The entry, valid time included.</param>
    /// <param name="SupersededBy">The successor's id in the source, or null.</param>
    public readonly record struct CarriedNode(long Id, GraphNodeImport Node, long? SupersededBy)
    {
        /// <summary>Whether this entry carries any valid time at all — what a store that cannot hold it refuses.</summary>
        public bool IsTimed => Node.ValidFrom is not null || Node.ValidTo is not null || Node.SupersededAt is not null;
    }

    // an optional instant, refused where present and not one — a bad bound is never read as no bound
    private static DateTimeOffset? Instant(MemoryPortableLine line, string name) =>
        !line.Body.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null ? null
        : element.ValueKind == JsonValueKind.String && element.TryGetDateTimeOffset(out var at) ? at.ToUniversalTime()
        : throw MemoryPortableFormat.Refuse(line.Number, $"carries a \"{name}\" that is not a time");

    private static string Required(MemoryPortableLine line, string name) =>
        MemoryPortableFormat.String(line.Body, name) is { Length: > 0 } value
            ? value
            : throw MemoryPortableFormat.Refuse(line.Number, $"carries no \"{name}\"");

    // a provenance field is zero for anything predating it, so an absent one reads as zero
    private static long Optional(MemoryPortableLine line, string name) =>
        line.Body.TryGetProperty(name, out _) ? MemoryPortableFormat.Whole(line, name) : 0;

    private static MemorySignals Signals(MemoryPortableLine line)
    {
        if (!line.Body.TryGetProperty("signals", out var element) || element.ValueKind == JsonValueKind.Null)
            return MemorySignals.Empty;
        if (element.ValueKind != JsonValueKind.Object)
            throw MemoryPortableFormat.Refuse(line.Number, "carries \"signals\" that are not an object");
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in element.EnumerateObject())
            values[pair.Name] = pair.Value.ValueKind == JsonValueKind.Number && pair.Value.TryGetDouble(out var value)
                ? value
                : throw MemoryPortableFormat.Refuse(line.Number, $"carries a non-numeric signal '{pair.Name}'");
        return MemorySignals.From(values);
    }

    private static List<string> Subjects(MemoryPortableLine line)
    {
        if (!line.Body.TryGetProperty("subjects", out var element) || element.ValueKind == JsonValueKind.Null) return [];
        if (element.ValueKind != JsonValueKind.Array)
            throw MemoryPortableFormat.Refuse(line.Number, "carries \"subjects\" that are not an array");
        return [.. element.EnumerateArray().Select(s => s.ValueKind == JsonValueKind.String
            ? s.GetString()!
            : throw MemoryPortableFormat.Refuse(line.Number, "carries a subject that is not a string"))];
    }
}
