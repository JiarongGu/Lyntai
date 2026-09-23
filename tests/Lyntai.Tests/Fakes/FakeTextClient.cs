using Lyntai.Inference;
using System.Runtime.CompilerServices;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted <see cref="ITextClient"/> for cortex tests that don't need the real router:
/// queue replies for CompleteAsync; records every request.</summary>
public sealed class FakeTextClient : ITextClient
{
    public Queue<TextResponse> Replies { get; } = new();
    public List<TextRequest> Calls { get; } = [];

    /// <summary>What <see cref="GetCapabilitiesAsync"/> answers; null (the default) is "unknown".</summary>
    public ProviderCapabilities? Capabilities { get; set; }

    /// <summary>How many times the capability probe was asked.</summary>
    public int CapabilityProbes { get; private set; }

    /// <summary>Writes through to <see cref="Capabilities"/>' <see cref="ProviderCapabilities.SupportsToolCalls"/>.</summary>
    public bool SupportsToolCallsResult
    {
        get => Capabilities?.SupportsToolCalls == true;
        set => Capabilities = (Capabilities ?? new()) with { SupportsToolCalls = value };
    }

    /// <summary>Writes through to <see cref="Capabilities"/>' <see cref="ProviderCapabilities.SupportsStreamingToolCalls"/>.
    /// SEPARATE from <see cref="SupportsToolCallsResult"/> on purpose — the two are independent in the contract,
    /// and a fake that conflated them could not express the case that matters most: a provider doing native
    /// tool-calling whose STREAM drops the calls.</summary>
    public bool SupportsStreamingToolCallsResult
    {
        get => Capabilities?.SupportsStreamingToolCalls == true;
        set => Capabilities = (Capabilities ?? new()) with { SupportsStreamingToolCalls = value };
    }

    public ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default)
    {
        CapabilityProbes++;
        return ValueTask.FromResult(Capabilities);
    }

    public Func<TextRequest, IReadOnlyList<TextChunk>>? StreamScript { get; set; }

    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        Calls.Add(req);
        return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : new TextResponse("fake", ProviderVerdict.Ok));
    }

    public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(req);
        var chunks = StreamScript?.Invoke(req) ?? [TextChunk.Content("fake stream"), TextChunk.Final()];
        foreach (var c in chunks) { await Task.Yield(); yield return c; }
    }
}
