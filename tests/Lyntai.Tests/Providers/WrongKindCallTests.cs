using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>A call of a kind the registration does not produce is answered with an
/// <see cref="ProviderVerdict.Unsupported"/> VERDICT on every sibling — the seam's own default, and what
/// ONNX already did — never a throw on HTTP and Ollama alone. A router never sends one (it selects on
/// capabilities); a direct caller that does gets the same shape from every backend, naming what to register
/// instead.</summary>
public class WrongKindCallTests
{
    private static readonly LyntaiOptions Options = new() { ProviderTimeout = TimeSpan.FromSeconds(5) };
    private static readonly TextRequest Chat = new() { Messages = [TextMessage.User("hi")], Model = "m" };

    private static HttpModelProvider Http(string produces, StubHttpHandler handler) =>
        new("h", new HttpModelOptions { BaseUrl = "http://localhost:8080", Produces = produces },
            () => new HttpClient(handler, disposeHandler: false), Options);

    private static OllamaProvider Ollama(string produces, StubHttpHandler handler) =>
        new("o", new OllamaOptions { Produces = produces },
            () => new HttpClient(handler, disposeHandler: false), Options);

    [Fact]
    public async Task Http_text_registration_answers_a_vector_or_score_call_with_Unsupported()
    {
        var handler = new StubHttpHandler();
        var provider = Http(ProviderKinds.Text, handler);

        var vectors = await provider.CallAsync(new VectorRequest(["a"]));
        var scores = await provider.CallAsync(new ScoreRequest("q", ["a"]));

        Assert.Equal(ProviderVerdict.Unsupported, vectors.Verdict);
        Assert.Contains("Produces = ProviderKinds.Vector", vectors.Detail, StringComparison.Ordinal);
        Assert.Equal(ProviderVerdict.Unsupported, scores.Verdict);
        Assert.Contains("Produces = ProviderKinds.Score", scores.Detail, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Http_vector_registration_answers_a_chat_with_Unsupported_on_both_paths()
    {
        var provider = Http(ProviderKinds.Vector, new StubHttpHandler());

        var reply = await provider.CompleteAsync(Chat);
        var chunk = Assert.Single(await provider.StreamAsync(Chat).ToListAsync());

        Assert.Equal(ProviderVerdict.Unsupported, reply.Verdict);
        Assert.Contains("Produces = ProviderKinds.Text", reply.Detail, StringComparison.Ordinal);
        Assert.Equal(TextChunkKind.Error, chunk.Kind);
        Assert.Equal(ProviderVerdict.Unsupported, chunk.Verdict);
    }

    [Fact]
    public async Task Ollama_answers_a_wrong_kind_call_with_Unsupported()
    {
        var text = Ollama(ProviderKinds.Text, new StubHttpHandler());
        var vector = Ollama(ProviderKinds.Vector, new StubHttpHandler());

        Assert.Equal(ProviderVerdict.Unsupported, (await text.CallAsync(new VectorRequest(["a"]))).Verdict);
        Assert.Equal(ProviderVerdict.Unsupported, (await vector.CompleteAsync(Chat)).Verdict);
        var chunk = Assert.Single(await vector.StreamAsync(Chat).ToListAsync());
        Assert.Equal(ProviderVerdict.Unsupported, chunk.Verdict);
    }
}
