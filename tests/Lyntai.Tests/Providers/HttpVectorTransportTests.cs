using System.Net;
using Lyntai;
using Lyntai.Lifecycle;
using Lyntai.Memory;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

public class HttpVectorTransportTests
{
    // OpenAI / LM Studio shape: { data: [ { index, embedding: [...] }, ... ] }. Integer-valued floats keep
    // the equality asserts exact (every value below is exactly representable in float32).
    private const string OpenAiBody = """
        {"object":"list","data":[
          {"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]},
          {"object":"embedding","index":1,"embedding":[4.0,5.0,6.0]}
        ],"model":"text-embedding-3-small","usage":{"prompt_tokens":4,"total_tokens":4}}
        """;

    // The same shape carrying ONE vector. HttpVectorTransport asserts the returned vector count matches the batch
    // size, so a single-input test scripted with the two-vector body above fails on that guard rather than
    // on what it meant to assert.
    private const string OpenAiBodyOne = """
        {"object":"list","data":[
          {"object":"embedding","index":0,"embedding":[1.0,2.0,3.0]}
        ],"model":"text-embedding-3-small","usage":{"prompt_tokens":2,"total_tokens":2}}
        """;

    private static HttpVectorTransport VectorProvider(StubHttpHandler handler, Action<HttpModelOptions>? configure = null)
    {
        var config = new HttpModelOptions
        { BaseUrl = "https://api.openai.com", ApiKey = "test-key", Model = "text-embedding-3-small" };
        configure?.Invoke(config);
        return new HttpVectorTransport("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    // A 401 answered to a call carrying NO key is NotConfigured, not AuthFailed — full parity with the chat
    // path, and the difference is not cosmetic: AuthFailed BENCHES this host for the cooldown window, while
    // NotConfigured advances blamelessly and lets a host offer setup. Until D153 an embed call had no verdict
    // to put that in and said it in the message instead; this asserts the verdict, and that the status still
    // reaches a human for diagnosis.
    [Fact]
    public async Task A_401_with_no_api_key_supplied_is_NOT_CONFIGURED_rather_than_a_benched_host()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "unauthorized");
        var vectorProvider = VectorProvider(handler, c => c.ApiKey = null);

        var response = await vectorProvider.CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.NotConfigured, response.Verdict);
        Assert.Empty(response.Vectors);
        Assert.Contains("401", response.Detail); // the status stays, for diagnosis
    }

    [Fact]
    public async Task A_401_WITH_a_key_supplied_is_AuthFailed_which_benches_the_host()
    {
        // The other half of the two-term promotion: credentials were sent and REJECTED, which is a fault
        // worth cooling the host over. Without this arm the test above passes for a classifier that answers
        // NotConfigured to every 401.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "unauthorized");
        var vectorProvider = VectorProvider(handler, c => c.ApiKey = "sk-real");

        var response = await vectorProvider.CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.AuthFailed, response.Verdict);
    }

    [Fact]
    public async Task A_429_is_RATE_LIMITED_so_the_router_can_cool_this_host()
    {
        // The gain D153 actually buys here: before, this threw and the embedding path retried the same
        // exhausted host on the very next recall, because it had no cooldown to reach.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.TooManyRequests, "slow down");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.RateLimited, response.Verdict);
    }

    // ---- a corrupt vector must FAIL, not arrive ------------------------------------------------------
    // Every element used to be coerced with `n.ValueKind == Number ? (float)n.GetDouble() : 0f`, so a null,
    // a string or a non-finite element became a silent 0 in an otherwise plausible vector — which then got
    // stored, or compared by cosine, with nothing anywhere reporting it. Since D153 a malformed body is a
    // Failed VERDICT rather than a throw, which is what lets the router advance to the next backend instead
    // of the call dying — these route the corrupt cases into it.

    [Theory]
    [InlineData("null", "a JSON null")]
    [InlineData("\"0.5\"", "a number sent as a string")]
    [InlineData("{}", "an object")]
    public async Task A_NON_NUMERIC_element_makes_the_response_malformed_rather_than_a_zeroed_vector(
        string element, string why)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            $$"""{"data":[{"index":0,"embedding":[1.0,{{element}},3.0]}]}""");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("malformed", response.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public async Task A_NON_FINITE_element_is_malformed_too_because_it_poisons_every_cosine_it_touches()
    {
        // JSON has no Infinity literal, but a large enough magnitude overflows float32 on the way in.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"index":0,"embedding":[1.0,1e400,3.0]}]}""");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("malformed", response.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_legitimate_ZERO_is_still_a_perfectly_good_component()
    {
        // The positive control: the fix must reject non-NUMBERS, never the number zero — which is common in
        // a real embedding and was indistinguishable from the coerced failure value.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"index":0,"embedding":[1.0,0.0,3.0]}]}""");

        var vectors = await VectorProvider(handler).EmbedAsync(["a"]);

        Assert.Equal([1.0f, 0.0f, 3.0f], vectors[0]);
    }

    // ---- role prefixes: driving an ASYMMETRIC model ---------------------------------------------------
    // The E5/BGE/nomic/Arctic families want a different instruction on each side of the comparison. The
    // library supplies neither prefix and knows no model's spelling — the deployment sets both strings.

    [Fact]
    public async Task A_QUERY_and_a_DOCUMENT_are_sent_with_their_own_configured_prefixes()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var vectorProvider = VectorProvider(handler, c =>
        {
            c.DocumentPrefix = "search_document: ";
            c.QueryPrefix = "search_query: ";
        });

        await vectorProvider.EmbedAsync(["paris is the capital"], EmbeddingRole.Document);
        await vectorProvider.EmbedAsync(["where is paris"], EmbeddingRole.Query);

        Assert.Contains("search_document: paris is the capital", handler.Requests[0].Body);
        Assert.Contains("search_query: where is paris", handler.Requests[1].Body);
    }

    [Fact]
    public async Task With_NO_prefixes_configured_the_text_is_sent_verbatim_whatever_the_role()
    {
        // The default must be a symmetric model, because that is what the library shipped before roles
        // existed and what most endpoints serve.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var vectorProvider = VectorProvider(handler);

        await vectorProvider.EmbedAsync(["paris"], EmbeddingRole.Query);

        Assert.Contains("\"paris\"", handler.Requests[0].Body);
        Assert.DoesNotContain("query", handler.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configuring_only_ONE_side_leaves_the_other_verbatim()
    {
        // A model with an instruction on queries only (the BGE shape) must not get an invented document
        // prefix — an empty string and "unset" have to mean the same thing here.
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBodyOne);
        var vectorProvider = VectorProvider(handler, c => c.QueryPrefix = "Represent this sentence: ");

        await vectorProvider.EmbedAsync(["stored text"], EmbeddingRole.Document);

        Assert.Contains("\"stored text\"", handler.Requests[0].Body);
        Assert.DoesNotContain("Represent", handler.Requests[0].Body);
    }

    [Fact]
    public async Task The_prefix_is_applied_to_EVERY_text_in_a_batch()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);
        var vectorProvider = VectorProvider(handler, c => c.DocumentPrefix = "passage: ");

        await vectorProvider.EmbedAsync(["first", "second"], EmbeddingRole.Document);

        Assert.Contains("passage: first", handler.Requests[0].Body);
        Assert.Contains("passage: second", handler.Requests[0].Body);
    }

    [Fact]
    public async Task A_401_with_an_api_key_supplied_does_not_claim_it_is_unconfigured()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, "unauthorized");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.NotEqual(ProviderVerdict.NotConfigured, response.Verdict);
    }

    [Fact]
    public async Task Openai_v1_embeddings_shape_parses_vectors_and_sends_model_input_and_bearer()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        var vectors = await VectorProvider(handler).EmbedAsync(["a", "b"]);

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

        var vectors = await VectorProvider(handler).EmbedAsync(["a", "b"]);

        Assert.Equal([1.0f, 2.0f, 3.0f], vectors[0]); // index 0 first, though it arrived second
        Assert.Equal([4.0f, 5.0f, 6.0f], vectors[1]);
    }

    [Fact] // Ollama dialect: the native batched /api/embed endpoint + its { embeddings: [[...]] } shape, no auth
    public async Task Ollama_flavor_hits_api_embed_and_parses_embeddings_array()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"model":"nomic-embed-text","embeddings":[[1.0,2.0],[3.0,4.0]]}
            """);

        var vectorProvider = VectorProvider(handler, c => { c.BaseUrl = "http://localhost:11434"; c.ApiKey = null; c.Model = "nomic-embed-text"; });
        var vectors = await vectorProvider.EmbedAsync(["a", "b"]);

        Assert.Equal([1.0f, 2.0f], vectors[0]);
        Assert.Equal([3.0f, 4.0f], vectors[1]);
        Assert.Equal(new Uri("http://localhost:11434/api/embed"), handler.Requests[0].Uri);
        Assert.Null(handler.Requests[0].Auth); // keyless local endpoint
    }

    [Fact] // a base already ending in /v1 (e.g. Ollama's OpenAI-compat surface) must not double the prefix
    public async Task Base_url_ending_in_v1_is_not_doubled()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        await VectorProvider(handler, c => c.BaseUrl = "http://localhost:11434/v1").EmbedAsync(["a", "b"]);

        Assert.Equal(new Uri("http://localhost:11434/v1/embeddings"), handler.Requests[0].Uri);
    }

    [Fact] // Azure: bare resource URL composes the /openai/v1 surface + sends the api-key header (mirrors chat)
    public async Task Azure_bare_resource_composes_openai_v1_embeddings_and_sends_api_key()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OpenAiBody);

        await VectorProvider(handler, c => { c.BaseUrl = "https://my-res.openai.azure.com"; c.ApiKey = "azure-key"; }).EmbedAsync(["a", "b"]);

        Assert.Equal(new Uri("https://my-res.openai.azure.com/openai/v1/embeddings"), handler.Requests[0].Uri);
        Assert.Equal("azure-key", handler.Requests[0].ApiKeyHeader);
        Assert.Equal("Bearer azure-key", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Empty_input_returns_empty_without_a_request()
    {
        var handler = new StubHttpHandler();

        var vectors = await VectorProvider(handler).EmbedAsync([]);

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests); // nothing to embed → no HTTP call
    }

    [Fact]
    public async Task Http_500_is_a_FAILED_verdict_carrying_the_id_and_status()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.InternalServerError, "boom");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("openai", response.Detail);
        Assert.Contains("500", response.Detail);
    }

    [Fact]
    public async Task Malformed_body_is_a_FAILED_verdict()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, "{not json");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Empty(response.Vectors);
    }

    [Fact] // a response with fewer vectors than inputs is a correctness fault, not a partial success
    public async Task Vector_count_mismatch_is_a_FAILED_verdict_rather_than_a_partial_success()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0,2.0]}]}""");

        var response = await VectorProvider(handler).CallAsync(new VectorRequest(["a", "b"]));

        Assert.Equal(ProviderVerdict.Failed, response.Verdict);
        Assert.Contains("2", response.Detail); // expected 2
    }

    [Fact] // BatchSize>0 chunks a large list into several requests, concatenating results in input order
    public async Task Batch_size_chunks_into_multiple_requests_preserving_order()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1.0]}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[2.0]}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[3.0]}]}""");

        var vectors = await VectorProvider(handler, c => c.BatchSize = 1).EmbedAsync(["a", "b", "c"]);

        Assert.Equal(3, handler.Requests.Count); // one request per input
        Assert.Equal([1.0f], vectors[0]);
        Assert.Equal([2.0f], vectors[1]);
        Assert.Equal([3.0f], vectors[2]);
    }

    [Fact] // the whole point: declaring the embeddings route wires a vector backend → ISemanticMemory turns on
    public async Task Declaring_the_embeddings_route_wires_the_vector_backend_and_enables_semantic_recall()
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
