using Lyntai.Inference;
using System.Net;
using System.Text.Json.Nodes;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.HttpProviders;

namespace Lyntai.Tests.Providers;

public class HttpProviderFaultTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")], Model = "m" };

    [Fact] // the /openai check reads the PATH: a host that merely starts with "openai" is not an /openai segment
    public async Task An_Azure_host_that_starts_with_openai_still_gets_the_openai_v1_surface()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""");
        var provider = Provider(handler, o => { o.BaseUrl = "https://openai-prod.example.com"; o.AzureConventions = true; });

        await provider.CompleteAsync(Req);

        Assert.Equal(new Uri("https://openai-prod.example.com/openai/v1/chat/completions"), handler.Requests[0].Uri);
    }

    [Fact] // HasCredentials and ApplyAuth agree: a blank key sends no header, so a 401 is NotConfigured
    public async Task A_whitespace_api_key_sends_no_auth_header_and_a_401_is_NotConfigured()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.Unauthorized, """{"error":{"message":"missing key"}}""");
        var provider = Provider(handler, o => o.ApiKey = "  ");

        var reply = await provider.CompleteAsync(Req);

        Assert.Null(handler.Requests[0].Auth);
        Assert.Equal(ProviderVerdict.NotConfigured, reply.Verdict);
    }

    [Fact] // an embedding or rerank registration with no BaseUrl is unavailable, not a throw at composition
    public async Task A_vector_registration_with_no_BaseUrl_builds_and_answers_NotConfigured()
    {
        var provider = Provider(new StubHttpHandler(), o => { o.BaseUrl = ""; o.Produces = ProviderKinds.Vector; });

        Assert.False(provider.IsAvailable);
        var response = await provider.CallAsync(new VectorRequest(["a"]));
        Assert.Equal(ProviderVerdict.NotConfigured, response.Verdict);
    }

    [Fact]
    public async Task A_rerank_registration_with_no_BaseUrl_builds_and_answers_NotConfigured()
    {
        var provider = Provider(new StubHttpHandler(), o => { o.BaseUrl = ""; o.Produces = ProviderKinds.Score; });

        Assert.False(provider.IsAvailable);
        var response = await provider.CallAsync(new ScoreRequest("q", ["a"]));
        Assert.Equal(ProviderVerdict.NotConfigured, response.Verdict);
    }

    [Fact]
    public async Task An_Ollama_vector_registration_with_no_BaseUrl_builds_and_answers_NotConfigured()
    {
        var provider = new OllamaProvider("ollama",
            new OllamaOptions { BaseUrl = "", Produces = ProviderKinds.Vector },
            () => new HttpClient(new StubHttpHandler(), disposeHandler: false), new LyntaiOptions());

        Assert.False(provider.IsAvailable);
        var response = await provider.CallAsync(new VectorRequest(["a"]));
        Assert.Equal(ProviderVerdict.NotConfigured, response.Verdict);
    }

    // The caller's cancel while a failure body is being read is the caller's cancel, not the 500's verdict.
    [Fact]
    public async Task A_cancel_while_an_error_body_is_read_propagates_on_complete()
    {
        var provider = Provider(HangingErrorBody());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Req, cts.Token));
    }

    [Fact]
    public async Task A_cancel_while_an_error_body_is_read_propagates_on_stream()
    {
        var provider = Provider(HangingErrorBody());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(Req, cts.Token)) { }
        });
    }

    [Fact]
    public async Task A_cancel_while_an_error_body_is_read_propagates_on_probe()
    {
        var provider = Provider(HangingErrorBody());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ProbeAsync(cts.Token));
    }

    [Fact] // arguments that are valid JSON but not an object are replayed as the empty object Ollama accepts
    public void A_replayed_Ollama_tool_call_with_non_object_arguments_is_sent_as_an_empty_object()
    {
        foreach (var arguments in new[] { "[1]", "\"x\"", "7", "null" })
        {
            var req = new TextRequest
            { Messages = [TextMessage.AssistantToolCalls([new TextToolCall("c1", "t", arguments)])] };

            var call = OllamaPayload.Build(req, "m", stream: false)["messages"]![0]!["tool_calls"]![0]!["function"]!;

            Assert.IsType<JsonObject>(call["arguments"]);
            Assert.Empty(call["arguments"]!.AsObject());
        }
    }

    private static StubHttpHandler HangingErrorBody() =>
        new StubHttpHandler().Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new HangingContent(),
        });

    /// <summary>A body that never finishes arriving — it ends only when the read is cancelled.</summary>
    private sealed class HangingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
