using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Text;

namespace Lyntai.Generation.Jobs;

/// <summary>The payload of a durable generation PIPELINE: ordered stages, each stage's artifact feeding the
/// next, run by <see cref="GenerationPipelineJobHandler"/>. The durable counterpart of the stages
/// <see cref="GenerationPipeline.RunPipelineAsync"/> takes — plain data rather than delegates, because it is
/// stored and may be resumed by another process. Serialized to the job's <c>Payload</c> string, by hand like
/// <see cref="GenerationRenderJob"/> (Core claims trim/AOT compatibility).</summary>
/// <remarks><b>Using it:</b> build the stages, then enqueue
/// <c>new JobSpec(lane, GenerationPipelineJobHandler.JobType, pipeline.ToJson())</c>. A stage names no door:
/// its candidates decide it (<see cref="GenerationPipelineJobHandler"/>).</remarks>
public sealed record GenerationPipelineJob
{
    /// <summary>A pipeline of <paramref name="stages"/>, validated as
    /// <see cref="GenerationPipeline.RunPipelineAsync"/> validates its own.</summary>
    /// <param name="stages">The stages, in order. At least one.</param>
    /// <exception cref="ArgumentException"><paramref name="stages"/> is empty or holds a null, or its first
    /// stage sets <see cref="GenerationPipelineJobStage.InputRole"/> or
    /// <see cref="GenerationPipelineJobStage.InputMediaType"/> — which chain from nothing there, so a caller
    /// who set one believes something is happening.</exception>
    public GenerationPipelineJob(IReadOnlyList<GenerationPipelineJobStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new ArgumentException("a pipeline needs at least one stage", nameof(stages));
        if (stages.Any(stage => stage is null))
            throw new ArgumentException("a pipeline stage cannot be null", nameof(stages));
        if (stages[0].InputRole is not null || stages[0].InputMediaType is not null)
            throw new ArgumentException(
                "the first stage chains from nothing, so InputRole and InputMediaType are never read there",
                nameof(stages));
        Stages = [.. stages];   // a copy: the caller's list may change after the payload is built
    }

    /// <summary>The stages, in order.</summary>
    public IReadOnlyList<GenerationPipelineJobStage> Stages { get; }

    /// <summary>Serialize for <c>JobSpec.Payload</c>. Inline input bytes travel as base64.</summary>
    public string ToJson() => JsonExtract.WriteObject(writer =>
    {
        writer.WriteStartArray("stages");
        foreach (var stage in Stages)
        {
            writer.WriteStartObject();
            GenerationJson.WriteCandidates(writer, stage.Candidates);
            GenerationJson.WriteRequest(writer, stage.Request);
            if (stage.InputRole is { } role) writer.WriteString("inputRole", role);
            if (stage.InputMediaType is { } mediaType) writer.WriteString("inputMediaType", mediaType);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    });

    /// <summary>Read a payload back, or null when it isn't a valid one — including a stage it cannot read,
    /// which is never skipped (every stage after it would chain from the wrong one). Null rather than
    /// throwing: the handler turns it into a FAILED job with a reason.</summary>
    public static GenerationPipelineJob? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("stages", out var array) || array.ValueKind != JsonValueKind.Array)
                return null;

            var stages = new List<GenerationPipelineJobStage>();
            foreach (var element in array.EnumerateArray())
            {
                if (GenerationJson.ReadRequest(element) is not { } request) return null;
                stages.Add(new GenerationPipelineJobStage(GenerationJson.ReadCandidates(element), request)
                {
                    InputRole = JsonExtract.StringProperty(element, "inputRole"),
                    InputMediaType = JsonExtract.StringProperty(element, "inputMediaType"),
                });
            }
            return new GenerationPipelineJob(stages);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;   // well-formed JSON the constructor refuses: no stages, or a first stage that chains
        }
    }
}

/// <summary>One stage of a <see cref="GenerationPipelineJob"/>: what to generate, which backends may serve it,
/// and how it takes the previous stage's artifact. The data form of <see cref="GenerationStage"/>.</summary>
/// <param name="Candidates">Backends for THIS stage, in routing order — <c>"provider"</c> or
/// <c>"provider:model"</c>, the spec <see cref="GenerationRenderJob"/> takes. They also decide the stage's door:
/// QUEUED when any of them can queue this request, INLINE otherwise.</param>
/// <param name="Request">The generation. From the second stage on, the chained input is APPENDED to its
/// <see cref="MediaRequest.Inputs"/>, after the stage's own.</param>
public sealed record GenerationPipelineJobStage(IReadOnlyList<string> Candidates, MediaRequest Request)
{
    /// <summary>What the previous stage's artifact IS to this one — a <see cref="MediaInputRoles"/> value, or
    /// null for the backend's own default. The library never picks it: the same PNG is a first frame to one
    /// video backend and an init image to another. Setting it on the FIRST stage, which chains from nothing,
    /// is refused.</summary>
    public string? InputRole { get; init; }

    /// <summary>Which of the previous stage's artifacts chains into this one, by media type: an exact type
    /// (<c>image/png</c>) or a wildcard (<c>model/*</c>), case-insensitive, parameters ignored. Null takes the
    /// previous stage's SINGLE artifact.
    /// <para>Either way exactly ONE artifact must qualify; zero or several FAIL the job naming both stages,
    /// never a guess — "the first <c>image/*</c>" of a mesh backend is a UV texture atlas, which chains, renders,
    /// and is wrong. Setting it on the first stage is refused.</para>
    /// <para><b>Using it:</b> set it wherever the previous backend can return more than one artifact — a mesh
    /// with its textures, a video with a preview frame.</para></summary>
    public string? InputMediaType { get; init; }
}
