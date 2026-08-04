using System.Runtime.CompilerServices;
using Lyntai.Media;

namespace Lyntai.Tests.Fakes;

/// <summary>An INLINE media backend for exercising the platform without any real service. Scriptable per
/// call so a router test can drive a specific verdict sequence.</summary>
public sealed class FakeMediaProvider : IMediaProvider
{
    public string Id { get; init; } = "fake-media";

    public MediaCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [MediaKinds.Image],
        Deliveries = [MediaDelivery.Inline],
        SupportsInputs = true,
    };

    /// <summary>Verdicts to return, in order; the last one repeats. Ok produces a 1-byte PNG artifact.</summary>
    public Queue<MediaVerdict> Verdicts { get; } = new();

    public bool ProbeAvailable { get; set; } = true;
    public int GenerateCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    public Task<MediaProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ProbeCalls++;
        return Task.FromResult(new MediaProbeResult(ProbeAvailable,
            ProbeAvailable ? "fake ready" : "fake not configured"));
    }

    public Task<MediaResult> GenerateAsync(MediaRequest request, CancellationToken ct = default)
    {
        GenerateCalls++;
        var verdict = Verdicts.Count > 1 ? Verdicts.Dequeue()
            : Verdicts.Count == 1 ? Verdicts.Peek()
            : MediaVerdict.Ok;
        return Task.FromResult(verdict == MediaVerdict.Ok
            ? MediaResult.Success([new MediaArtifact("image/png", Data: [0x89])], new MediaUsage(Count: 1))
            : MediaResult.Failure(verdict, $"fake {verdict}"));
    }
}

/// <summary>An ASYNC-JOB backend: submit → queued, first poll → succeeded, fetch → an mp4.</summary>
public sealed class FakeMediaJobProvider : IMediaProvider, IMediaJobProvider
{
    public string Id { get; init; } = "fake-video";

    public MediaCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [MediaKinds.Video],
        Deliveries = [MediaDelivery.Job],
        SupportsInputs = true,
    };

    private int _submits;

    public int SubmitCalls => _submits;

    public Task<MediaProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new MediaProbeResult(true, "fake video ready"));

    /// <summary>Inline is NOT this backend's mode; the base seam must still answer honestly.</summary>
    public Task<MediaResult> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResult.Failure(MediaVerdict.Unsupported, "this backend generates via submit/poll"));

    public Task<MediaOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(new MediaOperation($"op-{++_submits}", MediaOperationStatus.Queued));

    public Task<MediaOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new MediaOperation(operationId, MediaOperationStatus.Succeeded, Progress: 1));

    public Task<MediaResult> FetchAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(MediaResult.Success(
            [new MediaArtifact("video/mp4", Uri: $"https://example.invalid/{operationId}.mp4")],
            new MediaUsage(Seconds: 5)));

    public Task<MediaOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new MediaOperation(operationId, MediaOperationStatus.Cancelled));
}

/// <summary>A STREAMING backend: two content chunks, then a terminal completion.</summary>
public sealed class FakeMediaStreamProvider : IMediaProvider, IMediaStreamProvider
{
    public string Id { get; init; } = "fake-tts";

    public MediaCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [MediaKinds.Audio],
        Deliveries = [MediaDelivery.Stream, MediaDelivery.Inline],
    };

    public Task<MediaProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new MediaProbeResult(true, "fake tts ready"));

    public Task<MediaResult> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResult.Success([new MediaArtifact("audio/mpeg", Data: [1, 2, 3, 4])]));

    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        MediaRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return MediaChunk.Content([1, 2], "audio/mpeg");
        await Task.Yield();
        yield return MediaChunk.Content([3, 4]);
        yield return MediaChunk.Completed(new MediaUsage(Seconds: 1.5));
    }
}
