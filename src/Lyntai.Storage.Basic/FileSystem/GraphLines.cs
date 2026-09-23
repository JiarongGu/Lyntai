using System.Globalization;
using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Storage.FileSystem;

internal abstract record GraphLine;

internal sealed record HeaderLine(long Schema, string? Engine, long Ids) : GraphLine;

internal sealed record TotalsLine(string Engine, GraphTotals Totals) : GraphLine;

internal sealed record NodeLine(long Id, GraphNodeState State) : GraphLine;

internal sealed record EdgeLine(GraphEdgeKey Key, GraphEdgeState Edge) : GraphLine;

internal sealed record SubjectsLine(string Engine, long NodeId, IReadOnlyList<string> Subjects) : GraphLine;

internal sealed record ReviewLine(MemoryReview Review) : GraphLine;

/// <summary>
/// One JSON object per line for the graph store's journals. Each record line ASSIGNS the full state of one
/// record, so a journal is replayed by keeping the last line per record. Doubles round-trip exactly; a
/// non-finite one is written as the string <c>"NaN"</c>, <c>"Infinity"</c> or <c>"-Infinity"</c>, so what is
/// persisted is exactly what the in-memory graph held.
/// </summary>
internal static class GraphLines
{
    public static string Header(string engine, long ids) => Encode(w =>
    {
        w.WriteNumber("lyntai", RecordFile.Schema);
        w.WriteString("engine", engine);
        w.WriteNumber("ids", ids);
    });

    public static string Totals(string engine, GraphTotals t) => Encode(w =>
    {
        w.WriteString("totals", engine);
        Number(w, "position", t.Position);
        w.WriteNumber("ordinal", t.Ordinal);
        w.WriteNumber("chars", t.Chars);
        Time(w, "at", t.EncodedAt);
    });

    public static string Node(long id, GraphNodeState s) => Encode(w =>
    {
        w.WriteNumber("node", id);
        w.WriteNumber("recalls", s.RecallCount);
        Number(w, "stability", s.Stability);
        Number(w, "difficulty", s.Difficulty);
        Number(w, "position", s.LastRecalledPosition);
        w.WriteNumber("ordinal", s.EncodingOrdinal);
        w.WriteNumber("chars", s.EncodingChars);
        Time(w, "at", s.EncodingAt);
        if (s.Signals.Count == 0) w.WriteNull("signals");
        else
        {
            w.WriteStartObject("signals");
            foreach (var (name, value) in s.Signals.Values.OrderBy(p => p.Key, StringComparer.Ordinal)) Number(w, name, value);
            w.WriteEndObject();
        }
        w.WriteStartObject("provenance");
        w.WriteNumber("retrievability", s.ProvenanceRetrievability);
        w.WriteNumber("salience", s.ProvenanceSalience);
        w.WriteEndObject();
    });

    public static string Edge(GraphEdgeKey key, GraphEdgeState e) => Encode(w =>
    {
        w.WriteStartArray("edge");
        w.WriteNumberValue(key.From);
        w.WriteNumberValue(key.To);
        w.WriteStringValue(key.Kind);
        w.WriteEndArray();
        Number(w, "weight", e.Weight);
        Number(w, "position", e.Position);
        w.WriteNumber("ordinal", e.Ordinal);
        w.WriteNumber("chars", e.Chars);
        Time(w, "at", e.At);
    });

    public static string Subjects(string engine, long nodeId, IReadOnlyList<string> subjects) => Encode(w =>
    {
        w.WriteNumber("subjects", nodeId);
        w.WriteString("engine", engine);
        w.WriteStartArray("list");
        foreach (var s in subjects) w.WriteStringValue(s);
        w.WriteEndArray();
    });

    public static string Review(MemoryReview r) => Encode(w =>
    {
        w.WriteNumber("review", r.Id);
        w.WriteString("engine", r.Engine);
        w.WriteNumber("node", r.NodeId);
        w.WriteString("batch", r.BatchId.ToString("D", CultureInfo.InvariantCulture));
        Time(w, "at", r.CreatedAt);
        Number(w, "preAge", r.PreAge);
        Number(w, "preStability", r.PreStability);
        Number(w, "preDifficulty", r.PreDifficulty);
        Number(w, "preStrength", r.PreStrength);
        Number(w, "preStrengthAge", r.PreStrengthAge);
        if (r.ReviewGrade is double g) Number(w, "grade", g); else w.WriteNull("grade");
        Number(w, "postStability", r.PostStability);
        Number(w, "postDifficulty", r.PostDifficulty);
        w.WriteNumber("retrievability", r.ProvenanceRetrievability);
        if (r.Verified is bool v) w.WriteBoolean("verified", v); else w.WriteNull("verified");
    });

    /// <summary>The record a line holds; <see cref="FormatException"/> for anything that is not one.</summary>
    public static GraphLine Parse(JsonElement e)
    {
        try
        {
            if (e.ValueKind != JsonValueKind.Object) throw new FormatException("a graph line is a JSON object");
            if (e.TryGetProperty("lyntai", out var schema))
                return new HeaderLine(schema.GetInt64(), OptionalString(e, "engine"), e.TryGetProperty("ids", out var ids) ? ids.GetInt64() : 0);
            if (e.TryGetProperty("totals", out var engine))
                return new TotalsLine(engine.GetString()!, new GraphTotals(Number(e, "position"), Long(e, "ordinal"),
                    Long(e, "chars"), Time(e, "at")));
            if (e.TryGetProperty("edge", out var edge))
                return new EdgeLine(new GraphEdgeKey(edge[0].GetInt64(), edge[1].GetInt64(), edge[2].GetString()!),
                    new GraphEdgeState(Number(e, "weight"), Number(e, "position"), Long(e, "ordinal"), Long(e, "chars"),
                        Time(e, "at")));
            if (e.TryGetProperty("subjects", out var subjectsOf))
                return new SubjectsLine(e.GetProperty("engine").GetString()!, subjectsOf.GetInt64(),
                    [.. e.GetProperty("list").EnumerateArray().Select(s => s.GetString()!)]);
            // Checked before "node": a review line also carries a "node" field (the reviewed node's id), which
            // would otherwise collide with NodeLine's own discriminator key.
            if (e.TryGetProperty("review", out var review))
                return new ReviewLine(new MemoryReview(review.GetInt64(), e.GetProperty("engine").GetString()!,
                    Long(e, "node"), Guid.Parse(e.GetProperty("batch").GetString()!), Time(e, "at"), Number(e, "preAge"),
                    Number(e, "preStability"), Number(e, "preDifficulty"), Number(e, "preStrength"),
                    Number(e, "preStrengthAge"), e.GetProperty("grade").ValueKind == JsonValueKind.Null ? null : Number(e, "grade"),
                    Number(e, "postStability"), Number(e, "postDifficulty"), Long(e, "retrievability"),
                    e.GetProperty("verified").ValueKind == JsonValueKind.Null ? null : e.GetProperty("verified").GetBoolean()));
            if (e.TryGetProperty("node", out var node))
            {
                var provenance = e.GetProperty("provenance");
                return new NodeLine(node.GetInt64(), new GraphNodeState((int)Long(e, "recalls"), Number(e, "stability"),
                    Number(e, "position"), Signals(e.GetProperty("signals")), Long(e, "ordinal"), Long(e, "chars"),
                    Time(e, "at"), Long(provenance, "retrievability"), Long(provenance, "salience"), Number(e, "difficulty")));
            }
            throw new FormatException("the line holds no graph record");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException
                                   or ArgumentException)
        {
            throw new FormatException("a graph line is missing a field or holds the wrong type", ex);
        }
    }

    private static string Encode(Action<Utf8JsonWriter> body) => RecordFile.Encode(w =>
    {
        w.WriteStartObject();
        body(w);
        w.WriteEndObject();
    });

    private static void Number(Utf8JsonWriter w, string name, double value)
    {
        if (double.IsFinite(value)) w.WriteNumber(name, value);
        else w.WriteString(name, double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity");
    }

    private static double Number(JsonElement e, string name) => Number(e.GetProperty(name));

    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var other => throw new FormatException($"'{other}' is not a number"),
        }
        : value.GetDouble();

    private static long Long(JsonElement e, string name) => e.GetProperty(name).GetInt64();

    private static string? OptionalString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void Time(Utf8JsonWriter w, string name, DateTimeOffset value) =>
        w.WriteString(name, value.ToString("O", CultureInfo.InvariantCulture));

    private static DateTimeOffset Time(JsonElement e, string name) =>
        DateTimeOffset.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static MemorySignals Signals(JsonElement e) => e.ValueKind == JsonValueKind.Null
        ? MemorySignals.Empty
        : MemorySignals.From(e.EnumerateObject().Select(p => new KeyValuePair<string, double>(p.Name, Number(p.Value))));
}
