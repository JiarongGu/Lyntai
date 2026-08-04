using System.Runtime.CompilerServices;
using Lyntai.Generation;

namespace Lyntai.Tests.Fakes;

/// <summary>An INLINE media backend for exercising the platform without any real service. Scriptable per
/// call so a router test can drive a specific verdict sequence.</summary>
public sealed class FakeGenerationProvider : IGenerationProvider
{
    public string Id { get; init; } = "fake-generation";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,
    };

    /// <summary>Verdicts to return, in order; the last one repeats. Ok produces a 1-byte PNG artifact.</summary>
    public Queue<GenerationVerdict> Verdicts { get; } = new();

    public bool ProbeAvailable { get; set; } = true;
    public int GenerateCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ProbeCalls++;
        return Task.FromResult(new GenerationProbeResult(ProbeAvailable,
            ProbeAvailable ? "fake ready" : "fake not configured"));
    }

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        GenerateCalls++;
        var verdict = Verdicts.Count > 1 ? Verdicts.Dequeue()
            : Verdicts.Count == 1 ? Verdicts.Peek()
            : GenerationVerdict.Ok;
        return Task.FromResult(verdict == GenerationVerdict.Ok
            ? GenerationResult.Success([new GenerationArtifact("image/png", Data: [0x89])], new GenerationUsage(Count: 1))
            : GenerationResult.Failure(verdict, $"fake {verdict}"));
    }
}

/// <summary>An ASYNC-JOB backend: submit → queued, first poll → succeeded, fetch → an mp4.</summary>
public sealed class FakeGenerationJobProvider : IGenerationProvider, IGenerationJobProvider
{
    public string Id { get; init; } = "fake-video";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Video],
        Deliveries = [GenerationDelivery.Job],
        SupportsInputs = true,
    };

    private int _submits;

    public int SubmitCalls => _submits;

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "fake video ready"));

    /// <summary>Inline is NOT this backend's mode; the base seam must still answer honestly.</summary>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "this backend generates via submit/poll"));

    public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation($"op-{++_submits}", GenerationOperationStatus.Queued));

    public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Succeeded, Progress: 1));

    public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Success(
            [new GenerationArtifact("video/mp4", Uri: $"https://example.invalid/{operationId}.mp4")],
            new GenerationUsage(Seconds: 5)));

    public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Cancelled));
}

/// <summary>A STREAMING backend: two content chunks, then a terminal completion.</summary>
public sealed class FakeGenerationStreamProvider : IGenerationProvider, IGenerationStreamProvider
{
    public string Id { get; init; } = "fake-tts";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Audio],
        Deliveries = [GenerationDelivery.Stream, GenerationDelivery.Inline],
    };

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "fake tts ready"));

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Success([new GenerationArtifact("audio/mpeg", Data: [1, 2, 3, 4])]));

    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        GenerationRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return GenerationChunk.Content([1, 2], "audio/mpeg");
        await Task.Yield();
        yield return GenerationChunk.Content([3, 4]);
        yield return GenerationChunk.Completed(new GenerationUsage(Seconds: 1.5));
    }
}
