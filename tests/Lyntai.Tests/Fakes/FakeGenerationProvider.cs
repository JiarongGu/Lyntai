using Lyntai.Inference;
using System.Runtime.CompilerServices;
using Lyntai.Generation;

namespace Lyntai.Tests.Fakes;

/// <summary>An INLINE media backend for exercising the platform without any real service. Scriptable per
/// call so a router test can drive a specific verdict sequence.</summary>
public sealed class FakeGenerationProvider : IModelProvider
{
    public string Id { get; init; } = "fake-generation";

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
        SupportsInputs = true,
    };

    /// <summary>Verdicts to return, in order; the last one repeats. Ok produces a 1-byte PNG artifact.</summary>
    public Queue<ProviderVerdict> Verdicts { get; } = new();

    public bool ProbeAvailable { get; set; } = true;

    /// <summary>What each successful render REPORTS costing — for the spend-governance tests.</summary>
    public double? CostUsd { get; set; }

    public int GenerateCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ProbeCalls++;
        return Task.FromResult(new ProviderProbeResult(ProbeAvailable,
            ProbeAvailable ? "fake ready" : "fake not configured"));
    }

    /// <summary>When set, <see cref="GenerateAsync"/> THROWS it instead of returning a verdict — a backend
    /// that violates the fail-safe contract on purpose. The router is the trust boundary, so a BYO backend's
    /// bug must be classified and fallen over, never propagated to the caller.</summary>
    public Exception? Throws { get; set; }

    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
    {
        GenerateCalls++;
        if (Throws is not null) throw Throws;
        var verdict = Verdicts.Count > 1 ? Verdicts.Dequeue()
            : Verdicts.Count == 1 ? Verdicts.Peek()
            : ProviderVerdict.Ok;
        return Task.FromResult(verdict == ProviderVerdict.Ok
            ? MediaResponse.Success([new MediaArtifact("image/png", Data: [0x89])],
                new MediaUsage(Count: 1, CostUsd: CostUsd))
            : MediaResponse.Failure(verdict, $"fake {verdict}"));
    }
}

/// <summary>An ASYNC-JOB backend: submit → queued, first poll → succeeded, fetch → an mp4.</summary>
public sealed class FakeGenerationJobProvider : IModelProvider, IGenerationJobProvider
{
    public string Id { get; init; } = "fake-video";

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Video],
        Operations = [ProviderOperation.Queued],
        SupportsInputs = true,
    };

    private int _submits;

    public int SubmitCalls => _submits;

    /// <summary>What a submission reports — Queued by default, so a job test reaches polling in one hop.</summary>
    public QueuedOperationStatus SubmitStatus { get; set; } = QueuedOperationStatus.Queued;

    /// <summary>Whether a Failed submission is INCONCLUSIVE — the backend never answered, which the router
    /// surfaces instead of advancing (see <see cref="QueuedOperation.Inconclusive"/>).</summary>
    public bool SubmitInconclusive { get; set; }

    /// <summary>What the next poll reports — Succeeded by default, so a job test reaches delivery in one hop.</summary>
    public QueuedOperationStatus PollStatus { get; set; } = QueuedOperationStatus.Succeeded;

    /// <summary>Detail carried on the polled operation (a failure reason, a queue position).</summary>
    public string? PollDetail { get; set; }

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(true, "fake video ready"));

    /// <summary>Inline is NOT this backend's mode; the base seam must still answer honestly.</summary>
    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported, "this backend generates via submit/poll"));

    /// <summary>When set, <see cref="SubmitAsync"/> THROWS it — a backend violating the fail-safe contract on
    /// the one path where a throw may or may not already have committed money.</summary>
    public Exception? SubmitThrows { get; set; }

    public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default)
    {
        if (SubmitThrows is not null) throw SubmitThrows;
        return Task.FromResult(new QueuedOperation($"op-{++_submits}", SubmitStatus) { Inconclusive = SubmitInconclusive });
    }

    public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new QueuedOperation(operationId, PollStatus,
            Progress: PollStatus == QueuedOperationStatus.Succeeded ? 1 : 0.5, Detail: PollDetail));

    /// <summary>What the completed render COST, reported by the fetch. Null keeps the pre-existing usage
    /// (seconds only, no money), so every test written before this knob is byte-identical — a real queue
    /// backend prices at fetch, which is the only point the total is known.</summary>
    public double? FetchCostUsd { get; set; }

    public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Success(
            [new MediaArtifact("video/mp4", Uri: $"https://example.invalid/{operationId}.mp4")],
            new MediaUsage(Seconds: 5, CostUsd: FetchCostUsd)));

    public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Cancelled));
}

/// <summary>A STREAMING backend: two content chunks, then a terminal completion.</summary>
public sealed class FakeGenerationStreamProvider : IModelProvider
{
    public string Id { get; init; } = "fake-tts";

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Audio],
        Operations = [ProviderOperation.Stream, ProviderOperation.Complete],
    };

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(true, "fake tts ready"));

    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Success([new MediaArtifact("audio/mpeg", Data: [1, 2, 3, 4])]));

    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        MediaRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return MediaChunk.Content([1, 2], "audio/mpeg");
        await Task.Yield();
        yield return MediaChunk.Content([3, 4]);
        yield return MediaChunk.Completed(new MediaUsage(Seconds: 1.5));
    }
}

/// <summary>A streaming backend whose emissions are SCRIPTED, so a test can stage the shapes a real one
/// produces and the shapes the router has to survive: a failure before any data, a failure after data, a
/// stream that simply stops, and a throw. <see cref="StreamCalls"/> is what proves a fallback did — or did
/// NOT — reach the next candidate.</summary>
public sealed class ScriptedStreamProvider : IModelProvider
{
    public string Id { get; init; } = "scripted";

    public int StreamCalls { get; private set; }

    /// <summary>Chunks to emit, in order. May legitimately end without a terminal chunk — the router is
    /// what guarantees the caller gets one.</summary>
    public IReadOnlyList<MediaChunk> Script { get; init; } = [];

    /// <summary>Thrown from the enumerator AFTER <see cref="Script"/> is exhausted.</summary>
    public Exception? Throws { get; init; }

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Audio],
        Operations = [ProviderOperation.Stream],
    };

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(true, "scripted"));

    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported, "streaming only"));

    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        MediaRequest request, [EnumeratorCancellation] CancellationToken ct = default)
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

/// <summary>A backend whose PROBE misbehaves — it stalls until its token fires, or throws. Both are the
/// shapes <c>generate_backends</c> has to survive: a probe is capped only by the backend's own
/// <c>Timeout</c>, which is a RENDER budget (ten minutes on two shipped backends), and a BYO backend may
/// throw where the seam says to return a result.</summary>
public sealed class BadProbeProvider : IModelProvider
{
    public string Id { get; init; } = "bad-probe";

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
    };

    /// <summary>Thrown from <see cref="ProbeAsync"/> instead of stalling, when set.</summary>
    public Exception? Throws { get; init; }

    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (Throws is not null) throw Throws;
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);   // stalls until the listing's deadline
        return new ProviderProbeResult(true, "unreachable");
    }

    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Failed, "not used"));
}

/// <summary>Advertises <see cref="ProviderOperation.Stream"/> and never overrides
/// <c>StreamAsync(MediaRequest, …)</c>, so every call answers
/// <see cref="ProviderVerdict.Unsupported"/> from <see cref="IModelProvider"/>'s default member — the shape
/// a BYO backend can ship, and the reason a declared delivery is checked against the code behind it.
/// <para>It was built for a router branch that type-tested a separate streaming interface. <b>D127</b>
/// deleted that interface, which made the branch unreachable and left this fake used by NOTHING for a
/// release — the build and the suite stayed green throughout. It is now the negative fixture for
/// <c>GenerationProviderContract.ServesMediaStream</c>.</para></summary>
public sealed class LyingStreamProvider : IModelProvider
{
    public string Id { get; init; } = "liar";

    public ProviderCapabilities Capabilities { get; init; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Audio],
        Operations = [ProviderOperation.Stream],
    };

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(true, "liar"));

    public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(MediaResponse.Failure(ProviderVerdict.Unsupported, "no"));
}
