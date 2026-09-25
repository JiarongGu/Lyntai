using System.Net;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.HttpProviders;

namespace Lyntai.Tests.Providers;

/// <summary>An HTTP text backend answers "is this usable?" by ASKING the server — its model listing, which
/// generates nothing — rather than presuming itself available because a BaseUrl is set.</summary>
public class HttpProbeTests
{
    // through the interface, as a consumer reaches it
    private static Task<ProviderProbeResult> Probe(IModelProvider provider) => provider.ProbeAsync();

    private const string Listing = "{\"object\":\"list\",\"data\":[{\"id\":\"gpt-4o\"},{\"id\":\"gpt-4o-mini\"}]}";

    [Fact]
    public async Task A_listing_that_names_the_configured_model_is_available_and_names_it()
    {
        var method = "";
        var handler = new StubHttpHandler().Enqueue(req =>
        {
            method = req.Method.Method;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listing) };
        });

        var probe = await Probe(Provider(handler, o => o.Model = "gpt-4o"));

        Assert.True(probe.Available);
        Assert.Equal("gpt-4o", probe.Model);
        Assert.Equal(["gpt-4o", "gpt-4o-mini"], probe.Models);
        Assert.Equal("GET", method);                              // a listing, never a generation
        Assert.Equal("https://api.openai.com/v1/models", handler.Requests.Single().Uri!.ToString());
        Assert.Equal("Bearer test-key", handler.Requests.Single().Auth);
    }

    [Fact]
    public async Task A_configured_model_the_listing_omits_stays_available_and_says_so()
    {
        // llama-server serves whatever is loaded and lists its own alias, so an absent name is not a fault
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Listing);

        var probe = await Probe(Provider(handler, o => o.Model = "o3"));

        Assert.True(probe.Available);
        Assert.Null(probe.Model);
        Assert.Contains("o3", probe.Detail);
        Assert.Equal(2, probe.Models.Count);
    }

    [Fact]
    public async Task A_refused_key_is_unavailable_and_an_absent_one_says_there_is_none()
    {
        var withKey = await Probe(Provider(new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "{}")));
        var noKey = await Probe(Provider(new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "{}"),
            o => o.ApiKey = null));

        Assert.False(withKey.Available);
        Assert.Contains("refused", withKey.Detail);
        Assert.False(noKey.Available);
        Assert.Contains("no key", noKey.Detail);
    }

    [Fact]
    public async Task An_unreachable_server_is_unavailable()
    {
        var handler = new StubHttpHandler().Enqueue(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));

        var probe = await Probe(Provider(handler));

        Assert.False(probe.Available);
        Assert.Contains("not reachable", probe.Detail);
    }

    [Fact]
    public async Task A_server_with_no_listing_route_is_reachable_and_lists_nothing()
    {
        // some embedding and rerank servers serve no /models: reaching them is the answer there is
        var probe = await Probe(Provider(new StubHttpHandler().Enqueue(HttpStatusCode.NotFound, "not found")));

        Assert.True(probe.Available);
        Assert.Empty(probe.Models);
        Assert.Contains("lists no models", probe.Detail);
    }

    [Fact]
    public async Task A_server_error_is_unavailable_with_the_status()
    {
        var probe = await Probe(Provider(new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "boom")));

        Assert.False(probe.Available);
        Assert.Contains("500", probe.Detail);
    }

    [Fact]
    public async Task No_base_url_is_not_configured_and_sends_nothing()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Listing);

        var probe = await Probe(Provider(handler, o => o.BaseUrl = ""));

        Assert.False(probe.Available);
        Assert.Contains("not configured", probe.Detail);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Azure_lists_under_its_own_route_with_its_key_header()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Listing);

        await Probe(Provider(handler, o => o.BaseUrl = "https://my-res.openai.azure.com"));

        Assert.Equal("https://my-res.openai.azure.com/openai/v1/models", handler.Requests.Single().Uri!.ToString());
        Assert.Equal("test-key", handler.Requests.Single().ApiKeyHeader);
    }

    [Fact]
    public async Task Ollama_reads_its_tags_and_matches_an_untagged_name_to_latest()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            "{\"models\":[{\"name\":\"llama3:latest\"},{\"name\":\"nomic-embed-text:v1.5\"}]}");
        var ollama = new OllamaProvider("ollama", new OllamaOptions { BaseUrl = "http://localhost:11434", Model = "llama3" },
            () => new HttpClient(handler, disposeHandler: false), new LyntaiOptions());

        var probe = await Probe(ollama);

        Assert.True(probe.Available);
        Assert.Equal("llama3:latest", probe.Model);
        Assert.Equal(["llama3:latest", "nomic-embed-text:v1.5"], probe.Models);
        Assert.Equal("http://localhost:11434/api/tags", handler.Requests.Single().Uri!.ToString());
    }

    [Fact]
    public async Task Ollama_that_is_not_running_is_unavailable()
    {
        var handler = new StubHttpHandler().Enqueue(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));
        var ollama = new OllamaProvider("ollama", new OllamaOptions { BaseUrl = "http://localhost:11434" },
            () => new HttpClient(handler, disposeHandler: false), new LyntaiOptions());

        var probe = await Probe(ollama);

        Assert.False(probe.Available);
        Assert.Contains("not reachable", probe.Detail);
    }
}
