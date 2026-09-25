namespace Lyntai.Generation.Providers;

/// <summary>One call's <see cref="HttpClient"/>, disposed only when Lyntai created it — the rule each backend's
/// <c>disposeHttpClient</c> parameter documents.</summary>
/// <param name="client">The client this call runs on.</param>
/// <param name="owned">Whether disposing the lease disposes <paramref name="client"/>.</param>
internal readonly struct HttpClientLease(HttpClient client, bool owned) : IDisposable
{
    /// <summary>The client to send on. Valid until the lease is disposed.</summary>
    public HttpClient Client { get; } = client;

    /// <summary>Take a client for the duration of one call.</summary>
    /// <param name="httpFactory">The backend's own factory.</param>
    /// <param name="disposeHttpClient">The backend's own flag.</param>
    public static HttpClientLease From(Func<HttpClient> httpFactory, bool disposeHttpClient) =>
        new(httpFactory(), disposeHttpClient);

    /// <summary>Disposes the client only when it was ours to begin with.</summary>
    public void Dispose()
    {
        if (owned) Client.Dispose();
    }
}
