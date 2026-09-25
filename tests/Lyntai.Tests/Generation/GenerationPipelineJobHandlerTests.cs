using Lyntai.Generation.Jobs;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>The pipeline job: the render job's durability, several stages long. Driven through the REAL
/// <see cref="MediaRouter"/>, so a stage's door is chosen by the same capability filter production uses, and
/// through a context that persists what the handler checkpoints, the way the runner would.</summary>
public class GenerationPipelineJobHandlerTests
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(3);

    private sealed class CollectingSink(Func<double>? spent = null) : IGenerationArtifactSink
    {
        public List<GenerationArtifactDelivery> Received { get; } = [];

        /// <summary>What the ledger held when each delivery ARRIVED — how "billed before delivery" is seen.</summary>
        public List<double> SpentAtDelivery { get; } = [];

        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
        {
            Received.Add(delivery);
            if (spent is not null) SpentAtDelivery.Add(spent());
            return Task.CompletedTask;
        }
    }

    /// <summary>Persists every checkpoint the handler saves and answers the lease question as a store would;
    /// <see cref="LeaseHeldFor"/> loses the lease on a chosen save (its argument is the save's 0-based number).</summary>
    private sealed class RecordingContext
    {
        public string? Checkpoint { get; private set; }
        public List<string> Saved { get; } = [];
        public List<(int Done, int Total, string? Stage)> Progress { get; } = [];
        public Func<int, bool> LeaseHeldFor { get; set; } = _ => true;

        public JobContext Build(string payload, string? checkpoint = null)
        {
            Checkpoint = checkpoint;
            return new JobContext(Guid.NewGuid(), payload, checkpoint, attempts: 1,
                saveCheckpoint: (c, _) =>
                {
                    var held = LeaseHeldFor(Saved.Count);
                    Saved.Add(c);
                    if (held) Checkpoint = c;
                    return Task.FromResult(held);
                },
                reportProgress: (done, total, stage, _) =>
                {
                    Progress.Add((done, total, stage));
                    return Task.FromResult(true);
                });
        }
    }

    /// <summary>A queued backend whose answers are scripted: each submission is recorded, each poll reads the
    /// next status (Succeeded when none is left), and each fetch returns <see cref="Fetch"/>.</summary>
    private sealed class QueuedBackend : IModelProvider, IMediaJobProvider
    {
        public required string Id { get; init; }
        public string Produces { get; init; } = ProviderKinds.Video;

        /// <summary>Also declare <see cref="ProviderOperation.Complete"/> — a backend serving both doors.</summary>
        public bool AlsoInline { get; init; }

        public ProviderCapabilities Capabilities => new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [Produces],
            Operations = AlsoInline ? [ProviderOperation.Complete, ProviderOperation.Queued] : [ProviderOperation.Queued],
            SupportsInputs = true,
        };

        public List<MediaRequest> Submitted { get; } = [];
        public Queue<QueuedOperation> Polls { get; } = new();
        public int Fetches { get; private set; }
        public int InlineCalls { get; private set; }
        public bool Inconclusive { get; init; }
        public double? CostUsd { get; init; }

        public Func<string, IReadOnlyList<MediaArtifact>> Fetch { get; init; } =
            op => [new MediaArtifact("video/mp4", Uri: $"https://example.invalid/{op}.mp4")];

        public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderProbeResult(true, "ready"));

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
        {
            InlineCalls++;
            return Task.FromResult(MediaResponse.Success([new MediaArtifact("image/png", Data: [1])]));
        }

        public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default)
        {
            Submitted.Add(request);
            return Task.FromResult(Inconclusive
                ? new QueuedOperation("", QueuedOperationStatus.Failed, Detail: $"{Id}: the connection dropped")
                    { Inconclusive = true }
                : new QueuedOperation($"{Id}-op-{Submitted.Count}", QueuedOperationStatus.Queued));
        }

        public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(Polls.Count > 0
                ? Polls.Dequeue() with { Id = operationId }
                : new QueuedOperation(operationId, QueuedOperationStatus.Succeeded, Progress: 1));

        public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default)
        {
            Fetches++;
            return Task.FromResult(MediaResponse.Success(Fetch(operationId), new MediaUsage(Count: 1, CostUsd: CostUsd)));
        }

        public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Cancelled));
    }

    /// <summary>An inline-only backend that records what it was asked and answers with <see cref="Answer"/>.</summary>
    private sealed class InlineBackend : IModelProvider
    {
        public required string Id { get; init; }
        public string Produces { get; init; } = ProviderKinds.Image;

        public ProviderCapabilities Capabilities => new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [Produces],
            Operations = [ProviderOperation.Complete],
            SupportsInputs = true,
        };

        public List<MediaRequest> Requests { get; } = [];

        public Func<MediaRequest, MediaResponse> Answer { get; init; } =
            _ => MediaResponse.Success([new MediaArtifact("image/png", Data: [0x89, 0x50, 0x4E, 0x47])]);

        public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderProbeResult(true, "ready"));

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Answer(request));
        }
    }

    private static GenerationPipelineJobHandler Handler(
        IReadOnlyList<IModelProvider> providers, IGenerationArtifactSink sink,
        GenerationPipelineJobOptions? options = null, IUsageTracker? usage = null, IMediaRouter? router = null) =>
        new(router ?? new MediaRouter(providers), providers, sink,
            options ?? new GenerationPipelineJobOptions { PollDelay = PollDelay }, usage);

    private static MediaRequest Request(string kind) => new() { Kind = kind, Prompt = "a cat" };

    private static GenerationPipelineJobStage Stage(string kind, params string[] candidates) =>
        new(candidates, Request(kind));

    private static string Payload(params GenerationPipelineJobStage[] stages) => new GenerationPipelineJob(stages).ToJson();

    private static MediaResponse Produced(params MediaArtifact[] artifacts) => MediaResponse.Success(artifacts);

    /// <summary>Drive the job the way the runner does — step after step, each resumed from the checkpoint the
    /// last one saved — until it stops polling. The factory runs once per step, so each step is a new process
    /// holding only the checkpoint.</summary>
    private static async Task<(JobOutcome Outcome, int Steps)> RunAsync(
        Func<GenerationPipelineJobHandler> handler, RecordingContext ctx, string payload, int maxSteps = 20)
    {
        for (var step = 1; step <= maxSteps; step++)
        {
            var outcome = await handler().HandleAsync(ctx.Build(payload, ctx.Checkpoint));
            if (outcome.Result != JobOutcome.Kind.Poll) return (outcome, step);
            Assert.Equal(PollDelay, outcome.RetryDelay);
        }
        throw new InvalidOperationException("the job was still polling after the step budget");
    }

    [Fact]
    public void It_handles_its_own_job_type_distinct_from_the_render_job()
    {
        var handler = Handler([], new CollectingSink());

        Assert.Equal("lyntai.generation.pipeline", handler.Type);
        Assert.Equal(GenerationPipelineJobHandler.JobType, handler.Type);
        Assert.NotEqual(GenerationRenderJobHandler.JobType, handler.Type);
    }

    [Fact]
    public async Task An_all_queued_pipeline_submits_checkpoints_polls_fetches_chains_and_delivers_every_stage()
    {
        var image = new QueuedBackend
        {
            Id = "comfy-image", Produces = ProviderKinds.Image,
            Fetch = op => [new MediaArtifact("image/png", Uri: $"https://example.invalid/{op}.png")],
        };
        var video = new QueuedBackend { Id = "comfy-video" };
        var sink = new CollectingSink();
        IModelProvider[] providers = [image, video];
        var ctx = new RecordingContext();
        var ownInput = new MediaInput("image/png", Uri: "https://example.invalid/style.png", Role: MediaInputRoles.Reference);
        var payload = Payload(
            Stage(ProviderKinds.Image, "comfy-image"),
            new GenerationPipelineJobStage(["comfy-video"],
                Request(ProviderKinds.Video) with { Inputs = [ownInput] }) { InputRole = MediaInputRoles.FirstFrame });

        // step 1 submits stage 1 and checkpoints its operation BEFORE the first poll
        var first = await Handler(providers, sink).HandleAsync(ctx.Build(payload));
        Assert.Equal(JobOutcome.Kind.Poll, first.Result);
        Assert.Single(image.Submitted);
        Assert.Contains("comfy-image-op-1", ctx.Checkpoint);
        Assert.Empty(sink.Received);

        var (outcome, steps) = await RunAsync(() => Handler(providers, sink), ctx, payload);

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(2, steps);   // fetch stage 1 and submit stage 2; then fetch stage 2
        Assert.Equal((1, 1), (image.Submitted.Count, video.Submitted.Count));
        Assert.Equal((1, 1), (image.Fetches, video.Fetches));

        // the next stage's own inputs keep their place, and the chained artifact is appended in its role
        var inputs = video.Submitted[0].Inputs;
        Assert.Equal(2, inputs.Count);
        Assert.Equal(ownInput, inputs[0]);
        Assert.Equal("https://example.invalid/comfy-image-op-1.png", inputs[1].Uri);
        Assert.Equal(MediaInputRoles.FirstFrame, inputs[1].Role);

        Assert.Collection(sink.Received,
            d =>
            {
                Assert.Equal((0, false), (d.StageIndex, d.IsFinal));
                Assert.Equal(("comfy-image", "comfy-image-op-1"), (d.ProviderId, d.OperationId));
            },
            d =>
            {
                Assert.Equal((1, true), (d.StageIndex, d.IsFinal));
                Assert.Equal(("comfy-video", "comfy-video-op-1"), (d.ProviderId, d.OperationId));
                Assert.Equal("video/mp4", Assert.Single(d.Artifacts).MediaType);
            });
    }

    [Fact]
    public async Task A_restart_mid_poll_resumes_the_SAME_operation_and_never_submits_again()
    {
        var video = new QueuedBackend { Id = "fal" };
        video.Polls.Enqueue(new QueuedOperation("", QueuedOperationStatus.Running, Progress: 0.25));
        video.Polls.Enqueue(new QueuedOperation("", QueuedOperationStatus.Queued));
        var sink = new CollectingSink();
        IModelProvider[] providers = [video];
        var ctx = new RecordingContext();

        var (outcome, steps) = await RunAsync(() => Handler(providers, sink), ctx,
            Payload(Stage(ProviderKinds.Video, "fal")));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(4, steps);                               // submit, running, queued, succeeded
        Assert.Single(video.Submitted);                       // polled on every resume, never re-submitted
        Assert.All(ctx.Saved, saved => Assert.Contains("fal-op-1", saved));
        Assert.Equal(0, Assert.Single(sink.Received).StageIndex);
    }

    [Fact]
    public async Task Progress_names_the_stage_and_carries_the_backends_own_fraction()
    {
        var video = new QueuedBackend { Id = "fal" };
        video.Polls.Enqueue(new QueuedOperation("", QueuedOperationStatus.Running, Progress: 0.5));
        IModelProvider[] providers = [new InlineBackend { Id = "sd" }, video];
        var ctx = new RecordingContext();

        await RunAsync(() => Handler(providers, new CollectingSink()), ctx,
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal")));

        Assert.Contains((150, 200, "stage 2 of 2: running"), ctx.Progress);   // stage 1 done, half of stage 2
        Assert.Equal((200, 200), (ctx.Progress[^1].Done, ctx.Progress[^1].Total));
    }

    [Fact]
    public async Task A_restart_between_stages_resumes_at_the_next_stage_with_the_checkpointed_artifact()
    {
        byte[] still = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7];
        var image = new InlineBackend
        {
            Id = "sd",
            Answer = _ => Produced(new MediaArtifact("image/png", Data: still,
                Metadata: new Dictionary<string, string> { ["seed"] = "42" })),
        };
        var video = new QueuedBackend { Id = "fal" };
        IModelProvider[] providers = [image, video];
        var payload = Payload(
            Stage(ProviderKinds.Image, "sd"),
            Stage(ProviderKinds.Video, "fal") with { InputRole = MediaInputRoles.FirstFrame });

        // run stage 1, and keep the checkpoint saved BETWEEN the stages — before stage 2 was submitted
        var first = new RecordingContext();
        Assert.Equal(JobOutcome.Kind.Poll, (await Handler(providers, new CollectingSink()).HandleAsync(first.Build(payload))).Result);
        var between = Assert.Single(first.Saved, s => !s.Contains("fal-op"));
        Assert.Contains(Convert.ToBase64String(still), between);   // the bytes travel in the checkpoint
        video.Submitted.Clear();

        // a new process resumes from it, as if the first died the moment it was saved
        var sink = new CollectingSink();
        var resumed = await Handler(providers, sink).HandleAsync(new RecordingContext().Build(payload, between));

        Assert.Equal(JobOutcome.Kind.Poll, resumed.Result);
        Assert.Single(image.Requests);                              // stage 1 is NOT rendered again
        var input = Assert.Single(Assert.Single(video.Submitted).Inputs);
        Assert.Equal(("image/png", MediaInputRoles.FirstFrame), (input.MediaType, input.Role));
        Assert.Equal(still, input.Data);                            // restored byte for byte
        Assert.Empty(sink.Received);                                // stage 1 was delivered before that save
    }

    [Fact]
    public async Task A_mixed_pipeline_runs_inline_stages_in_the_step_and_polls_the_queued_one()
    {
        var sd = new InlineBackend { Id = "sd" };
        var fal = new QueuedBackend
        {
            Id = "fal", Produces = ProviderKinds.Image,
            Fetch = op => [new MediaArtifact("image/png", Uri: $"https://example.invalid/{op}.png")],
        };
        var upscale = new InlineBackend { Id = "upscale" };
        IModelProvider[] providers = [sd, fal, upscale];
        var sink = new CollectingSink();
        var ctx = new RecordingContext();
        var payload = Payload(
            Stage(ProviderKinds.Image, "sd"),
            Stage(ProviderKinds.Image, "fal") with { InputRole = MediaInputRoles.Init },
            Stage(ProviderKinds.Image, "upscale") with { InputRole = MediaInputRoles.Init });

        var (outcome, steps) = await RunAsync(() => Handler(providers, sink), ctx, payload);

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(2, steps);   // stage 1 inline + stage 2 submitted; then stage 2 fetched + stage 3 inline
        Assert.Equal((1, 1, 1), (sd.Requests.Count, fal.Submitted.Count, upscale.Requests.Count));
        Assert.Equal("https://example.invalid/fal-op-1.png", Assert.Single(upscale.Requests[0].Inputs).Uri);
        Assert.Equal([0, 1, 2], sink.Received.Select(d => d.StageIndex!.Value));
        Assert.Equal([false, false, true], sink.Received.Select(d => d.IsFinal));
        // the inline door does not say which candidate served it, so an inline stage's ids are empty
        Assert.Equal(("", ""), (sink.Received[0].ProviderId, sink.Received[0].OperationId));
        Assert.Equal(("fal", "fal-op-1"), (sink.Received[1].ProviderId, sink.Received[1].OperationId));
    }

    [Fact]
    public async Task A_candidate_that_can_queue_takes_the_queued_door_even_behind_an_inline_one()
    {
        // THE door rule: decided by what the candidates declare, queued first, whatever their order
        var inline = new InlineBackend { Id = "inline" };
        var queued = new QueuedBackend { Id = "queued", Produces = ProviderKinds.Image };
        IModelProvider[] providers = [inline, queued];

        var outcome = await Handler(providers, new CollectingSink())
            .HandleAsync(new RecordingContext().Build(Payload(Stage(ProviderKinds.Image, "inline", "queued"))));

        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        Assert.Single(queued.Submitted);
        Assert.Empty(inline.Requests);
    }

    [Fact]
    public async Task A_backend_declaring_both_doors_is_driven_through_the_queued_one()
    {
        var both = new QueuedBackend { Id = "both", Produces = ProviderKinds.Image, AlsoInline = true };
        IModelProvider[] providers = [both];
        var ctx = new RecordingContext();

        var (outcome, steps) = await RunAsync(() => Handler(providers, new CollectingSink()), ctx,
            Payload(Stage(ProviderKinds.Image, "both")));

        Assert.Equal((JobOutcome.Kind.Complete, 2), (outcome.Result, steps));
        Assert.Equal((1, 0), (both.Submitted.Count, both.InlineCalls));
    }

    [Fact]
    public async Task A_stage_refuses_to_guess_between_several_artifacts_and_keeps_what_was_paid_for()
    {
        var sd = new InlineBackend
        {
            Id = "sd",
            Answer = _ => Produced(new MediaArtifact("image/png", Data: [1]), new MediaArtifact("image/png", Data: [2])),
        };
        var fal = new QueuedBackend { Id = "fal" };
        IModelProvider[] providers = [sd, fal];
        var sink = new CollectingSink();

        var outcome = await Handler(providers, sink).HandleAsync(new RecordingContext().Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("stage 2 cannot choose among stage 1's 2 artifacts", outcome.Error);
        Assert.Contains(nameof(GenerationPipelineJobStage.InputMediaType), outcome.Error);
        Assert.Empty(fal.Submitted);
        Assert.Equal(2, Assert.Single(sink.Received).Artifacts.Count);   // stage 1 still reached the sink
    }

    [Fact]
    public async Task InputMediaType_picks_the_ONE_matching_artifact_by_type_or_wildcard()
    {
        // a mesh backend's image/* output is a texture atlas: the filter is how a caller names the mesh
        var mesh = new InlineBackend
        {
            Id = "mesh", Produces = ProviderKinds.Model3d,
            Answer = _ => Produced(
                new MediaArtifact("image/png", Uri: "https://example.invalid/atlas.png"),
                new MediaArtifact("model/gltf-binary", Uri: "https://example.invalid/mesh.glb")),
        };
        var render = new InlineBackend { Id = "render" };
        IModelProvider[] providers = [mesh, render];

        foreach (var filter in new[] { "Model/*", "model/gltf-binary; charset=binary" })
        {
            render.Requests.Clear();
            var outcome = await Handler(providers, new CollectingSink()).HandleAsync(new RecordingContext().Build(
                Payload(Stage(ProviderKinds.Model3d, "mesh"),
                    Stage(ProviderKinds.Image, "render") with { InputMediaType = filter })));

            Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
            Assert.Equal("https://example.invalid/mesh.glb", Assert.Single(Assert.Single(render.Requests).Inputs).Uri);
        }
    }

    [Theory]
    [InlineData("image/png", "matches 2 of stage 1's 3 artifacts")]   // several: never the first of them
    [InlineData("video/*", "matches 0 of stage 1's 3 artifacts")]
    public async Task InputMediaType_refuses_when_zero_or_several_artifacts_match(string filter, string said)
    {
        var sd = new InlineBackend
        {
            Id = "sd",
            Answer = _ => Produced(
                new MediaArtifact("image/png", Uri: "https://example.invalid/a.png"),
                new MediaArtifact("image/png", Uri: "https://example.invalid/b.png"),
                new MediaArtifact("model/gltf-binary", Uri: "https://example.invalid/c.glb")),
        };
        var next = new InlineBackend { Id = "next" };
        IModelProvider[] providers = [sd, next];

        var outcome = await Handler(providers, new CollectingSink()).HandleAsync(new RecordingContext().Build(
            Payload(Stage(ProviderKinds.Image, "sd"),
                Stage(ProviderKinds.Image, "next") with { InputMediaType = filter })));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains(said, outcome.Error);
        Assert.Contains("image/png, image/png, model/gltf-binary", outcome.Error);
        Assert.Empty(next.Requests);
    }

    [Fact]
    public async Task Over_the_byte_cap_the_job_FAILS_pointing_at_a_URI_producing_stage_and_checkpoints_nothing()
    {
        var sd = new InlineBackend { Id = "sd", Answer = _ => Produced(new MediaArtifact("image/png", Data: new byte[101])) };
        var fal = new QueuedBackend { Id = "fal" };
        IModelProvider[] providers = [sd, fal];
        var sink = new CollectingSink();
        var ctx = new RecordingContext();
        var options = new GenerationPipelineJobOptions { PollDelay = PollDelay, MaxCheckpointBytes = 100 };

        var outcome = await Handler(providers, sink, options).HandleAsync(ctx.Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("101 bytes", outcome.Error);
        Assert.Contains("URI", outcome.Error);
        Assert.Contains(nameof(GenerationPipelineJobOptions.MaxCheckpointBytes), outcome.Error);
        Assert.Empty(ctx.Saved);                                   // never truncated into a checkpoint
        Assert.Empty(fal.Submitted);
        Assert.Single(sink.Received);                              // the paid stage was still delivered

        // …and at the cap exactly, it chains
        var atCap = new InlineBackend { Id = "sd", Answer = _ => Produced(new MediaArtifact("image/png", Data: new byte[100])) };
        IModelProvider[] fits = [atCap, fal];
        var chained = await Handler(fits, new CollectingSink(), options).HandleAsync(new RecordingContext().Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal"))));
        Assert.Equal(JobOutcome.Kind.Poll, chained.Result);
    }

    [Fact]
    public async Task A_lost_lease_right_after_a_submission_stops_the_job()
    {
        var fal = new QueuedBackend { Id = "fal" };
        IModelProvider[] providers = [fal];
        var sink = new CollectingSink();
        var ctx = new RecordingContext { LeaseHeldFor = _ => false };

        var outcome = await Handler(providers, sink).HandleAsync(ctx.Build(Payload(Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("lease lost", outcome.Error);
        Assert.Contains("fal-op-1", outcome.Error);                // named for manual recovery
        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task A_lost_lease_between_stages_stops_before_the_next_stage_is_paid_for()
    {
        var sd = new InlineBackend { Id = "sd" };
        var fal = new QueuedBackend { Id = "fal" };
        IModelProvider[] providers = [sd, fal];
        var sink = new CollectingSink();
        var ctx = new RecordingContext { LeaseHeldFor = _ => false };

        var outcome = await Handler(providers, sink).HandleAsync(ctx.Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("lease lost", outcome.Error);
        Assert.Empty(fal.Submitted);                               // another worker owns stage 2 now
        Assert.Single(sink.Received);
    }

    [Fact]
    public async Task A_lost_lease_while_polling_stops_the_job()
    {
        var fal = new QueuedBackend { Id = "fal" };
        fal.Polls.Enqueue(new QueuedOperation("", QueuedOperationStatus.Running));
        IModelProvider[] providers = [fal];
        var ctx = new RecordingContext { LeaseHeldFor = save => save == 0 };   // held for the submit only
        var payload = Payload(Stage(ProviderKinds.Video, "fal"));
        await Handler(providers, new CollectingSink()).HandleAsync(ctx.Build(payload));

        var outcome = await Handler(providers, new CollectingSink()).HandleAsync(ctx.Build(payload, ctx.Checkpoint));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("lease lost while polling", outcome.Error);
    }

    [Fact]
    public async Task A_lost_lease_before_fetching_stops_before_anything_is_billed_or_delivered()
    {
        var fal = new QueuedBackend { Id = "fal" };                            // its first poll succeeds
        IModelProvider[] providers = [fal];
        var sink = new CollectingSink();
        var ctx = new RecordingContext { LeaseHeldFor = save => save == 0 };
        var payload = Payload(Stage(ProviderKinds.Video, "fal"));
        await Handler(providers, sink).HandleAsync(ctx.Build(payload));

        var outcome = await Handler(providers, sink).HandleAsync(ctx.Build(payload, ctx.Checkpoint));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("lease lost before fetching", outcome.Error);
        Assert.Equal(0, fal.Fetches);
        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task An_inconclusive_submission_fails_with_the_backend_named_and_is_never_retried()
    {
        var fal = new QueuedBackend { Id = "fal", Inconclusive = true };
        IModelProvider[] providers = [new InlineBackend { Id = "sd" }, fal];
        var ctx = new RecordingContext();

        var outcome = await Handler(providers, new CollectingSink()).HandleAsync(ctx.Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);        // Fail, never Retry: a retry could pay twice
        Assert.Contains("stage 2 of 2", outcome.Error);
        Assert.Contains("'fal'", outcome.Error);
        Assert.Contains("not retried", outcome.Error);
        Assert.Single(fal.Submitted);
        Assert.DoesNotContain(ctx.Saved, s => s.Contains("operationId"));
    }

    [Fact]
    public async Task Spend_is_recorded_per_stage_BEFORE_that_stage_is_delivered()
    {
        var tracker = new InMemoryUsageTracker();
        var image = new QueuedBackend
        {
            Id = "img", Produces = ProviderKinds.Image, CostUsd = 0.10,
            Fetch = op => [new MediaArtifact("image/png", Uri: $"https://example.invalid/{op}.png")],
        };
        var video = new QueuedBackend { Id = "vid", CostUsd = 0.25 };
        IModelProvider[] providers = [image, video];
        var sink = new CollectingSink(() => tracker.TotalAsync().AsTask().Result.CostUsd);

        var (outcome, _) = await RunAsync(() => Handler(providers, sink, usage: tracker), new RecordingContext(),
            Payload(Stage(ProviderKinds.Image, "img"), Stage(ProviderKinds.Video, "vid")));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(2, sink.SpentAtDelivery.Count);
        Assert.Equal(0.10, sink.SpentAtDelivery[0], 6);
        Assert.Equal(0.35, sink.SpentAtDelivery[1], 6);
    }

    [Fact]
    public async Task An_inline_stage_is_billed_by_its_router_and_never_again_by_the_job()
    {
        var tracker = new InMemoryUsageTracker();
        var sd = new InlineBackend
        {
            Id = "sd",
            Answer = _ => MediaResponse.Success([new MediaArtifact("image/png", Data: [1])], new MediaUsage(CostUsd: 0.40)),
        };
        IModelProvider[] providers = [sd];
        var router = new BudgetedMediaRouter(new MediaRouter(providers), tracker, new LyntaiOptions());

        var outcome = await Handler(providers, new CollectingSink(), usage: tracker, router: router)
            .HandleAsync(new RecordingContext().Build(Payload(Stage(ProviderKinds.Image, "sd"))));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(0.40, (await tracker.TotalAsync()).CostUsd, 6);       // once, by the router — not 0.80
    }

    [Fact]
    public async Task A_failing_stage_ends_the_job_naming_the_stage_and_its_verdicts_words()
    {
        var sd = new InlineBackend { Id = "sd" };
        var judge = new InlineBackend { Id = "judge", Answer = _ => MediaResponse.Failure(ProviderVerdict.Refused, "content policy") };
        IModelProvider[] providers = [sd, judge];
        var sink = new CollectingSink();

        var outcome = await Handler(providers, sink).HandleAsync(new RecordingContext().Build(
            Payload(Stage(ProviderKinds.Image, "sd"), Stage(ProviderKinds.Image, "judge") with { InputRole = MediaInputRoles.Init })));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("stage 2 of 2", outcome.Error);
        Assert.Contains("Refused", outcome.Error);
        Assert.Contains("content policy", outcome.Error);
        Assert.Equal(0, Assert.Single(sink.Received).StageIndex);  // stage 1 kept, never re-run
        Assert.Single(sd.Requests);
    }

    [Fact]
    public async Task A_queued_stage_whose_run_fails_ends_the_job_naming_the_stage()
    {
        var fal = new QueuedBackend { Id = "fal" };
        fal.Polls.Enqueue(new QueuedOperation("", QueuedOperationStatus.Failed, Detail: "node 3: out of memory"));
        IModelProvider[] providers = [fal];

        var (outcome, _) = await RunAsync(() => Handler(providers, new CollectingSink()), new RecordingContext(),
            Payload(Stage(ProviderKinds.Video, "fal")));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("stage 1 of 1", outcome.Error);
        Assert.Contains("node 3: out of memory", outcome.Error);
        Assert.Equal(0, fal.Fetches);
    }

    [Fact]
    public async Task A_submission_no_candidate_accepts_fails_rather_than_polling_forever()
    {
        IModelProvider[] providers = [new QueuedBackend { Id = "fal", Produces = ProviderKinds.Video }];

        var outcome = await Handler(providers, new CollectingSink()).HandleAsync(new RecordingContext().Build(
            Payload(Stage(ProviderKinds.Audio, "fal"))));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("stage 1 of 1", outcome.Error);
    }

    [Fact]
    public async Task An_unreadable_payload_or_checkpoint_fails_and_never_restarts_from_stage_one()
    {
        var sd = new InlineBackend { Id = "sd" };
        IModelProvider[] providers = [sd];
        var payload = Payload(Stage(ProviderKinds.Image, "sd"));

        var badPayload = await Handler(providers, new CollectingSink()).HandleAsync(new RecordingContext().Build("{not json"));
        Assert.Equal(JobOutcome.Kind.Fail, badPayload.Result);
        Assert.Contains("payload", badPayload.Error);

        // restarting from stage 1 would re-run every stage already paid for
        foreach (var checkpoint in new[] { "{not json", """{"stage":7}""", """{"stage":0,"providerId":"sd"}""" })
        {
            var outcome = await Handler(providers, new CollectingSink()).HandleAsync(new RecordingContext().Build(payload, checkpoint));
            Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
            Assert.Contains("checkpoint", outcome.Error);
        }
        Assert.Empty(sd.Requests);
    }

    [Fact]
    public void A_pipeline_needs_a_stage_and_its_first_stage_chains_from_nothing()
    {
        Assert.Throws<ArgumentException>(() => new GenerationPipelineJob([]));
        Assert.Throws<ArgumentException>(() => new GenerationPipelineJob(
            [Stage(ProviderKinds.Image, "sd") with { InputRole = MediaInputRoles.Init }]));
        Assert.Throws<ArgumentException>(() => new GenerationPipelineJob(
            [Stage(ProviderKinds.Image, "sd") with { InputMediaType = "image/png" }]));

        Assert.Null(GenerationPipelineJob.Parse("""{"stages":[]}"""));
        Assert.Null(GenerationPipelineJob.Parse("""{"stages":[{"candidates":["sd"],"kind":"image","inputRole":"init"}]}"""));
        Assert.Null(GenerationPipelineJob.Parse("""{"stages":[{"candidates":["sd"]}]}"""));   // a stage with no kind
    }

    [Fact]
    public void The_payload_round_trips_through_its_hand_written_json()
    {
        var job = new GenerationPipelineJob(
        [
            new GenerationPipelineJobStage(["comfyui"], new MediaRequest
            {
                Kind = ProviderKinds.Model3d,
                Consumer = "gallery",
                Inputs = [new MediaInput("model/gltf-binary", Data: [1, 2, 3])],
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["input-path"] = "1.inputs.model_file" },
            }),
            new GenerationPipelineJobStage(["fal:wan-i2v", "comfyui"], new MediaRequest
            {
                Kind = ProviderKinds.Video, Prompt = "orbit it", Model = "wan", TimeoutSeconds = 600,
            })
            { InputRole = MediaInputRoles.FirstFrame, InputMediaType = "image/*" },
        ]);

        var parsed = GenerationPipelineJob.Parse(job.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Stages.Count);
        var (first, second) = (parsed.Stages[0], parsed.Stages[1]);
        Assert.Equal(["comfyui"], first.Candidates);
        Assert.Equal((ProviderKinds.Model3d, "gallery"), (first.Request.Kind, first.Request.Consumer));
        Assert.Equal([1, 2, 3], Assert.Single(first.Request.Inputs).Data);
        Assert.Equal("1.inputs.model_file", first.Request.Option("INPUT-PATH"));
        Assert.Null(first.InputRole);
        Assert.Null(first.InputMediaType);
        Assert.Equal(["fal:wan-i2v", "comfyui"], second.Candidates);
        Assert.Equal(("orbit it", "wan", 600), (second.Request.Prompt, second.Request.Model, second.Request.TimeoutSeconds));
        Assert.Equal((MediaInputRoles.FirstFrame, "image/*"), (second.InputRole, second.InputMediaType));
    }

    [Fact]
    public void A_delivery_from_outside_a_pipeline_reads_as_final_with_no_stage()
    {
        // a render job's delivery IS its job's output: a sink keeping only final artifacts must keep it
        var delivery = new GenerationArtifactDelivery(Guid.NewGuid(), "fal", "op", []);

        Assert.Null(delivery.StageIndex);
        Assert.True(delivery.IsFinal);
    }

    [Fact]
    public async Task It_resolves_from_the_container_with_the_options_a_host_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GenerationPipelineJobOptions { PollDelay = TimeSpan.FromSeconds(1) });
        services.AddSingleton<IGenerationArtifactSink>(new CollectingSink());
        services.AddLyntai(b => b
            .AddProvider(_ => new QueuedBackend { Id = "fal" })
            .AddMediaRouting()
            .AddJobHandler<GenerationPipelineJobHandler>());
        using var sp = services.BuildServiceProvider();

        var handler = Assert.Single(sp.GetServices<IJobHandler>().OfType<GenerationPipelineJobHandler>());
        var outcome = await handler.HandleAsync(new RecordingContext().Build(Payload(Stage(ProviderKinds.Video, "fal"))));

        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        Assert.Equal(TimeSpan.FromSeconds(1), outcome.RetryDelay);   // the registered options, not the default
    }
}
