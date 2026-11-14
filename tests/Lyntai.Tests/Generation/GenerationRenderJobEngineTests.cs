using Lyntai.Generation.Jobs;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Jobs;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The render job runs on the pipeline job's machine, so it gets the pipeline's guarantees — above all
/// that a fetched result is billed and checkpointed BEFORE delivery, so a sink that throws never costs a second
/// fetch or a second bill — while a job checkpointed in the render handler's own older shape still resumes.</summary>
public class GenerationRenderJobEngineTests
{
    private sealed class Sink : IGenerationArtifactSink
    {
        public List<GenerationArtifactDelivery> Received { get; } = [];
        public int FailFirst { get; set; }

        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
        {
            if (FailFirst-- > 0) throw new IOException("store down");
            Received.Add(delivery);
            return Task.CompletedTask;
        }
    }

    private sealed class Context
    {
        public string? Checkpoint { get; set; }

        public JobContext Build(string payload) => new(Guid.NewGuid(), payload, Checkpoint, attempts: 1,
            saveCheckpoint: (c, _) => { Checkpoint = c; return Task.FromResult(true); },
            reportProgress: (_, _, _, _) => Task.FromResult(true));
    }

    private static string Payload() =>
        new GenerationRenderJob(["video"], new MediaRequest { Kind = ProviderKinds.Video, Prompt = "a cat" }).ToJson();

    [Fact]
    public async Task A_throwing_sink_gets_the_render_again_with_no_second_fetch_or_bill()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        IModelProvider[] providers = [backend];
        var usage = new InMemoryUsageTracker();
        var sink = new Sink { FailFirst = 1 };
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, sink, usage: usage);
        var ctx = new Context();

        Assert.Equal(JobOutcome.Kind.Poll, (await handler.HandleAsync(ctx.Build(Payload()))).Result);
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(ctx.Build(Payload())));   // the runner retries
        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(0.50, (await usage.TotalAsync()).CostUsd, precision: 6);   // billed once, not per delivery
        var delivery = Assert.Single(sink.Received);
        Assert.Null(delivery.StageIndex);                                       // a render job, not a pipeline stage
        Assert.True(delivery.IsFinal);
        Assert.Equal("op-1", delivery.OperationId);
    }

    /// <summary>A fetch that timed out or was rate limited is a retryable read of an operation the checkpoint already holds,
    /// so the job RETRIES (bounded by its attempts, then Dead, replayable) and the next run fetches it, billing and
    /// delivering once; every other failure still fails (the steward's ruling, <c>docs/DECISIONS.md</c> D333).</summary>
    [Theory]
    [InlineData(ProviderVerdict.Timeout)]
    [InlineData(ProviderVerdict.RateLimited)]
    public async Task A_transient_fetch_failure_retries_and_the_next_run_fetches_the_same_operation(ProviderVerdict verdict)
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50, FetchVerdict = verdict };
        IModelProvider[] providers = [backend];
        var usage = new InMemoryUsageTracker();
        var sink = new Sink();
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, sink, usage: usage);
        var ctx = new Context();

        Assert.Equal(JobOutcome.Kind.Poll, (await handler.HandleAsync(ctx.Build(Payload()))).Result);
        var retried = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Retry, retried.Result);
        Assert.Empty(sink.Received);
        Assert.Equal(0, (await usage.TotalAsync()).CostUsd);

        backend.FetchVerdict = null;
        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(1, backend.SubmitCalls);                                    // the operation was fetched, never re-submitted
        Assert.Equal(0.50, (await usage.TotalAsync()).CostUsd, precision: 6);
        Assert.Equal("op-1", Assert.Single(sink.Received).OperationId);
    }

    [Theory]
    [InlineData(ProviderVerdict.Failed)]
    [InlineData(ProviderVerdict.Unsupported)]
    [InlineData(ProviderVerdict.AuthFailed)]
    public async Task Any_other_fetch_failure_still_fails_the_job(ProviderVerdict verdict)
    {
        var backend = new FakeGenerationJobProvider { Id = "video", FetchVerdict = verdict };
        IModelProvider[] providers = [backend];
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new Sink());
        var ctx = new Context();

        await handler.HandleAsync(ctx.Build(Payload()));
        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("could not be fetched", outcome.Error);
    }

    [Fact]
    public async Task A_job_checkpointed_as_providerId_and_operationId_resumes_polling_that_operation()
    {
        // the render handler's own older checkpoint shape
        var backend = new FakeGenerationJobProvider { Id = "video" };
        IModelProvider[] providers = [backend];
        var sink = new Sink();
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, sink);
        var ctx = new Context { Checkpoint = """{"providerId":"video","operationId":"op-7"}""" };

        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Complete, outcome.Result);
        Assert.Equal(0, backend.SubmitCalls);                         // polled and fetched, never re-submitted
        Assert.Equal("op-7", Assert.Single(sink.Received).OperationId);
    }

    [Fact]
    public async Task A_checkpoint_whose_fields_are_not_strings_fails_as_unreadable_rather_than_throwing()
    {
        // GetString() on an unchecked element throws InvalidOperationException past the JsonException catch
        var backend = new FakeGenerationJobProvider { Id = "video" };
        IModelProvider[] providers = [backend];
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new Sink());
        var ctx = new Context { Checkpoint = """{"providerId":7,"operationId":"op-7"}""" };

        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("checkpoint", outcome.Error);
        Assert.Equal(0, backend.SubmitCalls);                         // never "starts over" and pays again
    }

    // ---- a deadline on polling: the backstop for a backend that never says a run is gone ---------------------

    private static GenerationJobEngine Engine(FakeGenerationJobProvider backend, GenerationPipelineJobOptions? options,
        TimeProvider time)
    {
        IModelProvider[] providers = [backend];
        return new GenerationJobEngine(new MediaRouter(providers), providers, new Sink(), options, usage: null,
            policy: null, time);
    }

    private static Task<JobOutcome> Run(GenerationJobEngine engine, Context ctx)
    {
        var render = GenerationRenderJob.Parse(Payload())!;
        var job = new GenerationPipelineJob([new GenerationPipelineJobStage(render.Candidates, render.Request)]);
        var built = ctx.Build(Payload());
        return engine.RunAsync(built, job, built.Checkpoint, GenerationRenderJobHandler.JobType, GenerationJobEngine.Render,
            CancellationToken.None);
    }

    [Fact]
    public async Task A_poll_past_MaxPollDuration_fails_naming_the_operation()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = QueuedOperationStatus.Running };
        var time = new ManualTimeProvider();
        var engine = Engine(backend, new GenerationPipelineJobOptions { MaxPollDuration = TimeSpan.FromHours(1) }, time);
        var ctx = new Context();

        Assert.Equal(JobOutcome.Kind.Poll, (await Run(engine, ctx)).Result);   // submitted
        time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(JobOutcome.Kind.Poll, (await Run(engine, ctx)).Result);   // still inside the hour
        time.Advance(TimeSpan.FromMinutes(2));
        var outcome = await Run(engine, ctx);

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("op-1", outcome.Error);
        Assert.Contains(nameof(GenerationPipelineJobOptions.MaxPollDuration), outcome.Error);
        Assert.Empty(backend.Cancelled);   // the backend is never cancelled for the host (its own call)
    }

    [Fact]
    public async Task No_deadline_by_default()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = QueuedOperationStatus.Running };
        var time = new ManualTimeProvider();
        var engine = Engine(backend, options: null, time);
        var ctx = new Context();

        await Run(engine, ctx);
        time.Advance(TimeSpan.FromDays(365));

        Assert.Equal(JobOutcome.Kind.Poll, (await Run(engine, ctx)).Result);
    }

    [Fact]
    public async Task An_older_checkpoint_without_submittedAt_is_unbounded()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = QueuedOperationStatus.Running };
        var options = new GenerationPipelineJobOptions { MaxPollDuration = TimeSpan.FromMinutes(1) };
        var time = new ManualTimeProvider();
        var engine = Engine(backend, options, time);
        var ctx = new Context { Checkpoint = """{"stage":0,"providerId":"video","operationId":"op-7"}""" };
        time.Advance(TimeSpan.FromDays(2));

        Assert.Equal(JobOutcome.Kind.Poll, (await Run(engine, ctx)).Result);
        Assert.Equal(JobOutcome.Kind.Poll, (await Run(engine, ctx)).Result);   // and re-saving it adds no deadline

        // the render handler's own oldest shape too
        IModelProvider[] providers = [backend];
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new Sink(), options);
        var legacy = new Context { Checkpoint = """{"providerId":"video","operationId":"op-7"}""" };
        Assert.Equal(JobOutcome.Kind.Poll, (await handler.HandleAsync(legacy.Build(Payload()))).Result);
    }

    // ---- one bill per operation, whichever worker fetches it ------------------------------------------------

    [Fact]
    public async Task A_reclaimed_job_does_not_bill_the_operation_twice()
    {
        // worker A fetches and bills, then its lease is gone before its checkpoint lands; worker B resumes from the
        // checkpoint the store kept (the operation), and fetches and bills the same render again
        var backend = new FakeGenerationJobProvider { Id = "video", FetchCostUsd = 0.50 };
        IModelProvider[] providers = [backend];
        var usage = new InMemoryUsageTracker();
        var sink = new Sink();
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, sink, usage: usage);
        string? stored = null;
        var refuse = false;
        JobContext Worker() => new(Guid.NewGuid(), Payload(), stored, attempts: 1,
            saveCheckpoint: (c, _) =>
            {
                if (refuse && c.Contains("pending", StringComparison.Ordinal)) return Task.FromResult(false);
                stored = c;
                return Task.FromResult(true);
            },
            reportProgress: (_, _, _, _) => Task.FromResult(true));

        Assert.Equal(JobOutcome.Kind.Poll, (await handler.HandleAsync(Worker())).Result);
        refuse = true;
        var a = await handler.HandleAsync(Worker());
        Assert.Equal(JobOutcome.Kind.Fail, a.Result);
        Assert.Contains("lease lost", a.Error);
        refuse = false;
        var b = await handler.HandleAsync(Worker());

        Assert.Equal(JobOutcome.Kind.Complete, b.Result);
        Assert.Equal(1, backend.SubmitCalls);
        Assert.Equal(0.50, (await usage.TotalAsync()).CostUsd, precision: 6);   // fetched twice, billed once
        Assert.Equal(1, (await usage.TotalAsync()).Calls);
        Assert.Single(sink.Received);
    }

    [Fact]
    public async Task A_render_job_never_renders_inline_even_when_an_inline_backend_leads()
    {
        var inline = new FakeGenerationProvider
        {
            Id = "image",
            Capabilities = new()
            {
                Accepts = [ProviderKinds.Text],
                Produces = [ProviderKinds.Video],
                Operations = [ProviderOperation.Complete],
            },
        };
        var queued = new FakeGenerationJobProvider { Id = "video" };
        IModelProvider[] providers = [inline, queued];
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new Sink());
        var payload = new GenerationRenderJob(["image", "video"],
            new MediaRequest { Kind = ProviderKinds.Video, Prompt = "a cat" }).ToJson();

        var outcome = await handler.HandleAsync(new Context().Build(payload));

        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        Assert.Equal(0, inline.GenerateCalls);
        Assert.Equal(1, queued.SubmitCalls);
    }
}
