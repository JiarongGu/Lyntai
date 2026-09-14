using System.Net;
using Lyntai;
using Lyntai.Embeddings;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>One OpenAI-compatible host answers <c>/chat/completions</c> AND <c>/embeddings</c>, so it is ONE
/// backend producing two kinds (D131). These pin the three things that makes true: the declaration, the
/// inheritance of the host, and that both front doors see the single registration.</summary>
public class OneHostTwoKindsTests
{
    private const string ChatBody = """
        {"choices":[{"message":{"role":"assistant","content":"hi"}}]}
        """;

    private const string EmbedBody = """
        {"object":"list","data":[{"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]}]}
        """;

    private static OpenAiCompatibleProvider Provider(
        StubHttpHandler handler, Action<OpenAiCompatibleOptions> configure)
    {
        var config = new OpenAiCompatibleOptions { BaseUrl = "http://localhost:8080", DefaultModel = "chat-model" };
        configure(config);
        return new OpenAiCompatibleProvider("host", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    // ---- the declaration is DERIVED from configuration, not fixed by the type -------------------------

    [Fact]
    public void Configuring_embeddings_adds_vector_WITHOUT_removing_text()
    {
        var provider = Provider(new StubHttpHandler(), o => o.Embeddings = new() { Model = "embed-model" });

        Assert.Equal([ProviderKinds.Text, ProviderKinds.Vector], provider.Capabilities.Produces);
        Assert.True(provider.Capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Complete));
        Assert.True(provider.Capabilities.Supports(ProviderKinds.Text, ProviderOperation.Complete));
        Assert.True(provider.Capabilities.Supports(ProviderKinds.Text, ProviderOperation.Stream));
    }

    [Fact]
    public void A_chat_only_host_produces_text_ALONE_so_a_router_never_offers_it_an_embedding()
    {
        var provider = Provider(new StubHttpHandler(), _ => { });

        Assert.Equal([ProviderKinds.Text], provider.Capabilities.Produces);
        Assert.False(provider.Capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Complete));
    }

    // Reached only by a caller that ignored Capabilities — a router checks first. It THROWS rather than
    // returning an empty list, for the reason IModelProvider.EmbedAsync's own default gives: there is no
    // vector that means "I could not", and a zero vector compares as real.
    [Fact]
    public async Task Embedding_an_unconfigured_host_throws_rather_than_returning_a_vector()
    {
        var provider = Provider(new StubHttpHandler(), _ => { });

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.EmbedAsync(["a"]));

        Assert.Contains("Embeddings", ex.Message, StringComparison.Ordinal); // names the field to set
    }

    // ---- both halves go to the host they were declared on ---------------------------------------------

    [Fact]
    public async Task The_embedding_half_INHERITS_the_hosts_url_and_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, EmbedBody);
        var builder = Build(handler, o =>
        {
            o.BaseUrl = "http://localhost:8080";
            o.ApiKey = "host-key";
            o.Embeddings = new() { Model = "embed-model" };   // no BaseUrl, no ApiKey
        });

        await builder.GetRequiredService<IEmbedder>().EmbedAsync(["a"]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:8080/v1/embeddings", request.Uri?.ToString());
        Assert.Equal("Bearer host-key", request.Auth);
    }

    // The split-port case: chat on 8080, embeddings on 8081. An explicitly set field must beat inheritance,
    // or declaring embeddings on a host would FORCE them onto it.
    [Fact]
    public async Task An_explicit_embedding_url_OVERRIDES_the_host_it_was_declared_on()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, EmbedBody);
        var builder = Build(handler, o =>
        {
            o.BaseUrl = "http://localhost:8080";
            o.Embeddings = new() { BaseUrl = "http://localhost:8081", Model = "embed-model" };
        });

        await builder.GetRequiredService<IEmbedder>().EmbedAsync(["a"]);

        Assert.Equal("http://localhost:8081/v1/embeddings", Assert.Single(handler.Requests).Uri?.ToString());
    }

    // DefaultModel names a CHAT model. Inheriting it would post a plausible request that returns nonsense —
    // or, on a host that serves one model per route, a confusing 404. The two fields stay independent.
    [Fact]
    public async Task The_embedding_model_does_NOT_inherit_DefaultModel()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, EmbedBody);
        var builder = Build(handler, o =>
        {
            o.DefaultModel = "gpt-4o";
            o.Embeddings = new() { Model = "text-embedding-3-small" };
        });

        await builder.GetRequiredService<IEmbedder>().EmbedAsync(["a"]);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("text-embedding-3-small", body, StringComparison.Ordinal);
        Assert.DoesNotContain("gpt-4o", body, StringComparison.Ordinal);
    }

    // ---- ONE registration, BOTH front doors ------------------------------------------------------------

    // The payoff. Before D131 this needed AddOpenAiCompatibleProvider AND AddOpenAiCompatibleEmbedder — two
    // ids, two HttpClients and two configurations aimed at one server.
    [Fact]
    public async Task One_registration_serves_the_router_AND_the_embedder()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ChatBody)
            .Enqueue(HttpStatusCode.OK, EmbedBody);
        var services = Build(handler, o => o.Embeddings = new() { Model = "embed-model" });

        var provider = Assert.Single(services.GetServices<IModelProvider>());
        var reply = await provider.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });
        var vectors = await services.GetRequiredService<IEmbedder>().EmbedAsync(["a"]);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal([1f, 2f, 3f], Assert.Single(vectors));
        Assert.Equal(
            ["http://localhost:8080/v1/chat/completions", "http://localhost:8080/v1/embeddings"],
            handler.Requests.Select(r => r.Uri?.ToString()));
    }

    private static ServiceProvider Build(StubHttpHandler handler, Action<OpenAiCompatibleOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddOpenAiCompatibleProvider("host", o =>
        {
            o.BaseUrl = "http://localhost:8080";
            configure(o);
        }, httpClient: _ => new HttpClient(handler, disposeHandler: false)));
        return services.BuildServiceProvider();
    }
}
