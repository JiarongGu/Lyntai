namespace Lyntai.Tests.Fakes;

/// <summary>An embedding backend whose endpoint is down: every embed throws, which the router classifies
/// exactly as it would an escaping exception from a real backend.</summary>
public sealed class ThrowingVectorProvider : FakeVectorProviderBase
{
    public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("embedding endpoint is down");
}
