using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>An inline image backend that blocks inside <see cref="GenerateAsync"/> until
/// <see cref="Release"/>, so a test can observe what queues at an admission gate and what does not.</summary>
public sealed class GatedGenerationProvider : IModelProvider
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _concurrent;

    public string Id { get; init; } = "a1111";

    /// <summary>Completes when the first call is inside.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when TWO calls are inside at once — what an admission limit of one makes impossible.</summary>
    public TaskCompletionSource BothInside { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many calls are inside right now.</summary>
    public int Concurrent => Volatile.Read(ref _concurrent);

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Image],
        Operations = [ProviderOperation.Complete],
    };

    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(true, "ready"));

    public async Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _concurrent) == 2) BothInside.TrySetResult();
        Entered.TrySetResult();
        await _gate.Task.ConfigureAwait(false);
        Interlocked.Decrement(ref _concurrent);
        return MediaResponse.Success([new MediaArtifact("image/png", Data: [0x89])]);
    }

    /// <summary>Lets every blocked call, and every later one, through.</summary>
    public void Release() => _gate.TrySetResult();
}
