using System.Text.Json;
using Lyntai.Inference;

namespace Lyntai.Generation.Jobs;

/// <summary>The payload of a durable generation job: which candidates to route across, and the request to
/// generate. Serialized to the job's <c>Payload</c> string.</summary>
/// <remarks>JSON is built and parsed BY HAND (<see cref="Utf8JsonWriter"/> / <see cref="JsonDocument"/>) rather
/// than reflection-serialized, matching <c>MemoryPruneJobHandler</c>: Core claims trim/AOT compatibility, and a
/// reflection serializer here would quietly make that claim false.</remarks>
/// <param name="Candidates">Candidate specs in routing order — <c>"provider"</c> or <c>"provider:model"</c>,
/// the same shape <c>UseDefaultMediaCandidates</c> accepts.</param>
/// <param name="Request">What to generate.</param>
public sealed record GenerationRenderJob(IReadOnlyList<string> Candidates, MediaRequest Request)
{
    /// <summary>Serialize for <c>JobSpec.Payload</c>.</summary>
    public string ToJson() => GenerationJson.WriteObject(writer =>
    {
        GenerationJson.WriteCandidates(writer, Candidates);
        GenerationJson.WriteRequest(writer, Request);
    });

    /// <summary>Read a payload back, or null when it isn't one. Null rather than throwing: a handler turns an
    /// unreadable payload into a FAILED job with a reason, which is more useful than an exception in a queue.</summary>
    public static GenerationRenderJob? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return GenerationJson.ReadRequest(doc.RootElement) is { } request
                ? new GenerationRenderJob(GenerationJson.ReadCandidates(doc.RootElement), request)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
