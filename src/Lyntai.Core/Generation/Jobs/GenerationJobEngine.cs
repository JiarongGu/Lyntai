using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;

namespace Lyntai.Generation.Jobs;

/// <summary>The ONE durable generation machine — submit → checkpoint → poll → fetch → bill → checkpoint →
/// deliver, stage after stage — behind both job handlers. <see cref="GenerationPipelineJobHandler"/> runs a
/// pipeline through it and <see cref="GenerationRenderJobHandler"/> a single render, so a rule written for one
/// (a result checkpointed BEFORE delivery, so a throwing sink never re-bills) cannot drift from the other.</summary>
internal sealed class GenerationJobEngine(
    IMediaRouter router,
    IEnumerable<IModelProvider> providers,
    IGenerationArtifactSink sink,
    GenerationPipelineJobOptions? options,
    IUsageTracker? usage,
    MediaRoutingPolicy? policy)
{
    /// <summary>How a job type drives the machine.</summary>
    /// <param name="QueuedOnly">Submit only — never render inline, never fall back to the inline door.</param>
    /// <param name="Standalone">A lone render rather than a pipeline: its messages and progress name no stage,
    /// and its delivery carries no <see cref="GenerationArtifactDelivery.StageIndex"/>.</param>
    internal sealed record Mode(bool QueuedOnly, bool Standalone);

    /// <summary>A pipeline: each stage through the door its first capable candidate serves.</summary>
    internal static readonly Mode Pipeline = new(QueuedOnly: false, Standalone: false);

    /// <summary>A render job: one queued stage.</summary>
    internal static readonly Mode Render = new(QueuedOnly: true, Standalone: true);

    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];
    private readonly GenerationPipelineJobOptions _options = options ?? new GenerationPipelineJobOptions();
    private readonly MediaRoutingPolicy _policy = policy ?? new MediaRoutingPolicy();

    private long Cap => Math.Max(0, _options.MaxCheckpointBytes);

    /// <summary>Run <paramref name="job"/> from <paramref name="checkpoint"/> — the context's own, or a legacy
    /// shape a handler translated — until it completes, fails or waits on a queued stage.</summary>
    public async Task<JobOutcome> RunAsync(
        JobContext ctx, GenerationPipelineJob job, string? checkpoint, string jobType, Mode mode, CancellationToken ct)
    {
        var stages = job.Stages.Count;
        var at = string.IsNullOrWhiteSpace(checkpoint)
            ? new PipelineCheckpoint(0)
            : PipelineCheckpoint.Parse(checkpoint, stages);
        if (at is null)
            return JobOutcome.Fail($"unreadable {jobType} checkpoint — not starting over, which would run again " +
                                   "everything already paid for");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stage = job.Stages[at.Stage];
            var name = mode.Standalone ? "the render" : $"stage {at.Stage + 1} of {stages}";
            Produced produced;

            if (at.Pending is { } pending)
                // checkpointed before a delivery that did not land: deliver it, never render or fetch it again
                produced = pending;
            else
            {
                var (wait, result) = at.OperationId is not null
                    ? await PollAsync(ctx, at, name, stages, mode, ct).ConfigureAwait(false)
                    : await RunStageAsync(ctx, at, stage, name, stages, mode, ct).ConfigureAwait(false);
                if (wait is not null) return wait;
                produced = result!;

                // bill before anything else: the money is spent either way. Only a queued stage — the router
                // that ran an inline one recorded it, when the budget decorator wraps it.
                if (produced.Queued && usage is not null)
                    await BudgetGate.RecordCostAsync(usage, stage.Request.Consumer, produced.Response.Usage, ct)
                        .ConfigureAwait(false);

                // the result is checkpointed BEFORE delivery, so a sink that throws gets it again without a second
                // render, fetch or bill; one too big for the checkpoint is delivered unprotected
                if (InlineBytes(produced.Response.Artifacts) <= Cap &&
                    !await ctx.SaveCheckpointAsync(new PipelineCheckpoint(at.Stage, Pending: produced).ToJson(), ct)
                        .ConfigureAwait(false))
                    return JobOutcome.Fail($"lease lost after {name} produced — stopping so the worker that " +
                                           "reclaimed the job is the only one to deliver it");
            }

            var final = at.Stage == stages - 1;
            // every stage is delivered as it finishes, so a later failure never loses one already paid for
            await sink.ReceiveAsync(new GenerationArtifactDelivery(ctx.JobId, produced.ProviderId,
                produced.OperationId, produced.Response.Artifacts, produced.Response.Usage)
            {
                StageIndex = mode.Standalone ? null : at.Stage,
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
            if ((chained.Data?.LongLength ?? 0) > Cap)
                return JobOutcome.Fail(
                    $"{name}'s artifact carries {chained.Data!.LongLength} bytes inline, over the {Cap}-byte " +
                    $"checkpoint cap ({nameof(GenerationPipelineJobOptions)}.{nameof(GenerationPipelineJobOptions.MaxCheckpointBytes)}) — " +
                    $"route stage {at.Stage + 1} to a backend that returns a URI, or raise the cap");

            at = new PipelineCheckpoint(next, Chained: [chained]);
            if (!await ctx.SaveCheckpointAsync(at.ToJson(), ct).ConfigureAwait(false))
                return JobOutcome.Fail($"lease lost after {name} was delivered — stopping so the worker that " +
                                       $"reclaimed the job is the only one to run stage {next + 1}");
            await Progress(ctx, at.Stage - 1, stages, 1, "delivered", mode, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The checkpoint of a stage-0 operation already submitted — what a job that checkpointed only
    /// <c>{providerId, operationId}</c> resumes from.</summary>
    internal static string SubmittedCheckpoint(string providerId, string operationId) =>
        new PipelineCheckpoint(0, providerId, operationId).ToJson();

    /// <summary>Run a stage not yet started, through the door its candidates choose — and the other door when
    /// that one is exhausted without committing anything.</summary>
    private async Task<(JobOutcome? Wait, Produced? Produced)> RunStageAsync(JobContext ctx, PipelineCheckpoint at,
        GenerationPipelineJobStage stage, string name, int stages, Mode mode, CancellationToken ct)
    {
        var request = at.Stage == 0 ? stage.Request : stage.Request with
        {
            Inputs = [.. stage.Request.Inputs, .. (at.Chained ?? []).Select(a => a.ToInput(stage.InputRole))],
        };
        var candidates = stage.Candidates.Select(ProviderCandidateSpec.Parse).ToList();

        // the FIRST candidate able to serve the stage picks the door, in the caller's order
        bool[] doors = mode.QueuedOnly ? [true]
            : candidates.Select(c => Door(c, request)).FirstOrDefault(d => d is not null) == ProviderOperation.Queued
                ? [true, false]
                : [false, true];
        string? exhausted = null;   // the first door's own words, once it ran out of candidates

        foreach (var queued in doors)
        {
            if (exhausted is not null && !Serves(candidates, request, queued)) break;

            string reason;
            ProviderVerdict? verdict;
            if (queued)
            {
                var submission = await router.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
                var operation = submission.Operation;
                if (operation.Status != QueuedOperationStatus.Failed)
                    return (await CheckpointSubmissionAsync(ctx, at, name, stages, submission, mode, ct).ConfigureAwait(false), null);
                // INCONCLUSIVE is never retried blind, on this door or the other: the backend may already hold a
                // billable render this job knows no id for, so a human checks that account first
                if (operation.Inconclusive)
                    return (JobOutcome.Fail(
                        $"{name}: submission to '{submission.ProviderId}' had no answer, so it may already be running: " +
                        $"{operation.Detail}. Check that backend before re-running this job — it is not retried " +
                        "automatically, because a duplicate submission is a duplicate charge."), null);
                (reason, verdict) = ($"was not accepted: {operation.Detail ?? "no backend accepted the generation"}",
                    operation.Verdict);
            }
            else
            {
                var response = await router.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
                if (response.IsOk) return (null, new Produced(response, response.ProviderId ?? "", "", Queued: false));
                (reason, verdict) = ($"failed ({response.Verdict}): {response.Detail}", response.Verdict);
            }

            if (exhausted is not null) reason = $"{reason} — after its other door: {exhausted}";
            // a refusal the policy SURFACED is an answer about the request, so the other door is not asked;
            // a door the router ran out of candidates on committed nothing
            if (verdict is not { } v || _policy.ActionFor(v) == FallbackAction.Surface || exhausted is not null)
                return (JobOutcome.Fail($"{name} {reason}"), null);
            exhausted = reason;
        }

        return (JobOutcome.Fail($"{name} {exhausted}"), null);
    }

    /// <summary>Which door <paramref name="candidate"/> would serve <paramref name="request"/> through — queued
    /// wherever it can — or null when it can serve neither.</summary>
    private ProviderOperation? Door(ProviderCandidate candidate, MediaRequest request) =>
        Serves([candidate], request, queued: true) ? ProviderOperation.Queued
        : Serves([candidate], request, queued: false) ? ProviderOperation.Complete
        : null;

    /// <summary>The router's own capability filter, for one door.</summary>
    private bool Serves(IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, bool queued) =>
        MediaRouter.Capable(_providers, candidates, request,
            queued ? ProviderOperation.Queued : ProviderOperation.Complete).Count > 0;

    private async Task<JobOutcome> CheckpointSubmissionAsync(JobContext ctx, PipelineCheckpoint at, string name,
        int stages, MediaSubmission submission, Mode mode, CancellationToken ct)
    {
        var operation = submission.Operation;
        // checkpoint FIRST: everything after this can be redone safely, and the submission cannot. The chained
        // artifact is dropped — the backend has it, and nothing reads it again.
        if (!await ctx.SaveCheckpointAsync(
                new PipelineCheckpoint(at.Stage, submission.ProviderId, operation.Id).ToJson(), ct).ConfigureAwait(false))
            return JobOutcome.Fail(
                $"lease lost right after {name} submitted operation {operation.Id} to '{submission.ProviderId}' — " +
                "stopping so another worker doesn't run a second paid render (the operation id is in this " +
                "message for manual recovery)");

        await Progress(ctx, at.Stage, stages, 0, "submitted", mode, ct).ConfigureAwait(false);
        return JobOutcome.Poll(_options.PollDelay);   // Poll, never Retry: the submit succeeded (JobOutcome.Poll)
    }

    /// <summary>A pending outcome for the runner, or the fetched stage.</summary>
    private async Task<(JobOutcome? Wait, Produced? Fetched)> PollAsync(
        JobContext ctx, PipelineCheckpoint at, string name, int stages, Mode mode, CancellationToken ct)
    {
        var (providerId, operationId) = (at.ProviderId!, at.OperationId!);
        if (MediaJobBackends.Find(_providers, providerId) is not { } backend)
            return (JobOutcome.Fail($"{name}: backend '{providerId}' holds operation {operationId} but is no " +
                                    "longer registered — re-register it to resume, or fail the job deliberately"), null);

        var operation = await backend.PollAsync(operationId, ct).ConfigureAwait(false);
        switch (operation.Status)
        {
            case QueuedOperationStatus.Queued:
            case QueuedOperationStatus.Running:
                await Progress(ctx, at.Stage, stages, operation.Progress ?? 0, "running", mode, ct).ConfigureAwait(false);
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

    /// <summary>Overall progress in hundredths of a stage, labelled with the stage the fraction belongs to —
    /// except for a lone render, which has no stage to name.</summary>
    private static Task<bool> Progress(
        JobContext ctx, int stage, int stages, double fraction, string what, Mode mode, CancellationToken ct) =>
        ctx.ReportProgressAsync(stage * 100 + (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100), stages * 100,
            mode.Standalone ? what : $"stage {stage + 1} of {stages}: {what}", ct);

    private static long InlineBytes(IReadOnlyList<MediaArtifact> artifacts) =>
        artifacts.Sum(a => a.Data?.LongLength ?? 0);

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

    /// <summary>What a finished stage handed back, and whose it was. <see cref="Queued"/> says whether this
    /// machine still owes its bill.</summary>
    private sealed record Produced(MediaResponse Response, string ProviderId, string OperationId, bool Queued);

    /// <summary>Where the job is — exactly one of three shapes, and <see cref="Parse"/> accepts no other: a stage
    /// SUBMITTED (its operation and the backend that issued it), a stage READY to run (the one artifact it
    /// chains; never stage 0, which needs no checkpoint), or a stage PRODUCED but not yet delivered.</summary>
    private sealed record PipelineCheckpoint(
        int Stage, string? ProviderId = null, string? OperationId = null,
        IReadOnlyList<MediaArtifact>? Chained = null, Produced? Pending = null)
    {
        public string ToJson() => GenerationJson.WriteObject(writer =>
        {
            writer.WriteNumber("stage", Stage);
            if (OperationId is not null)
            {
                writer.WriteString("providerId", ProviderId);
                writer.WriteString("operationId", OperationId);
            }
            else if (Pending is { } pending)
            {
                writer.WriteStartObject("pending");
                if (pending.ProviderId.Length > 0) writer.WriteString("providerId", pending.ProviderId);
                if (pending.OperationId.Length > 0) writer.WriteString("operationId", pending.OperationId);
                if (pending.Response.Usage is { } usage)
                {
                    writer.WriteStartObject("usage");
                    if (usage.Count is { } count) writer.WriteNumber("count", count);
                    if (usage.Seconds is { } seconds) writer.WriteNumber("seconds", seconds);
                    if (usage.CostUsd is { } cost) writer.WriteNumber("costUsd", cost);
                    writer.WriteEndObject();
                }
                WriteArtifacts(writer, pending.Response.Artifacts);
                writer.WriteEndObject();
            }
            else
                WriteArtifacts(writer, Chained ?? []);
        });

        /// <summary>Null for anything but the three shapes <see cref="ToJson"/> writes — never a partial read, since
        /// guessing at a checkpoint is guessing at what was already paid for.</summary>
        public static PipelineCheckpoint? Parse(string checkpoint, int stages)
        {
            try
            {
                using var doc = JsonDocument.Parse(checkpoint);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("stage", out var s) || s.ValueKind != JsonValueKind.Number ||
                    !s.TryGetInt32(out var stage) || stage < 0 || stage >= stages)
                    return null;

                var (providerId, operationId) = (GenerationJson.Str(root, "providerId"), GenerationJson.Str(root, "operationId"));
                var hasArtifacts = root.TryGetProperty("artifacts", out var artifacts);
                var hasPending = root.TryGetProperty("pending", out var pending);

                if (providerId is not null || operationId is not null)
                    return providerId is not null && operationId is not null && !hasArtifacts && !hasPending
                        ? new PipelineCheckpoint(stage, providerId, operationId)
                        : null;

                if (hasPending)   // a produced result is never empty: an Ok with nothing in it is not one
                    return !hasArtifacts && ReadArtifacts(pending, "artifacts") is { Count: > 0 } produced
                        ? new PipelineCheckpoint(stage, Pending: new Produced(
                            new MediaResponse(ProviderVerdict.Ok, produced, ReadUsage(pending)),
                            GenerationJson.Str(pending, "providerId") ?? "",
                            GenerationJson.Str(pending, "operationId") ?? "",
                            Queued: false))
                        : null;

                return stage > 0 && hasArtifacts && ReadArtifacts(root, "artifacts") is { Count: 1 } chained
                    ? new PipelineCheckpoint(stage, Chained: chained)
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void WriteArtifacts(Utf8JsonWriter writer, IReadOnlyList<MediaArtifact> artifacts)
        {
            writer.WriteStartArray("artifacts");
            foreach (var artifact in artifacts)
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
        }

        /// <summary>The artifacts under <paramref name="name"/>, or null when any of them does not read back
        /// whole — a missing media type, or bytes that are not base64.</summary>
        private static List<MediaArtifact>? ReadArtifacts(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
                return null;
            var read = new List<MediaArtifact>();
            foreach (var entry in array.EnumerateArray())
            {
                if (GenerationJson.Str(entry, "mediaType") is not { } mediaType) return null;
                var data = GenerationJson.Bytes(entry);
                if (data is null && GenerationJson.Str(entry, "data") is not null) return null;
                read.Add(new MediaArtifact(mediaType, data, GenerationJson.Str(entry, "uri"), Metadata(entry)));
            }
            return read;
        }

        private static MediaUsage? ReadUsage(JsonElement pending)
        {
            if (!pending.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
            return new MediaUsage(
                usage.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var count) ? count : null,
                usage.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null,
                usage.TryGetProperty("costUsd", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null);
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

/// <summary>The one lookup of a registered backend that can hold a queued operation — shared by the job engine
/// and the status/fetch tools.</summary>
internal static class MediaJobBackends
{
    /// <summary>The registered backend with this id, IF it is asynchronous. Null covers both "no such backend"
    /// and "that one is inline-only".</summary>
    public static IMediaJobProvider? Find(IEnumerable<IModelProvider> providers, string id) =>
        ProviderLookup.Find(providers, id) as IMediaJobProvider;
}
