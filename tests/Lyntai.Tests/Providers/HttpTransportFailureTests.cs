using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>ONE policy for a transport throw (socket reset, DNS, TLS) on every HTTP surface: a
/// <see cref="ProviderVerdict.Failed"/> verdict carrying the message. The chat path always answered so; the
/// vector and rerank transports let the exception escape the provider, so a direct caller got a verdict from
/// one kind and an exception from the next.</summary>
public class HttpTransportFailureTests
{
    private static HttpModelProvider Provider(string produces) =>
        new("h", new HttpModelOptions { BaseUrl = "http://localhost:8080", Produces = produces },
            () => new HttpClient(new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("connection reset")),
                disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });

    [Fact]
    public async Task A_transport_throw_is_a_Failed_verdict_on_the_vector_path()
    {
        var response = await Provider(ProviderKinds.Vector).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("connection reset", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_throw_is_a_Failed_verdict_on_the_rerank_path()
    {
        var response = await Provider(ProviderKinds.Score).CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("connection reset", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_throw_is_a_Failed_verdict_on_the_chat_path()
    {
        var reply = await Provider(ProviderKinds.Text).CompleteAsync(
            new TextRequest { Messages = [TextMessage.User("hi")], Model = "m" });

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Contains("connection reset", reply.Detail, StringComparison.Ordinal);
    }
}
