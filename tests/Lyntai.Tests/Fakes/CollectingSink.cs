using Lyntai.Generation.Jobs;

namespace Lyntai.Tests.Fakes;

/// <summary>An <see cref="IGenerationArtifactSink"/> that keeps every delivery, and can be scripted to fail.</summary>
/// <param name="spent">Read at each delivery into <see cref="SpentAtDelivery"/> — how "billed before delivery"
/// is observed; null records nothing.</param>
public sealed class CollectingSink(Func<double>? spent = null) : IGenerationArtifactSink
{
    public List<GenerationArtifactDelivery> Received { get; } = [];

    /// <summary>What the ledger held when each delivery ARRIVED.</summary>
    public List<double> SpentAtDelivery { get; } = [];

    /// <summary>The 1-based calls that throw <see cref="Down"/> instead of storing — a store that is
    /// momentarily down.</summary>
    public HashSet<int> ThrowOn { get; init; } = [];

    public int Calls { get; private set; }

    public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
    {
        if (ThrowOn.Contains(++Calls)) throw new Down();
        Received.Add(delivery);
        if (spent is not null) SpentAtDelivery.Add(spent());
        return Task.CompletedTask;
    }

    /// <summary>What a scripted failure throws.</summary>
    public sealed class Down : Exception;
}
