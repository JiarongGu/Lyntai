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

    /// <summary>What each successful render REPORTS costing — for the spend-governance tests.</summary>
    public double? CostUsd { get; set; }

    public int GenerateCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ProbeCalls++;
        return Task.FromResult(new GenerationProbeResult(ProbeAvailable,
            ProbeAvailable ? "fake ready" : "fake not configured"));
    }

    /// <summary>When set, <see cref="GenerateAsync"/> THROWS it instead of returning a verdict — a backend
    /// that violates the fail-safe contract on purpose. The router is the trust boundary, so a BYO backend's
    /// bug must be classified and fallen over, never propagated to the caller.</summary>
    public Exception? Throws { get; set; }

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        GenerateCalls++;
        if (Throws is not null) throw Throws;
        var verdict = Verdicts.Count > 1 ? Verdicts.Dequeue()
            : Verdicts.Count == 1 ? Verdicts.Peek()
            : GenerationVerdict.Ok;
        return Task.FromResult(verdict == GenerationVerdict.Ok
            ? GenerationResult.Success([new GenerationArtifact("image/png", Data: [0x89])],
                new GenerationUsage(Count: 1, CostUsd: CostUsd))
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

    /// <summary>What a submission reports — Queued by default, so a job test reaches polling in one hop.</summary>
    public GenerationOperationStatus SubmitStatus { get; set; } = GenerationOperationStatus.Queued;

    /// <summary>Whether a Failed submission is INCONCLUSIVE — the backend never answered, which the router
    /// surfaces instead of advancing (see <see cref="GenerationOperation.Inconclusive"/>).</summary>
    public bool SubmitInconclusive { get; set; }

    /// <summary>What the next poll reports — Succeeded by default, so a job test reaches delivery in one hop.</summary>
    public GenerationOperationStatus PollStatus { get; set; } = GenerationOperationStatus.Succeeded;

    /// <summary>Detail carried on the polled operation (a failure reason, a queue position).</summary>
    public string? PollDetail { get; set; }

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "fake video ready"));

    /// <summary>Inline is NOT this backend's mode; the base seam must still answer honestly.</summary>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "this backend generates via submit/poll"));

    /// <summary>When set, <see cref="SubmitAsync"/> THROWS it — a backend violating the fail-safe contract on
    /// the one path where a throw may or may not already have committed money.</summary>
    public Exception? SubmitThrows { get; set; }

    public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default)
    {
        if (SubmitThrows is not null) throw SubmitThrows;
        return Task.FromResult(new GenerationOperation($"op-{++_submits}", SubmitStatus) { Inconclusive = SubmitInconclusive });
    }

    public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation(operationId, PollStatus,
            Progress: PollStatus == GenerationOperationStatus.Succeeded ? 1 : 0.5, Detail: PollDetail));

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

/// <summary>A streaming backend whose emissions are SCRIPTED, so a test can stage the shapes a real one
/// produces and the shapes the router has to survive: a failure before any data, a failure after data, a
/// stream that simply stops, and a throw. <see cref="StreamCalls"/> is what proves a fallback did — or did
/// NOT — reach the next candidate.</summary>
public sealed class ScriptedStreamProvider : IGenerationProvider, IGenerationStreamProvider
{
    public string Id { get; init; } = "scripted";

    public int StreamCalls { get; private set; }

    /// <summary>Chunks to emit, in order. May legitimately end without a terminal chunk — the router is
    /// what guarantees the caller gets one.</summary>
    public IReadOnlyList<GenerationChunk> Script { get; init; } = [];

    /// <summary>Thrown from the enumerator AFTER <see cref="Script"/> is exhausted.</summary>
    public Exception? Throws { get; init; }

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Audio],
        Deliveries = [GenerationDelivery.Stream],
    };

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "scripted"));

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "streaming only"));

    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        GenerationRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamCalls++;
        foreach (var chunk in Script)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
        if (Throws is not null) throw Throws;
    }
}

/// <summary>Advertises <see cref="GenerationDelivery.Stream"/> and does NOT implement
/// <see cref="IGenerationStreamProvider"/> — the shape a BYO backend can ship, and the reason the router
/// re-checks a capability claim rather than casting on trust.</summary>
public sealed class LyingStreamProvider : IGenerationProvider
{
    public string Id { get; init; } = "liar";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Audio],
        Deliveries = [GenerationDelivery.Stream],
    };

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "liar"));

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "no"));
}
