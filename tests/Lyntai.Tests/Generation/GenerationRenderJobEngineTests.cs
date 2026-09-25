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

    [Fact]
    public async Task A_job_checkpointed_as_providerId_and_operationId_resumes_polling_that_operation()
    {
        // the shape the render handler checkpointed before it ran on the pipeline's machine
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
        // GetString() on an unchecked element threw InvalidOperationException past the JsonException catch
        var backend = new FakeGenerationJobProvider { Id = "video" };
        IModelProvider[] providers = [backend];
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new Sink());
        var ctx = new Context { Checkpoint = """{"providerId":7,"operationId":"op-7"}""" };

        var outcome = await handler.HandleAsync(ctx.Build(Payload()));

        Assert.Equal(JobOutcome.Kind.Fail, outcome.Result);
        Assert.Contains("checkpoint", outcome.Error);
        Assert.Equal(0, backend.SubmitCalls);                         // never "starts over" and pays again
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
