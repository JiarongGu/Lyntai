using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Microsoft.Extensions.DependencyInjection;

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
/// next — in which any stage may be QUEUED: submitted, checkpointed and polled across process lifetimes as
/// <see cref="GenerationRenderJobHandler"/> runs one render. <see cref="GenerationPipeline.RunPipelineAsync"/>
/// is the in-memory form, and drives the inline door only.</summary>
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

    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];
    private readonly GenerationPipelineJobOptions _options = options ?? new GenerationPipelineJobOptions();
    private readonly MediaRoutingPolicy _policy = policy ?? new MediaRoutingPolicy();

    private long Cap => Math.Max(0, _options.MaxCheckpointBytes);

    /// <inheritdoc/>
    public string Type => JobType;

    /// <inheritdoc/>
    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        if (GenerationPipelineJob.Parse(ctx.Payload) is not { } job)
            return JobOutcome.Fail($"unreadable {JobType} payload — expected GenerationPipelineJob.ToJson()");

        var stages = job.Stages.Count;
        var at = string.IsNullOrWhiteSpace(ctx.Checkpoint)
            ? new PipelineCheckpoint(0)
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

            if (at.Pending is { } pending)
                // checkpointed before a delivery that did not land: deliver it, never render or fetch it again
                produced = pending;
            else
            {
                var (wait, result) = at.OperationId is not null
                    ? await PollAsync(ctx, at, name, stages, ct).ConfigureAwait(false)
                    : await RunAsync(ctx, at, stage, name, stages, ct).ConfigureAwait(false);
                if (wait is not null) return wait;
                produced = result!;

                // bill before anything else: the money is spent either way. Only a queued stage — the router
                // that ran an inline one recorded it, when the budget decorator wraps it.
                if (produced.Queued && usage is not null)
                    await BudgetedMediaRouter.RecordAsync(usage, stage.Request.Consumer, produced.Response.Usage, ct)
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
            if ((chained.Data?.LongLength ?? 0) > Cap)
                return JobOutcome.Fail(
                    $"{name}'s artifact carries {chained.Data!.LongLength} bytes inline, over the {Cap}-byte " +
                    $"checkpoint cap ({nameof(GenerationPipelineJobOptions)}.{nameof(GenerationPipelineJobOptions.MaxCheckpointBytes)}) — " +
                    $"route stage {at.Stage + 1} to a backend that returns a URI, or raise the cap");

            at = new PipelineCheckpoint(next, Chained: [chained]);
            if (!await ctx.SaveCheckpointAsync(at.ToJson(), ct).ConfigureAwait(false))
                return JobOutcome.Fail($"lease lost after {name} was delivered — stopping so the worker that " +
                                       $"reclaimed the job is the only one to run stage {next + 1}");
            await Progress(ctx, at.Stage - 1, stages, 1, "delivered", ct).ConfigureAwait(false);
        }
    }

    /// <summary>Run a stage not yet started, through the door its candidates choose — and the other door when
    /// that one is exhausted without committing anything.</summary>
    private async Task<(JobOutcome? Wait, Produced? Produced)> RunAsync(JobContext ctx, PipelineCheckpoint at,
        GenerationPipelineJobStage stage, string name, int stages, CancellationToken ct)
    {
        var request = at.Stage == 0 ? stage.Request : stage.Request with
        {
            Inputs = [.. stage.Request.Inputs, .. (at.Chained ?? []).Select(a => a.ToInput(stage.InputRole))],
        };
        var candidates = stage.Candidates.Select(ProviderCandidateSpec.Parse).ToList();

        // the FIRST candidate able to serve the stage picks the door, in the caller's order
        var queuedFirst = candidates.Select(c => Door(c, request)).FirstOrDefault(d => d is not null)
            == ProviderOperation.Queued;
        string? exhausted = null;   // the first door's own words, once it ran out of candidates

        bool[] doors = queuedFirst ? [true, false] : [false, true];
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
                    return (await CheckpointSubmissionAsync(ctx, at, name, stages, submission, ct).ConfigureAwait(false), null);
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

            reason = exhausted is null ? $"{name} {reason}" : $"{name} {reason} — after its other door: {exhausted}";
            // a refusal the policy SURFACED is an answer about the request, so the other door is not asked;
            // a door the router ran out of candidates on committed nothing
            if (verdict is not { } v || _policy.ActionFor(v) == FallbackAction.Surface)
                return (JobOutcome.Fail(reason), null);
            if (exhausted is not null) return (JobOutcome.Fail(reason), null);
            exhausted = reason[(name.Length + 1)..];
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
    private bool Serves(IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, bool queued) => queued
        ? MediaRouter.Capable(_providers, candidates, request, ProviderOperation.Queued)
            .Any(capable => capable.Provider is IMediaJobProvider)
        : MediaRouter.Capable(_providers, candidates, request, ProviderOperation.Complete).Count > 0;

    private async Task<JobOutcome> CheckpointSubmissionAsync(JobContext ctx, PipelineCheckpoint at, string name,
        int stages, MediaSubmission submission, CancellationToken ct)
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
    /// handler still owes its bill.</summary>
    private sealed record Produced(MediaResponse Response, string ProviderId, string OperationId, bool Queued);

    /// <summary>Where the job is — exactly one of three shapes, and <see cref="Parse"/> accepts no other: a stage
    /// SUBMITTED (its operation and the backend that issued it), a stage READY to run (the one artifact it
    /// chains; never stage 0, which needs no checkpoint), or a stage PRODUCED but not yet delivered.</summary>
    private sealed record PipelineCheckpoint(
        int Stage, string? ProviderId = null, string? OperationId = null,
        IReadOnlyList<MediaArtifact>? Chained = null, Produced? Pending = null)
    {
        public string ToJson()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
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
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

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
