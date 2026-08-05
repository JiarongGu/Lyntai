namespace Lyntai.Generation.Providers;

/// <summary>The BYO-or-ours disposal rule for one call's <see cref="HttpClient"/>, in one place.
///
/// <para>Every HTTP backend in this package takes a <c>Func&lt;HttpClient&gt;</c> plus a
/// <c>disposeHttpClient</c> flag (design §7): a client Lyntai's <c>Add*</c> shim creates per call is ours to
/// dispose, while a client the HOST supplied outlives the call and is the host's. Getting that backwards is not
/// a leak but an <see cref="ObjectDisposedException"/> on the SECOND render — the first one succeeds, which is
/// why the ternary deciding it is worth having in one place rather than at each of the eleven
/// calls.</para></summary>
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
