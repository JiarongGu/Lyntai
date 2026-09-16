using System.Net;
using Lyntai;
using Lyntai.Lifecycle;
using Lyntai.Memory;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

public class HttpEmbeddingsTransportTests
{
    // OpenAI / LM Studio shape: { data: [ { index, embedding: [...] }, ... ] }. Integer-valued floats keep
    // the equality asserts exact (every value below is exactly representable in float32).
    private const string OpenAiBody = """
        {"object":"list","data":[
          {"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]},
          {"object":"embedding","index":1,"embedding":[4.0,5.0,6.0]}
        ],"model":"text-embedding-3-small","usage":{"prompt_tokens":4,"total_tokens":4}}
        """;

    // The same shape carrying ONE vector. HttpEmbeddingsTransport asserts the returned vector count matches the batch
    // size, so a single-input test scripted with the two-vector body above fails on that guard rather than
    // on what it meant to assert.
    private const string OpenAiBodyOne = """
        {"object":"list","data":[
          {"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]}
        ],"model":"text-embedding-3-small","usage":{"prompt_tokens":2,"total_tokens":2}}
        """;

    private static HttpEmbeddingsTransport Embedder(StubHttpHandler handler, Action<HttpModelOptions>? configure = null)
    {
        var config = new HttpModelOptions
        { BaseUrl = "https://api.openai.com", ApiKey = "test-key", Model = "text-embedding-3-small" };
        configure?.Invoke(config);
        return new HttpEmbeddingsTransport("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    // An embedder has NO verdict and NO fallback — it throws (see the HttpEmbeddingsTransport type doc), so there is no
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

    // ---- a corrupt vector must FAIL, not arrive ------------------------------------------------------
    // Every element used to be coerced with `n.ValueKind == Number ? (float)n.GetDouble() : 0f`, so a null,
    // a string or a non-finite element became a silent 0 in an otherwise plausible vector — which then got
    // stored, or compared by cosine, with nothing anywhere reporting it. The type's own XML doc already
    // promises InvalidOperationException on a malformed body; these route the corrupt cases into it.

    [Theory]
    [InlineData("null", "a JSON null")]
    [InlineData("\"0.5\"", "a number sent as a string")]
    [InlineData("{}", "an object")]
    public async Task A_NON_NUMERIC_element_makes_the_response_malformed_rather_than_a_zeroed_vector(
        string element, string why)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            $$"""{"data":[{"index":0,"embedding":[1.0,{{element}},3.0]}]}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Embedder(handler).EmbedAsync(["a"]));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public async Task A_NON_FINITE_element_is_malformed_too_because_it_poisons_every_cosine_it_touches()
    {
        // JSON has no Infinity literal, but a large enough magnitude overflows float32 on the way in.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"index":0,"embedding":[1.0,1e400,3.0]}]}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Embedder(handler).EmbedAsync(["a"]));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_legitimate_ZERO_is_still_a_perfectly_good_component()
    {
        // The positive control: the fix must reject non-NUMBERS, never the number zero — which is common in
        // a real embedding and was indistinguishable from the coerced failure value.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"index":0,"embedding":[1.0,0.0,3.0]}]}""");

        var vectors = await Embedder(handler).EmbedAsync(["a"]);

        Assert.Equal([1.0f, 0.0f, 3.0f], vectors[0]);
    }

    // ---- role prefixes: driving an ASYMMETRIC model ---------------------------------------------------
    // The E5/BGE/nomic/Arctic families want a different instruction on each side of the comparison. The
    // library supplies neither prefix and knows no model's spelling — the deployment sets both strings.

    [Fact]
    public async Task A_QUERY_and_a_DOCUMENT_are_sent_with_their_own_configured_prefixes()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var embedder = Embedder(handler, c =>
        {
            c.DocumentPrefix = "search_document: ";
            c.QueryPrefix = "search_query: ";
        });

        await embedder.EmbedAsync(["paris is the capital"], EmbeddingRole.Document);
        await embedder.EmbedAsync(["where is paris"], EmbeddingRole.Query);

        Assert.Contains("search_document: paris is the capital", handler.Requests[0].Body);
        Assert.Contains("search_query: where is paris", handler.Requests[1].Body);
    }

    [Fact]
    public async Task With_NO_prefixes_configured_the_text_is_sent_verbatim_whatever_the_role()
    {
        // The default must be a symmetric model, because that is what the library shipped before roles
        // existed and what most endpoints serve.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var embedder = Embedder(handler);

        await embedder.EmbedAsync(["paris"], EmbeddingRole.Query);

        Assert.Contains("\"paris\"", handler.Requests[0].Body);
        Assert.DoesNotContain("query", handler.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configuring_only_ONE_side_leaves_the_other_verbatim()
    {
        // A model with an instruction on queries only (the BGE shape) must not get an invented document
        // prefix — an empty string and "unset" have to mean the same thing here.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var embedder = Embedder(handler, c => c.QueryPrefix = "Represent this sentence: ");

        await embedder.EmbedAsync(["stored text"], EmbeddingRole.Document);

        Assert.Contains("\"stored text\"", handler.Requests[0].Body);
        Assert.DoesNotContain("Represent", handler.Requests[0].Body);
    }

    [Fact]
    public async Task The_prefix_is_applied_to_EVERY_text_in_a_batch()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);
        var embedder = Embedder(handler, c => c.DocumentPrefix = "passage: ");

        await embedder.EmbedAsync(["first", "second"], EmbeddingRole.Document);

        Assert.Contains("passage: first", handler.Requests[0].Body);
        Assert.Contains("passage: second", handler.Requests[0].Body);
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

    [Fact] // Ollama dialect: the native batched /api/embed endpoint + its { embeddings: [[...]] } shape, no auth
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

    [Fact] // the whole point: declaring the embeddings route wires IEmbedder → ISemanticMemory turns on
    public async Task Declaring_the_embeddings_route_wires_the_embedder_and_enables_semantic_recall()
    {
        // same vector for every input → the query embeds identically to the stored content (cosine 1.0),
        // so recall returns it. The stub repeats its last script, so one Enqueue covers both calls.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0,0.0]}]}""");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddHttpProvider("test",
            o =>
            {
                o.BaseUrl = "https://api.openai.com";
                o.ApiKey = "k";
                o.Model = "text-embedding-3-small";
                o.Produces = ProviderKinds.Vector;
            },
            httpClient: _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        Assert.True(EmbeddingRouting.CanEmbed(sp.GetServices<IModelProvider>()));
        var memory = sp.GetRequiredService<ISemanticMemory>();
        await memory.RememberAsync("task", "scope", "the sky is blue");
        var hits = await memory.RecallAsync("task", "scope", "sky color?");

        var hit = Assert.Single(hits);
        Assert.Equal("the sky is blue", hit.Content);
    }
}
