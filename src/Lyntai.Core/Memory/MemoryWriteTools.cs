using System.Globalization;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Text;

namespace Lyntai.Memory;

/// <summary>What <see cref="MemoryEngineBuilderExtensions.AddMemoryWriteTools"/> lets an agent do, and how far.
/// <para>The host owns the task (the tools take it from <see cref="MemoryToolScope"/>, never an argument), the grade (a tool
/// write is always <see cref="MemoryGrade.Associative"/>, with no grade argument a model's words could use to escape decay)
/// and the stamp every write carries.</para></summary>
public sealed class MemoryWriteToolOptions
{
    /// <summary>Metadata stamped on every entry a tool writes — the host's own vocabulary (who wrote it, from where), so a
    /// reader can attribute what an agent asserted. Null stamps nothing.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>The longest content one call may write, in characters; a longer one is refused. The model picks the text, so it
    /// is bounded as a recall's limit is. Default 2000.</summary>
    public int MaxContentChars { get; init; } = 2000;

    /// <summary>Whether a model may name the scope of a write. Off by default: the scope is the host's, as the task is.</summary>
    public bool ModelMayNameScope { get; init; }

    /// <summary>How many entries <c>mayUpdate</c> lists after a write: the live entries it may update, which the writer
    /// confirms through <c>supersedes</c> or the end tool. Default 10, the measured default of
    /// <see cref="IMemoryCandidates.FindUpdatesAsync"/>; zero omits the list.</summary>
    public int CandidateLimit { get; init; } = 10;
}

internal static partial class MemoryTools
{
    private static readonly MemoryWriteToolOptions DefaultWriteOptions = new();

    private static string Error(string message) => JsonExtract.WriteObject(w => w.WriteString("error", message));

    private static JsonElement? Root(string argumentsJson, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : null;
        }
        catch (JsonException) { return null; }
    }

    private static DateTimeOffset? ReadInstant(JsonElement root, string name) =>
        JsonExtract.StringProperty(root, name) is { } s
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    /// <summary>The refs a model listed: each a string ref, or an object with <c>ref</c> and <c>revision</c>.</summary>
    private static List<(MemoryRef Ref, string? Revision)>? ReadRefs(JsonElement root, string name, out string? bad)
    {
        bad = null;
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return null;
        var result = new List<(MemoryRef, string?)>();
        foreach (var element in array.EnumerateArray())
        {
            var text = element.ValueKind == JsonValueKind.Object ? JsonExtract.StringProperty(element, "ref") : element.GetString();
            if (Parse(text) is not { } reference) { bad = text ?? "(not a string)"; return null; }
            result.Add((reference, element.ValueKind == JsonValueKind.Object ? JsonExtract.StringProperty(element, "revision") : null));
        }
        return result;
    }

    /// <summary>The remember tool: stores one associative entry, states the relations the writer names, and shows what it may
    /// update. <c>supersedes</c> goes through the approver; <c>disputes</c> and <c>amends</c> end nothing and apply alone.</summary>
    internal static ITool Remember(IMemoryEngine engine, MemoryChangeGate gate, string prefix, string taskKey, string? scope,
        MemoryWriteToolOptions? options)
    {
        var settings = options ?? DefaultWriteOptions;
        return new FunctionTool(
            $"{prefix}_remember",
            async (argumentsJson, ct) =>
            {
                if (Root(argumentsJson, out var doc) is not { } root) return Error("pass a JSON object with the 'content' to remember");
                using (doc)
                {
                    var content = JsonExtract.StringProperty(root, "content");
                    if (string.IsNullOrWhiteSpace(content)) return Error("'content' is required: the claim to remember");
                    if (content.Length > settings.MaxContentChars)
                        return Error($"'content' is {content.Length} characters and at most {settings.MaxContentChars} are kept; shorten it");
                    var (task, defaultScope) = MemoryToolScope.Resolve(taskKey, scope);
                    var writeScope = (settings.ModelMayNameScope ? JsonExtract.StringProperty(root, "scope") : null)
                        ?? defaultScope ?? scope ?? "default";

                    var supersedes = ReadRefs(root, "supersedes", out var badSupersedes);
                    var disputes = ReadRefs(root, "disputes", out var badDisputes);
                    var amends = ReadRefs(root, "amends", out var badAmends);
                    var bad = badSupersedes ?? badDisputes ?? badAmends;
                    if (bad is not null) return Error($"'{bad}' is not a ref returned by the matching recall tool");

                    var write = new MemoryWrite(task, writeScope, content, JsonExtract.StringProperty(root, "headline"),
                        MemoryGrade.Associative, settings.Metadata)
                    {
                        ValidFrom = ReadInstant(root, "validFrom"),
                        ValidTo = ReadInstant(root, "validTo"),
                        Disputes = disputes?.Select(r => r.Ref).ToList(),
                        Amends = amends?.Select(r => r.Ref).ToList(),
                    };
                    try
                    {
                        if (supersedes is { Count: > 0 })
                        {
                            var reason = JsonExtract.StringProperty(root, "reason");
                            if (string.IsNullOrWhiteSpace(reason)) return Error("'reason' is required with 'supersedes': say why it replaces them");
                            if (supersedes.Any(s => string.IsNullOrWhiteSpace(s.Revision)))
                                return Error("each 'supersedes' entry needs its 'ref' and the 'revision' you read in the recall");
                            var result = await gate.SupersedeAsync(engine, write,
                                [.. supersedes.Select(s => new MemoryChangeTarget(s.Ref, s.Revision!))], reason,
                                JsonExtract.StringProperty(root, "evidence"), ct).ConfigureAwait(false);
                            return await RenderWrittenAsync(engine, write, result.Successor, ct, settings, result).ConfigureAwait(false);
                        }
                        var written = await engine.RememberAsync(write, ct).ConfigureAwait(false);
                        return await RenderWrittenAsync(engine, write, written.Reference, ct, settings, null).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException
                                                   or KeyNotFoundException)
                    {
                        // the engine's own refusal, as a result the model can act on
                        return Error(ex.Message);
                    }
                }
            },
            $"Remember one fact in '{engine.Name}'. Returns its 'ref' and 'mayUpdate': the live entries this may update, each with its " +
            "'ref' and 'revision'. Writing does not replace them. To replace one, pass 'supersedes' with its ref and revision and a " +
            "'reason'; a person or check decides whether it applies. 'disputes' and 'amends' name entries you assert this disagrees " +
            "with or corrects; they change nothing.",
            """
            {"type":"object","required":["content"],"properties":{
              "content":{"type":"string","description":"The fact, stated on its own."},
              "headline":{"type":"string","description":"Optional one-line form."},
              "validFrom":{"type":"string","description":"When it started holding, ISO 8601."},
              "validTo":{"type":"string","description":"When it stopped holding, ISO 8601."},
              "supersedes":{"type":"array","description":"Entries this replaces, each as its ref and the revision you read.","items":{"type":"object","required":["ref","revision"],"properties":{"ref":{"type":"string"},"revision":{"type":"string"}}}},
              "reason":{"type":"string","description":"Why it replaces them; required with supersedes."},
              "evidence":{"type":"string","description":"What supports it."},
              "disputes":{"type":"array","description":"Refs of entries you say this contradicts.","items":{"type":"string"}},
              "amends":{"type":"array","description":"Refs of entries this corrects or extends in one claim.","items":{"type":"string"}},
              "scope":{"type":"string","description":"Only where the host allows a model to name one."}
            }}
            """);
    }

    private static async Task<string> RenderWrittenAsync(IMemoryEngine engine, MemoryWrite write, MemoryRef? reference,
        CancellationToken ct, MemoryWriteToolOptions settings, MemoryChangeResult? change)
    {
        IReadOnlyList<MemoryUpdateCandidate> candidates = [];
        if (settings.CandidateLimit > 0 && engine is IMemoryCandidates finder)
        {
            try { candidates = await finder.FindUpdatesAsync(write, settings.CandidateLimit, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* a suggestion: its failure costs the list, never the write */ }
        }
        return JsonExtract.WriteObject(w =>
        {
            if (reference is { } r) w.WriteString("ref", Format(r));
            if (change is not null)
            {
                w.WriteString("status", change.Status.ToString());
                w.WriteString("message", change.Message);
            }
            if (settings.CandidateLimit > 0)
            {
                w.WriteStartArray("mayUpdate");
                foreach (var candidate in candidates.Where(c => reference is null || c.Item.Reference != reference))
                {
                    w.WriteStartObject();
                    w.WriteString("ref", Format(candidate.Item.Reference));
                    w.WriteString("headline", candidate.Item.Headline);
                    if (candidate.Item.Revision is { } revision) w.WriteString("revision", revision);
                    w.WriteString("basis", candidate.Basis.ToString());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
        });
    }

    /// <summary>The end tool: proposes that an entry stops being current. It is applied only if a registered approver approves,
    /// and only to the revision the model read.</summary>
    internal static ITool End(IMemoryEngine engine, MemoryChangeGate gate, string prefix, string taskKey, string? scope) =>
        new FunctionTool(
            $"{prefix}_end",
            async (argumentsJson, ct) =>
            {
                if (Root(argumentsJson, out var doc) is not { } root) return Error("pass a JSON object with 'ref', 'revision' and 'reason'");
                using (doc)
                {
                    if (Parse(JsonExtract.StringProperty(root, "ref")) is not { } target)
                        return Error("pass the 'ref' of an item returned by the matching recall tool");
                    var by = JsonExtract.StringProperty(root, "by");
                    if (by is not null && Parse(by) is null) return Error($"'{by}' is not a ref returned by the matching recall tool");
                    var (task, _) = MemoryToolScope.Resolve(taskKey, scope);
                    try
                    {
                        var result = await gate.EndAsync(engine, task, target, JsonExtract.StringProperty(root, "revision") ?? "",
                            JsonExtract.StringProperty(root, "reason") ?? "", JsonExtract.StringProperty(root, "evidence"),
                            by is null ? null : Parse(by), ReadInstant(root, "at"), ct).ConfigureAwait(false);
                        return JsonExtract.WriteObject(w =>
                        {
                            w.WriteString("status", result.Status.ToString());
                            w.WriteString("message", result.Message);
                            if (result.Revisions is { Count: > 0 } found)
                            {
                                w.WriteStartObject("currentRevisions");
                                foreach (var (entry, revision) in found) w.WriteString(Format(entry), revision);
                                w.WriteEndObject();
                            }
                        });
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException
                                                   or KeyNotFoundException)
                    {
                        return Error(ex.Message);
                    }
                }
            },
            $"Propose that an item in '{engine.Name}' is no longer true. It is applied only if an approver approves, and only if it " +
            "is still at the 'revision' you read; otherwise you are told why. Give the 'ref' and 'revision' from the recall, and a 'reason'.",
            """
            {"type":"object","required":["ref","revision","reason"],"properties":{
              "ref":{"type":"string","description":"The 'ref' of the item, from a recall."},
              "revision":{"type":"string","description":"The 'revision' of the item as you read it."},
              "reason":{"type":"string","description":"Why it is no longer true."},
              "by":{"type":"string","description":"Optional ref of the item that replaced it."},
              "at":{"type":"string","description":"Optional ISO 8601 instant it stopped holding."},
              "evidence":{"type":"string","description":"What supports ending it."}
            }}
            """);
}
