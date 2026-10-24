using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Providers.Http;
using Lyntai.Providers.LlamaSharp;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>Which providers declare <see cref="ProviderCapabilities.SupportsStructuredOutput"/> — the claim a
/// <see cref="TextRequest.RequireNativeSupport"/> call routes on (D205). True only where the WIRE carries the
/// schema; a backend that turns it into prompt text, or drops it, must say no.</summary>
public class StructuredOutputPostureTests
{
    private static HttpClient NoNetwork() => new(new StubHttpHandler(), disposeHandler: false);

    [Fact]
    public void An_HTTP_chat_backend_declares_it_because_the_schema_travels_as_response_format()
    {
        var chat = new HttpModelProvider("http", new HttpModelOptions { BaseUrl = "http://localhost:8080" },
            NoNetwork, new LyntaiOptions());

        Assert.True(chat.Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public void An_HTTP_vector_backend_does_not_since_it_serves_no_text()
    {
        var vectors = new HttpModelProvider("http", new HttpModelOptions
        {
            BaseUrl = "http://localhost:8080",
            Produces = ProviderKinds.Vector,
        }, NoNetwork, new LyntaiOptions());

        Assert.False(vectors.Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public void Ollama_declares_it_because_the_schema_travels_as_format()
    {
        var ollama = new OllamaProvider("ollama", new OllamaOptions(), NoNetwork, new LyntaiOptions());

        Assert.True(ollama.Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public void A_CLI_does_not_because_the_schema_becomes_prompt_text()
    {
        Assert.False(new ClaudeCliProvider(new FakeProcessRunner(), new LyntaiOptions()).Capabilities.SupportsStructuredOutput);
        Assert.False(new CodexCliProvider(new FakeProcessRunner(), new LyntaiOptions()).Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public void A_local_GGUF_model_does_not_because_the_schema_is_dropped()
    {
        var local = new LlamaSharpProvider("local", new LlamaSharpOptions { ModelPath = "x.gguf" }, new LyntaiOptions());

        Assert.False(((IModelProvider)local).Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public void A_bridge_claims_it_only_when_its_registration_declares_it()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddBridgeProvider("plain", (_, _) => Task.FromResult(new TextResponse("x", ProviderVerdict.Ok)))
            .AddBridgeProvider("schema", (_, _) => Task.FromResult(new TextResponse("x", ProviderVerdict.Ok)),
                capabilities: new ProviderCapabilities { SupportsStructuredOutput = true }));
        using var sp = services.BuildServiceProvider();
        var byId = sp.GetServices<IModelProvider>().ToDictionary(p => p.Id);

        Assert.False(byId["plain"].Capabilities.SupportsStructuredOutput);
        Assert.True(byId["schema"].Capabilities.SupportsStructuredOutput);
    }
}
