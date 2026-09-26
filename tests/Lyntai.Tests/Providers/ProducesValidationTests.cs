using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>A <c>Produces</c> the backend cannot serve is refused at COMPOSITION, by all three siblings that
/// take one, with ONE exception type naming the option and the kinds it does serve. Accepting it would register
/// a backend that declares the kind to every router and answers every call by throwing — a typo in an open
/// vocabulary (<c>"embedding"</c>, <c>"vectors"</c>) is the likely way to reach it.</summary>
public class ProducesValidationTests
{
    private static readonly LyntaiOptions Options = new();

    [Theory]
    [InlineData("embedding")]
    [InlineData("vectors")]
    [InlineData(ProviderKinds.Image)]
    [InlineData("")]
    public void The_http_provider_refuses_a_kind_it_does_not_serve(string produces)
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpModelProvider("h",
            new HttpModelOptions { BaseUrl = "http://localhost:8080", Produces = produces },
            () => new HttpClient(), Options));

        Assert.Contains(nameof(HttpModelOptions.Produces), ex.Message, StringComparison.Ordinal);
        foreach (var served in new[] { ProviderKinds.Text, ProviderKinds.Vector, ProviderKinds.Score })
            Assert.Contains(served, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderKinds.Score)]
    [InlineData("embedding")]
    [InlineData(ProviderKinds.Audio)]
    public void The_ollama_provider_refuses_a_kind_it_does_not_serve(string produces)
    {
        var ex = Assert.Throws<ArgumentException>(() => new OllamaProvider("o",
            new OllamaOptions { Produces = produces }, () => new HttpClient(), Options));

        Assert.Contains(nameof(OllamaOptions.Produces), ex.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderKinds.Text, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderKinds.Vector, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_onnx_provider_refuses_a_kind_it_does_not_serve_before_loading_anything()
    {
        // the directory does not exist: the option is judged first, so the refusal names the real mistake
        var ex = Assert.Throws<ArgumentException>(() => OnnxProvider.FromDirectory(
            Path.Combine(Path.GetTempPath(), "lyntai-no-such-model"),
            new OnnxProviderOptions { Produces = "embedding" }));

        Assert.Contains(nameof(OnnxProviderOptions.Produces), ex.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderKinds.Vector, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderKinds.Score, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registrations_refuse_it_at_composition()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b =>
            b.AddHttpProvider("h", o => { o.BaseUrl = "http://localhost:8080"; o.Produces = "vectors"; })));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b =>
            b.AddOllamaProvider("o", o => o.Produces = ProviderKinds.Score)));
    }

    [Theory] // the positive control: every served kind still registers, in any case
    [InlineData(ProviderKinds.Text)]
    [InlineData(ProviderKinds.Vector)]
    [InlineData(ProviderKinds.Score)]
    [InlineData("VECTOR")]
    public void A_served_kind_is_accepted(string produces)
    {
        var provider = new HttpModelProvider("h",
            new HttpModelOptions { BaseUrl = "http://localhost:8080", Produces = produces },
            () => new HttpClient(), Options);

        Assert.Equal([produces], provider.Capabilities.Produces);
    }
}
