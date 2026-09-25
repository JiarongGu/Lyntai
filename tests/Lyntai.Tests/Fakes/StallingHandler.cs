namespace Lyntai.Tests.Fakes;

/// <summary>A backend that accepts the connection and then never answers — the failure a deadline exists for.
/// It honours the token it is given, so only a clock can end the call.</summary>
public sealed class StallingHandler : HttpMessageHandler
{
    /// <summary>Every request that reached it, in order.</summary>
    public List<Uri?> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Seen) Seen.Add(request.RequestUri);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("unreachable");
    }
}
