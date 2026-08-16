using System.Text.Json;
using Lyntai.Generation.Routing;
using Lyntai.Jobs;
using Lyntai.Llm.Budgeting;

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
/// The operation id is <b>checkpointed before the first poll</b>, so a crash, deploy or restart resumes
/// polling the render already running instead of submitting — and paying for — a second one. The lease is
/// renewed by checkpointing, and a lost lease stops the handler rather than letting two workers drive the
/// same paid render.
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
/// <param name="sink">Where finished artifacts go (the app's concern, D24).</param>
/// <param name="options">Poll cadence.</param>
/// <param name="usage">Optional spend ledger. A durable render's cost is only known when it FINISHES, and by
/// then the request that submitted it is long gone — so the handler is the only place that can bill it. Wired
/// automatically when <c>AddGenerationUsageBudget()</c> is configured.</param>
public sealed class GenerationRenderJobHandler(
    IGenerationRouter router,
    IEnumerable<IGenerationProvider> providers,
    IGenerationArtifactSink sink,
    GenerationRenderJobOptions? options = null,
    IUsageTracker? usage = null) : IJobHandler
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
            return await PollAsync(ctx, checkpoint, job, ct).ConfigureAwait(false);

        return await SubmitAsync(ctx, job, ct).ConfigureAwait(false);
    }

    private async Task<JobOutcome> SubmitAsync(JobContext ctx, GenerationRenderJob job, CancellationToken ct)
    {
        // "provider" or "provider:model" — the same spec shape the DI builder accepts, so a payload can be
        // written by hand or copied from configuration
        var candidates = job.Candidates.Select(GenerationCandidateSpec.Parse).ToList();
        var submission = await router.SubmitAsync(candidates, job.Request, ct).ConfigureAwait(false);

        if (submission.Operation.Status == GenerationOperationStatus.Failed)
            // An INCONCLUSIVE submission is the one failure that must not be retried blind: the backend never
            // answered, so it may already be running a billable render this job knows no id for. Fail with the
            // backend NAMED, the same manual-recovery move the lost-lease path below makes — a human can check
            // that account, and re-running the job is their call to make, not ours.
            return JobOutcome.Fail(submission.Operation.Inconclusive
                ? $"submission to '{submission.ProviderId}' had no answer, so it may already be running: " +
                  $"{submission.Operation.Detail}. Check that backend before re-running this job — it is not " +
                  "retried automatically, because a duplicate submission is a duplicate charge."
                // nothing accepted it: a capability/config problem, which a retry cannot fix
                : submission.Operation.Detail ?? "no backend accepted the generation");

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
        // Poll, never Retry: the submit SUCCEEDED, so counting this against MaxAttempts spends the job's
        // budget on looking rather than on failing. At the default of 3 that dead-lettered every render
        // slower than two poll delays — see JobOutcome.Poll.
        return JobOutcome.Poll(_options.EffectivePollDelay);
    }

    private async Task<JobOutcome> PollAsync(
        JobContext ctx, RenderCheckpoint checkpoint, GenerationRenderJob job, CancellationToken ct)
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
                // Re-checkpoint the same value to RENEW THE LEASE across a long render — and HONOUR the
                // answer. `IJobStore` states the rule as a must: a false means this worker lost the lease,
                // so the caller must abandon. The submit arm above already stops on it; this arm discarded
                // it, so a worker that stalled past the lease kept polling a render another worker had
                // already reclaimed.
                if (!await ctx.SaveCheckpointAsync(checkpoint.ToJson(), ct).ConfigureAwait(false))
                    return JobOutcome.Fail(
                        $"lease lost while polling operation {checkpoint.OperationId} — stopping so the "
                        + "worker that reclaimed it is the only one still running it");
                // the backend says the render is progressing normally, so this is a Poll — see JobOutcome.Poll
                return JobOutcome.Poll(_options.EffectivePollDelay);

            case GenerationOperationStatus.Succeeded:
                // Revalidate the lease BEFORE fetching, because everything past this point has side
                // effects the job store cannot fence: the fetch RECORDS SPEND and the sink RECEIVES the
                // artifacts. `CompleteAsync` is fenced, so a zombie worker's outcome is discarded — but
                // only after those landed. Double delivery is explicitly allowed (a sink must be safe to
                // call twice) and a double spend record over-counts, so a cap fires early rather than
                // late; neither is a disaster, and neither is free.
                if (!await ctx.SaveCheckpointAsync(checkpoint.ToJson(), ct).ConfigureAwait(false))
                    return JobOutcome.Fail(
                        $"lease lost before fetching operation {checkpoint.OperationId} — stopping so the "
                        + "render is not fetched, billed and delivered twice");
                var result = await backend.FetchAsync(checkpoint.OperationId, ct).ConfigureAwait(false);
                if (!result.IsOk)
                    return JobOutcome.Fail(
                        $"operation {checkpoint.OperationId} succeeded but its artifacts could not be " +
                        $"fetched: {result.Detail}");

                // bill the render BEFORE delivery: a sink that throws makes the job retry, and the money is
                // already spent either way — recording it after delivery would lose it on that retry, while
                // recording it here at worst double-counts a render whose delivery keeps failing
                if (usage is not null)
                    await BudgetedGenerationRouter.RecordAsync(usage, job.Request.Consumer, result.Usage, ct)
                        .ConfigureAwait(false);

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
