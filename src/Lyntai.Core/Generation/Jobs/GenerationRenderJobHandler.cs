using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Generation.Jobs;

/// <summary>
/// Runs ONE asynchronous generation as a DURABLE job: submit once, then poll to completion across as many
/// process lifetimes as it takes. It is a one-stage, queued-only run of the machine
/// <see cref="GenerationPipelineJobHandler"/> runs, so the two cannot drift: the operation id is
/// <b>checkpointed before the first poll</b>, and the fetched result is billed and checkpointed BEFORE it is
/// delivered, so a sink that throws gets it again with no second fetch or bill.
/// </summary>
/// <remarks>
/// <para>Register it with <c>AddJobHandler&lt;GenerationRenderJobHandler&gt;()</c> and enqueue
/// <see cref="JobType"/> with <see cref="GenerationRenderJob.ToJson"/>. A pipeline of one stage does the same
/// work with the door chosen by its candidates; this type serves the render job type, including jobs checkpointed
/// by an earlier release as <c>{providerId, operationId}</c>, which resume polling that operation.</para>
/// <para>Failure posture: a submission that no candidate accepts FAILS the job (a configuration problem retrying
/// cannot fix), an Inconclusive one fails naming the backend that may hold it, and a backend still working
/// returns <see cref="JobOutcome.Poll"/>. The delivery carries no
/// <see cref="GenerationArtifactDelivery.StageIndex"/>.</para>
/// </remarks>
/// <param name="router">Routes the submission across capable candidates.</param>
/// <param name="providers">The registered backends — used to find the one that owns a checkpointed operation.</param>
/// <param name="sink">Where finished artifacts go (the app's concern, D24).</param>
/// <param name="options">Poll cadence and the checkpoint's byte cap.</param>
/// <param name="usage">Optional spend ledger. A durable render's cost is only known when it FINISHES, and by
/// then the request that submitted it is long gone — so the handler is the only place that can bill it. The
/// container fills it only when <c>AddMediaUsageBudget()</c> is configured.</param>
public sealed class GenerationRenderJobHandler(
    IMediaRouter router,
    IEnumerable<IModelProvider> providers,
    IGenerationArtifactSink sink,
    GenerationPipelineJobOptions? options = null,
    [FromKeyedServices(GenerationBuilderExtensions.MediaSpendKey)] IUsageTracker? usage = null) : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string JobType = "lyntai.generation.render";

    private readonly GenerationJobEngine _engine = new(router, providers, sink, options, usage, policy: null);

    /// <inheritdoc/>
    public string Type => JobType;

    /// <inheritdoc/>
    public Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        if (GenerationRenderJob.Parse(ctx.Payload) is not { } render)
            return Task.FromResult(
                JobOutcome.Fail($"unreadable {JobType} payload — expected GenerationRenderJob.ToJson()"));

        var job = new GenerationPipelineJob([new GenerationPipelineJobStage(render.Candidates, render.Request)]);
        return _engine.RunAsync(ctx, job, Resumable(ctx.Checkpoint), JobType, GenerationJobEngine.Render, ct);
    }

    /// <summary>The checkpoint to resume from: an earlier release's <c>{providerId, operationId}</c> as the stage-0
    /// operation it names, anything else as written — the engine refuses what it cannot read.</summary>
    private static string? Resumable(string? checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint)) return checkpoint;
        try
        {
            using var doc = JsonDocument.Parse(checkpoint);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("stage", out _) &&
                   GenerationJson.Str(root, "providerId") is { } providerId &&
                   GenerationJson.Str(root, "operationId") is { } operationId
                ? GenerationJobEngine.SubmittedCheckpoint(providerId, operationId)
                : checkpoint;
        }
        catch (JsonException)
        {
            return checkpoint;
        }
    }
}
