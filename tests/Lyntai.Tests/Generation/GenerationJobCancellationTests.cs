using Lyntai.Generation.Jobs;
using Lyntai.Inference;
using Lyntai.Jobs;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>A durable render is cancelled at its backend by the HOST's call, never by the engine: a cancel flips the job
/// between polls, when no handler runs, and a handler's token cannot tell a cancel from a shutdown, which must leave a
/// resumable render alone. <see cref="GenerationJobCancellation"/> is that call — the job first, then the operation its
/// checkpoint holds.</summary>
public class GenerationJobCancellationTests
{
    private const string Lane = "render";

    private static string Payload() =>
        new GenerationRenderJob(["video"], new MediaRequest { Kind = ProviderKinds.Video, Prompt = "a cat" }).ToJson();

    /// <summary>A job submitted by its handler and waiting for its next poll, as the runner leaves it.</summary>
    private static async Task<Guid> PollingJobAsync(InMemoryJobStore store, IModelProvider[] providers)
    {
        var id = await store.EnqueueAsync(new JobSpec(Lane, GenerationRenderJobHandler.JobType, Payload()));
        var claimed = await store.ClaimNextAsync(Lane, "worker", TimeSpan.FromMinutes(5));
        var handler = new GenerationRenderJobHandler(new MediaRouter(providers), providers, new CollectingSink());
        var outcome = await handler.HandleAsync(new JobContext(id, claimed!.Payload, null, attempts: 1,
            saveCheckpoint: (c, ct) => store.SaveCheckpointAsync(id, "worker", c, ct),
            reportProgress: (_, _, _, _) => Task.FromResult(true)));
        Assert.Equal(JobOutcome.Kind.Poll, outcome.Result);
        Assert.True(await store.PollAgainAsync(id, "worker", DateTimeOffset.UtcNow.AddSeconds(15)));
        return id;
    }

    [Fact]
    public async Task A_polling_job_is_cancelled_then_its_backend_asked()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = QueuedOperationStatus.Running };
        IModelProvider[] providers = [backend];
        var store = new InMemoryJobStore();
        var queue = new JobQueue(store, new LyntaiOptions());
        var id = await PollingJobAsync(store, providers);
        JobStatus? whenAsked = null;
        backend.OnCancel = () => whenAsked = store.GetAsync(id).GetAwaiter().GetResult()!.Status;

        var answer = await GenerationJobCancellation.CancelAsync(queue, providers, id);

        Assert.Equal(JobStatus.Cancelled, whenAsked);   // the job first: no worker polls it again meanwhile
        Assert.Equal(["op-1"], backend.Cancelled);
        Assert.NotNull(answer);
        Assert.Equal(QueuedOperationStatus.Cancelled, answer.Status);   // the backend's own answer
    }

    [Fact]
    public async Task A_job_with_no_operation_asks_nobody()
    {
        var backend = new FakeGenerationJobProvider { Id = "video" };
        IModelProvider[] providers = [backend];
        var store = new InMemoryJobStore();
        var queue = new JobQueue(store, new LyntaiOptions());
        var never = await store.EnqueueAsync(new JobSpec(Lane, GenerationRenderJobHandler.JobType, Payload()));
        // another job type whose checkpoint happens to carry the same fields is not a render's
        var other = await store.EnqueueAsync(new JobSpec("export", "app.export", "{}"));
        Assert.Equal(other, (await store.ClaimNextAsync("export", "worker", TimeSpan.FromMinutes(5)))!.Id);
        Assert.True(await store.SaveCheckpointAsync(other, "worker", """{"stage":0,"providerId":"video","operationId":"op-9"}"""));

        Assert.Null(await GenerationJobCancellation.CancelAsync(queue, providers, never));
        Assert.Null(await GenerationJobCancellation.CancelAsync(queue, providers, other));
        Assert.Null(await GenerationJobCancellation.CancelAsync(queue, providers, Guid.NewGuid()));   // no such job

        Assert.Empty(backend.Cancelled);
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(never))!.Status);
    }

    [Fact]
    public async Task A_held_operation_whose_backend_is_gone_says_so_rather_than_answering_nothing()
    {
        var backend = new FakeGenerationJobProvider { Id = "video", PollStatus = QueuedOperationStatus.Running };
        var store = new InMemoryJobStore();
        var queue = new JobQueue(store, new LyntaiOptions());
        var id = await PollingJobAsync(store, [backend]);

        var answer = await GenerationJobCancellation.CancelAsync(queue, [], id);

        Assert.NotNull(answer);
        Assert.NotEqual(QueuedOperationStatus.Cancelled, answer.Status);
        Assert.Contains("op-1", answer.Detail);
        Assert.Contains("video", answer.Detail);
    }
}
