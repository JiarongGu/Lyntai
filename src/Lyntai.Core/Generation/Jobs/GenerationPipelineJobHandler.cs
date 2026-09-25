using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Generation.Jobs;

/// <summary>Tuning for <see cref="GenerationPipelineJobHandler"/> and <see cref="GenerationRenderJobHandler"/>.</summary>
/// <remarks><b>Using it:</b> a handler takes whichever instance the container holds, so register one before
/// the handler resolves — <c>services.AddSingleton(new GenerationPipelineJobOptions { … })</c>. None
/// registered, every default applies.</remarks>
public sealed record GenerationPipelineJobOptions
{
    /// <summary>How long the job waits between polls of a QUEUED stage. Default 15 seconds: a hosted render
    /// takes minutes, so polling every few seconds spends requests for nothing.</summary>
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The most INLINE bytes one checkpoint may carry, counted before the base64 encoding that adds a
    /// third. Default 4 MiB; zero or less admits none. The checkpoint is rewritten on every poll, so it bounds
    /// two things differently.
    /// <para>A stage's RESULT is checkpointed before it is delivered only when its artifacts fit; one that does
    /// not is delivered unprotected, so a sink that throws gets it again by a second render or fetch. This never
    /// fails the job.</para>
    /// <para>The ONE artifact a stage chains into the next must fit, or the job FAILS, naming the stage, rather
    /// than truncating it — a stage whose backend returns a URI carries no bytes, which is the fix the failure
    /// names.</para></summary>
    public long MaxCheckpointBytes { get; init; } = 4L * 1024 * 1024;
}

/// <summary>Runs a generation PIPELINE as a durable job — ordered stages, each stage's artifact feeding the
/// next — in which any stage may be QUEUED: submitted, checkpointed and polled across process lifetimes.
/// <see cref="GenerationPipeline.RunPipelineAsync"/> is the in-memory form, and drives the inline door only;
/// <see cref="GenerationRenderJobHandler"/> runs a single queued render on the same machine.</summary>
/// <remarks>
/// <para><b>The caller's order picks each stage's door.</b> The FIRST candidate able to serve the stage decides
/// it: QUEUED when it implements <see cref="IMediaJobProvider"/> and declares <see cref="ProviderOperation.Queued"/>
/// for the request — so a backend declaring both doors, leading the list, goes queued — and INLINE otherwise. A
/// door exhausted without committing anything (every candidate advanced past, blamelessly or on cooldown) falls
/// back to the other door's candidates; an Inconclusive submission, or a refusal the policy surfaces, never does.</para>
/// <para><b>Nothing paid for is re-run, but for three narrow cases.</b> A checkpointed operation is polled, never
/// re-submitted, and a stage's result is billed, checkpointed, THEN delivered, so a sink that throws gets it again
/// with no second render, fetch or bill. What stays at-least-once: a crash between a submission returning and its
/// operation id being saved (the resume submits again), a crash between a render returning and its checkpoint, and
/// a result whose inline bytes exceed <see cref="GenerationPipelineJobOptions.MaxCheckpointBytes"/>.</para>
/// <para>Every failure names its stage from 1 and fails the job; a backend still working returns
/// <see cref="JobOutcome.Poll"/>. <b>Using it:</b> <c>AddJobHandler&lt;GenerationPipelineJobHandler&gt;()</c>,
/// then enqueue <see cref="JobType"/> with <see cref="GenerationPipelineJob.ToJson"/>. Why:
/// <c>docs/DECISIONS.md</c> D181.</para>
/// </remarks>
/// <param name="router">Routes each stage across its capable candidates.</param>
/// <param name="providers">The registered backends — read to choose a stage's door, and to find the one holding
/// a checkpointed operation.</param>
/// <param name="sink">Where every stage's artifacts go, tagged with <see cref="GenerationArtifactDelivery.StageIndex"/>.</param>
/// <param name="options">Poll cadence and the checkpoint's byte cap.</param>
/// <param name="usage">Optional spend ledger for QUEUED stages, recorded after the fetch and before delivery. The
/// container fills it only when <c>AddMediaUsageBudget()</c> is configured — the same gate the router's budget
/// decorator is under, so a text-only budget never bills a render here. An inline stage is recorded by that
/// decorator, never here, so it is never counted twice.</param>
/// <param name="policy">The fallback policy the ROUTER applies, read to tell a refusal it surfaced from a door it
/// exhausted — so pass the SAME <see cref="MediaRoutingPolicy"/> instance the router was built with. The container
/// does this: <c>ConfigureMediaRouting</c> configures the one instance both receive. Null = the defaults, which is
/// also what the router uses when none is configured. A router whose failures carry no verdict — a custom
/// <see cref="IMediaRouter"/> — gets no cross-door fallback.</param>
public sealed class GenerationPipelineJobHandler(
    IMediaRouter router,
    IEnumerable<IModelProvider> providers,
    IGenerationArtifactSink sink,
    GenerationPipelineJobOptions? options = null,
    [FromKeyedServices(GenerationBuilderExtensions.MediaSpendKey)] IUsageTracker? usage = null,
    MediaRoutingPolicy? policy = null) : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string JobType = "lyntai.generation.pipeline";

    private readonly GenerationJobEngine _engine = new(router, providers, sink, options, usage, policy);

    /// <inheritdoc/>
    public string Type => JobType;

    /// <inheritdoc/>
    public Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default) =>
        GenerationPipelineJob.Parse(ctx.Payload) is { } job
            ? _engine.RunAsync(ctx, job, ctx.Checkpoint, JobType, GenerationJobEngine.Pipeline, ct)
            : Task.FromResult(
                JobOutcome.Fail($"unreadable {JobType} payload — expected GenerationPipelineJob.ToJson()"));
}
