using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;

namespace Lyntai.Generation.Jobs;

/// <summary>Tuning for <see cref="GenerationPipelineJobHandler"/>.</summary>
/// <remarks><b>Using it:</b> the handler takes whichever instance the container holds, so register one before
/// the handler resolves — <c>services.AddSingleton(new GenerationPipelineJobOptions { … })</c>. None
/// registered, every default applies.</remarks>
public sealed record GenerationPipelineJobOptions
{
    /// <summary>How long the job waits between polls of a QUEUED stage. Default 15 seconds: a hosted render
    /// takes minutes, so polling every few seconds spends requests for nothing.</summary>
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The most INLINE bytes the checkpoint may carry from one stage to the next, counted before the
    /// base64 encoding that adds a third. Default 4 MiB; zero or less admits none.
    /// <para>The artifact a stage chains is checkpointed so a restart resumes with it, and the checkpoint is
    /// rewritten on every poll of the next stage. So one over this cap FAILS the job, naming the stage, rather
    /// than being truncated — and a stage whose backend returns a URI carries no bytes, which is the fix the
    /// failure names. The last stage's artifacts are delivered, never checkpointed, so this never bounds them.</para></summary>
    public long MaxCheckpointBytes { get; init; } = 4L * 1024 * 1024;
}

/// <summary>Runs a generation PIPELINE as a durable job — ordered stages, each stage's artifact feeding the
/// next — in which any stage may be QUEUED: submitted, checkpointed and polled across process lifetimes as
/// <see cref="GenerationRenderJobHandler"/> runs one render. <see cref="GenerationPipeline.RunPipelineAsync"/>
/// is the in-memory form, and drives the inline door only.</summary>
/// <remarks>
/// <para><b>A stage's door is decided by its candidates, never declared.</b> It is QUEUED when any candidate is
/// registered, implements <see cref="IMediaJobProvider"/> and declares <see cref="ProviderOperation.Queued"/>
/// for the stage's request (the router's own capability filter) — even behind an inline candidate, because a
/// queued stage is the one a restart resumes. Otherwise it is INLINE. A submission no candidate accepts fails
/// the job; it is never retried inline.</para>
/// <para><b>Nothing paid for is re-run.</b> A queued stage's operation id is checkpointed before its first
/// poll and never re-submitted. A stage that produces is billed, then DELIVERED, then the artifact the next
/// stage chains is checkpointed. An inline stage has no handle to resume, so a crash, lost lease or throwing
/// sink before that checkpoint renders it again on the retry — the at-least-once of <see cref="IJobHandler"/>.</para>
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
/// <param name="usage">Optional spend ledger for QUEUED stages, recorded after the fetch and before delivery. An
/// inline stage is billed by the router it runs through, so it is never recorded twice.</param>
public sealed class GenerationPipelineJobHandler(
    IMediaRouter router,
    IEnumerable<IModelProvider> providers,
    IGenerationArtifactSink sink,
    GenerationPipelineJobOptions? options = null,
    IUsageTracker? usage = null) : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string JobType = "lyntai.generation.pipeline";

    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];
    private readonly GenerationPipelineJobOptions _options = options ?? new GenerationPipelineJobOptions();

    /// <inheritdoc/>
    public string Type => JobType;

    /// <inheritdoc/>
    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        if (GenerationPipelineJob.Parse(ctx.Payload) is not { } job)
            return JobOutcome.Fail($"unreadable {JobType} payload — expected GenerationPipelineJob.ToJson()");

        var stages = job.Stages.Count;
        var at = string.IsNullOrWhiteSpace(ctx.Checkpoint)
            ? new PipelineCheckpoint(0, null, null, [])
            : PipelineCheckpoint.Parse(ctx.Checkpoint, stages);
        if (at is null)
            return JobOutcome.Fail($"unreadable {JobType} checkpoint — not restarting from stage 1, which would " +
                                   "run again every stage already paid for");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stage = job.Stages[at.Stage];
            var name = $"stage {at.Stage + 1} of {stages}";
            Produced produced;

            if (at.OperationId is not null)
            {
                // a checkpointed operation is ALREADY RUNNING at a backend: poll it, never re-submit
                var (wait, fetched) = await PollAsync(ctx, at, name, stages, ct).ConfigureAwait(false);
                if (wait is not null) return wait;
                produced = fetched!;
            }
            else
            {
                var request = at.Stage == 0 ? stage.Request : stage.Request with
                {
                    Inputs = [.. stage.Request.Inputs, .. at.Chained.Select(a => a.ToInput(stage.InputRole))],
                };
                var candidates = stage.Candidates.Select(ProviderCandidateSpec.Parse).ToList();

                if (MediaRouter.Capable(_providers, candidates, request, ProviderOperation.Queued)
                    .Any(capable => capable.Provider is IMediaJobProvider))
                    return await SubmitAsync(ctx, at, name, stages, candidates, request, ct).ConfigureAwait(false);

                var response = await router.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
                if (!response.IsOk)
                    return JobOutcome.Fail($"{name} failed ({response.Verdict}): {response.Detail}");
                produced = new Produced(response, "", "", Queued: false);
            }

            // bill BEFORE delivery: a sink that throws makes the job retry, and the money is spent either way.
            // Only a queued stage: the router that ran an inline one has already recorded it.
            if (produced.Queued && usage is not null)
                await BudgetedMediaRouter.RecordAsync(usage, stage.Request.Consumer, produced.Response.Usage, ct)
                    .ConfigureAwait(false);

            var final = at.Stage == stages - 1;
            // every stage is delivered as it finishes, so a later failure never loses one already paid for
            await sink.ReceiveAsync(new GenerationArtifactDelivery(ctx.JobId, produced.ProviderId,
                produced.OperationId, produced.Response.Artifacts, produced.Response.Usage)
            {
                StageIndex = at.Stage,
                IsFinal = final,
            }, ct).ConfigureAwait(false);

            if (final)
            {
                await ctx.ReportProgressAsync(stages * 100, stages * 100, "delivered", ct).ConfigureAwait(false);
                return JobOutcome.Complete;
            }

            var next = at.Stage + 1;
            if (Chain(job.Stages[next], produced.Response.Artifacts) is not { } chained)
                return JobOutcome.Fail(Refusal(job.Stages[next], produced.Response.Artifacts, next));
            if ((chained.Data?.LongLength ?? 0) > Math.Max(0, _options.MaxCheckpointBytes))
                return JobOutcome.Fail(
                    $"{name}'s artifact carries {chained.Data!.LongLength} bytes inline, over the " +
                    $"{Math.Max(0, _options.MaxCheckpointBytes)}-byte checkpoint cap " +
                    $"({nameof(GenerationPipelineJobOptions)}.{nameof(GenerationPipelineJobOptions.MaxCheckpointBytes)}) — " +
                    $"route stage {at.Stage + 1} to a backend that returns a URI, or raise the cap");

            at = new PipelineCheckpoint(next, null, null, [chained]);
            if (!await ctx.SaveCheckpointAsync(at.ToJson(), ct).ConfigureAwait(false))
                return JobOutcome.Fail($"lease lost after {name} was delivered — stopping so the worker that " +
                                       $"reclaimed the job is the only one to run stage {next + 1}");
            await Progress(ctx, at.Stage - 1, stages, 1, "delivered", ct).ConfigureAwait(false);
        }
    }

    private async Task<JobOutcome> SubmitAsync(JobContext ctx, PipelineCheckpoint at, string name, int stages,
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct)
    {
        var submission = await router.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
        var operation = submission.Operation;

        if (operation.Status == QueuedOperationStatus.Failed)
            // INCONCLUSIVE is the one failure never retried blind: the backend may already hold a billable
            // render this job knows no id for, so a human checks that account before anything re-runs
            return JobOutcome.Fail(operation.Inconclusive
                ? $"{name}: submission to '{submission.ProviderId}' had no answer, so it may already be running: " +
                  $"{operation.Detail}. Check that backend before re-running this job — it is not retried " +
                  "automatically, because a duplicate submission is a duplicate charge."
                : $"{name} was not accepted: {operation.Detail ?? "no backend accepted the generation"}");

        // checkpoint FIRST: everything after this can be redone safely, and the submission cannot
        if (!await ctx.SaveCheckpointAsync(
                (at with { ProviderId = submission.ProviderId, OperationId = operation.Id }).ToJson(), ct)
                .ConfigureAwait(false))
            return JobOutcome.Fail(
                $"lease lost right after {name} submitted operation {operation.Id} to '{submission.ProviderId}' — " +
                "stopping so another worker doesn't run a second paid render (the operation id is in this " +
                "message for manual recovery)");

        await Progress(ctx, at.Stage, stages, 0, "submitted", ct).ConfigureAwait(false);
        return JobOutcome.Poll(_options.PollDelay);   // Poll, never Retry: the submit succeeded (JobOutcome.Poll)
    }

    /// <summary>A pending outcome for the runner, or the fetched stage.</summary>
    private async Task<(JobOutcome? Wait, Produced? Fetched)> PollAsync(
        JobContext ctx, PipelineCheckpoint at, string name, int stages, CancellationToken ct)
    {
        var (providerId, operationId) = (at.ProviderId!, at.OperationId!);
        if (Backend(providerId) is not { } backend)
            return (JobOutcome.Fail($"{name}: backend '{providerId}' holds operation {operationId} but is no " +
                                    "longer registered — re-register it to resume, or fail the job deliberately"), null);

        var operation = await backend.PollAsync(operationId, ct).ConfigureAwait(false);
        switch (operation.Status)
        {
            case QueuedOperationStatus.Queued:
            case QueuedOperationStatus.Running:
                await Progress(ctx, at.Stage, stages, operation.Progress ?? 0, "running", ct).ConfigureAwait(false);
                // re-saving renews the lease across a long render, and a false means another worker has it
                if (!await ctx.SaveCheckpointAsync(at.ToJson(), ct).ConfigureAwait(false))
                    return (JobOutcome.Fail($"lease lost while polling {name}'s operation {operationId} — stopping " +
                                            "so the worker that reclaimed it is the only one still running it"), null);
                return (JobOutcome.Poll(_options.PollDelay), null);

            case QueuedOperationStatus.Succeeded:
                // revalidate BEFORE fetching: the fetch bills and the sink receives, and the store fences neither
                if (!await ctx.SaveCheckpointAsync(at.ToJson(), ct).ConfigureAwait(false))
                    return (JobOutcome.Fail($"lease lost before fetching {name}'s operation {operationId} — " +
                                            "stopping so the render is not fetched, billed and delivered twice"), null);
                var result = await backend.FetchAsync(operationId, ct).ConfigureAwait(false);
                return result.IsOk
                    ? (null, new Produced(result, providerId, operationId, Queued: true))
                    : (JobOutcome.Fail($"{name}: operation {operationId} succeeded but its artifacts could not be " +
                                       $"fetched: {result.Detail}"), null);

            default:
                return (JobOutcome.Fail($"{name}: operation {operationId} at '{providerId}' ended " +
                                        $"{operation.Status}: {operation.Detail}"), null);
        }
    }

    /// <summary>Overall progress in hundredths of a stage, labelled with the stage the fraction belongs to.</summary>
    private static Task<bool> Progress(
        JobContext ctx, int stage, int stages, double fraction, string what, CancellationToken ct) =>
        ctx.ReportProgressAsync(stage * 100 + (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100), stages * 100,
            $"stage {stage + 1} of {stages}: {what}", ct);

    private IMediaJobProvider? Backend(string providerId) => _providers
        .FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase))
        as IMediaJobProvider;

    /// <summary>The ONE artifact <paramref name="next"/> chains from <paramref name="produced"/>, or null when
    /// zero or several qualify — a refusal, never a fallback to the first.</summary>
    private static MediaArtifact? Chain(GenerationPipelineJobStage next, IReadOnlyList<MediaArtifact> produced)
    {
        var qualifying = next.InputMediaType is { } wanted
            ? produced.Where(a => Matches(wanted, a.MediaType)).ToList()
            : [.. produced];
        return qualifying.Count == 1 ? qualifying[0] : null;
    }

    private static string Refusal(GenerationPipelineJobStage next, IReadOnlyList<MediaArtifact> produced, int nextIndex) =>
        next.InputMediaType is { } wanted
            ? $"stage {nextIndex + 1}'s {nameof(GenerationPipelineJobStage.InputMediaType)} '{wanted}' matches " +
              $"{produced.Count(a => Matches(wanted, a.MediaType))} of stage {nextIndex}'s {produced.Count} artifacts " +
              $"({string.Join(", ", produced.Select(a => a.MediaType))}); exactly one must chain forward"
            : $"stage {nextIndex + 1} cannot choose among stage {nextIndex}'s {produced.Count} artifacts " +
              $"({string.Join(", ", produced.Select(a => a.MediaType))}); set " +
              $"{nameof(GenerationPipelineJobStage)}.{nameof(GenerationPipelineJobStage.InputMediaType)} to say " +
              "which one chains forward";

    /// <summary>An exact media type or a <c>type/*</c> wildcard, case-insensitive, parameters ignored.</summary>
    private static bool Matches(string wanted, string mediaType)
    {
        var (want, have) = (Essence(wanted), Essence(mediaType));
        return want.EndsWith("/*", StringComparison.Ordinal)
            ? have.StartsWith(want[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(want, have, StringComparison.OrdinalIgnoreCase);
    }

    private static string Essence(string mediaType) =>
        (mediaType.IndexOf(';') is var at and >= 0 ? mediaType[..at] : mediaType).Trim();

    /// <summary>What a finished stage handed back, and whose it was.</summary>
    private sealed record Produced(MediaResponse Response, string ProviderId, string OperationId, bool Queued);

    /// <summary>Where the job is: the stage in hand, its queued operation once submitted (the id means nothing
    /// without its issuer, so both), and the artifact it chains from the stage before.</summary>
    private sealed record PipelineCheckpoint(
        int Stage, string? ProviderId, string? OperationId, IReadOnlyList<MediaArtifact> Chained)
    {
        public string ToJson()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("stage", Stage);
                if (ProviderId is not null) writer.WriteString("providerId", ProviderId);
                if (OperationId is not null) writer.WriteString("operationId", OperationId);
                writer.WriteStartArray("artifacts");
                foreach (var artifact in Chained)
                {
                    writer.WriteStartObject();
                    writer.WriteString("mediaType", artifact.MediaType);
                    if (artifact.Data is { } data) writer.WriteString("data", Convert.ToBase64String(data));
                    if (artifact.Uri is { } uri) writer.WriteString("uri", uri);
                    if (artifact.Metadata is { Count: > 0 } metadata)
                    {
                        writer.WriteStartObject("metadata");
                        foreach (var (key, value) in metadata) writer.WriteString(key, value);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>Null for anything this handler did not write — a stage out of range, an operation missing
        /// its issuer, a later stage with nothing to chain, undecodable bytes. Never a partial read.</summary>
        public static PipelineCheckpoint? Parse(string checkpoint, int stages)
        {
            try
            {
                using var doc = JsonDocument.Parse(checkpoint);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("stage", out var s) || s.ValueKind != JsonValueKind.Number ||
                    !s.TryGetInt32(out var stage) ||
                    stage < 0 || stage >= stages)
                    return null;

                var (providerId, operationId) = (GenerationJson.Str(root, "providerId"), GenerationJson.Str(root, "operationId"));
                if ((providerId is null) != (operationId is null)) return null;

                var chained = new List<MediaArtifact>();
                if (root.TryGetProperty("artifacts", out var array) && array.ValueKind == JsonValueKind.Array)
                    foreach (var element in array.EnumerateArray())
                    {
                        if (GenerationJson.Str(element, "mediaType") is not { } mediaType) return null;
                        var data = GenerationJson.Bytes(element);
                        if (data is null && GenerationJson.Str(element, "data") is not null) return null;
                        chained.Add(new MediaArtifact(mediaType, data, GenerationJson.Str(element, "uri"), Metadata(element)));
                    }

                return (stage == 0) == (chained.Count == 0)
                    ? new PipelineCheckpoint(stage, providerId, operationId, chained)
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static Dictionary<string, string>? Metadata(JsonElement element)
        {
            if (!element.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
                return null;
            var values = new Dictionary<string, string>();
            foreach (var entry in metadata.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.String) values[entry.Name] = entry.Value.GetString() ?? "";
            return values;
        }
    }
}
