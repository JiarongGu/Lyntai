using System.Net;
using Lyntai;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

public class HttpEmbedderTests
{
    // OpenAI / LM Studio shape: { data: [ { index, embedding: [...] }, ... ] }. Integer-valued floats keep
    // the equality asserts exact (every value below is exactly representable in float32).
    private const string OpenAiBody = """
        {"object":"list","data":[
          {"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]},
          {"object":"embedding","index":1,"embedding":[4.0,5.0,6.0]}
        ],"model":"text-embedding-3-small","usage":{"prompt_tokens":4,"total_tokens":4}}
        """;

    private static HttpEmbedder Embedder(StubHttpHandler handler, Action<OpenAiCompatibleEmbedderOptions>? configure = null)
    {
        var config = new OpenAiCompatibleEmbedderOptions
        { BaseUrl = "https://api.openai.com", ApiKey = "test-key", Model = "text-embedding-3-small" };
        configure?.Invoke(config);
        return new HttpEmbedder("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    // An embedder has NO verdict and NO fallback — it throws (see the HttpEmbedder type doc), so there is no
    // "advance without blame" for it to reach and no verdict change to make. The only thing a host can act on
    // is the WORDING: "not configured" points at setup, while a bare 401 points at a key that was never
    // supplied. Message-only parity with the provider-side NotConfigured distinction.
    [Fact]
    public async Task A_401_with_no_api_key_supplied_says_not_configured()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "unauthorized");
        var embedder = Embedder(handler, c => c.ApiKey = null);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () => await embedder.EmbedAsync(["a"]));

        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("401", ex.Message); // the status stays, for diagnosis
    }

    [Fact]
    public async Task A_401_with_an_api_key_supplied_does_not_claim_it_is_unconfigured()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "unauthorized");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () => await Embedder(handler).EmbedAsync(["a"]));

        Assert.DoesNotContain("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Openai_v1_embeddings_shape_parses_vectors_and_sends_model_input_and_bearer()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        var vectors = await Embedder(handler).EmbedAsync(["a", "b"]);

        Assert.Equal(2, vectors.Count);
        Assert.Equal([1.0f, 2.0f, 3.0f], vectors[0]);
        Assert.Equal([4.0f, 5.0f, 6.0f], vectors[1]);
        Assert.Equal(new Uri("https://api.openai.com/v1/embeddings"), handler.Requests[0].Uri);
        Assert.Equal("Bearer test-key", handler.Requests[0].Auth);
        Assert.Contains("\"model\":\"text-embedding-3-small\"", handler.Requests[0].Body);
        Assert.Contains("\"input\":[\"a\",\"b\"]", handler.Requests[0].Body); // batched: one call, array input
    }

    [Fact] // the data[].index is authoritative — an out-of-order response must be re-sorted to match input order
    public async Task Data_entries_are_ordered_by_index_not_arrival()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[{"index":1,"embedding":[4.0,5.0,6.0]},{"index":0,"embedding":[1.0,2.0,3.0]}]}
            """);

        var vectors = await Embedder(handler).EmbedAsync(["a", "b"]);

        Assert.Equal([1.0f, 2.0f, 3.0f], vectors[0]); // index 0 first, though it arrived second
        Assert.Equal([4.0f, 5.0f, 6.0f], vectors[1]);
    }

    [Fact] // Ollama flavor: the native batched /api/embed endpoint + its { embeddings: [[...]] } shape, no auth
    public async Task Ollama_flavor_hits_api_embed_and_parses_embeddings_array()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"model":"nomic-embed-text","embeddings":[[1.0,2.0],[3.0,4.0]]}
            """);

        var embedder = Embedder(handler, c => { c.BaseUrl = "http://localhost:11434"; c.ApiKey = null; c.Model = "nomic-embed-text"; });
        var vectors = await embedder.EmbedAsync(["a", "b"]);

        Assert.Equal([1.0f, 2.0f], vectors[0]);
        Assert.Equal([3.0f, 4.0f], vectors[1]);
        Assert.Equal(new Uri("http://localhost:11434/api/embed"), handler.Requests[0].Uri);
        Assert.Null(handler.Requests[0].Auth); // keyless local endpoint
    }

    [Fact] // a base already ending in /v1 (e.g. Ollama's OpenAI-compat surface) must not double the prefix
    public async Task Base_url_ending_in_v1_is_not_doubled()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        await Embedder(handler, c => c.BaseUrl = "http://localhost:11434/v1").EmbedAsync(["a", "b"]);

        Assert.Equal(new Uri("http://localhost:11434/v1/embeddings"), handler.Requests[0].Uri);
    }

    [Fact] // Azure: bare resource URL composes the /openai/v1 surface + sends the api-key header (mirrors chat)
    public async Task Azure_bare_resource_composes_openai_v1_embeddings_and_sends_api_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        await Embedder(handler, c => { c.BaseUrl = "https://my-res.openai.azure.com"; c.ApiKey = "azure-key"; }).EmbedAsync(["a", "b"]);

        Assert.Equal(new Uri("https://my-res.openai.azure.com/openai/v1/embeddings"), handler.Requests[0].Uri);
        Assert.Equal("azure-key", handler.Requests[0].ApiKeyHeader);
        Assert.Equal("Bearer azure-key", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Empty_input_returns_empty_without_a_request()
    {
        var handler = new StubHttpHandler();

        var vectors = await Embedder(handler).EmbedAsync([]);

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests); // nothing to embed → no HTTP call
    }

    [Fact]
    public async Task Http_500_throws_with_the_id_and_status()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Embedder(handler).EmbedAsync(["a"]));

        Assert.Contains("openai", ex.Message);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Malformed_body_throws()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, "{not json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Embedder(handler).EmbedAsync(["a"]));
    }

    [Fact] // a response with fewer vectors than inputs is a correctness fault, not a partial success
    public async Task Vector_count_mismatch_throws()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0,2.0]}]}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Embedder(handler).EmbedAsync(["a", "b"]));

        Assert.Contains("2", ex.Message); // expected 2
    }

    [Fact] // BatchSize>0 chunks a large list into several requests, concatenating results in input order
    public async Task Batch_size_chunks_into_multiple_requests_preserving_order()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0]}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[2.0]}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[3.0]}]}""");

        var vectors = await Embedder(handler, c => c.BatchSize = 1).EmbedAsync(["a", "b", "c"]);

        Assert.Equal(3, handler.Requests.Count); // one request per input
        Assert.Equal([1.0f], vectors[0]);
        Assert.Equal([2.0f], vectors[1]);
        Assert.Equal([3.0f], vectors[2]);
    }

    [Fact] // the whole point: AddOpenAiCompatibleEmbedder wires IEmbedder → ISemanticMemory turns on
    public async Task AddOpenAiCompatibleEmbedder_wires_the_embedder_and_enables_semantic_recall()
    {
        // same vector for every input → the query embeds identically to the stored content (cosine 1.0),
        // so recall returns it. The stub repeats its last script, so one Enqueue covers both calls.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0,0.0]}]}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddOpenAiCompatibleEmbedder("test",
            o => { o.BaseUrl = "https://api.openai.com"; o.Model = "text-embedding-3-small"; o.ApiKey = "k"; },
            httpClient: _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IEmbedder>());
        var memory = sp.GetRequiredService<ISemanticMemory>();
        await memory.RememberAsync("task", "scope", "the sky is blue");
        var hits = await memory.RecallAsync("task", "scope", "sky color?");

        var hit = Assert.Single(hits);
        Assert.Equal("the sky is blue", hit.Content);
    }
}
