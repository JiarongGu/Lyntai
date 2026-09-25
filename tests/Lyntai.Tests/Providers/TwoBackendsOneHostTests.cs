using System.Net;
using Lyntai;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>A chat model and an embedding model are different backends that happen to share a hostname, so
/// they are two registrations (D133). These pin what <c>Produces</c> decides — the route, the operations and
/// which methods answer — and that two of them against one server stay two, with two ids.</summary>
public class TwoBackendsOneHostTests
{
    private const string Host = "http://localhost:8080"; // llama-server: chat AND embeddings off one root

    private const string ChatBody = """
        {"choices":[{"message":{"role":"assistant","content":"hi"}}]}
        """;

    private const string VectorResponseBody = """
        {"object":"list","data":[{"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]}]}
        """;

    private static HttpModelProvider Provider(
        StubHttpHandler handler, Action<HttpModelOptions> configure)
    {
        var config = new HttpModelOptions { BaseUrl = Host, Model = "a-model" };
        configure(config);
        return new HttpModelProvider("host", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    // ---- one field decides what the backend is -------------------------------------------------------

    [Fact]
    public void Text_is_the_default_so_an_unannotated_registration_is_a_chat_backend()
    {
        var provider = Provider(new StubHttpHandler(), _ => { });

        Assert.Equal([ProviderKinds.Text], provider.Capabilities.Produces);
        Assert.True(provider.Capabilities.Supports(ProviderKinds.Text, ProviderOperation.Stream));
        Assert.False(provider.Capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Complete));
    }

    // Vector drops Stream: there is no partially delivered embedding, and a capability that promised one
    // would be admitted by a router and then fail at the wire.
    [Fact]
    public void Vector_serves_ONE_batched_call_and_declares_no_stream_and_no_tools()
    {
        var provider = Provider(new StubHttpHandler(), o => o.Produces = ProviderKinds.Vector);

        Assert.Equal([ProviderKinds.Vector], provider.Capabilities.Produces);
        Assert.True(provider.Capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Complete));
        Assert.False(provider.Capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Stream));
        Assert.False(provider.Capabilities.SupportsToolCalls);
        Assert.False(provider.Capabilities.SupportsStreamingToolCalls);
    }

    // Reached only by a caller that ignored Capabilities — a router checks first. It answers an Unsupported
    // VERDICT with no vectors, never an empty Ok: there is no vector that means "I could not".
    [Fact]
    public async Task Embedding_a_TEXT_backend_is_Unsupported_and_says_what_to_register_instead()
    {
        var provider = Provider(new StubHttpHandler(), _ => { });

        var response = await provider.CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Unsupported, response.Verdict);
        Assert.Empty(response.Vectors);
        Assert.Contains("Produces = ProviderKinds.Vector", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Produces_picks_the_ROUTE_a_call_is_posted_to()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, VectorResponseBody);
        var provider = Provider(handler, o => o.Produces = ProviderKinds.Vector);

        await provider.EmbedAsync(["a"]);

        Assert.Equal($"{Host}/v1/embeddings", Assert.Single(handler.Requests).Uri?.ToString());
    }

    // ---- two backends, one host ----------------------------------------------------------------------

    // The shared hostname is TRANSPORT, not identity. Two registrations means two ids, so a trace says
    // which backend answered — which one id serving both routes could never report.
    [Fact]
    public async Task A_host_serving_both_routes_is_TWO_registrations_with_two_ids()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ChatBody)
            .Enqueue(HttpStatusCode.OK, VectorResponseBody);
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddHttpProvider("local-chat", o =>
            {
                o.BaseUrl = Host;
                o.Model = "llama3.1";
            }, httpClient: _ => new HttpClient(handler, disposeHandler: false))
            .AddHttpProvider("local-embed", o =>
            {
                o.BaseUrl = Host;
                o.Model = "nomic-embed-text";
                o.Produces = ProviderKinds.Vector;
            }, httpClient: _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var providers = sp.GetServices<IModelProvider>().ToList();
        Assert.Equal(["local-chat", "local-embed"], providers.Select(p => p.Id));

        var chat = providers.Single(p => p.Capabilities.Produces.Contains(ProviderKinds.Text));
        var reply = await chat.CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });
        var vectors = await EmbeddingRouting.EmbedAsync(sp.GetServices<IModelProvider>(), ["a"]);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal([1f, 2f, 3f], Assert.Single(vectors));
        Assert.Equal(
            [$"{Host}/v1/chat/completions", $"{Host}/v1/embeddings"],
            handler.Requests.Select(r => r.Uri?.ToString()));
    }

    // Declaring Vector is what arms the routed IModelProvider and AddSemanticMemory, both of which are decided
    // at composition time — before any provider is built (D129).
    [Fact]
    public void A_chat_only_deployment_gets_NO_vector_backend_rather_than_a_broken_one()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddHttpProvider("chat", o => o.BaseUrl = Host));
        using var sp = services.BuildServiceProvider();

        Assert.False(EmbeddingRouting.CanEmbed(sp.GetServices<IModelProvider>()));
    }
}
