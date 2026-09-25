using Lyntai.Inference;

namespace Lyntai.Tests.Generation;

/// <summary>What a submission no backend accepted SAYS. The job handler fails the job with it and the submit
/// tool hands it to a model, so "every backend is on cooldown" must be said only when nothing was attempted —
/// never beside a backend that was asked and refused.</summary>
public class MediaSubmitNothingServedTests
{
    private static readonly MediaRequest Video = new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };

    private static (MediaRouter Router, DeadHostTracker Tracker) Routed(params IModelProvider[] providers)
    {
        var tracker = new DeadHostTracker(threshold: 3, cooldown: TimeSpan.FromMinutes(5));
        return (new MediaRouter(providers, deadHosts: tracker), tracker);
    }

    [Fact]
    public async Task A_benched_backend_beside_one_that_refused_reports_the_refusal_not_a_cooldown()
    {
        var (router, tracker) = Routed(new Rejecting("a"), new Rejecting("b") { Detail = "quota" });
        tracker.MarkDead("generation::a");

        var submission = await router.SubmitAsync([new("a"), new("b")], Video);

        var detail = submission.Operation.Detail!;
        Assert.DoesNotContain("dead-host cooldown", detail, StringComparison.Ordinal);
        Assert.Contains("'b' said: quota", detail, StringComparison.Ordinal);
        Assert.Equal(ProviderVerdict.Failed, submission.Operation.Verdict);
    }

    [Fact]
    public async Task A_benched_backend_beside_a_silent_blameless_one_is_NotConfigured_without_a_cooldown_sentence()
    {
        var (router, tracker) = Routed(new Rejecting("a"), new Rejecting("b") { Verdict = ProviderVerdict.NotConfigured });
        tracker.MarkDead("generation::a");

        var submission = await router.SubmitAsync([new("a"), new("b")], Video);

        Assert.Equal(ProviderVerdict.NotConfigured, submission.Operation.Verdict);
        Assert.DoesNotContain("dead-host cooldown", submission.Operation.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_capable_backend_benched_says_so()
    {
        var (router, tracker) = Routed(new Rejecting("a"), new Rejecting("b"));
        tracker.MarkDead("generation::a");
        tracker.MarkDead("generation::b");

        var submission = await router.SubmitAsync([new("a"), new("b")], Video);

        Assert.Equal(ProviderVerdict.RateLimited, submission.Operation.Verdict);
        Assert.Contains("dead-host cooldown", submission.Operation.Detail!, StringComparison.Ordinal);
        Assert.Equal("", submission.ProviderId);
    }

    private sealed class Rejecting(string id) : IModelProvider, IMediaJobProvider
    {
        public string Id => id;

        public string? Detail { get; init; }

        public ProviderVerdict? Verdict { get; init; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Video],
            Operations = [ProviderOperation.Queued],
        };

        public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation("", QueuedOperationStatus.Failed, Detail: Detail) { Verdict = Verdict });

        public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Failed));

        public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(MediaResponse.Failure(ProviderVerdict.Failed, "nothing"));

        public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Cancelled));
    }
}
