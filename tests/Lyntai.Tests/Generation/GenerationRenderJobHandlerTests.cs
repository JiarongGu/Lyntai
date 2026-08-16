using Lyntai.Generation;
using Lyntai.Generation.Jobs;
using Lyntai.Generation.Routing;
using Lyntai.Jobs;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The render job handler — where the platform's value over a thin HTTP client actually shows. A
/// hosted render costs money and takes minutes, so the operation id is CHECKPOINTED before the first poll:
/// a process restart resumes polling instead of paying for the same render twice.</summary>
public class GenerationRenderJobHandlerTests
{
    private sealed class CollectingSink : IGenerationArtifactSink
    {
        public List<GenerationArtifactDelivery> Received { get; } = [];

        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
        {
            Received.Add(delivery);
            return Task.CompletedTask;
        }
    }

    /// <summary>A job context that records what the handler checkpointed / reported, the way the runner would
    /// persist it.</summary>
    private sealed class RecordingContext
    {
        public string? Checkpoint { get; private set; }
        public List<string> Stages { get; } = [];
        public bool LeaseHeld { get; set; } = true;

        public JobContext Build(string payload, string? checkpoint = null, int attempts = 1)
        {
            Checkpoint = checkpoint;
            return new JobContext(Guid.NewGuid(), payload, checkpoint, attempts,
                saveCheckpoint: (c, _) => { Checkpoint = c; return Task.FromResult(LeaseHeld); },
                reportProgress: (_, _, stage, _) => { if (stage is not null) Stages.Add(stage); return Task.FromResult(LeaseHeld); });
        }
    }

    private static (GenerationRenderJobHandler Handler, FakeGenerationJobProvider Backend, CollectingSink Sink)
        Handler(FakeGenerationJobProvider? backend = null)
    {
        backend ??= new FakeGenerationJobProvider { Id = "video" };
        IGenerationProvider[] providers = [backend];
        var sink = new CollectingSink();
        var handler = new GenerationRenderJobHandler(new GenerationRouter(providers), providers, sink);
        return (handler, backend, sink);
    }

    private static string Payload() => new GenerationRenderJob(
        ["video"],
        new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "a cat surfing" }).ToJson();

    [Fact]
    public void It_handles_one_well_known_job_type()
    {
        var (handler, _, _) = Handler();

        Assert.Equal("lyntai.generation.render", handler.Type);
        Assert.Equal(GenerationRenderJobHandler.JobType, handler.Type);
    }

    [Fact]
    public async Task The_first_run_submits_and_checkpoints_the_operation_id_before_polling()
    {
        // the whole point: if the process dies now, the resume must poll — not re-submit a paid render
        var (handler, backend, sink) = Handler();
        var ctx = new RecordingContext();

        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);           // come back and poll — NOT an attempt
        Assert.Equal(1, backend.SubmitCalls);
        Assert.NotNull(ctx.Checkpoint);
        Assert.Contains("op-1", ctx.Checkpoint);
        Assert.Contains("video", ctx.Checkpoint);                      // the provider that owns the operation
        Assert.Empty(sink.Received);                                   // nothing delivered yet
    }

    [Fact]
    public async Task A_resume_polls_the_checkpointed_operation_and_never_submits_again()
    {
        var (handler, backend, sink) = Handler();
        var ctx = new RecordingContext();
        var first = await handler.HandleAsync(ctx.Build(Payload()));
        Assert.Equal(JobOutcome.Kind.Poll, first.Result);

        // a new process picks the job back up with the checkpoint the previous one saved
        var resumed = await handler.HandleAsync(ctx.Build(Payload(), ctx.Checkpoint, attempts: 2));

        Assert.Equal(JobOutcome.Kind.Complete, resumed.Result);
        Assert.Equal(1, backend.SubmitCalls);                          // NOT re-submitted — the money shot
        var delivery = Assert.Single(sink.Received);
        Assert.Equal("video", delivery.ProviderId);
        Assert.Equal("op-1", delivery.OperationId);
        Assert.Equal("video/mp4", delivery.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_still_running_operation_retries_and_reports_progress_rather_than_failing()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = GenerationOperationStatus.Running };
        var (handler, _, sink) = Handler(backend);
        var ctx = new RecordingContext();
        await handler.HandleAsync(ctx.Build(Payload()));

        var outcome = await handler.HandleAsync(ctx.Build(Payload(), ctx.Checkpoint, attempts: 2));

        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        Assert.Empty(sink.Received);
        Assert.Contains("running", ctx.Stages);
    }

    [Fact]
    public async Task A_backend_that_fails_the_operation_fails_the_job_with_its_reason()
    {
        var backend = new FakeGenerationJobProvider
        {
            Id = "video",
            PollStatus = GenerationOperationStatus.Failed,
            PollDetail = "content policy",
        };
        var (handler, _, sink) = Handler(backend);
        var ctx = new RecordingContext();
        await handler.HandleAsync(ctx.Build(Payload()));

        var outcome = await handler.HandleAsync(ctx.Build(Payload(), ctx.Checkpoint, attempts: 2));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("content policy", outcome.Error);
        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task A_submission_no_candidate_accepts_FAILS_rather_than_retrying_forever()
    {
        // no capable backend is a configuration problem: retrying cannot fix it, so don't burn the queue on it
        var image = new FakeGenerationProvider { Id = "image-only" };
        IGenerationProvider[] providers = [image];
        var handler = new GenerationRenderJobHandler(new GenerationRouter(providers), providers, new CollectingSink());

        var outcome = await handler.HandleAsync(new RecordingContext().Build(
            new GenerationRenderJob(["image-only"], new GenerationRequest { Kind = GenerationKinds.Video }).ToJson()));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("no capable", outcome.Error);
    }

    [Fact]
    public async Task A_lost_lease_stops_the_handler_instead_of_paying_twice()
    {
        // SaveCheckpointAsync returning false means another worker re-claimed the job — carrying on would run a
        // second paid render for the same job
        var (handler, backend, sink) = Handler();
        var ctx = new RecordingContext { LeaseHeld = false };

        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("lease", outcome.Error);
        Assert.Equal(1, backend.SubmitCalls);
        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task An_unreadable_payload_fails_immediately_and_says_why()
    {
        var (handler, _, _) = Handler();

        var outcome = await handler.HandleAsync(new RecordingContext().Build("{not json"));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("payload", outcome.Error);
    }

    [Fact]
    public void The_payload_round_trips_through_its_hand_written_json()
    {
        // Core is AOT-compatible, so payloads are hand-written JSON rather than reflection-serialized
        var job = new GenerationRenderJob(
            ["fal:wan-t2v", "comfyui"],
            new GenerationRequest
            {
                Kind = GenerationKinds.Video,
                Prompt = "a cat surfing",
                Model = "wan-t2v",
                TimeoutSeconds = 600,
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["duration"] = "5",
                    ["size"] = "1280x720",
                },
                Inputs = [new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.FirstFrame)],
            });

        var parsed = GenerationRenderJob.Parse(job.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(["fal:wan-t2v", "comfyui"], parsed.Candidates);
        Assert.Equal(GenerationKinds.Video, parsed.Request.Kind);
        Assert.Equal("a cat surfing", parsed.Request.Prompt);
        Assert.Equal("wan-t2v", parsed.Request.Model);
        Assert.Equal(600, parsed.Request.TimeoutSeconds);
        Assert.Equal("5", parsed.Request.Option("duration"));
        Assert.Equal("1280x720", parsed.Request.Option("SIZE"));            // options stay case-insensitive
        var input = Assert.Single(parsed.Request.Inputs);
        Assert.Equal("image/png", input.MediaType);
        Assert.Equal([1, 2, 3], input.Data);                                 // bytes survive the round trip
        Assert.Equal(GenerationInputRoles.FirstFrame, input.Role);
    }
}
