using System.Text.Json;
using Lyntai.Generation.Routing;
using Lyntai.Jobs;

namespace Lyntai.Generation.Jobs;

/// <summary>Tuning for <see cref="GenerationRenderJobHandler"/>.</summary>
/// <param name="PollDelay">How long the job waits between polls. A hosted render takes minutes, so polling
/// every few seconds spends requests for nothing.</param>
public sealed record GenerationRenderJobOptions(TimeSpan? PollDelay = null)
{
    /// <summary>The delay actually used.</summary>
    public TimeSpan EffectivePollDelay => PollDelay ?? TimeSpan.FromSeconds(15);
}

/// <summary>
/// Runs an asynchronous generation as a DURABLE job: submit once, then poll to completion across as many
/// process lifetimes as it takes.
///
/// This is where the platform earns its keep over a thin HTTP client. The operation id is
/// <b>checkpointed before the first poll</b>, so a crash, deploy or restart resumes polling the render that is
/// already running instead of submitting — and paying for — a second one. Progress is reported for a UI, the
/// lease is renewed by checkpointing, and a lost lease stops the handler rather than letting two workers drive
/// the same paid render.
/// </summary>
/// <remarks>
/// <para>Composes with <c>Lyntai.Jobs</c> rather than reimplementing any of it: lanes, priorities, retry
/// backoff, cancellation, DLQ and progress already exist there. Register it with
/// <c>AddJobHandler&lt;GenerationRenderJobHandler&gt;()</c> and enqueue
/// <c>JobSpec { Type = GenerationRenderJobHandler.JobType, Payload = renderJob.ToJson() }</c>.</para>
/// <para>Failure posture: a submission that no candidate accepts FAILS the job (a configuration problem retrying
/// cannot fix), whereas a backend that is merely still working retries. An unreadable payload fails with a
/// reason rather than throwing into the queue.</para>
/// </remarks>
/// <param name="router">Routes the submission across capable candidates.</param>
/// <param name="providers">The registered backends — used to find the one that owns a checkpointed operation.</param>
/// <param name="sink">Where finished artifacts go (the app's concern, D30).</param>
/// <param name="options">Poll cadence.</param>
public sealed class GenerationRenderJobHandler(
    IGenerationRouter router,
    IEnumerable<IGenerationProvider> providers,
    IGenerationArtifactSink sink,
    GenerationRenderJobOptions? options = null) : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string JobType = "lyntai.generation.render";

    private readonly GenerationRenderJobOptions _options = options ?? new GenerationRenderJobOptions();

    /// <inheritdoc/>
    public string Type => JobType;

    /// <inheritdoc/>
    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        if (GenerationRenderJob.Parse(ctx.Payload) is not { } job)
            return JobOutcome.Fail($"unreadable {JobType} payload — expected GenerationRenderJob.ToJson()");

        // A checkpoint means a render is ALREADY RUNNING at a backend: poll it. Never re-submit.
        if (RenderCheckpoint.Parse(ctx.Checkpoint) is { } checkpoint)
            return await PollAsync(ctx, checkpoint, ct).ConfigureAwait(false);

        return await SubmitAsync(ctx, job, ct).ConfigureAwait(false);
    }

    private async Task<JobOutcome> SubmitAsync(JobContext ctx, GenerationRenderJob job, CancellationToken ct)
    {
        var candidates = job.Candidates.Select(ParseCandidate).ToList();
        var submission = await router.SubmitAsync(candidates, job.Request, ct).ConfigureAwait(false);

        if (submission.Operation.Status == GenerationOperationStatus.Failed)
            // nothing accepted it: a capability/config problem, which a retry cannot fix
            return JobOutcome.Fail(submission.Operation.Detail ?? "no backend accepted the generation");

        // Checkpoint FIRST — before reporting progress, before returning. Everything after this point can be
        // redone safely; the submission cannot, because it costs money.
        var saved = await ctx.SaveCheckpointAsync(
            new RenderCheckpoint(submission.ProviderId, submission.Operation.Id).ToJson(), ct).ConfigureAwait(false);
        if (!saved)
            return JobOutcome.Fail(
                $"lease lost right after submitting operation {submission.Operation.Id} to " +
                $"'{submission.ProviderId}' — stopping so another worker doesn't run a second paid render " +
                "(the operation id is in this message for manual recovery)");

        await ctx.ReportProgressAsync(0, 1, "submitted", ct).ConfigureAwait(false);
        return JobOutcome.Retry(_options.EffectivePollDelay);
    }

    private async Task<JobOutcome> PollAsync(JobContext ctx, RenderCheckpoint checkpoint, CancellationToken ct)
    {
        if (Backend(checkpoint.ProviderId) is not { } backend)
            return JobOutcome.Fail(
                $"backend '{checkpoint.ProviderId}' holds operation {checkpoint.OperationId} but is no longer " +
                "registered — re-register it to resume, or fail the job deliberately");

        var operation = await backend.PollAsync(checkpoint.OperationId, ct).ConfigureAwait(false);

        switch (operation.Status)
        {
            case GenerationOperationStatus.Queued:
            case GenerationOperationStatus.Running:
                var done = operation.Progress is { } fraction ? (int)Math.Round(fraction * 100) : 0;
                await ctx.ReportProgressAsync(done, 100, "running", ct).ConfigureAwait(false);
                // re-checkpoint the same value to RENEW THE LEASE across a long render
                await ctx.SaveCheckpointAsync(checkpoint.ToJson(), ct).ConfigureAwait(false);
                return JobOutcome.Retry(_options.EffectivePollDelay);

            case GenerationOperationStatus.Succeeded:
                var result = await backend.FetchAsync(checkpoint.OperationId, ct).ConfigureAwait(false);
                if (!result.IsOk)
                    return JobOutcome.Fail(
                        $"operation {checkpoint.OperationId} succeeded but its artifacts could not be " +
                        $"fetched: {result.Detail}");

                // the sink may throw — a store that is momentarily unavailable should retry, not lose the render
                await sink.ReceiveAsync(new GenerationArtifactDelivery(
                    ctx.JobId, checkpoint.ProviderId, checkpoint.OperationId, result.Artifacts, result.Usage), ct)
                    .ConfigureAwait(false);

                await ctx.ReportProgressAsync(100, 100, "delivered", ct).ConfigureAwait(false);
                return JobOutcome.Complete;

            default:
                return JobOutcome.Fail(
                    $"operation {checkpoint.OperationId} at '{checkpoint.ProviderId}' ended " +
                    $"{operation.Status}: {operation.Detail}");
        }
    }

    private IGenerationJobProvider? Backend(string providerId) => providers
        .FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase))
        as IGenerationJobProvider;

    /// <summary><c>"provider"</c> or <c>"provider:model"</c> — the same spec shape the DI builder accepts, so a
    /// payload can be written by hand or copied from configuration.</summary>
    private static GenerationCandidate ParseCandidate(string spec)
    {
        var at = spec.IndexOf(':');
        return at < 0
            ? new GenerationCandidate(spec.Trim())
            : new GenerationCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
    }

    /// <summary>What the job remembers between polls: WHICH backend holds the render, and its operation id.
    /// Both, because an operation id is meaningless without its issuer.</summary>
    private sealed record RenderCheckpoint(string ProviderId, string OperationId)
    {
        public string ToJson()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("providerId", ProviderId);
                writer.WriteString("operationId", OperationId);
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        public static RenderCheckpoint? Parse(string? checkpoint)
        {
            if (string.IsNullOrWhiteSpace(checkpoint)) return null;
            try
            {
                using var doc = JsonDocument.Parse(checkpoint);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return doc.RootElement.TryGetProperty("providerId", out var provider) &&
                    doc.RootElement.TryGetProperty("operationId", out var operation) &&
                    provider.GetString() is { Length: > 0 } providerId &&
                    operation.GetString() is { Length: > 0 } operationId
                        ? new RenderCheckpoint(providerId, operationId)
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
